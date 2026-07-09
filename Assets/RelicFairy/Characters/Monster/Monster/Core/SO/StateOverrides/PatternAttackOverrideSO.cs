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

    private static void ExecutePatternAttack(MonsterContext ctx, AttackPattern pattern, Vector3 warningCenter)
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
            bool hit = TryExecuteGridHit(ctx, gridShape, damage, knockback, pattern.slowScale, pattern.slowDuration);
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

        player.TakeDamage(damage);
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
        float slowScale = 0f,
        float slowDuration = 0f)
    {
        if (ctx.Runtime?.PlayerTarget == null) return false;

        Transform target = ctx.Runtime.PlayerTarget;
        if (!IsInsideGrid(ctx.Transform.position, ctx.Transform.forward, target.position, shape))
            return false;

        var player = target.GetComponent<PlayerController>();
        if (player == null) return false;

        player.TakeDamage(damage);
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

    private static void SpawnPatternWarning(MonsterContext ctx, AttackPattern pattern)
    {
        if (pattern.disableWarning) return;

        Vector3 warningPos = ctx.Transform.position;
        float warningDuration = pattern.warningDuration > 0f
            ? pattern.warningDuration
            : Mathf.Max(0.2f, pattern.damageDelay);
        float warningRadius = pattern.warningRadius;
        var shape = pattern.attackShapeOverride != null ? pattern.attackShapeOverride : ctx.Stat.attackShape;

        // User-requested: ranged attacks do not display ground warning.
        if (shape is MonsterRangedAttackSO)
            return;

        var warningShape = ResolveWarningShape(pattern, shape);

        if (warningShape != MonsterGroundWarning.GridShape.Front1 || pattern.warningShape != AttackPattern.WarningShapeType.Circle)
        {
            // Grid warning path (all shapes except explicit circle request).
            if (pattern.warningShape != AttackPattern.WarningShapeType.Circle)
            {
                MonsterGroundWarning.SpawnGrid(
                    warningPos,
                    ctx.Transform.forward,
                    warningShape,
                    warningDuration,
                    new Color(1f, 0.15f, 0.15f, 0.95f));
                return;
            }
        }

        // Cone attacks should warn in front of the monster, not around its center.
        if (shape is MonsterConeAttackSO cone)
        {
            float forwardOffset = Mathf.Clamp(cone.range * 0.55f, 0.4f, 2.2f);
            warningPos += ctx.Transform.forward * forwardOffset;

            if (warningRadius <= 0.01f)
                warningRadius = Mathf.Max(0.6f, cone.range * 0.55f);
        }

        MonsterGroundWarning.Spawn(
            warningPos,
            warningRadius,
            warningDuration,
            new Color(1f, 0.15f, 0.15f, 0.95f));
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
        private readonly PatternAttackOverrideSO _owner;
        private readonly PatternRuntime _runtime;

        public PatternChaseState(PatternAttackOverrideSO owner, PatternRuntime runtime)
        {
            _owner = owner;
            _runtime = runtime;
        }

        public override void Enter(MonsterContext ctx)
        {
            base.Enter(ctx);
            ctx.Agent.stoppingDistance = _owner.GetCloserPatternStoppingDistance(ctx, _runtime);
        }

        public override void Update(MonsterContext ctx)
        {
            _runtime.Tick(Time.deltaTime);

            if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
            {
                ctx.Monster.ChangeState<PatrolState>();
                return;
            }

            if (ctx.Runtime.DistToPlayer <= _owner.GetEffectiveEngageDistance())
            {
                int selected = _owner.SelectPattern(ctx, _runtime);
                if (selected >= 0)
                {
                    _runtime.SelectedPatternIndex = selected;
                    ctx.Monster.ChangeState<AttackReadyState>();
                    return;
                }

                if (!_owner.ShouldChaseForCloserPattern(ctx, _runtime))
                {
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
    }

    private sealed class PatternAttackReadyState : AttackReadyState
    {
        private readonly PatternAttackOverrideSO _owner;
        private readonly PatternRuntime _runtime;

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

            ctx.Runtime.StateTimer -= Time.deltaTime;
            if (ctx.Runtime.StateTimer <= 0f)
                ctx.Monster.ChangeState<AttackState>();
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

        public PatternAttackState(PatternAttackOverrideSO owner, PatternRuntime runtime)
        {
            _owner = owner;
            _runtime = runtime;
        }

        public void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();
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
            _runtime.StartCooldown(index, _pattern.cooldown);

            _cooldownTimer = _pattern.attackDuration > 0f
                ? _pattern.attackDuration
                : 1f / Mathf.Max(0.01f, ctx.Stat.attackRate);
            _damageTimer = _pattern.damageDelay >= 0f ? _pattern.damageDelay : ctx.Combat.damageApplyDelay;
            _damageDealt = false;

            ctx.Runtime.AttackHitDealt = false;
            PatternAttackOverrideSO.FacePlayer(ctx, 100f);

            // 경고장판 위치를 Enter 시점에 고정 캐싱 — 이후 몬스터 회전과 무관하게 유지
            _cachedWarningCenter = GetWarningCenterPosition(ctx, _pattern);

            SpawnPatternWarning(ctx, _pattern);
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
        }

        public void Update(MonsterContext ctx)
        {
            _runtime.Tick(Time.deltaTime);

            if (!_castVfxSpawned)
            {
                _castVfxDelayTimer -= Time.deltaTime;
                if (_castVfxDelayTimer <= 0f)
                {
                    _castVfxInstance = SpawnPatternVfx(ctx, _pattern);
                    _castVfxSpawned = true;
                    if (!_damageDealt)
                    {
                        _damageDealt = true;
                        ctx.Runtime.AttackHitDealt = true;
                        bool canceled = ctx.Monster.ConsumeAttackCancel();
                        ctx.Monster.EndAttackTelegraph();
                        if (!canceled)
                            ExecutePatternAttack(ctx, _pattern, _cachedWarningCenter);
                    }
                }
            }
            else if (!_damageDealt)
            {
                // castVfxDelay == 0 이어서 Enter에서 즉시 스폰된 경우 타이머 폴백
                _damageTimer -= Time.deltaTime;
                if (_damageTimer <= 0f)
                {
                    _damageDealt = true;
                    ctx.Runtime.AttackHitDealt = true;
                    bool canceled = ctx.Monster.ConsumeAttackCancel();
                    ctx.Monster.EndAttackTelegraph();
                    if (!canceled)
                        ExecutePatternAttack(ctx, _pattern, _cachedWarningCenter);
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
                    if (!string.IsNullOrEmpty(ctx.Animation.idleStateName))
                        ctx.Animator.CrossFade(ctx.Animation.idleStateName, 0.15f, 0);
                }
            }

            _cooldownTimer -= Time.deltaTime;
            if (_cooldownTimer > 0f) return;

            if (_waitForAnimFinish && !IsAnimNearlyFinished(ctx))
                return;

            if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
            {
                ctx.Monster.ChangeState<PatrolState>();
                return;
            }

            if (ctx.Runtime.DistToPlayer <= _owner.GetEngageDistance())
                ctx.Monster.ChangeState<AttackReadyState>();
            else
                ctx.Monster.ChangeState<ChaseState>();
        }

        public void Exit(MonsterContext ctx)
        {
            ctx.Monster.EndAttackTelegraph();
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
