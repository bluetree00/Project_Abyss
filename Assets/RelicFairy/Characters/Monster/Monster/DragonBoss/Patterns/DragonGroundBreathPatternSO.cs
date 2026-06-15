using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 지상 브레스 패턴.
/// 준비(경고+서서히 조준) → 브레스 발사(플레이어 추적 회전) → ChaseState 복귀
/// </summary>
[CreateAssetMenu(fileName = "DragonGroundBreathPattern",
    menuName = "RelicFairy/Boss/Dragon/GroundBreathPattern")]
public class DragonGroundBreathPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string _breathStartStateName = "UAttack FireBreath L";
    [SerializeField] private string _breathLoopStateName  = "UAttack FireBreath Loop";

    [Header("타이밍")]
    [SerializeField] private float _prepareDuration = 2f;
    [SerializeField] private float _breathDuration  = 5f;

    [Header("회전 속도 (도/초)")]
    [SerializeField] private float _rotateSpeedPrepare = 100f;
    [SerializeField] private float _rotateSpeedBreath  = 60f;

    [Header("브레스 데미지")]
    [SerializeField] private float _breathRange        = 12f;
    [SerializeField] private float _breathRadius       = 0.8f;
    [SerializeField] private int   _breathDamagePerSec = 20;

    [Header("이펙트")]
    [Tooltip("Style 2 - Flamethrower 프리팹")]
    [SerializeField] private GameObject _breathEffectPrefab;
    [Tooltip("준비 단계 경고 이펙트 (없으면 경고 생략)")]
    [SerializeField] private GameObject _warningEffectPrefab;

    [Header("속성")]
    [SerializeField] private Color                _breathColor  = new Color(1.0f, 0.35f, 0.1f);
    [SerializeField] private PlayerStatusEffectSO _statusEffect;

    [Header("EndPose (반격 창)")]
    [SerializeField] private float _endPoseDuration = 0.4f;

    public string BreathStartStateName  => _breathStartStateName;
    public string BreathLoopStateName   => _breathLoopStateName;
    public float  PrepareDuration       => _prepareDuration;
    public float  BreathDuration        => _breathDuration;
    public float  RotateSpeedPrepare    => _rotateSpeedPrepare;
    public float  RotateSpeedBreath     => _rotateSpeedBreath;
    public float  BreathRange           => _breathRange;
    public float  BreathRadius          => _breathRadius;
    public int    BreathDamagePerSec    => _breathDamagePerSec;
    public GameObject BreathEffectPrefab   => _breathEffectPrefab;
    public GameObject WarningEffectPrefab  => _warningEffectPrefab;
    public Color  BreathColor           => _breathColor;
    public PlayerStatusEffectSO StatusEffect => _statusEffect;
    public float  EndPoseDuration       => _endPoseDuration;

    private DragonGroundBreathState _runtimeState;

    public override void Initialize(BossPatternContext ctx)
        => _runtimeState = new DragonGroundBreathState(this);

    public override void OnRecycled() => _runtimeState?.Reset();

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.PlayerTarget != null
           && ctx.Blackboard is DragonBossBlackboard bb
           && bb.BodyState == BodyState.Grounded;

    public override SpecialStateBase GetRuntimeState() => _runtimeState;
}

// ────────────────────────────────────────────────────────────────────────────
// Runtime state
// ────────────────────────────────────────────────────────────────────────────

internal sealed class DragonGroundBreathState : FullLockState<DragonGroundBreathPatternSO>
{
    private enum Phase { Prepare, Breathing, EndPose, Done }

    private Phase      _phase;
    private float      _timer;
    private float      _damageTick;
    private Transform  _mouthBone;
    private GameObject _breathEffect;
    private GameObject _warningEffect;
    private GameObject _rangeIndicator;

    private const float DamageTick = 0.15f;

    internal DragonGroundBreathState(DragonGroundBreathPatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase = Phase.Done;
        DestroyEffect(ref _breathEffect);
        DestroyEffect(ref _warningEffect);
        DestroyEffect(ref _rangeIndicator);
    }

    // ── FSM ──────────────────────────────────────────────────────────────────

    public override void Enter(MonsterContext ctx)
    {
        _phase      = Phase.Prepare;
        _timer      = 0f;
        _damageTick = 0f;

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.isStopped = true;

        _mouthBone = FindBone(ctx.Transform, "Jaw");

        PlayAnim(ctx, Data.BreathStartStateName);
        SpawnWarning(ctx);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Prepare:   UpdatePrepare(ctx);   break;
            case Phase.Breathing: UpdateBreathing(ctx); break;
            case Phase.EndPose:   UpdateEndPose(ctx);   break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.isStopped = false;
        DestroyEffect(ref _breathEffect);
        DestroyEffect(ref _warningEffect);
        DestroyEffect(ref _rangeIndicator);
    }

    // ── Phase: Prepare ────────────────────────────────────────────────────────

    private void UpdatePrepare(MonsterContext ctx)
    {
        RotateToPlayer(ctx, Data.RotateSpeedPrepare);
        SyncEffect(_warningEffect, ctx);
        SyncRangeIndicator(ctx);

        if (_timer < Data.PrepareDuration) return;

        DestroyEffect(ref _warningEffect);
        _phase      = Phase.Breathing;
        _timer      = 0f;
        _damageTick = 0f;

        PlayAnim(ctx, Data.BreathLoopStateName);
        SpawnBreath(ctx);
        // 범위 경고장판은 브레스 중에도 유지 (색상만 완전 불투명으로 전환)
        SetRangeIndicatorAlpha(Data.BreathColor.r, Data.BreathColor.g, Data.BreathColor.b, 0.8f);
    }

    // ── Phase: Breathing ──────────────────────────────────────────────────────

    private void UpdateBreathing(MonsterContext ctx)
    {
        RotateToPlayer(ctx, Data.RotateSpeedBreath);
        SyncEffect(_breathEffect, ctx);
        SyncRangeIndicator(ctx);

        _damageTick += Time.deltaTime;
        if (_damageTick >= DamageTick)
        {
            _damageTick = 0f;
            ApplyDamage(ctx);
        }

        if (_timer >= Data.BreathDuration)
        {
            DestroyEffect(ref _breathEffect);
            DestroyEffect(ref _rangeIndicator);
            _phase = Phase.EndPose;
            _timer = 0f;
        }
    }

    // ── 이펙트 ────────────────────────────────────────────────────────────────

    private void SpawnWarning(MonsterContext ctx)
    {
        if (Data.WarningEffectPrefab != null)
        {
            _warningEffect = Object.Instantiate(Data.WarningEffectPrefab);
            SyncEffect(_warningEffect, ctx);
            TintEffect(_warningEffect, Data.BreathColor * 0.55f);
        }

        SpawnRangeIndicator(ctx);
    }

    private void SpawnBreath(MonsterContext ctx)
    {
        if (Data.BreathEffectPrefab == null) return;
        _breathEffect = Object.Instantiate(Data.BreathEffectPrefab);
        SyncEffect(_breathEffect, ctx);
        TintEffect(_breathEffect, Data.BreathColor);
    }

    private void SyncEffect(GameObject go, MonsterContext ctx)
    {
        if (go == null) return;
        go.transform.SetPositionAndRotation(GetMouthPos(ctx), ctx.Transform.rotation);
    }

    // ── 범위 경고장판 ──────────────────────────────────────────────────────────

    private void SpawnRangeIndicator(MonsterContext ctx)
    {
        _rangeIndicator = new GameObject("BreathRangeWarning");
        var lr = _rangeIndicator.AddComponent<LineRenderer>();

        lr.useWorldSpace     = true;
        lr.positionCount     = 2;
        lr.startWidth        = Data.BreathRadius * 2f;
        lr.endWidth          = Data.BreathRadius * 2f;
        lr.numCapVertices    = 4;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows    = false;

        var mat = new Material(Shader.Find("Sprites/Default"));
        Color c = Data.BreathColor;
        mat.color     = new Color(c.r, c.g, c.b, 0.55f);
        lr.material   = mat;
        lr.startColor = new Color(c.r, c.g, c.b, 0.6f);
        lr.endColor   = new Color(c.r, c.g, c.b, 0.15f);

        SyncRangeIndicator(ctx);
    }

    private void SyncRangeIndicator(MonsterContext ctx)
    {
        if (_rangeIndicator == null) return;
        var lr = _rangeIndicator.GetComponent<LineRenderer>();
        if (lr == null) return;

        Vector3 forward = ctx.Transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) return;
        forward.Normalize();

        Vector3 mouthPos = GetMouthPos(ctx);
        float groundY = DragonPatternFloorUtils.GetFloorY(mouthPos, ctx.Runtime.SpawnPosition.y) + 0.05f;
        Vector3 origin = new Vector3(mouthPos.x, groundY, mouthPos.z);

        lr.SetPosition(0, origin);
        lr.SetPosition(1, origin + forward * Data.BreathRange);
    }

    private void SetRangeIndicatorAlpha(float r, float g, float b, float a)
    {
        if (_rangeIndicator == null) return;
        var lr = _rangeIndicator.GetComponent<LineRenderer>();
        if (lr == null) return;
        lr.startColor = new Color(r, g, b, a);
        lr.endColor   = new Color(r, g, b, a * 0.4f);
        if (lr.material != null)
            lr.material.color = new Color(r, g, b, a);
    }

    // ── TintEffect ────────────────────────────────────────────────────────────

    private static void TintEffect(GameObject go, Color tint)
    {
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(tint);

            // Color over Lifetime 그라디언트의 색상 키를 tint로 교체, alpha는 유지
            var col = ps.colorOverLifetime;
            if (col.enabled)
                col.color = TintGradient(col.color, tint);
        }

        // Particles/Additive: output = 2 × _TintColor × particleColor × texture
        // 불꽃 텍스처에 blue 채널이 없어 얼음/번개 색이 묻힌다.
        // 텍스처를 white로 교체하면 순수하게 tint 색상만 표현된다.
        foreach (var rend in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
        {
            var mat = rend.material; // 이미 인스턴스
            mat.mainTexture = Texture2D.whiteTexture;
            // 셰이더가 2× 곱하므로 0.5 스케일로 입력해 적정 밝기 유지
            if (mat.HasProperty("_TintColor"))
                mat.SetColor("_TintColor", new Color(tint.r * 0.5f, tint.g * 0.5f, tint.b * 0.5f, tint.a * 0.5f));
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     tint);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
        }
    }

    private static ParticleSystem.MinMaxGradient TintGradient(
        ParticleSystem.MinMaxGradient src, Color tint)
    {
        switch (src.mode)
        {
            case ParticleSystemGradientMode.Gradient:
            {
                var newGrad = ReplaceGradientColors(src.gradient, tint);
                return new ParticleSystem.MinMaxGradient(newGrad);
            }
            case ParticleSystemGradientMode.TwoGradients:
            {
                var g1 = ReplaceGradientColors(src.gradientMin, tint);
                var g2 = ReplaceGradientColors(src.gradientMax, tint);
                return new ParticleSystem.MinMaxGradient(g1, g2);
            }
            case ParticleSystemGradientMode.Color:
                return new ParticleSystem.MinMaxGradient(tint);
            case ParticleSystemGradientMode.TwoColors:
                return new ParticleSystem.MinMaxGradient(tint * 0.6f, tint);
            default:
                return src;
        }
    }

    // 그라디언트 color key를 tint 색조로 교체하되 원본 밝기 비율 유지, alpha key는 보존
    private static Gradient ReplaceGradientColors(Gradient src, Color tint)
    {
        var colorKeys = src.colorKeys;
        for (int i = 0; i < colorKeys.Length; i++)
        {
            float brightness = Mathf.Max(colorKeys[i].color.maxColorComponent, 0.1f);
            colorKeys[i] = new GradientColorKey(tint * brightness, colorKeys[i].time);
        }
        var g = new Gradient();
        g.SetKeys(colorKeys, src.alphaKeys);
        return g;
    }

    private static void DestroyEffect(ref GameObject go)
    {
        if (go == null) return;
        Object.Destroy(go);
        go = null;
    }

    // ── 데미지 ────────────────────────────────────────────────────────────────

    private void UpdateEndPose(MonsterContext ctx)
    {
        if (_timer >= Data.EndPoseDuration)
        {
            _phase = Phase.Done;
            ctx.Monster.ChangeState<ChaseState>();
        }
    }

    private void ApplyDamage(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        float dmg = Data.BreathDamagePerSec * DamageTick;
        if (Physics.SphereCast(GetMouthPos(ctx), Data.BreathRadius,
                               ctx.Transform.forward, out var hit, Data.BreathRange))
        {
            var player = hit.collider.GetComponent<PlayerController>()
                      ?? hit.collider.GetComponentInParent<PlayerController>();
            if (player == null) return;
            player.TakeDamage(Mathf.RoundToInt(dmg));
            Data.StatusEffect?.Apply(player);
        }
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────────────

    private void RotateToPlayer(MonsterContext ctx, float speed)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        ctx.Transform.rotation = Quaternion.RotateTowards(
            ctx.Transform.rotation,
            Quaternion.LookRotation(dir),
            speed * Time.deltaTime);
    }

    private Vector3 GetMouthPos(MonsterContext ctx) =>
        _mouthBone != null
            ? _mouthBone.position
            : ctx.Transform.position + Vector3.up * 2f + ctx.Transform.forward * 0.5f;

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        int hash = Animator.StringToHash(stateName);
        if (ctx.Animator.HasState(0, hash))
            ctx.Animator.CrossFade(stateName, 0.1f, 0, 0f);
    }

    private static Transform FindBone(Transform root, string boneName)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == boneName) return t;
        return null;
    }
}
}
