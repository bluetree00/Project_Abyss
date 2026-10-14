using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RelicFairy.Monster
{
[CreateAssetMenu(fileName = "PatternAttackOverride", menuName = "Lee/Monster/Override/PatternAttack")]
public class PatternAttackOverrideSO : MonsterStateOverrideSO
{
    public enum PatternSelectionMode
    {
        HighestPriority = 0,
        RandomAmongValid = 1,
    }

    [Serializable]
    public class AttackPattern
    {
        public enum WarningShapeType
        {
            Auto = 0,
            Circle = 1,
            Front1 = 2,
            Front2 = 3,
            FrontWide3 = 4,
            Around8 = 5,
        }

        public string name;
        public string animationStateName;
        public float minDistance = 0f;
        public float maxDistance = 2f;
        public int priority = 0;
        public float cooldown = 0f;
        public float damageDelay = -1f;
        public float attackDuration = -1f;
        public float damageMultiplier = 1f;
        public float knockbackMultiplier = 1f;
        public MonsterAttackShapeSO attackShapeOverride;
        public bool disableWarning = false;
        public float warningRadius = 0f;
        public float warningDuration = 0f;
        public WarningShapeType warningShape = WarningShapeType.Auto;
        public GameObject castVfxPrefab;
        public float castVfxDuration = 0f;
        public float castVfxDelay = 0f;
        public float castVfxLeadTime = 0.15f;
        public float castVfxXRotationOffset = 0f;
        public float castVfxYRotationOffset = 0f;
        public float castVfxZRotationOffset = 0f;
        public float castVfxForwardOffset = 0f;
        public float castVfxRightOffset = 0f;
        public float castVfxVerticalOffset = 0.8f;
        public float castVfxScale = 1f;
        public bool castVfxAttachToMonster = true;
        public bool castVfxAtPlayerPosition = false;
        public bool useProceduralBeamVfx = false;
        public float proceduralBeamLength = 3f;
        public float proceduralBeamWidth = 0.16f;
        public Color proceduralBeamColor = new Color(1f, 0.2f, 0.2f, 0.95f);

        [Header("Debuff (Optional)")]
        public float slowScale;
        public float slowDuration;

        [Header("Hit VFX")]
        [Tooltip("공격이 실제로 맞았을 때 스폰할 VFX 프리팹. null이면 생략.")]
        public GameObject hitVfxPrefab;
        [Tooltip("Hit VFX 스케일 배율.")]
        public float hitVfxScale = 1f;
        [Tooltip("피격 위치 기준 오프셋.")]
        public Vector3 hitVfxOffset = Vector3.zero;
    }

    [Tooltip("Ordered list of attack patterns. Highest priority among valid patterns wins.")]
    public List<AttackPattern> patterns = new();
    public PatternSelectionMode selectionMode = PatternSelectionMode.HighestPriority;
    [Tooltip("Distance at which the monster stops chasing and starts attack-ready. Negative uses the farthest pattern range.")]
    public float engageDistance = -1f;
    [Range(0.5f, 1f)]
    [Tooltip("Scale engage distance to avoid stopping too early before attacks can actually connect.")]
    public float engageDistanceScale = 0.85f;

    public override void RegisterOverrides(MonsterFSM fsm, MonsterBase monster)
    {
        var runtime = new PatternRuntime(patterns.Count);
        monster.RegisterOnEnabledCallback(runtime.Reset);

        fsm.RegisterAs<ChaseState>(new PatternChaseState(this, runtime));
        fsm.RegisterAs<AttackReadyState>(new PatternAttackReadyState(this, runtime));
        fsm.RegisterAs<AttackState>(new PatternAttackState(this, runtime));
    }

    private float GetMaxAttackRange()
    {
        float maxRange = 0f;
        for (int i = 0; i < patterns.Count; i++)
            maxRange = Mathf.Max(maxRange, patterns[i].maxDistance);
        return maxRange;
    }

    private float GetEngageDistance()
        => engageDistance > 0f ? engageDistance : GetMaxAttackRange();

    private float GetEffectiveEngageDistance()
        => Mathf.Max(0.1f, GetEngageDistance() * Mathf.Clamp(engageDistanceScale, 0.5f, 1f));

    /// <summary>패턴이 쓰는 동시 공격 칸 — 투사체 모양이면 원거리, 아니면 근접(10-11 P2-②).</summary>
    // ── 일반방 긴장감(10-13 · 79 몫 마무리) ──
    private const float CycleCommon = 1.2f, CycleThreat = 1.5f;   // 공격 시작 사이 최소 간격(기획 P1-6 · 배율만큼 줄어듦)
    private const float EliteBigCooldown = 4f;                     // 정예 큰 공격(판정 3칸 이상) 패턴 쿨다운 최소(기획 「정예 큰 공격 4 ~ 6초」)

    /// <summary>공격 시작 사이 최소 간격 — 잡몹 1.2 · 위협 · 정예 1.5초 · 보스는 없음.</summary>
    private static float MinCycleOf(MonsterGrade g) => g == MonsterGrade.Common ? CycleCommon : g == MonsterGrade.Boss ? 0f : CycleThreat;

    /// <summary>정예의 큰 공격 — 판정 격자가 3칸 이상(가로 3칸 · 둘레 8칸).</summary>
    private bool IsEliteBig(MonsterContext ctx, int index)
    {
        if (ctx.Monster.Grade != MonsterGrade.Elite || index < 0 || index >= patterns.Count) return false;
        var shape = patterns[index].attackShapeOverride != null ? patterns[index].attackShapeOverride : ctx.Stat.attackShape;
        if (shape is MonsterRangedAttackSO) return false;
        var g = ResolveWarningShape(patterns[index], shape);
        return g == MonsterGroundWarning.GridShape.FrontWide3 || g == MonsterGroundWarning.GridShape.Around8;
    }

    private const float RangedKeepMin = 2.5f;   // 원거리가 물러나기 시작하는 최소 거리(P3-⑥)

    /// <summary>원거리만 가진 몬스터에게 플레이어가 너무 가깝다 — 공격 거리 절반(최소 2.5 m) 안.</summary>
    private bool TooCloseForRanged(MonsterContext ctx)
        => ctx.Runtime.DistToPlayer < Mathf.Max(RangedKeepMin, GetEffectiveEngageDistance() * 0.5f);

    /// <summary>패턴이 전부 원거리 — 역할 AI에서 「거리를 지키는」 몬스터.</summary>
    private bool IsRangedOnly(MonsterContext ctx)
    {
        if (patterns == null || patterns.Count == 0 || ctx.Monster.Grade == MonsterGrade.Boss) return false;   // 보스는 제 패턴대로(DemonKing)
        for (int i = 0; i < patterns.Count; i++)
            if (SlotKindOf(ctx, i) != AttackSlotKind.Ranged) return false;
        return true;
    }

    private AttackSlotKind SlotKindOf(MonsterContext ctx, int index)
    {
        if (index < 0 || index >= patterns.Count) return AttackSlotKind.Melee;
        var shape = patterns[index].attackShapeOverride != null ? patterns[index].attackShapeOverride : ctx.Stat.attackShape;
        return shape is MonsterRangedAttackSO ? AttackSlotKind.Ranged : AttackSlotKind.Melee;
    }

    private int SelectPattern(MonsterContext ctx, PatternRuntime runtime)
    {
        float dist = ctx.Runtime.DistToPlayer;
        List<int> validIndices = null;
        List<int> highestPriorityIndices = null;
        int selectedPriority = int.MinValue;

        for (int i = 0; i < patterns.Count; i++)
        {
            var pattern = patterns[i];
            if (dist < pattern.minDistance || dist > pattern.maxDistance)
                continue;

            if (runtime.IsCoolingDown(i))
                continue;

            validIndices ??= new List<int>();
            validIndices.Add(i);

            if (selectionMode == PatternSelectionMode.HighestPriority)
            {
                if (pattern.priority > selectedPriority)
                {
                    selectedPriority = pattern.priority;
                    highestPriorityIndices ??= new List<int>();
                    highestPriorityIndices.Clear();
                    highestPriorityIndices.Add(i);
                }
                else if (pattern.priority == selectedPriority)
                {
                    highestPriorityIndices ??= new List<int>();
                    highestPriorityIndices.Add(i);
                }
            }
        }

        if (selectionMode == PatternSelectionMode.RandomAmongValid && validIndices != null && validIndices.Count > 0)
            return validIndices[UnityEngine.Random.Range(0, validIndices.Count)];

        if (selectionMode == PatternSelectionMode.HighestPriority &&
            highestPriorityIndices != null &&
            highestPriorityIndices.Count > 0)
        {
            return highestPriorityIndices[UnityEngine.Random.Range(0, highestPriorityIndices.Count)];
        }

        return -1;
    }

    private bool ShouldChaseForCloserPattern(MonsterContext ctx, PatternRuntime runtime)
    {
        float dist = ctx.Runtime.DistToPlayer;
        for (int i = 0; i < patterns.Count; i++)
        {
            if (runtime.IsCoolingDown(i))
                continue;

            if (dist > patterns[i].maxDistance)
                return true;
        }

        return false;
    }

    private float GetCloserPatternStoppingDistance(MonsterContext ctx, PatternRuntime runtime)
    {
        float dist = ctx.Runtime.DistToPlayer;
        float desired = GetEffectiveEngageDistance();
        float scale = Mathf.Clamp(engageDistanceScale, 0.5f, 1f);

        for (int i = 0; i < patterns.Count; i++)
        {
            if (runtime.IsCoolingDown(i))
                continue;

            var pattern = patterns[i];
            if (dist <= pattern.maxDistance)
                continue;

            desired = Mathf.Min(desired, pattern.maxDistance * scale);
        }

        return Mathf.Max(0.1f, desired);
    }

    private static void FacePlayer(MonsterContext ctx, float turnSpeed)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        var target = Quaternion.LookRotation(dir);
        ctx.Transform.rotation = Quaternion.Slerp(ctx.Transform.rotation, target, Time.deltaTime * turnSpeed);
    }

    private static void PlayAttackAnimation(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null)
            return;

        var animator = ctx.Animator;
        string key = stateName;
        if (string.IsNullOrEmpty(key))
            key = ctx.Animation.attackStateName;
        if (string.IsNullOrEmpty(key))
            return;

        animator.speed = 1f;
        float fadeDuration = Mathf.Max(0.08f, ctx.Animation.crossFadeDuration);

        if (animator.HasState(0, Animator.StringToHash(key)))
            animator.CrossFade(key, fadeDuration, 0, 0f);
    }

    private static void ExecutePatternAttack(MonsterContext ctx, AttackPattern pattern, Vector3 warningCenter, Vector3 origin, Vector3 forward)
    {
        int damage = Mathf.RoundToInt(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * pattern.damageMultiplier);
        float knockback = ctx.Stat.knockbackForce * pattern.knockbackMultiplier;
        var shape = pattern.attackShapeOverride != null ? pattern.attackShapeOverride : ctx.Stat.attackShape;

        // 비원거리(근접/콘/구체) 패턴은 히트 판정을 화면에 표시된 경고 그리드와 정확히 일치시킨다.
        // 그리드(=경고) 밖이면 맞지 않음 — 콘/구체 실제 형상으로 폴백해 경고 밖을 때리지 않도록 제거.
        // (원거리 발사체는 아래 shape.Execute 경로 유지.)
        if (!(shape is MonsterRangedAttackSO))
        {
            var gridShape = ResolveWarningShape(pattern, shape);
            bool hit = TryExecuteGridHit(ctx, gridShape, damage, knockback, origin, forward, pattern.slowScale, pattern.slowDuration);
            if (hit && ctx.Runtime?.PlayerTarget != null)
                SpawnPatternHitVfx(ctx, pattern, ctx.Runtime.PlayerTarget.position);
            return;
        }

        if (shape != null)
        {
            shape.Execute(ctx, damage, knockback);
            return;
        }

        if (ctx.Runtime?.PlayerTarget == null) return;

        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        if (dist > ctx.Monster.GetCombatHitDistance(ctx)) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        player.TakeDamage(damage, ctx.Transform.gameObject);
        if (pattern.slowDuration > 0f)
            player.ApplySlow(pattern.slowScale, pattern.slowDuration);

        Vector3 dir = (ctx.Runtime.PlayerTarget.position - ctx.Transform.position).normalized;
        dir.y = 0.3f;
        player.ApplyKnockback(dir.normalized * knockback);
        SpawnPatternHitVfx(ctx, pattern, ctx.Runtime.PlayerTarget.position);
    }

    private static Vector3 GetWarningCenterPosition(MonsterContext ctx, AttackPattern pattern)
    {
        var shape = pattern.attackShapeOverride != null ? pattern.attackShapeOverride : ctx.Stat.attackShape;
        if (shape is MonsterConeAttackSO cone && pattern.warningShape == AttackPattern.WarningShapeType.Auto)
        {
            float forwardOffset = Mathf.Clamp(cone.range * 0.55f, 0.4f, 2.2f);
            return ctx.Transform.position + ctx.Transform.forward * forwardOffset;
        }
        return ctx.Transform.position;
    }

    private static void ApplyVfxHierarchyScaling(GameObject go)
    {
        if (go == null) return;
        var systems = go.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }
    }

    private static void SpawnPatternHitVfx(MonsterContext ctx, AttackPattern pattern, Vector3 warningCenter)
    {
        // 패턴 전용 VFX가 있으면 사용, 없으면 config stat의 공통 VFX로 폴백
        var prefab = pattern.hitVfxPrefab != null ? pattern.hitVfxPrefab : ctx.Stat.hitVfxPrefab;
        if (prefab == null) return;

        float scale = pattern.hitVfxPrefab != null ? pattern.hitVfxScale : ctx.Stat.hitVfxScale;
        Vector3 offset = pattern.hitVfxPrefab != null ? pattern.hitVfxOffset : ctx.Stat.hitVfxOffset;
        Vector3 pos = warningCenter + offset;

        var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
        go.transform.localScale = Vector3.one * Mathf.Max(0.001f, scale);

        var systems = go.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }

        var ps = go.GetComponent<ParticleSystem>() ?? go.GetComponentInChildren<ParticleSystem>();
        float lifetime = ps != null ? ps.main.duration + ps.main.startLifetimeMultiplier + 0.3f : 3f;
        UnityEngine.Object.Destroy(go, lifetime);
    }

    private static bool TryExecuteGridHit(
        MonsterContext ctx,
        MonsterGroundWarning.GridShape shape,
        int damage,
        float knockback,
        Vector3 origin,
        Vector3 forward,
        float slowScale = 0f,
        float slowDuration = 0f)
    {
        if (ctx.Runtime?.PlayerTarget == null) return false;

        Transform target = ctx.Runtime.PlayerTarget;
        if (!IsInsideGrid(origin, forward, target.position, shape))   // 예고를 띄운 자리 · 방향(10-12 D2 — 판정 = 예고)
            return false;

        var player = target.GetComponent<PlayerController>();
        if (player == null) return false;

        player.TakeDamage(damage, ctx.Transform.gameObject);
        if (slowDuration > 0f)
            player.ApplySlow(slowScale, slowDuration);
        Vector3 dir = (target.position - ctx.Transform.position).normalized;
        dir.y = 0.3f;
        player.ApplyKnockback(dir.normalized * knockback);
        return true;
    }

    private static bool IsInsideGrid(
        Vector3 origin,
        Vector3 forward,
        Vector3 target,
        MonsterGroundWarning.GridShape shape)
    {
        const float cellSize = 1f;
        const float cellHalf = 0.5f;

        Vector3 planarForward = forward;
        planarForward.y = 0f;
        if (planarForward.sqrMagnitude < 0.001f)
            planarForward = Vector3.forward;
        planarForward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, planarForward).normalized;

        Vector3 toTarget = target - origin;
        toTarget.y = 0f;
        float tx = Vector3.Dot(toTarget, right);
        float tz = Vector3.Dot(toTarget, planarForward);

        Vector2Int[] cells = GetGridCells(shape);
        for (int i = 0; i < cells.Length; i++)
        {
            float cx = cells[i].x * cellSize;
            float cz = cells[i].y * cellSize;

            if (Mathf.Abs(tx - cx) <= cellHalf && Mathf.Abs(tz - cz) <= cellHalf)
                return true;
        }
        return false;
    }

    private static Vector2Int[] GetGridCells(MonsterGroundWarning.GridShape shape)
    {
        switch (shape)
        {
            case MonsterGroundWarning.GridShape.Front1:
                return new[] { new Vector2Int(0, 1) };
            case MonsterGroundWarning.GridShape.Front2:
                return new[] { new Vector2Int(0, 1), new Vector2Int(0, 2) };
            case MonsterGroundWarning.GridShape.FrontWide3:
                return new[] { new Vector2Int(-1, 1), new Vector2Int(0, 1), new Vector2Int(1, 1) };
            case MonsterGroundWarning.GridShape.Around8:
                return new[]
                {
                    new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
                    new Vector2Int(-1, 0), new Vector2Int(1, 0),
                    new Vector2Int(-1, 1), new Vector2Int(0, 1), new Vector2Int(1, 1),
                };
            default:
                return new[] { new Vector2Int(0, 1) };
        }
    }

    // 범위 예고 외곽선 색 — 따뜻한 흰빛(빨강 경고판은 보스 패턴 몫 · 일반 몬스터는 톤을 낮춘다, 10-11). 알파는 MonsterGroundWarning이 시간에 따라 올린다.
    /// <summary>범위 예고 외곽선 색(얇은 금빛) — 특수 상태(골렘 포효 · 비홀더 눈 빔)도 같은 색을 쓴다.</summary>
    public static readonly Color WarningOutlineColor = new Color(1f, 0.82f, 0.55f, 1f);

    /// <summary>
    /// 범위 예고 — 통일 규칙(10-12 몬스터 전수조사 §4 · <see cref="MonsterTelegraph"/>): 판정과 같은 칸의 외곽선 + 몬스터 쪽에서 차오르는 앞줄이
    /// 피해 순간에 끝에 닿는다. 원거리가 아닌 패턴은 판정이 늘 격자 칸(TryExecuteGridHit)이라 예고도 늘 격자다 —
    /// 예전엔 「원」 요청 패턴이 원을 그리고 격자로 때렸다. 원거리는 비행이 예고라 그리지 않는다(사용자 요청 유지).
    /// </summary>
    private static MonsterTelegraph SpawnPatternTelegraph(MonsterContext ctx, AttackPattern pattern, Vector3 origin, Vector3 forward, float secondsUntilHit)
    {
        if (pattern.disableWarning) return null;
        var shape = pattern.attackShapeOverride != null ? pattern.attackShapeOverride : ctx.Stat.attackShape;
        if (shape is MonsterRangedAttackSO) return null;
        Color color = ctx.Monster.Grade == MonsterGrade.Elite ? MonsterTelegraph.Heavy : MonsterTelegraph.Normal;
        return MonsterTelegraph.Grid(origin, forward, ResolveWarningShape(pattern, shape), Mathf.Max(0.05f, secondsUntilHit), color);
    }

    private static GameObject SpawnPatternVfx(MonsterContext ctx, AttackPattern pattern)
    {
        if (pattern.useProceduralBeamVfx)
            return SpawnProceduralBeamVfx(ctx, pattern);

        if (pattern.castVfxPrefab == null) return null;

        Vector3 origin = (pattern.castVfxAtPlayerPosition && ctx.Runtime?.PlayerTarget != null)
            ? ctx.Runtime.PlayerTarget.position
            : ctx.Transform.position
              + ctx.Transform.forward * pattern.castVfxForwardOffset
              + ctx.Transform.right * pattern.castVfxRightOffset;
        Vector3 pos = origin + Vector3.up * pattern.castVfxVerticalOffset;
        Quaternion rot = ctx.Transform.rotation * Quaternion.Euler(pattern.castVfxXRotationOffset, pattern.castVfxYRotationOffset, pattern.castVfxZRotationOffset);
        Transform parent = (pattern.castVfxAttachToMonster && !pattern.castVfxAtPlayerPosition) ? ctx.Transform : null;

        GameObject instance = null;
        try
        {
            instance = Instantiate(pattern.castVfxPrefab, pos, rot, parent);
        }
        catch (InvalidCastException)
        {
            // Some assets may serialize as non-GameObject main objects; fallback to Object instantiate.
            var obj = Instantiate((UnityEngine.Object)pattern.castVfxPrefab, pos, rot, parent);
            if (obj is GameObject go) instance = go;
            else if (obj is Component comp) instance = comp.gameObject;
        }

        if (instance == null) return null;

        float scale = pattern.castVfxScale > 0.001f ? pattern.castVfxScale : 1f;
        instance.transform.localScale *= scale;
        ApplyVfxHierarchyScaling(instance);

        var spawnedSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < spawnedSystems.Length; i++)
        {
            var m = spawnedSystems[i].main;
            m.loop = false;
        }

        ForcePlayVfx(instance);

        float life = pattern.castVfxDuration;
        if (life <= 0f)
            life = Mathf.Max(0.35f, pattern.attackDuration);
        Destroy(instance, life);
        return instance;
    }

    private static GameObject SpawnProceduralBeamVfx(MonsterContext ctx, AttackPattern pattern)
    {
        var go = new GameObject("[PatternBeamVfx]");
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.textureMode = LineTextureMode.Stretch;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.alignment = LineAlignment.View;
        line.numCapVertices = 4;
        line.startWidth = pattern.proceduralBeamWidth;
        line.endWidth = pattern.proceduralBeamWidth;
        line.startColor = pattern.proceduralBeamColor;
        line.endColor = pattern.proceduralBeamColor;

        var shader = Shader.Find("Sprites/Default");
        line.material = new Material(shader);

        var follower = go.AddComponent<BeamVfxFollower>();
        follower.Init(
            ctx.Transform,
            pattern.castVfxForwardOffset,
            pattern.castVfxVerticalOffset,
            pattern.proceduralBeamLength,
            line);

        float life = pattern.castVfxDuration;
        if (life <= 0f)
            life = Mathf.Max(0.35f, pattern.attackDuration);
        Destroy(go, life);
        return go;
    }

    private static void ForcePlayVfx(GameObject root)
    {
        if (root == null) return;
        if (!root.activeSelf) root.SetActive(true);

        var particleSystems = root.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            var ps = particleSystems[i];
            if (ps == null) continue;
            if (!ps.gameObject.activeSelf) ps.gameObject.SetActive(true);
            ps.Play(false);
        }

        var lines = root.GetComponentsInChildren<LineRenderer>(true);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line == null) continue;
            if (!line.gameObject.activeSelf) line.gameObject.SetActive(true);
            line.enabled = true;
        }

        var trails = root.GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trails.Length; i++)
        {
            var tr = trails[i];
            if (tr == null) continue;
            if (!tr.gameObject.activeSelf) tr.gameObject.SetActive(true);
            tr.enabled = true;
            tr.Clear();
        }
    }

    private static MonsterGroundWarning.GridShape ResolveWarningShape(AttackPattern pattern, MonsterAttackShapeSO shape)
    {
        switch (pattern.warningShape)
        {
            case AttackPattern.WarningShapeType.Front1:
                return MonsterGroundWarning.GridShape.Front1;
            case AttackPattern.WarningShapeType.Front2:
                return MonsterGroundWarning.GridShape.Front2;
            case AttackPattern.WarningShapeType.FrontWide3:
                return MonsterGroundWarning.GridShape.FrontWide3;
            case AttackPattern.WarningShapeType.Around8:
                return MonsterGroundWarning.GridShape.Around8;
        }

        if (shape is MonsterSphereAttackSO)
            return MonsterGroundWarning.GridShape.Around8;

        if (shape is MonsterConeAttackSO cone)
        {
            if (cone.halfAngle >= 50f)
                return MonsterGroundWarning.GridShape.FrontWide3;

            if (cone.range >= 1.8f)
                return MonsterGroundWarning.GridShape.Front2;

            return MonsterGroundWarning.GridShape.Front1;
        }

        return MonsterGroundWarning.GridShape.Front1;
    }

    private sealed class PatternRuntime
    {
        private readonly float[] _cooldowns;
        public float LastAttackStart = -999f;   // 공격 시작 사이 최소 간격(P1-6)
        public int SelectedPatternIndex { get; set; } = -1;

        public PatternRuntime(int patternCount)
        {
            _cooldowns = new float[Mathf.Max(0, patternCount)];
        }

        public void Reset()
        {
            SelectedPatternIndex = -1;
            for (int i = 0; i < _cooldowns.Length; i++)
                _cooldowns[i] = 0f;
        }

        public void Tick(float deltaTime)
        {
            for (int i = 0; i < _cooldowns.Length; i++)
                _cooldowns[i] = Mathf.Max(0f, _cooldowns[i] - deltaTime);
        }

        public bool IsCoolingDown(int index)
            => index >= 0 && index < _cooldowns.Length && _cooldowns[index] > 0f;

        public void StartCooldown(int index, float duration)
        {
            if (index < 0 || index >= _cooldowns.Length) return;
            _cooldowns[index] = Mathf.Max(0f, duration);
        }
    }

    private sealed class PatternChaseState : ChaseState
    {
        private const float OrbitRadiusMin = 3f;     // 칸을 기다리는 몬스터가 도는 거리(공격 거리 + 1 m보다 작으면 이것)
        private const float OrbitStepDeg   = 35f;    // 한 번에 옆으로 도는 각
        private const float OrbitRetarget  = 0.6f;
        private const float RetreatStep    = 2.5f;
        private const float FlankStepDeg   = 30f;    // 다가오는 각 나눔 한 칸(−60 ~ +60°)
        private int _rangedOnly = -1;               // −1 아직 모름 · 0 근접 섞임 · 1 원거리만

        private readonly PatternAttackOverrideSO _owner;
        private readonly PatternRuntime _runtime;
        private float _orbitTimer;
        private int   _orbitSide;
        private float _waitUntil;            // 칸을 기다리는 중(이 시각까지) — 도는 거리에서도 계속 묻는다
        private bool  _walking;
        private float _runSpeed;
        private AttackSlotKind _waitKind;

        public PatternChaseState(PatternAttackOverrideSO owner, PatternRuntime runtime)
        {
            _owner = owner;
            _runtime = runtime;
        }

        public override void Enter(MonsterContext ctx)
        {
            base.Enter(ctx);
            _walking = false;
            ctx.Agent.stoppingDistance = _owner.GetCloserPatternStoppingDistance(ctx, _runtime);
        }

        public override void Exit(MonsterContext ctx)
        {
            Run(ctx);
            base.Exit(ctx);
        }

        // 둘레 돌기 · 물러나기는 걷는 속도 — 옆 · 뒤 걷기 동작과 보폭이 맞게(10-14 정예 애니 점검). 다가갈 땐 들어올 때의 달리는 속도로
        private void Walk(MonsterContext ctx)
        {
            if (_walking) return;
            _walking  = true;
            _runSpeed = ctx.Agent.speed;
            float walk = ctx.Patrol != null && ctx.Patrol.patrolSpeed > 0f ? ctx.Patrol.patrolSpeed : ctx.Stat.moveSpeed * 0.5f;
            ctx.Agent.speed = Mathf.Min(_runSpeed, walk * ctx.Runtime.SpeedMultiplier);
        }

        private void Run(MonsterContext ctx)
        {
            if (!_walking) return;
            _walking = false;
            ctx.Agent.speed = _runSpeed;
        }

        public override void Update(MonsterContext ctx)
        {
            _runtime.Tick(Time.deltaTime);

            if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
            {
                ctx.Monster.ChangeState<PatrolState>();
                return;
            }

            // 칸을 기다리는 중 — 도는 거리에서도 계속 물어 줄 자리를 지킨다. 잡으면 평소처럼 다가가 공격한다.
            if (Time.time < _waitUntil && ctx.Runtime.DistToPlayer <= OrbitRadius() + 1.5f)
            {
                if (!MonsterAttackSlots.TryAcquire(ctx.Monster, _waitKind)) { Wait(ctx, _waitKind); return; }
                _waitUntil = 0f;
            }

            // 역할 AI(P3-⑥) — 원거리만 가진 몬스터는 플레이어가 공격 거리 절반(최소 2.5 m) 안으로 들어오면 물러난다(공격 판단보다 먼저)
            if (_rangedOnly < 0) _rangedOnly = _owner.IsRangedOnly(ctx) ? 1 : 0;
            if (_rangedOnly == 1 && _owner.TooCloseForRanged(ctx))
            {
                MonsterAttackSlots.Release(ctx.Monster);
                Retreat(ctx);
                return;
            }

            if (ctx.Runtime.DistToPlayer <= _owner.GetEffectiveEngageDistance())
            {
                int selected = _owner.SelectPattern(ctx, _runtime);
                if (selected >= 0)
                {
                    // 동시 공격 상한(10-11 P2-②) — 칸을 못 잡으면 둘레를 돌며 기다린다
                    if (!MonsterAttackSlots.TryAcquire(ctx.Monster, _owner.SlotKindOf(ctx, selected))) { Wait(ctx, _owner.SlotKindOf(ctx, selected)); return; }
                    _runtime.SelectedPatternIndex = selected;
                    ctx.Monster.ChangeState<AttackReadyState>();
                    return;
                }

                if (!_owner.ShouldChaseForCloserPattern(ctx, _runtime))
                {
                    if (!MonsterAttackSlots.TryAcquire(ctx.Monster, AttackSlotKind.Melee)) { Wait(ctx, AttackSlotKind.Melee); return; }
                    ctx.Monster.ChangeState<AttackReadyState>();
                    return;
                }
            }

            if (ctx.Monster.ShouldGiveUpChase(ctx))
            {
                ctx.Monster.ChangeState<PatrolState>();
                return;
            }

            ctx.Agent.stoppingDistance = _owner.GetCloserPatternStoppingDistance(ctx, _runtime);

            Vector3 targetPos = ctx.Runtime.PlayerTarget.position;
            // 근접은 다가오는 각을 몬스터마다 나눈다(−60 ~ +60°) — 한 줄로 따라붙지 않고 둘러싼다. 가까워지면(공격 거리 + 1.5 m) 곧장
            if (_rangedOnly == 0 && ctx.Monster.Grade != MonsterGrade.Boss && ctx.Runtime.DistToPlayer > _owner.GetEffectiveEngageDistance() + 1.5f)
            {
                Vector3 from = ctx.Transform.position - targetPos; from.y = 0f;
                if (from.sqrMagnitude > 0.01f)
                {
                    float side = ((ctx.Monster.GetInstanceID() & 0x7fffffff) % 5 - 2) * FlankStepDeg;
                    Vector3 slot = targetPos + Quaternion.Euler(0f, side, 0f) * from.normalized * Mathf.Max(1f, _owner.GetEffectiveEngageDistance());
                    if (UnityEngine.AI.NavMesh.SamplePosition(slot, out var nh, 1.5f, ctx.Agent.areaMask)) targetPos = nh.position;
                }
            }
            Run(ctx);
            ctx.Agent.SetDestination(targetPos);
            FaceTarget(ctx);
            KeepChaseAnimation(ctx);

            if (!string.IsNullOrEmpty(ctx.Animation.speedParam) && ctx.Animator != null)
            {
                ctx.Animator.SetFloat(
                    ctx.Animation.speedParam,
                    ctx.Agent.velocity.magnitude,
                    ctx.Animation.speedDampTime,
                    Time.deltaTime);
            }
        }

        /// <summary>
        /// 공격 칸을 기다린다 — 플레이어를 보며 둘레(공격 거리 + 1 m, 최소 3 m)를 한쪽으로 돈다. 멈춰 서서 구경하지 않게.
        /// 갈 곳이 내비메시 밖이면 도는 방향을 바꾼다.
        /// </summary>
        private float OrbitRadius() => Mathf.Max(OrbitRadiusMin, _owner.GetEffectiveEngageDistance() + 1f);

        /// <summary>원거리 몬스터가 플레이어에게서 물러난다 — 플레이어를 보며 뒤로(내비메시 밖이면 옆으로).</summary>
        private void Retreat(MonsterContext ctx)
        {
            Vector3 player = ctx.Runtime.PlayerTarget.position;
            Vector3 away = ctx.Transform.position - player; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -ctx.Transform.forward;
            Vector3 dest = ctx.Transform.position + away.normalized * RetreatStep;
            if (!UnityEngine.AI.NavMesh.SamplePosition(dest, out var hit, 1.5f, ctx.Agent.areaMask))
            {
                dest = ctx.Transform.position + Quaternion.Euler(0f, 90f, 0f) * away.normalized * RetreatStep;
                if (!UnityEngine.AI.NavMesh.SamplePosition(dest, out hit, 1.5f, ctx.Agent.areaMask)) { FaceTarget(ctx); return; }
            }
            ctx.Agent.stoppingDistance = 0.2f;
            Walk(ctx);
            ctx.Agent.SetDestination(hit.position);
            FaceTarget(ctx);
            KeepMoveAnimation(ctx);
            if (!string.IsNullOrEmpty(ctx.Animation.speedParam) && ctx.Animator != null)
                ctx.Animator.SetFloat(ctx.Animation.speedParam, ctx.Agent.velocity.magnitude, ctx.Animation.speedDampTime, Time.deltaTime);
        }

        private void Wait(MonsterContext ctx, AttackSlotKind kind)
        {
            _waitUntil = Time.time + 0.5f;
            _waitKind  = kind;
            Orbit(ctx);
        }

        private void Orbit(MonsterContext ctx)
        {
            Vector3 player = ctx.Runtime.PlayerTarget.position;
            Vector3 away = ctx.Transform.position - player; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = ctx.Transform.forward;
            float radius = OrbitRadius();
            if (_orbitSide == 0) _orbitSide = (ctx.Monster.GetInstanceID() & 1) == 0 ? 1 : -1;

            _orbitTimer -= Time.deltaTime;
            if (_orbitTimer <= 0f || (!ctx.Agent.pathPending && ctx.Agent.remainingDistance < 0.3f))
            {
                _orbitTimer = OrbitRetarget;
                Vector3 dir = Quaternion.Euler(0f, _orbitSide * OrbitStepDeg, 0f) * away.normalized;
                if (UnityEngine.AI.NavMesh.SamplePosition(player + dir * radius, out var hit, 1.5f, ctx.Agent.areaMask))
                    ctx.Agent.SetDestination(hit.position);
                else
                    _orbitSide = -_orbitSide;
            }
            ctx.Agent.stoppingDistance = 0.2f;
            Walk(ctx);
            FaceTarget(ctx);
            KeepMoveAnimation(ctx);
            if (!string.IsNullOrEmpty(ctx.Animation.speedParam) && ctx.Animator != null)
                ctx.Animator.SetFloat(ctx.Animation.speedParam, ctx.Agent.velocity.magnitude, ctx.Animation.speedDampTime, Time.deltaTime);
        }
    }

    private sealed class PatternAttackReadyState : AttackReadyState
    {
        private readonly PatternAttackOverrideSO _owner;
        private readonly PatternRuntime _runtime;
        private int _rangedOnly = -1;   // 역할 AI(P3-⑥)

        public PatternAttackReadyState(PatternAttackOverrideSO owner, PatternRuntime runtime)
        {
            _owner = owner;
            _runtime = runtime;
        }

        public override void Enter(MonsterContext ctx)
        {
            _runtime.Tick(0f);
            _runtime.SelectedPatternIndex = _owner.SelectPattern(ctx, _runtime);
            base.Enter(ctx);
        }

        public override void Update(MonsterContext ctx)
        {
            _runtime.Tick(Time.deltaTime);

            if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
            {
                ctx.Monster.ChangeState<PatrolState>();
                return;
            }

            if (ctx.Runtime.DistToPlayer > _owner.GetEffectiveEngageDistance() * 1.15f)
            {
                ctx.Monster.ChangeState<ChaseState>();
                return;
            }
            if (_rangedOnly < 0) _rangedOnly = _owner.IsRangedOnly(ctx) ? 1 : 0;
            if (_rangedOnly == 1 && _owner.TooCloseForRanged(ctx))   // 역할 AI(P3-⑥) — 붙으면 쏘지 않고 물러난다
            {
                ctx.Monster.ChangeState<ChaseState>();
                return;
            }

            _runtime.SelectedPatternIndex = _owner.SelectPattern(ctx, _runtime);
            if (_runtime.SelectedPatternIndex < 0)
            {
                if (_owner.ShouldChaseForCloserPattern(ctx, _runtime))
                    ctx.Monster.ChangeState<ChaseState>();
                else
                    PatternAttackOverrideSO.FacePlayer(ctx, 15f);
                return;
            }
            PatternAttackOverrideSO.FacePlayer(ctx, 15f);

            float speed = Mathf.Max(0.1f, ctx.Monster.AttackSpeedMultiplier);   // 공격 속도 배율(10-11 P2-③)
            // 공격 주기(P1-6) — 지난 공격 시작부터 최소 간격이 안 됐으면 준비 시계를 멈춘다(반짝임은 시계가 다시 돌 때 제때 나온다)
            if ((Time.time - _runtime.LastAttackStart) * speed + ctx.Runtime.StateTimer < MinCycleOf(ctx.Monster.Grade)) return;
            ctx.Runtime.StateTimer -= Time.deltaTime * speed;
            ctx.Monster.NotifyAttackIncoming((ctx.Runtime.StateTimer + HitDelay(ctx)) / speed);
            if (ctx.Runtime.StateTimer <= 0f)
                ctx.Monster.ChangeState<AttackState>();
        }

        /// <summary>고른 패턴이 공격 상태에 들어가서 맞히기까지의 초(PatternAttackState의 _damageTimer와 같다).</summary>
        private float HitDelay(MonsterContext ctx)
        {
            int i = _runtime.SelectedPatternIndex;
            float d = i >= 0 && i < _owner.patterns.Count ? _owner.patterns[i].damageDelay : -1f;
            return d >= 0f ? d : ctx.Combat.damageApplyDelay;
        }
    }

    private sealed class PatternAttackState : IMonsterState
    {
        private readonly PatternAttackOverrideSO _owner;
        private readonly PatternRuntime _runtime;
        private float _cooldownTimer;
        private float _damageTimer;
        private bool _damageDealt;
        private AttackPattern _pattern;
        private GameObject _castVfxInstance;
        private Vector3 _cachedWarningCenter;
        private bool _waitForAnimFinish;
        private string _currentAnimState;
        private bool _animFirstCycleDone;
        private float _castVfxDelayTimer;
        private bool _castVfxSpawned;
        private float _speed = 1f;   // 공격 속도 배율(10-11 P2-③) — 들어갈 때 잡아 공격 하나 동안 고정
        private MonsterTelegraph _telegraph;          // 이 공격의 범위 예고(끊기면 지운다)
        private Vector3 _lockOrigin, _lockForward;   // 예고를 띄운 순간의 자리 · 방향 — 격자 판정도 여기서(D2)
        private AttackSlotKind _slotKind;   // 이 공격이 쓰는 동시 공격 칸(10-11 P2-②)
        private bool _big;                  // 정예 큰 공격(10-13)

        public PatternAttackState(PatternAttackOverrideSO owner, PatternRuntime runtime)
        {
            _owner = owner;
            _runtime = runtime;
        }

        public void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;   // 추적 속도가 남아 공격 동작 중 미끄러졌다(10-14 정예 애니 점검 · 사이클롭스 0.65초)
            _runtime.Tick(0f);

            int index = _runtime.SelectedPatternIndex >= 0
                ? _runtime.SelectedPatternIndex
                : _owner.SelectPattern(ctx, _runtime);
            if (index < 0 || index >= _owner.patterns.Count)
            {
                ctx.Monster.ChangeState<ChaseState>();
                return;
            }

            _pattern = _owner.patterns[index];
            _slotKind = _owner.SlotKindOf(ctx, index);
            MonsterAttackSlots.TryAcquire(ctx.Monster, _slotKind);   // 다른 길(강제 전환 · 준비에서 바로)로 들어와도 칸에 센다
            _big = _owner.IsEliteBig(ctx, index);
            _runtime.StartCooldown(index, _big ? Mathf.Max(_pattern.cooldown, EliteBigCooldown) : _pattern.cooldown);
            _runtime.LastAttackStart = Time.time;
            if (_big) MonsterAttackSlots.BeginExclusive(ctx.Monster);   // 정예 큰 공격 동안 다른 근접은 새로 못 들어온다

            _cooldownTimer = _pattern.attackDuration > 0f
                ? _pattern.attackDuration
                : 1f / Mathf.Max(0.01f, ctx.Stat.attackRate);
            _damageTimer = _pattern.damageDelay >= 0f ? _pattern.damageDelay : ctx.Combat.damageApplyDelay;
            _damageDealt = false;

            ctx.Runtime.AttackHitDealt = false;
            PatternAttackOverrideSO.FacePlayer(ctx, 100f);

            // 경고장판 위치를 Enter 시점에 고정 캐싱 — 이후 몬스터 회전과 무관하게 유지
            _cachedWarningCenter = GetWarningCenterPosition(ctx, _pattern);

            float vfxDelay = _pattern.castVfxDelay > 0f ? _pattern.castVfxDelay
                : (_pattern.castVfxLeadTime > 0f && _pattern.damageDelay >= 0f
                    ? Mathf.Max(0f, _pattern.damageDelay - _pattern.castVfxLeadTime)
                    : 0f);
            if (vfxDelay > 0f)
            {
                _castVfxDelayTimer = vfxDelay;
                _castVfxSpawned = false;
            }
            else
            {
                _castVfxInstance = SpawnPatternVfx(ctx, _pattern);
                _castVfxSpawned = true;
            }

            string animState = string.IsNullOrEmpty(_pattern.animationStateName)
                ? ctx.Animation.attackStateName
                : _pattern.animationStateName;
            PlayAttackAnimation(ctx, animState);
            if (ctx.Monster.Grade == MonsterGrade.Elite) MonsterRootXZLock.Begin(ctx.Monster);   // 몸 = 판정 자리(10-14 정예 점검)
            _speed = Mathf.Max(0.1f, ctx.Monster.AttackSpeedMultiplier);
            if (ctx.Animator != null) ctx.Animator.speed = _speed;   // PlayAttackAnimation이 1로 둔 뒤 — 애니도 같은 배율
            // 예고는 배율을 잡은 뒤에 — 예전엔 배율 전 시간으로 그려 빨라진 공격과 앞줄 박자가 어긋났다
            _lockOrigin  = ctx.Transform.position;
            _lockForward = ctx.Transform.forward;
            _telegraph   = SpawnPatternTelegraph(ctx, _pattern, _lockOrigin, _lockForward, _damageTimer / _speed);
            _currentAnimState = animState;
            _waitForAnimFinish = ctx.Animator != null
                && !string.IsNullOrEmpty(animState)
                && ctx.Animator.HasState(0, Animator.StringToHash(animState));
            _animFirstCycleDone = false;

            // 근접 windup(예고) 노출 — 데미지 지연 동안 아이템 저스트가드/섬광 판정용(공용 AttackState와 동일).
            // 투사체(원거리)는 비행이 예고이므로 제외.
            var telegraphShape = _pattern.attackShapeOverride != null ? _pattern.attackShapeOverride : ctx.Stat.attackShape;
            if (!(telegraphShape is MonsterRangedAttackSO))
                ctx.Monster.BeginAttackTelegraph();
            ctx.Monster.NotifyAttackIncoming(_damageTimer / _speed);
        }

        public void Update(MonsterContext ctx)
        {
            _runtime.Tick(Time.deltaTime);
            float dt = Time.deltaTime * _speed;   // 공격 시간표(이펙트 · 피해 · 쿨다운)는 배율만큼 빨리

            if (!_castVfxSpawned)
            {
                _castVfxDelayTimer -= dt;
                if (_castVfxDelayTimer <= 0f)
                {
                    _castVfxInstance = SpawnPatternVfx(ctx, _pattern);
                    _castVfxSpawned = true;
                }
            }

            // 피해는 시전 이펙트와 따로 damageDelay에 넣는다. 이펙트가 나오는 순간 피해를 함께 넣었더니 castVfxDelay 0.1인 7종이
            // 휘두르기도 전(시작 0.1초)에 맞혔다(10-07 실측: 휘두름 정점 0.26~0.73초 · 사용자 「언제 공격하는지 전혀 알 수 없다」).
            // 이펙트는 피해보다 castVfxLeadTime만큼 먼저 나온다(Enter의 vfxDelay).
            if (!_damageDealt)
            {
                _damageTimer -= dt;
                ctx.Monster.NotifyAttackIncoming(_damageTimer / _speed);
                if (_damageTimer <= 0f)
                {
                    _damageDealt = true;
                    ctx.Runtime.AttackHitDealt = true;
                    bool canceled = ctx.Monster.ConsumeAttackCancel();
                    ctx.Monster.EndAttackTelegraph();
                    if (canceled && _telegraph != null) _telegraph.Cancel();
                    _telegraph = null;   // 맞는 순간까지 그렸다 — 스스로 사라진다
                    if (!canceled)
                        ExecutePatternAttack(ctx, _pattern, _cachedWarningCenter, _lockOrigin, _lockForward);
                }
            }

            // 첫 번째 사이클 92% 도달 시 idle로 복귀 → 루프 방지, cooldown은 계속 진행
            if (_waitForAnimFinish && !_animFirstCycleDone && ctx.Animator != null
                && !string.IsNullOrEmpty(_currentAnimState) && !ctx.Animator.IsInTransition(0))
            {
                int h = Animator.StringToHash(_currentAnimState);
                var s = ctx.Animator.GetCurrentAnimatorStateInfo(0);
                if ((s.shortNameHash == h || s.fullPathHash == h) && s.normalizedTime >= 0.92f)
                {
                    _animFirstCycleDone = true;
                    _waitForAnimFinish = false;
                    // 공격 뒤엔 전투 대기로 — 평상 대기로 섰다가 전투 대기로 튀었다 · 상자는 닫힌 상자 자세로 돌아갔다(10-14 정예 애니 점검)
                    string back = !string.IsNullOrEmpty(ctx.Animation.attackReadyStateName)
                                  && ctx.Animator.HasState(0, Animator.StringToHash(ctx.Animation.attackReadyStateName))
                        ? ctx.Animation.attackReadyStateName : ctx.Animation.idleStateName;
                    if (!string.IsNullOrEmpty(back))
                        ctx.Animator.CrossFade(back, 0.15f, 0);
                }
            }

            _cooldownTimer -= dt;
            if (_cooldownTimer > 0f) return;

            if (_waitForAnimFinish && !IsAnimNearlyFinished(ctx))
                return;

            if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
            {
                ctx.Monster.ChangeState<PatrolState>();
                return;
            }

            // 동시 공격 상한 — 공격 하나가 끝나면 칸을 놓고 줄 맨 뒤에 다시 선다(기다리던 몬스터가 먼저 · 10-11 P2-②)
            MonsterAttackSlots.Release(ctx.Monster);
            if (ctx.Runtime.DistToPlayer <= _owner.GetEngageDistance()
                && MonsterAttackSlots.TryAcquire(ctx.Monster, _slotKind))
                ctx.Monster.ChangeState<AttackReadyState>();
            else
                ctx.Monster.ChangeState<ChaseState>();
        }

        public void Exit(MonsterContext ctx)
        {
            MonsterRootXZLock.End(ctx.Monster);
            if (ctx.Animator != null && !Mathf.Approximately(_speed, 1f)) ctx.Animator.speed = 1f;
            _speed = 1f;
            if (_big) MonsterAttackSlots.EndExclusive(ctx.Monster);
            _big = false;
            ctx.Monster.EndAttackTelegraph();
            ctx.Monster.ResetAttackGlint();
            if (_telegraph != null) _telegraph.Cancel();   // 피해 전에 끊겼다(넉백 · 포이즈 · 사망) — 헛예고를 지운다
            _telegraph = null;
            if (_castVfxInstance != null)
            {
                // 부모에서 분리만 하고 강제 삭제하지 않음 — SpawnPatternVfx의 Destroy(life)로 자연 수명 만료
                // (플레이어가 멀어져 상태가 일찍 종료되어도 이펙트가 중단되지 않음)
                _castVfxInstance.transform.SetParent(null, true);
                _castVfxInstance = null;
            }
            _animFirstCycleDone = false;
            _runtime.SelectedPatternIndex = -1;
        }

        private bool IsAnimNearlyFinished(MonsterContext ctx)
        {
            if (ctx.Animator == null || string.IsNullOrEmpty(_currentAnimState)) return true;
            int hash = Animator.StringToHash(_currentAnimState);
            if (!ctx.Animator.HasState(0, hash)) return true;
            var state = ctx.Animator.GetCurrentAnimatorStateInfo(0);
            if (state.shortNameHash != hash && state.fullPathHash != hash) return true;
            return state.normalizedTime >= 0.92f;
        }
    }

    private sealed class BeamVfxFollower : MonoBehaviour
    {
        private Transform _origin;
        private float _forwardOffset;
        private float _verticalOffset;
        private float _length;
        private LineRenderer _line;

        public void Init(
            Transform origin,
            float forwardOffset,
            float verticalOffset,
            float length,
            LineRenderer line)
        {
            _origin = origin;
            _forwardOffset = forwardOffset;
            _verticalOffset = verticalOffset;
            _length = Mathf.Max(0.25f, length);
            _line = line;
            Refresh();
        }

        private void Update() => Refresh();

        private void Refresh()
        {
            if (_origin == null || _line == null) return;

            Vector3 start =
                _origin.position
                + _origin.forward * _forwardOffset
                + Vector3.up * _verticalOffset;
            Vector3 end = start + _origin.forward * _length;
            _line.SetPosition(0, start);
            _line.SetPosition(1, end);
        }
    }
}
}
