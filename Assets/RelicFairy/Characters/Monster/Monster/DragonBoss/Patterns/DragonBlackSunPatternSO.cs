using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DL-S 「검은 태양」 — 2페이지(심연) 간판 패턴. 2페이지 체력 50%에서 한 번(<see cref="BossPages.SignatureDue"/>, 강제 실행).
///   높이 떠올라 10초 충전 — 양 날개에 약점(<see cref="DragonWingWeakPoint"/>, 각 4타 · 근접 · 원거리 모두 됨, 청록).
///   · 둘 다 깨면: 추락 → 그로기 4초(HUD 무방비 창).
///   · 못 깨면: 전역 폭발 — 플레이어 최대 체력의 60%, 즉사하지 않는다(남은 체력 1 이상).
/// 약점 창 동안 보스는 무적이 아니다(몸도 원거리로 칠 수 있다 — 공중이라 근접 면역은 평소대로).
/// 이 패턴 동안 수동 운석은 쉰다.
/// </summary>
[CreateAssetMenu(fileName = "DragonBlackSunPattern",
    menuName = "RelicFairy/Boss/Dragon/BlackSunPattern")]
public class DragonBlackSunPatternSO : BossPatternSO
{
    [Header("체공")]
    [Tooltip("바닥 기준 높이 (m)")]
    [SerializeField] private float _hoverHeight = 8f;
    [SerializeField] private float _liftSpeed   = 8f;
    [Tooltip("아레나 가장자리에서 이만큼 안쪽에 뜬다 (m) — 날개 약점이 가장자리 불 띠에 걸리지 않게")]
    [SerializeField] private float _edgeMargin  = 11f;

    [Header("충전")]
    [SerializeField] private float _chargeSeconds  = 10f;
    [Tooltip("폭발 직전 예고를 판정 색으로 바꾸는 시간 (초)")]
    [SerializeField] private float _armLeadSeconds = 0.6f;

    [Header("날개 약점")]
    [Tooltip("몸 중심에서 날개 약점까지 옆 거리 (m)")]
    [SerializeField] private float      _wingOffset          = 6f;
    [SerializeField] private float      _weakPointRadius     = 1.3f;
    [SerializeField] private int        _hitsPerWing         = 4;
    [SerializeField] private GameObject _weakPointHitVfx;
    [SerializeField] private float      _weakPointHitVfxScale   = 0.8f;
    [SerializeField] private GameObject _weakPointBreakVfx;
    [SerializeField] private float      _weakPointBreakVfxScale = 1.6f;
    [SerializeField] private AudioClip  _breakSfx;

    [Header("격추 — 둘 다 깨면")]
    [SerializeField] private float      _fallSpeed      = 16f;
    [SerializeField] private float      _groggySeconds  = 4f;
    [SerializeField] private GameObject _fallImpactVfx;
    [SerializeField] private float      _fallImpactVfxScale = 1.5f;
    [SerializeField] private AudioClip  _fallImpactSfx;

    [Header("실패 — 전역 폭발")]
    [Tooltip("플레이어 최대 체력 대비 피해 — 남은 체력 1은 남긴다")]
    [SerializeField] private float      _failMaxHpRatio   = 0.6f;
    [SerializeField] private float      _failKnockbackMult = 0.8f;
    [SerializeField] private GameObject _failVfxPrefab;
    [SerializeField] private float      _failVfxScale      = 4f;
    [SerializeField] private AudioClip  _failSfx;
    [SerializeField] private float      _descendSpeed      = 10f;

    [Header("검은 태양 연출")]
    [Tooltip("보스 머리 위에서 자라는 검은 태양 (Effect_02_BlackHole) — 없으면 도형 구체")]
    [SerializeField] private GameObject _sunVfxPrefab;
    [SerializeField] private float      _sunScaleStart  = 0.3f;
    [SerializeField] private float      _sunScaleEnd    = 1.6f;
    [SerializeField] private float      _sunHeightAbove = 5f;
    [SerializeField] private AudioClip  _chargeSfx;

    [Header("대사 (비우면 없음)")]
    [SerializeField] private string _announceLine = "검은 태양이… 떠오른다.";
    [SerializeField] private string _brokenLine   = "";
    [SerializeField] private string _failLine     = "";

    [Header("애니메이션")]
    [SerializeField] private string _launchStateName    = "Takeoff_Launch";
    [SerializeField] private string _riseStateName      = "Takeoff_Rise";
    [SerializeField] private string _hoverStateName     = "Airborne_Hover";
    [SerializeField] private string _crashStateName     = "AirDashCrash";
    [SerializeField] private string _fallStateName      = "AirDashFall";
    [SerializeField] private string _groggyStateName    = "USleep Idle";
    [SerializeField] private string _getUpStateName     = "AirDashRecover";
    [SerializeField] private string _descendStateName   = "Landing_Descend";
    [SerializeField] private string _touchdownStateName = "Landing_Touchdown";
    [Tooltip("일어나는 / 착지 모션 뒤 지상전으로 넘어가기까지 (초)")]
    [SerializeField] private float  _recoverSeconds     = 1.2f;

    public float      HoverHeight            => _hoverHeight;
    public float      LiftSpeed              => Mathf.Max(1f, _liftSpeed);
    public float      EdgeMargin             => _edgeMargin;
    public float      ChargeSeconds          => Mathf.Max(1f, _chargeSeconds);
    public float      ArmLeadSeconds         => _armLeadSeconds;
    public float      WingOffset             => _wingOffset;
    public float      WeakPointRadius        => _weakPointRadius;
    public int        HitsPerWing            => Mathf.Max(1, _hitsPerWing);
    public GameObject WeakPointHitVfx        => _weakPointHitVfx;
    public float      WeakPointHitVfxScale   => _weakPointHitVfxScale;
    public GameObject WeakPointBreakVfx      => _weakPointBreakVfx;
    public float      WeakPointBreakVfxScale => _weakPointBreakVfxScale;
    public AudioClip  BreakSfx               => _breakSfx;
    public float      FallSpeed              => Mathf.Max(1f, _fallSpeed);
    public float      GroggySeconds          => _groggySeconds;
    public GameObject FallImpactVfx          => _fallImpactVfx;
    public float      FallImpactVfxScale     => _fallImpactVfxScale;
    public AudioClip  FallImpactSfx          => _fallImpactSfx;
    public float      FailMaxHpRatio         => Mathf.Clamp01(_failMaxHpRatio);
    public float      FailKnockbackMult      => _failKnockbackMult;
    public GameObject FailVfxPrefab          => _failVfxPrefab;
    public float      FailVfxScale           => _failVfxScale;
    public AudioClip  FailSfx                => _failSfx;
    public float      DescendSpeed           => Mathf.Max(1f, _descendSpeed);
    public GameObject SunVfxPrefab           => _sunVfxPrefab;
    public float      SunScaleStart          => _sunScaleStart;
    public float      SunScaleEnd            => _sunScaleEnd;
    public float      SunHeightAbove         => _sunHeightAbove;
    public AudioClip  ChargeSfx              => _chargeSfx;
    public string     AnnounceLine           => _announceLine;
    public string     BrokenLine             => _brokenLine;
    public string     FailLine               => _failLine;
    public string     LaunchStateName        => _launchStateName;
    public string     RiseStateName          => _riseStateName;
    public string     HoverStateName         => _hoverStateName;
    public string     CrashStateName         => _crashStateName;
    public string     FallStateName          => _fallStateName;
    public string     GroggyStateName        => _groggyStateName;
    public string     GetUpStateName         => _getUpStateName;
    public string     DescendStateName       => _descendStateName;
    public string     TouchdownStateName     => _touchdownStateName;
    public float      RecoverSeconds         => _recoverSeconds;

    private DragonBlackSunState _runtimeState;

    public override void Initialize(BossPatternContext ctx) => _runtimeState = new DragonBlackSunState(this);

    public override void OnRecycled() => _runtimeState?.Reset();

    /// <summary>2페이지 체력 50% · 아직 안 씀 — 강제 실행 엔트리(Page_SignatureDue)에서 부른다.</summary>
    public override bool CanExecute(BossPatternContext ctx)
        => ctx?.Ctx?.Monster is IPagedBoss paged && paged.Pages != null
           && paged.Pages.SignatureDue(ctx.Ctx.Monster.CurrentHp);

    public override SpecialStateBase GetRuntimeState() => _runtimeState ??= new DragonBlackSunState(this);
}

// ────────────────────────────────────────────────────────────────────────────
// Runtime state
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Rise → Charge(약점 창) → [둘 다 깸] Fall → Groggy → GetUp / [시간 초과] Blast → Descend → Touchdown → 복귀.
/// 중단 불가 · 이동 잠금 — 무적 제약은 쓰지 않는다(약점 창).
/// </summary>
internal sealed class DragonBlackSunState : FullLockState<DragonBlackSunPatternSO>
{
    private enum Phase { Rise, Charge, Fall, Groggy, Blast, Descend, Recover, Done }

    private const float SunSphereRadius = 1.2f;   // 이펙트가 없을 때 도형 구체 반경(× 자라는 배율)

    private static readonly Color SunCore = new Color(0.08f, 0.01f, 0.12f);

    private Phase               _phase;
    private float               _timer;
    private bool                _startedGrounded;
    private bool                _inRiseLoop;
    private bool                _armed;
    private bool                _landed;
    private bool                _fallLooping;
    private Vector3             _hoverPos;
    private float               _floorY;
    private int                 _wingsBroken;
    private DragonWingWeakPoint _leftWing;
    private DragonWingWeakPoint _rightWing;
    private GameObject          _sun;
    private GameObject          _sunShape;
    private GameObject          _disc;
    private AudioSource         _chargeAudio;
    private MonsterContext      _ctx;

    internal DragonBlackSunState(DragonBlackSunPatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase = Phase.Done;
        CleanupCharge();
    }

    public override void Enter(MonsterContext ctx)
    {
        _ctx = ctx;
        (ctx.Monster as IPagedBoss)?.Pages?.MarkSignatureDone();
        GameCameraController.Instance?.DeactivateDragonTopDownView(0.8f);

        _startedGrounded = !DragonAbyssFx.IsAirborne(ctx);
        _inRiseLoop      = false;
        _armed           = false;
        _landed          = false;
        _fallLooping     = false;
        _wingsBroken     = 0;
        _timer           = 0f;
        _phase           = Phase.Rise;

        if (ctx.Agent != null) ctx.Agent.enabled = false;
        var bb = DragonAbyssFx.Blackboard(ctx);
        if (bb != null)
        {
            bb.BodyState               = BodyState.Airborne;
            bb.PassiveMeteorSuppressed = true;
        }

        // 날개 약점이 가장자리 불 띠에 걸리지 않게 아레나 안쪽에 뜬다
        Vector3 xz = DragonAbyssFx.ClampToArena(ctx.Transform.position, Data.EdgeMargin);
        _floorY   = DragonAbyssFx.FloorY(ctx, xz);
        _hoverPos = new Vector3(xz.x, _floorY + Data.HoverHeight, xz.z);

        DragonAbyssFx.PlayAnim(ctx, _startedGrounded ? Data.LaunchStateName : Data.HoverStateName, 0.1f);
        if (!string.IsNullOrEmpty(Data.AnnounceLine)) UI_BossBark.Show(Data.AnnounceLine, BossBarkType.PatternAnnounce);
        Debug.Log("[DragonBlackSun] 간판 — 검은 태양 시작", ctx.Monster);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        switch (_phase)
        {
            case Phase.Rise:    UpdateRise(ctx);    break;
            case Phase.Charge:  UpdateCharge(ctx);  break;
            case Phase.Fall:    UpdateFall(ctx);    break;
            case Phase.Groggy:  UpdateGroggy(ctx);  break;
            case Phase.Descend: UpdateDescend(ctx); break;
            case Phase.Recover: UpdateRecover(ctx); break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        CleanupCharge();
        if (!_landed) DragonAbyssFx.Land(ctx);
        var bb = DragonAbyssFx.Blackboard(ctx);
        if (bb != null) bb.PassiveMeteorSuppressed = false;
        _phase = Phase.Done;
        _ctx   = null;
    }

    // ── Rise ─────────────────────────────────────────────────────────────────

    private void UpdateRise(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget != null)
            DragonAbyssFx.Face(ctx, ctx.Runtime.PlayerTarget.position - ctx.Transform.position, 180f);

        ctx.Transform.position = Vector3.MoveTowards(ctx.Transform.position, _hoverPos, Data.LiftSpeed * Time.deltaTime);
        if (_startedGrounded && !_inRiseLoop && DragonAbyssFx.IsAnimNearEnd(ctx, Data.LaunchStateName))
        {
            _inRiseLoop = true;
            DragonAbyssFx.PlayAnim(ctx, Data.RiseStateName, 0.12f);
        }

        if ((ctx.Transform.position - _hoverPos).sqrMagnitude > 0.01f) return;
        StartCharge(ctx);
    }

    // ── Charge — 약점 창 ─────────────────────────────────────────────────────

    private void StartCharge(MonsterContext ctx)
    {
        _phase = Phase.Charge;
        _timer = 0f;
        ctx.Transform.position = _hoverPos;
        DragonAbyssFx.PlayAnim(ctx, Data.HoverStateName, 0.15f);

        // 몸 방향은 여기서 고정 — 날개 약점이 움직이지 않게
        float height = Data.HoverHeight;
        _leftWing  = DragonWingWeakPoint.Create(ctx.Transform, -1f, Data.WingOffset, _floorY, height, Data.WeakPointRadius,
                                                Data.HitsPerWing, Data.WeakPointHitVfx, Data.WeakPointHitVfxScale,
                                                Data.WeakPointBreakVfx, Data.WeakPointBreakVfxScale, OnWingBroken);
        _rightWing = DragonWingWeakPoint.Create(ctx.Transform,  1f, Data.WingOffset, _floorY, height, Data.WeakPointRadius,
                                                Data.HitsPerWing, Data.WeakPointHitVfx, Data.WeakPointHitVfxScale,
                                                Data.WeakPointBreakVfx, Data.WeakPointBreakVfxScale, OnWingBroken);

        Vector3 sunPos = ctx.Transform.position + Vector3.up * Data.SunHeightAbove;
        _sun = DragonAbyssFx.SpawnVisual(Data.SunVfxPrefab, sunPos, Quaternion.identity, Data.SunScaleStart, forceLoop: true);
        if (_sun == null) _sunShape = PatternGuideHelper.Sphere(sunPos, SunSphereRadius * Data.SunScaleStart, SunCore);

        // 전역 폭발 예고 — 아레나 전체가 검보라로 차오른다(약점 원은 그 위에 그린다)
        Vector3 center = new Vector3(_hoverPos.x, _floorY, _hoverPos.z);
        Color   abyss  = DragonAbyssFx.WithAlpha(DragonAbyssFx.Abyss, 0.45f);
        _disc = PatternGuideHelper.Prepare(PatternGuideHelper.Disc(center, DragonAbyssFx.ArenaHalfDiagonal() * 2f, abyss), abyss);

        _chargeAudio = Managers.Sound?.PlayEffectAt(Data.ChargeSfx, sunPos);
    }

    private void UpdateCharge(MonsterContext ctx)
    {
        ctx.Transform.position = _hoverPos;
        float t = Mathf.Clamp01(_timer / Data.ChargeSeconds);

        float scale = Mathf.Lerp(Data.SunScaleStart, Data.SunScaleEnd, t);
        if (_sun != null) _sun.transform.localScale = Vector3.one * scale;
        if (_sunShape != null) _sunShape.transform.localScale = Vector3.one * (SunSphereRadius * 2f * scale);

        PatternGuideHelper.SetProgress(_disc, t);
        if (!_armed && _timer >= Data.ChargeSeconds - Data.ArmLeadSeconds)
        {
            _armed = true;
            PatternGuideHelper.Arm(_disc);
        }

        if (_wingsBroken >= 2)
        {
            StartFall(ctx);
            return;
        }
        if (_timer < Data.ChargeSeconds) return;
        Blast(ctx);
    }

    private void OnWingBroken(DragonWingWeakPoint wing)
    {
        _wingsBroken++;
        if (_ctx != null) Managers.Sound?.PlayEffectAt(Data.BreakSfx, wing.transform.position);
        LichCinematics.Flash(PatternGuideHelper.Breakable, 0.15f, 0.2f);
    }

    // ── 격추 → 그로기 ────────────────────────────────────────────────────────

    private void StartFall(MonsterContext ctx)
    {
        CleanupCharge();
        _phase = Phase.Fall;
        _timer = 0f;
        DragonAbyssFx.PlayAnim(ctx, Data.CrashStateName, 0.05f);
        if (!string.IsNullOrEmpty(Data.BrokenLine)) UI_BossBark.Show(Data.BrokenLine, BossBarkType.Bark);
        Debug.Log("[DragonBlackSun] 날개 약점 둘 다 격파 — 추락 · 그로기", ctx.Monster);
    }

    private void UpdateFall(MonsterContext ctx)
    {
        if (!_fallLooping && DragonAbyssFx.IsAnimNearEnd(ctx, Data.CrashStateName, 0.6f))
        {
            _fallLooping = true;
            DragonAbyssFx.PlayAnim(ctx, Data.FallStateName, 0.1f);
        }

        Vector3 p = ctx.Transform.position;
        p.y = Mathf.MoveTowards(p.y, _floorY, Data.FallSpeed * Time.deltaTime);
        ctx.Transform.position = p;
        if (p.y > _floorY + 0.05f) return;

        DragonAbyssFx.Land(ctx);
        _landed = true;
        DragonAbyssFx.Burst(Data.FallImpactVfx, ctx.Transform.position, Quaternion.identity, Data.FallImpactVfxScale, 3f);
        Managers.Sound?.PlayEffectAt(Data.FallImpactSfx, ctx.Transform.position);
        DragonAbyssFx.PlayAnim(ctx, Data.GroggyStateName, 0.15f);
        (ctx.Monster as DragonBossMonster)?.RaiseHudVulnerableWindow(Data.GroggySeconds);

        _phase = Phase.Groggy;
        _timer = 0f;
    }

    private void UpdateGroggy(MonsterContext ctx)
    {
        if (_timer < Data.GroggySeconds) return;
        DragonAbyssFx.PlayAnim(ctx, Data.GetUpStateName, 0.15f);
        _phase = Phase.Recover;
        _timer = 0f;
    }

    // ── 실패 — 전역 폭발 ─────────────────────────────────────────────────────

    private void Blast(MonsterContext ctx)
    {
        Vector3 at = _sun != null ? _sun.transform.position : ctx.Transform.position;
        CleanupCharge();
        _phase = Phase.Blast;

        LichCinematics.Flash(DragonAbyssFx.WithAlpha(DragonAbyssFx.Abyss, 1f), 0.45f, 0.4f);
        LichCinematics.Chroma(0.5f, 0.4f);
        DragonAbyssFx.Burst(Data.FailVfxPrefab, at, Quaternion.identity, Data.FailVfxScale, 3f);
        Managers.Sound?.PlayEffectAt(Data.FailSfx, at);
        if (!string.IsNullOrEmpty(Data.FailLine)) UI_BossBark.Show(Data.FailLine, BossBarkType.Bark);

        var player = DragonAbyssFx.Player(ctx);
        var stats  = player != null ? player.RuntimeStats : null;
        if (stats != null)
        {
            // 즉사하지 않음 — 남은 체력 − 1로 자른다(방어력 · 피해 감소는 더 깎을 뿐이다)
            int raw = Mathf.RoundToInt(stats.MaxHp * Data.FailMaxHpRatio);
            int dmg = Mathf.Min(raw, stats.Hp - 1);
            bool dodged = player.IsInvincible;
            if (dmg > 0) player.TakeDamage(dmg, ctx.Monster.gameObject, true, HitWeight.Heavy);
            if (!dodged) DragonAbyssFx.Knockback(ctx, player, new Vector3(at.x, _floorY, at.z), Data.FailKnockbackMult);
            Debug.Log($"[DragonBlackSun] 시간 초과 — 전역 폭발 {dmg} (최대 체력 {stats.MaxHp}의 {Data.FailMaxHpRatio:P0}, 즉사 없음)", ctx.Monster);
        }

        _phase = Phase.Descend;
        _timer = 0f;
        DragonAbyssFx.PlayAnim(ctx, Data.DescendStateName, 0.15f);
    }

    private void UpdateDescend(MonsterContext ctx)
    {
        Vector3 p = ctx.Transform.position;
        p.y = Mathf.MoveTowards(p.y, _floorY, Data.DescendSpeed * Time.deltaTime);
        ctx.Transform.position = p;
        if (p.y > _floorY + 0.05f) return;

        DragonAbyssFx.Land(ctx);
        _landed = true;
        DragonAbyssFx.PlayAnim(ctx, Data.TouchdownStateName, 0.1f);
        _phase = Phase.Recover;
        _timer = 0f;
    }

    // ── 복귀 ─────────────────────────────────────────────────────────────────

    private void UpdateRecover(MonsterContext ctx)
    {
        if (_timer < Data.RecoverSeconds) return;
        _phase = Phase.Done;
        DragonAbyssFx.ReturnToCombat(ctx);
    }

    private void CleanupCharge()
    {
        if (_leftWing != null) Object.Destroy(_leftWing.gameObject);
        if (_rightWing != null) Object.Destroy(_rightWing.gameObject);
        _leftWing  = null;
        _rightWing = null;
        if (_sun != null) Object.Destroy(_sun);
        _sun = null;
        PatternGuideHelper.SafeDestroy(ref _sunShape);
        PatternGuideHelper.SafeDestroy(ref _disc);
        if (_chargeAudio != null && Data.ChargeSfx != null) Managers.Sound?.StopEffect(_chargeAudio, Data.ChargeSfx);
        _chargeAudio = null;
    }
}
}
