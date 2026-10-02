using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 삼연 낫무 (Reaper's Cadence) 패턴 — Phase 2 일반 공격.
///
/// 흐름: 접근(DashClose) → 빠른 좁은 호 2연타 → 페인트 홀드 → 광각 딜레이드 피니셔 → 복귀
///
/// UX: 빠른 2타에 익숙해진 플레이어가 반격하러 붙는 순간, 한 박자 늦게 들어오는
///     광각 3타(피니셔)로 탐욕을 응징한다. 정답은 2타 후 바로 붙지 말고
///     피니셔 빨강을 보고 회피 → 그 후 반격(진짜 응징 창은 Recovery).
///
/// 동작(09-19 재작업 — 휘두름 드라이버로 접촉 프레임 = 판정):
///   1타 = 오른쪽→왼쪽 수평, 2타 = 왼쪽→오른쪽 수평(좁은 부채꼴),
///   피니셔 = 머리 위 내려찍기 → 앞쪽 원형 충격파(동작과 같은 모양).
/// 예고: 각 타격 단계 시작부터 진홍 부채꼴/원이 차오르고 판정 0.15초 전 빨강. 빨강이 뜨면 방향 고정(옆으로 피하면 산다).
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_ReaperCadencePattern", fileName = "Lich_ReaperCadencePattern")]
public class LichReaperCadencePatternSO : BossPatternSO
{
    [Header("Reaper Cadence — Range")]
    [Tooltip("패턴 발동 최소 거리 (근접 전용)")]
    public float minTriggerRange = 2.5f;
    [Tooltip("패턴 발동 최대 거리 (근접 전용 — DashClose 도달 범위 내로 제한)")]
    public float maxTriggerRange = 8f;
    [Tooltip("접근 종료 거리 — 이 안에 들면 즉시 1타로 전환")]
    public float approachStopRange = 3f;

    [Header("Reaper Cadence — Timing")]
    [Tooltip("DashClose 접근 대기 시간 (초)")]
    public float approachDuration = 0.7f;
    [Tooltip("1·2타: 단계 진입 후 타격 프레임까지 선딜 (클립 임팩트에 맞춤)")]
    public float sweepHitDelay = 0.4f;
    [Tooltip("1·2타 단계 총 길이 (초). Attack 클립(60·70프레임)이 읽히도록 ~1s.")]
    public float sweepDuration = 1.0f;
    [Tooltip("1타와 2타 사이 간격 (초)")]
    public float gapDuration = 0.25f;
    [Tooltip("페인트 홀드 시간 (초). 길수록 탐욕 응징↑ (0.5~0.9 권장)")]
    public float feintHoldDuration = 0.7f;
    [Tooltip("피니셔 타격 선딜 (초)")]
    public float finisherHitDelay = 0.55f;
    [Tooltip("피니셔 단계 총 길이 (초). 큰 스윙이 끝까지 읽히도록 길게.")]
    public float finisherDuration = 1.3f;
    [Tooltip("복귀(반격 창) 대기 시간 (초)")]
    public float recoveryDuration = 0.7f;

    [Header("Reaper Cadence — Hit")]
    [Tooltip("윈드업 중 추적 각속도(도/초). 타격 시 방향 고정 → 측면 회피 가능.")]
    public float windupTrackSpeed = 240f;
    [Tooltip("1·2타 판정 반경 (m)")]
    public float sweepRadius = 4f;
    [Tooltip("1·2타 전방 호 반각 (도)")]
    public float sweepHalfAngle = 60f;
    [Tooltip("피니셔 충격파 반경 = 1·2타 반경 × 이 값")]
    public float finisherRadiusMult = 1.4f;
    [Tooltip("피니셔 — 낫이 바닥에 닿는 자리(리치 앞 m). 충격파 원의 중심")]
    public float finisherSlamOffset = 1.5f;
    [Tooltip("판정 직전 패링 창을 연다(낫이 금빛으로 빛남)")]
    public bool  parryable = true;

    [Header("Reaper Cadence — Damage")]
    [Tooltip("1·2타 데미지 배율")]
    public float sweepDamageMult = 1.0f;
    [Tooltip("피니셔 데미지 배율")]
    public float finisherDamageMult = 2.0f;
    [Tooltip("1·2타 넉백 배율")]
    public float sweepKnockbackMult = 1.2f;
    [Tooltip("피니셔 넉백 배율")]
    public float finisherKnockbackMult = 2.5f;

    [Header("Reaper Cadence — Cooldown")]
    [Tooltip("패턴 완료 후 재사용 대기 시간 (초)")]
    public float patternCooldown = 11f;

    // ── 런타임 ───────────────────────────────────────────
    private LichReaperCadenceState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichReaperCadenceState(this);
    public override void OnRecycled()                       => _state = new LichReaperCadenceState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB == null || !lichBB.IsPhase2) return false;
        if (lichBB.ReaperCadenceCooldown > 0f) return false;
        float dist = Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position);
        return dist >= minTriggerRange && dist <= maxTriggerRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichReaperCadenceState — UnInterruptible (이동 자유)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichReaperCadenceState : UnInterruptibleState<LichReaperCadencePatternSO>
{
    private enum Phase { Approach, Sweep1, Gap, Sweep2, FeintHold, Finisher, Recovery }

    private const float SignalSeconds = 0.35f;   // 방향 고정 → 판정(0.15 → 0.35, 10-02 — 옆으로 피할 실제 창)

    private Phase      _phase;
    private float      _timer;
    private float      _lead;       // 이번 예고가 판정까지 차오르는 시간
    private float      _elapsed;    // 이번 예고가 뜬 뒤 흐른 시간
    private bool       _hitDone;
    private bool       _signaled;
    private bool       _parryOpen;
    private float      _yaw;
    private GameObject _guide;

    public LichReaperCadenceState(LichReaperCadencePatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase   = Phase.Approach;
        _timer   = 0f;
        _hitDone = false;

        var mc = LichPatternUtil.Mover(ctx);
        mc?.RequestMovementState(LichMovementState.DashClose);
        mc?.SetLocked(true);
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;
        _timer   += dt;
        _elapsed += dt;
        FollowGuide(ctx);

        switch (_phase)
        {
            case Phase.Approach:
            {
                float d = ctx.Runtime.PlayerTarget != null
                    ? LichPatternUtil.FlatDistance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position)
                    : 999f;
                if (d <= Data.approachStopRange || _timer >= Data.approachDuration)
                {
                    LichPatternUtil.Mover(ctx)?.RequestMovementState(LichMovementState.IdleHover);
                    BeginSweep(ctx, Phase.Sweep1, LichSwing.RightToLeft);
                }
                break;
            }

            case Phase.Sweep1:
            case Phase.Sweep2:
                TickWindup(ctx);
                if (!_hitDone && _timer >= Data.sweepHitDelay)
                    SweepHit(ctx, _phase == Phase.Sweep1 ? LichSwing.RightToLeft : LichSwing.LeftToRight);
                if (_timer >= Data.sweepDuration)
                {
                    if (_phase == Phase.Sweep1) Next(Phase.Gap);
                    else                        EnterFeint(ctx);
                }
                break;

            case Phase.Gap:
                if (_timer >= Data.gapDuration)
                    BeginSweep(ctx, Phase.Sweep2, LichSwing.LeftToRight);
                break;

            case Phase.FeintHold:
                // 한 박자 멈춤 — 피니셔 원이 이미 차오르고 있다(탐욕을 부르면 맞는다).
                TickWindup(ctx);
                if (_timer >= Data.feintHoldDuration) Next(Phase.Finisher);
                break;

            case Phase.Finisher:
                TickWindup(ctx);
                if (!_hitDone && _timer >= Data.finisherHitDelay) FinisherHit(ctx);
                if (_timer >= Data.finisherDuration)
                {
                    PatternGuideHelper.SafeDestroy(ref _guide);
                    LichPatternUtil.Lich(ctx)?.NotifyVulnerableWindow(Data.recoveryDuration);
                    Next(Phase.Recovery);
                }
                break;

            case Phase.Recovery:
                if (_timer >= Data.recoveryDuration)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _guide);
        LichPatternUtil.Mover(ctx)?.SetLocked(false);

        var lich = LichPatternUtil.Lich(ctx);
        if (lich?.LichBB != null)
            lich.LichBB.ReaperCadenceCooldown = Data.patternCooldown;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void Next(Phase phase)
    {
        _phase = phase;
        _timer = 0f;
    }

    /// <summary>수평 베기 하나 — 부채꼴 예고가 판정 크기로 차오르고, 접촉 프레임이 판정 순간에 온다.</summary>
    private void BeginSweep(MonsterContext ctx, Phase phase, LichSwing swing)
    {
        Next(phase);
        FacePlayerNow(ctx);
        OpenGuide(ctx, Data.sweepHitDelay,
                  PatternGuideHelper.Sector(LichPatternUtil.OnFloor(ctx, ctx.Transform.position),
                                            Data.sweepRadius, Data.sweepHalfAngle * 2f, _yaw, LichPatternUtil.Crimson));
        LichPatternUtil.Swing(ctx, swing, Data.sweepHitDelay);
    }

    /// <summary>피니셔 예고 — 멈춤 동안 이미 원이 차오른다. 내려찍기 동작은 피니셔 단계 시작부터.</summary>
    private void EnterFeint(MonsterContext ctx)
    {
        Next(Phase.FeintHold);
        FacePlayerNow(ctx);
        OpenGuide(ctx, Data.feintHoldDuration + Data.finisherHitDelay,
                  PatternGuideHelper.Disc(SlamPoint(ctx), FinisherRadius, LichPatternUtil.Crimson));
        LichPatternUtil.Swing(ctx, LichSwing.Overhead, Data.feintHoldDuration + Data.finisherHitDelay, 0.12f);
    }

    private void OpenGuide(MonsterContext ctx, float lead, GameObject guide)
    {
        PatternGuideHelper.SafeDestroy(ref _guide);
        _guide     = LichPatternUtil.PrepareTelegraph(guide, LichPatternUtil.Crimson);
        _lead      = Mathf.Max(0.05f, lead);
        _elapsed   = 0f;
        _hitDone   = false;
        _signaled  = false;
        _parryOpen = false;
        LichSfx.Play(LichSfxSlot.CastShort, ctx.Transform.position, 0.5f);
    }

    /// <summary>예고가 차오르는 동안 — 빨강 전까지는 제한 속도로 플레이어를 따라 돈다. 빨강이 뜨면 고정.</summary>
    private void TickWindup(MonsterContext ctx)
    {
        if (_hitDone) return;
        if (!_signaled)
        {
            TrackPlayer(ctx, Data.windupTrackSpeed);
            if (_guide != null && _phase != Phase.FeintHold && _phase != Phase.Finisher)
                _guide.transform.rotation = PatternGuideHelper.SectorRotation(_yaw);
        }
        if (LichPatternUtil.TickTelegraph(_guide, _elapsed, _lead, SignalSeconds, ref _signaled))
            LichPatternUtil.HoldFacing(ctx, _yaw);

        if (Data.parryable && !_parryOpen && _lead - _elapsed <= LichPatternUtil.ParryWindow)
        {
            _parryOpen = true;
            LichPatternUtil.OpenParry(ctx);
        }
    }

    private void SweepHit(MonsterContext ctx, LichSwing swing)
    {
        _hitDone = true;
        PatternGuideHelper.SafeDestroy(ref _guide);
        if (Parried(ctx)) return;

        Vector3 origin = LichPatternUtil.OnFloor(ctx, ctx.Transform.position);
        Vector3 dir    = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
        LichPatternUtil.SlashVfx(origin, dir, Data.sweepRadius, swing);
        LichSfx.Play(LichSfxSlot.ScytheSlash, origin);

        bool hit = InArc(ctx, origin, dir, Data.sweepRadius, Data.sweepHalfAngle)
                && LichPatternUtil.HitCircle(ctx, origin, Data.sweepRadius, Data.sweepDamageMult, Data.sweepKnockbackMult);
        LichPatternUtil.Impact(LichImpact.Slash, hit);
    }

    private void FinisherHit(MonsterContext ctx)
    {
        _hitDone = true;
        PatternGuideHelper.SafeDestroy(ref _guide);
        if (Parried(ctx)) return;

        Vector3 slam = SlamPoint(ctx);
        Vector3 dir  = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
        LichPatternUtil.SlashVfx(slam, dir, Data.finisherSlamOffset * 1.6f, LichSwing.Overhead);
        LichVfx.Play(LichVfxSlot.SlamImpact, slam, Quaternion.identity, LichPatternUtil.NovaScale(FinisherRadius));
        LichSfx.Play(LichSfxSlot.SlamImpact, slam);
        ArenaTileGrid.Active?.Tremble(slam, FinisherRadius, 0.5f);

        bool hit = LichPatternUtil.HitCircle(ctx, slam, FinisherRadius, Data.finisherDamageMult, Data.finisherKnockbackMult);
        LichPatternUtil.Impact(LichImpact.Heavy, hit);
    }

    /// <summary>튕겨냈으면 남은 타격을 접고 휘청으로(반격창이 곧 복귀 구간).</summary>
    private bool Parried(MonsterContext ctx)
    {
        _parryOpen = false;
        float stagger = Data.parryable ? LichPatternUtil.ConsumeParry(ctx) : 0f;
        if (stagger <= 0f) return false;
        PatternGuideHelper.SafeDestroy(ref _guide);
        Next(Phase.Recovery);
        _timer = -stagger;   // 휘청 동안 복귀를 미룬다
        return true;
    }

    private float FinisherRadius => Data.sweepRadius * Data.finisherRadiusMult;

    private Vector3 SlamPoint(MonsterContext ctx)
        => LichPatternUtil.OnFloor(ctx, ctx.Transform.position)
         + Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward * Data.finisherSlamOffset;

    private void FollowGuide(MonsterContext ctx)
    {
        if (_guide == null || _signaled) return;
        Vector3 p = _phase == Phase.FeintHold || _phase == Phase.Finisher
            ? SlamPoint(ctx)
            : LichPatternUtil.OnFloor(ctx, ctx.Transform.position);
        p.y += 0.03f;
        _guide.transform.position = p;
    }

    private static bool InArc(MonsterContext ctx, Vector3 origin, Vector3 dir, float radius, float halfAngle)
    {
        var target = ctx.Runtime.PlayerTarget;
        if (target == null) return false;
        Vector3 to = target.position - origin;
        to.y = 0f;
        if (to.magnitude > radius) return false;
        return to.sqrMagnitude <= 0.01f || Vector3.Angle(dir, to) <= halfAngle;
    }

    private void FacePlayerNow(MonsterContext ctx)
    {
        LichPatternUtil.FaceInstant(ctx, LichPatternUtil.PlayerFloorPos(ctx));
        _yaw = ctx.Transform.eulerAngles.y;
        LichPatternUtil.HoldFacing(ctx, _yaw, Data.windupTrackSpeed);
    }

    /// <summary>제한 속도로 플레이어 쪽 방위를 튼다 — 몸은 쥔 방향(HoldFacing)으로 따라온다.</summary>
    private void TrackPlayer(MonsterContext ctx, float degPerSec)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 to = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.001f) return;
        float want = Quaternion.LookRotation(to).eulerAngles.y;
        _yaw = Mathf.MoveTowardsAngle(_yaw, want, degPerSec * Time.deltaTime);
        LichPatternUtil.HoldFacing(ctx, _yaw, degPerSec * 2f);
    }
}
}
