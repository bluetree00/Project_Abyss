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

    [Header("Back 모션 (브레스 시작 전 후퇴)")]
    [SerializeField] private string _backStateName    = "Back";
    [SerializeField] private float  _backDuration     = 0.35f;
    [SerializeField] private float  _backStepDistance = 1f;

    [Header("타이밍")]
    [SerializeField] private float _prepareDuration = 2f;
    [SerializeField] private float _breathDuration  = 5f;

    [Header("회전 속도 (도/초)")]
    [SerializeField] private float _rotateSpeedPrepare = 100f;
    [SerializeField] private float _rotateSpeedBreath  = 60f;

    [Header("브레스 데미지")]
    [Tooltip("사거리 최대 한도. 맵 경계가 이보다 가까우면 경계까지만 닿는다 — 맵 끝까지 닿게 하려면 맵 크기보다 크게 설정")]
    [SerializeField] private float _breathRange        = 24f;
    [SerializeField] private float _breathRadius       = 0.8f;
    [SerializeField] private int   _breathDamagePerSec = 20;

    [Header("사거리 점진 확장 (브레스 진행에 따라 맵 끝까지 늘어남)")]
    [Tooltip("브레스 시작 시점 사거리 = 전체 사거리(맵 경계까지) * 이 비율")]
    [SerializeField] private float _rangeStartRatio   = 0.3f;
    [Tooltip("시작 비율에서 전체 사거리까지 늘어나는 데 걸리는 시간(초)")]
    [SerializeField] private float _rangeGrowDuration = 2f;
    [Tooltip("브레스 VFX 원본 startSpeed(배율 1)로 시각적으로 도달하는 기준 길이(m) — 에디터에서 실측 후 조정")]
    [SerializeField] private float _vfxReferenceLength = 8f;

    [Header("이펙트")]
    [Tooltip("Style 2 - Flamethrower 프리팹")]
    [SerializeField] private GameObject _breathEffectPrefab;
    [Tooltip("준비 단계 경고 이펙트 (없으면 경고 생략)")]
    [SerializeField] private GameObject _warningEffectPrefab;
    [Tooltip("브레스 발사 시작 시 재생할 사운드")]
    [SerializeField] private AudioClip _breathSfx;

    [Header("속성")]
    [SerializeField] private Color                _breathColor  = new Color(1.0f, 0.35f, 0.1f);
    [SerializeField] private PlayerStatusEffectSO _statusEffect;

    [Header("EndPose (반격 창)")]
    [SerializeField] private float _endPoseDuration = 0.4f;

    public string BreathStartStateName  => _breathStartStateName;
    public string BreathLoopStateName   => _breathLoopStateName;
    public string BackStateName         => _backStateName;
    public float  BackDuration          => _backDuration;
    public float  BackStepDistance      => _backStepDistance;
    public float  PrepareDuration       => _prepareDuration;
    public float  BreathDuration        => _breathDuration;
    public float  RotateSpeedPrepare    => _rotateSpeedPrepare;
    public float  RotateSpeedBreath     => _rotateSpeedBreath;
    public float  BreathRange           => _breathRange;
    public float  BreathRadius          => _breathRadius;
    public int    BreathDamagePerSec    => _breathDamagePerSec;
    public float  RangeStartRatio       => _rangeStartRatio;
    public float  RangeGrowDuration     => _rangeGrowDuration;
    public float  VfxReferenceLength    => _vfxReferenceLength;
    public GameObject BreathEffectPrefab   => _breathEffectPrefab;
    public GameObject WarningEffectPrefab  => _warningEffectPrefab;
    public AudioClip  BreathSfx            => _breathSfx;
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
    private enum Phase { Back, Prepare, Breathing, EndPose, Done }

    private Phase      _phase;
    private float      _timer;
    private float      _damageTick;
    private Transform   _mouthBone;
    private GameObject  _breathEffect;
    private GameObject  _warningEffect;
    private GameObject  _rangeIndicator;
    private AudioSource _breathAudioSource;
    private Vector3      _backStartPos;
    private Vector3      _backForward;
    private float        _currentRange;
    private readonly System.Collections.Generic.List<ParticleSystem> _breathParticles = new();
    private readonly System.Collections.Generic.List<SpeedBase>      _breathBaseSpeed  = new();

    /// <summary>스케일 1(=VfxReferenceLength) 기준 startSpeed 원본값. mode와 무관하게 4개 필드 모두 캐싱 후 동일 배율로 스케일.</summary>
    private struct SpeedBase
    {
        public float Constant;
        public float ConstantMin;
        public float ConstantMax;
        public float CurveMultiplier;
    }

    private const float DamageTick = 0.15f;

    internal DragonGroundBreathState(DragonGroundBreathPatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase = Phase.Done;
        _currentRange = 0f;
        _breathParticles.Clear();
        _breathBaseSpeed.Clear();
        StopBreathSfx();
        DestroyEffect(ref _breathEffect);
        DestroyEffect(ref _warningEffect);
        DestroyEffect(ref _rangeIndicator);
    }

    // ── FSM ──────────────────────────────────────────────────────────────────

    public override void Enter(MonsterContext ctx)
    {
        _phase      = Phase.Back;
        _timer      = 0f;
        _damageTick = 0f;

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.isStopped = true;

        _mouthBone = FindBone(ctx.Transform, "Jaw");

        _backStartPos = ctx.Transform.position;
        _backForward  = ctx.Transform.forward;

        PlayAnim(ctx, Data.BackStateName);
        (ctx.Monster as DragonBossMonster)?.PlayWingFlapSfxOnce();
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Back:      UpdateBack(ctx);      break;
            case Phase.Prepare:   UpdatePrepare(ctx);   break;
            case Phase.Breathing: UpdateBreathing(ctx); break;
            case Phase.EndPose:   UpdateEndPose(ctx);   break;
        }
    }

    // ── Phase: Back (브레스 전 후퇴) ─────────────────────────────────────────────

    private void UpdateBack(MonsterContext ctx)
    {
        float t = Data.BackDuration > 0.0001f ? Mathf.Clamp01(_timer / Data.BackDuration) : 1f;
        ctx.Transform.position = Vector3.Lerp(
            _backStartPos, _backStartPos - _backForward * Data.BackStepDistance, t);

        if (_timer < Data.BackDuration) return;

        StartPrepare(ctx);
    }

    private void StartPrepare(MonsterContext ctx)
    {
        _phase = Phase.Prepare;
        _timer = 0f;

        PlayAnim(ctx, Data.BreathStartStateName);
        SpawnWarning(ctx);
    }

    public override void Exit(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.isStopped = false;
        StopBreathSfx();
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

        UpdateCurrentRange(ctx);
        SyncEffect(_breathEffect, ctx);
        SyncBreathEffectScale(_breathEffect);
        SyncRangeIndicator(ctx, _currentRange);

        _damageTick += Time.deltaTime;
        if (_damageTick >= DamageTick)
        {
            _damageTick = 0f;
            ApplyDamage(ctx);
        }

        if (_timer >= Data.BreathDuration)
        {
            StopBreathSfx();
            DestroyEffect(ref _breathEffect);
            DestroyEffect(ref _rangeIndicator);
            _phase = Phase.EndPose;
            _timer = 0f;
        }
    }

    private void StopBreathSfx()
        => Managers.Sound?.StopEffect(_breathAudioSource, Data.BreathSfx);

    /// <summary>브레스 진행 시간에 따라 사거리를 시작 비율→맵 경계까지 점진적으로 늘린다.</summary>
    private void UpdateCurrentRange(MonsterContext ctx)
    {
        Vector3 mouthPos = GetMouthPos(ctx);
        float fullRange  = DragonPatternFloorUtils.DistanceToFloorEdge(mouthPos, ctx.Transform.forward, Data.BreathRange);
        float startRange = fullRange * Mathf.Clamp01(Data.RangeStartRatio);
        float t = Data.RangeGrowDuration > 0.0001f ? Mathf.Clamp01(_timer / Data.RangeGrowDuration) : 1f;
        _currentRange = Mathf.Lerp(startRange, fullRange, t);
    }

    /// <summary>
    /// localScale은 ScalingMode.Hierarchy라 파티클 크기/모양만 키울 뿐 사거리(속도×수명)는 늘리지 않는다.
    /// 실제로 더 멀리 뻗어나가게 하려면 startSpeed 자체를 사거리 비율로 스케일해야 한다.
    /// </summary>
    private void SyncBreathEffectScale(GameObject go)
    {
        if (go == null) return;
        float reference = Mathf.Max(0.01f, Data.VfxReferenceLength);
        float factor    = Mathf.Max(0.01f, _currentRange / reference);

        for (int i = 0; i < _breathParticles.Count; i++)
        {
            var ps = _breathParticles[i];
            if (ps == null) continue;

            var main  = ps.main;
            var speed = main.startSpeed;
            var baseSpeed = _breathBaseSpeed[i];

            speed.constant        = baseSpeed.Constant * factor;
            speed.constantMin     = baseSpeed.ConstantMin * factor;
            speed.constantMax     = baseSpeed.ConstantMax * factor;
            speed.curveMultiplier = baseSpeed.CurveMultiplier * factor;
            main.startSpeed = speed;
        }
    }

    /// <summary>SpawnBreath 시점 (스케일 1) 의 startSpeed 원본값을 캐싱 — 이후 매 프레임 이 값을 기준으로 배율 적용.</summary>
    private void CacheBreathParticleBaseSpeeds(GameObject go)
    {
        _breathParticles.Clear();
        _breathBaseSpeed.Clear();
        if (go == null) return;

        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var speed = ps.main.startSpeed;
            _breathParticles.Add(ps);
            _breathBaseSpeed.Add(new SpeedBase
            {
                Constant        = speed.constant,
                ConstantMin     = speed.constantMin,
                ConstantMax     = speed.constantMax,
                CurveMultiplier = speed.curveMultiplier,
            });
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
        _breathAudioSource = Managers.Sound?.PlayEffectAt(Data.BreathSfx, GetMouthPos(ctx));

        if (Data.BreathEffectPrefab == null) return;
        _breathEffect = Object.Instantiate(Data.BreathEffectPrefab);
        SyncEffect(_breathEffect, ctx);
        TintEffect(_breathEffect, Data.BreathColor);
        CacheBreathParticleBaseSpeeds(_breathEffect);
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

    /// <summary>overrideRange를 주면 그 거리를 그대로 사용 (브레스 중 점진 확장), 없으면 맵 경계까지의 전체 사거리를 사용 (준비 단계 예고).</summary>
    private void SyncRangeIndicator(MonsterContext ctx, float? overrideRange = null)
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
        float range = overrideRange ?? DragonPatternFloorUtils.DistanceToFloorEdge(origin, forward, Data.BreathRange);

        lr.SetPosition(0, origin);
        lr.SetPosition(1, origin + forward * range);
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

    /// <summary>
    /// 경고장판(SyncRangeIndicator)과 완전히 동일한 기준(정면 투영 거리 ≤ _currentRange, 수직 거리 ≤ BreathRadius)으로 판정.
    /// Physics.SphereCast 대신 직접 투영 계산을 사용해 중간 장애물에 막히지 않고, 사거리가 경고장판과 항상 일치한다.
    /// </summary>
    private void ApplyDamage(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        Vector3 forward = ctx.Transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) return;
        forward.Normalize();

        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - GetMouthPos(ctx);
        toPlayer.y = 0f;

        float along = Vector3.Dot(toPlayer, forward);
        if (along < 0f || along > _currentRange) return;

        Vector3 lateral = toPlayer - forward * along;
        if (lateral.magnitude > Data.BreathRadius) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>()
                  ?? ctx.Runtime.PlayerTarget.GetComponentInParent<PlayerController>();
        if (player == null) return;

        float dmg = Data.BreathDamagePerSec * DamageTick;
        player.TakeDamage(Mathf.RoundToInt(dmg));
        Data.StatusEffect?.Apply(player);
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
