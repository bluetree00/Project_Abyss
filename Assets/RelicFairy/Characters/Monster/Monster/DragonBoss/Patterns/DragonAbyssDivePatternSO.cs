using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DL4 「심연 급강하」 — 2페이지(심연) 일반 패턴. 멀어진 플레이어에게 다가가는 패턴(09-28 규칙 R2).
///   (지상이면 날아오른다) → 플레이어 자리를 조준(1.0초 — 앞 0.6초는 따라가고 마지막 0.4초는 고정 · 판정 색, R3)
///   → 급강하 → 착지 충격(반경 4 m) → 그 자리(= 플레이어 가까이)에서 지상전으로 이어간다.
/// 발동 거리 ≥ 10 m(R6 — 가까우면 할퀴기 · 원소 합주가 맡는다).
/// </summary>
[CreateAssetMenu(fileName = "DragonAbyssDivePattern",
    menuName = "RelicFairy/Boss/Dragon/AbyssDivePattern")]
public class DragonAbyssDivePatternSO : BossPatternSO
{
    [Header("쿨다운 · 거리")]
    [SerializeField] private float _cooldown    = 14f;
    [Tooltip("플레이어가 이 거리(수평) 이상일 때만 (m)")]
    [SerializeField] private float _minDistance = 10f;

    [Header("이륙 · 체공")]
    [Tooltip("조준할 때의 높이 — 바닥 기준 (m)")]
    [SerializeField] private float _diveHeight = 9f;
    [SerializeField] private float _liftSpeed  = 10f;

    [Header("조준")]
    [SerializeField] private float _aimSeconds    = 1.0f;
    [Tooltip("판정 전 조준을 멈추는 시간 (초) — R3: 0.4 이상")]
    [SerializeField] private float _lockSeconds   = 0.4f;
    [SerializeField] private float _impactRadius  = 4f;

    [Header("급강하 · 충격")]
    [SerializeField] private float _diveSpeed     = 38f;
    [SerializeField] private float _damageMult    = 1.8f;
    [SerializeField] private float _knockbackMult = 1f;
    [Tooltip("착지 뒤 일어나 지상전으로 넘어가기까지 (초)")]
    [SerializeField] private float _recoverSeconds = 0.9f;

    [Header("이펙트")]
    [Tooltip("급강하 중 몸에 붙이는 검은 불 꼬리 (Projectile 3 black fire)")]
    [SerializeField] private GameObject _trailVfxPrefab;
    [SerializeField] private float      _trailVfxScale  = 2f;
    [Tooltip("착지 충격 (Meteor hit 2)")]
    [SerializeField] private GameObject _impactVfxPrefab;
    [SerializeField] private float      _impactVfxScale = 1.4f;
    [SerializeField] private AudioClip  _diveSfx;
    [SerializeField] private AudioClip  _impactSfx;

    [Header("애니메이션")]
    [SerializeField] private string _launchStateName    = "Takeoff_Launch";
    [SerializeField] private string _riseStateName      = "Takeoff_Rise";
    [SerializeField] private string _hoverStateName     = "Airborne_Hover";
    [SerializeField] private string _diveStateName      = "URFly Down";
    [SerializeField] private string _touchdownStateName = "Landing_Touchdown";

    public float      Cooldown           => _cooldown;
    public float      MinDistance        => _minDistance;
    public float      DiveHeight         => _diveHeight;
    public float      LiftSpeed          => Mathf.Max(1f, _liftSpeed);
    public float      AimSeconds         => Mathf.Max(_aimSeconds, _lockSeconds);
    public float      LockSeconds        => _lockSeconds;
    public float      ImpactRadius       => _impactRadius;
    public float      DiveSpeed          => Mathf.Max(5f, _diveSpeed);
    public float      DamageMult         => _damageMult;
    public float      KnockbackMult      => _knockbackMult;
    public float      RecoverSeconds     => _recoverSeconds;
    public GameObject TrailVfxPrefab     => _trailVfxPrefab;
    public float      TrailVfxScale      => _trailVfxScale;
    public GameObject ImpactVfxPrefab    => _impactVfxPrefab;
    public float      ImpactVfxScale     => _impactVfxScale;
    public AudioClip  DiveSfx            => _diveSfx;
    public AudioClip  ImpactSfx          => _impactSfx;
    public string     LaunchStateName    => _launchStateName;
    public string     RiseStateName      => _riseStateName;
    public string     HoverStateName     => _hoverStateName;
    public string     DiveStateName      => _diveStateName;
    public string     TouchdownStateName => _touchdownStateName;

    private DragonAbyssDiveState _runtimeState;

    public override void Initialize(BossPatternContext ctx) => _runtimeState = new DragonAbyssDiveState(this);

    public override void OnRecycled() => _runtimeState?.Reset();

    public override bool CanExecute(BossPatternContext ctx)
    {
        var target = ctx.Ctx.Runtime.PlayerTarget;
        if (target == null || ctx.Blackboard is not DragonBossBlackboard bb || bb.AbyssDiveCooldown > 0f) return false;
        Vector3 d = target.position - ctx.Ctx.Transform.position;
        d.y = 0f;
        return d.sqrMagnitude >= _minDistance * _minDistance;
    }

    public override SpecialStateBase GetRuntimeState() => _runtimeState ??= new DragonAbyssDiveState(this);
}

// ────────────────────────────────────────────────────────────────────────────
// Runtime state
// ────────────────────────────────────────────────────────────────────────────

/// <summary>Rise → Aim(추적 → 고정) → Dive → Recover → 지상 복귀. 중단 불가 · 이동 잠금.</summary>
internal sealed class DragonAbyssDiveState : FullLockState<DragonAbyssDivePatternSO>
{
    private enum Phase { Rise, Aim, Dive, Recover, Done }

    private Phase      _phase;
    private float      _timer;
    private bool       _startedGrounded;
    private bool       _inRiseLoop;
    private bool       _locked;
    private bool       _landed;
    private float      _hoverY;
    private Vector3    _target;
    private GameObject _guide;
    private float      _guideLift;
    private GameObject _trail;

    internal DragonAbyssDiveState(DragonAbyssDivePatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase = Phase.Done;
        PatternGuideHelper.SafeDestroy(ref _guide);
        ReleaseTrail();
    }

    public override void Enter(MonsterContext ctx)
    {
        GameCameraController.Instance?.DeactivateDragonTopDownView(0.8f);
        _startedGrounded = !DragonAbyssFx.IsAirborne(ctx);
        _inRiseLoop      = false;
        _locked          = false;
        _landed          = false;
        _timer           = 0f;
        _phase           = Phase.Rise;
        _hoverY          = DragonAbyssFx.FloorY(ctx, ctx.Transform.position) + Data.DiveHeight;

        if (ctx.Agent != null) ctx.Agent.enabled = false;
        var bb = DragonAbyssFx.Blackboard(ctx);
        if (bb != null)
        {
            bb.AbyssDiveCooldown = Data.Cooldown;
            bb.BodyState         = BodyState.Airborne;   // 공중 판정 · 근접 면역은 뜨는 순간부터
        }
        DragonAbyssFx.PlayAnim(ctx, _startedGrounded ? Data.LaunchStateName : Data.HoverStateName, 0.1f);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        switch (_phase)
        {
            case Phase.Rise:    UpdateRise(ctx);    break;
            case Phase.Aim:     UpdateAim(ctx);     break;
            case Phase.Dive:    UpdateDive(ctx);    break;
            case Phase.Recover: UpdateRecover(ctx); break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _guide);
        ReleaseTrail();
        // 끊겼으면(사망 등) 공중에 떠 있지 않게 내려 둔다
        if (!_landed) DragonAbyssFx.Land(ctx);
        _phase = Phase.Done;
    }

    // ── Rise ─────────────────────────────────────────────────────────────────

    private void UpdateRise(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget != null)
            DragonAbyssFx.Face(ctx, ctx.Runtime.PlayerTarget.position - ctx.Transform.position, 180f);

        Vector3 p = ctx.Transform.position;
        p.y = Mathf.MoveTowards(p.y, _hoverY, Data.LiftSpeed * Time.deltaTime);
        ctx.Transform.position = p;

        if (_startedGrounded && !_inRiseLoop && DragonAbyssFx.IsAnimNearEnd(ctx, Data.LaunchStateName))
        {
            _inRiseLoop = true;
            DragonAbyssFx.PlayAnim(ctx, Data.RiseStateName, 0.12f);
        }

        if (Mathf.Abs(p.y - _hoverY) > 0.05f) return;
        StartAim(ctx);
    }

    // ── Aim ──────────────────────────────────────────────────────────────────

    private void StartAim(MonsterContext ctx)
    {
        _phase  = Phase.Aim;
        _timer  = 0f;
        _locked = false;
        DragonAbyssFx.PlayAnim(ctx, Data.HoverStateName, 0.15f);

        _target = ResolveTarget(ctx);
        Color c = DragonAbyssFx.WithAlpha(DragonAbyssFx.Abyss, 0.85f);
        _guide  = PatternGuideHelper.Prepare(PatternGuideHelper.Disc(_target, Data.ImpactRadius, c), c);
        _guideLift = _guide != null ? _guide.transform.position.y - _target.y : 0f;
    }

    private void UpdateAim(MonsterContext ctx)
    {
        if (_timer < Data.AimSeconds - Data.LockSeconds)
        {
            _target = ResolveTarget(ctx);
            if (_guide != null) _guide.transform.position = _target + Vector3.up * _guideLift;   // 가이드 헬퍼가 띄운 높이는 그대로
        }
        else if (!_locked)
        {
            // R3 — 급강하 LockSeconds 전부터 자리를 고정하고 판정 색으로
            _locked = true;
            PatternGuideHelper.Arm(_guide);
        }
        PatternGuideHelper.SetProgress(_guide, _timer / Data.AimSeconds);
        DragonAbyssFx.Face(ctx, _target - ctx.Transform.position, 240f);

        if (_timer < Data.AimSeconds) return;
        StartDive(ctx);
    }

    /// <summary>플레이어 발밑 바닥(아레나 안쪽).</summary>
    private static Vector3 ResolveTarget(MonsterContext ctx)
    {
        Vector3 p = ctx.Runtime.PlayerTarget != null ? ctx.Runtime.PlayerTarget.position : ctx.Transform.position;
        return DragonAbyssFx.OnFloor(ctx, DragonAbyssFx.ClampToArena(p, 1.5f));
    }

    // ── Dive ─────────────────────────────────────────────────────────────────

    private void StartDive(MonsterContext ctx)
    {
        _phase = Phase.Dive;
        _timer = 0f;
        DragonAbyssFx.PlayAnim(ctx, Data.DiveStateName, 0.05f);
        Managers.Sound?.PlayEffectAt(Data.DiveSfx, ctx.Transform.position);
        if (Data.TrailVfxPrefab != null)
        {
            _trail = BossEffectPool.Spawn(Data.TrailVfxPrefab, ctx.Transform.position, ctx.Transform.rotation, ctx.Transform, true);
            if (_trail != null) _trail.transform.localScale = Vector3.one * Data.TrailVfxScale;
        }
    }

    private void UpdateDive(MonsterContext ctx)
    {
        Vector3 pos  = ctx.Transform.position;
        Vector3 to   = _target - pos;
        float   step = Data.DiveSpeed * Time.deltaTime;
        if (to.sqrMagnitude > 0.01f) ctx.Transform.rotation = Quaternion.LookRotation(to.normalized);   // 머리부터 내리꽂는다

        if (to.magnitude > step)
        {
            ctx.Transform.position = pos + to.normalized * step;
            return;
        }
        ctx.Transform.position = _target;
        Impact(ctx);
    }

    private void Impact(MonsterContext ctx)
    {
        ReleaseTrail();
        DragonAbyssFx.Land(ctx);
        _landed = true;
        DragonAbyssFx.PlayAnim(ctx, Data.TouchdownStateName, 0.08f);

        DragonAbyssFx.Burst(Data.ImpactVfxPrefab, _target, Quaternion.identity, Data.ImpactVfxScale, 3f);
        Managers.Sound?.PlayEffectAt(Data.ImpactSfx, _target);

        var player = DragonAbyssFx.Player(ctx);
        if (player != null && DragonAbyssFx.InDisc(player.transform.position, _target, Data.ImpactRadius))
            DragonAbyssFx.HitPlayer(ctx, Data.DamageMult, HitWeight.Heavy,
                                    knockbackMult: Data.KnockbackMult, knockFrom: _target);   // 착지 충격 — 강

        PatternGuideHelper.SafeDestroy(ref _guide);
        _phase = Phase.Recover;
        _timer = 0f;
    }

    // ── Recover ──────────────────────────────────────────────────────────────

    private void UpdateRecover(MonsterContext ctx)
    {
        if (_timer < Data.RecoverSeconds) return;
        _phase = Phase.Done;
        DragonAbyssFx.ReturnToCombat(ctx);
    }

    private void ReleaseTrail()
    {
        if (_trail == null) return;
        BossEffectPool.Release(_trail);
        _trail = null;
    }
}
}
