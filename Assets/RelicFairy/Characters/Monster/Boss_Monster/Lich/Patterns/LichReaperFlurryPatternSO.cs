using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// C6 「사신의 연무」 — 2페이지 낫 + 마법 연계. 연출·UX 시나리오 §12-6.
///
/// 베기 한 번(× strikeCount):
///   O 돌진 예고 — 플레이어 쪽으로 짧게 돌진하며 발밑 앞 진홍 부채꼴이 차오른다(첫 베기는 조금 길게)
///   → 판정 0.25초 전 패링 창(낫이 금빛으로 빛남)
///   → A 베기 — 부채꼴 판정 · 붉은 베기 궤적
///   → 마법 추격 — 베기 직후 그 순간 플레이어 자리에 보라 마법진(예고가 차오른 뒤 착탄) → 피한 자리에서 또 움직여야 한다
/// 마지막 베기 뒤 E 무방비(endDuration) → R 복귀.
/// 패링하면 남은 베기와 추격이 취소되고 리치가 휘청한다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_ReaperFlurryPattern", fileName = "Lich_ReaperFlurryPattern")]
public class LichReaperFlurryPatternSO : BossPatternSO
{
    [Header("사신의 연무 — 발동")]
    public float maxTriggerRange = 14f;
    public float patternCooldown = 12f;

    [Header("사신의 연무 — 타이밍 (초)")]
    public int   strikeCount      = 3;
    [Tooltip("첫 베기 예고 — 처음은 조금 길게")]
    public float firstWindup      = 0.7f;
    [Tooltip("둘째 이후 베기 예고")]
    public float nextWindup       = 0.55f;
    [Tooltip("베기 뒤 다음 예고까지")]
    public float gapDuration      = 0.2f;
    [Tooltip("마지막 베기 뒤 무방비")]
    public float endDuration      = 1.2f;
    public float recoveryDuration = 0.4f;

    [Header("사신의 연무 — 판정")]
    public float hitRadius           = 3.8f;
    [Tooltip("부채꼴 반각 (도)")]
    public float hitHalfAngle        = 70f;
    public float damageMultiplier    = 1.1f;
    public float knockbackMultiplier = 1.2f;
    public bool  parryable           = true;

    [Header("사신의 연무 — 마무리 내려찍기 (09-19 동작 일치)")]
    [Tooltip("마지막 베기를 내려찍기로 — 동작(수직)과 같은 모양인 앞쪽 직선 판정. 끄면 수평 베기")]
    public bool  finisherOverhead  = true;
    [Tooltip("내려찍기 직선 판정 길이 (m) — 낫이 박힌 자리에서 앞으로 뻗는 충격")]
    public float finisherLength    = 7f;
    [Tooltip("직선 판정 반폭 (m)")]
    public float finisherHalfWidth = 1.1f;
    [Tooltip("내려찍기 예고 — 들어 올리는 동작이 보이게 조금 길게 (초)")]
    public float finisherWindup    = 0.75f;
    public float finisherDamage    = 1.4f;

    [Header("사신의 연무 — 돌진")]
    [Tooltip("베기마다 플레이어 쪽으로 돌진하는 최대 거리 (m)")]
    public float lungeDistance  = 4f;
    [Tooltip("돌진 뒤 플레이어와 남기는 거리 (m)")]
    public float lungeStopShort = 1.6f;

    [Header("사신의 연무 — 마법 추격")]
    [Tooltip("베기 직후 플레이어 자리에 떨어지는 마법진 반경 (m). 0이면 없음")]
    public float boltRadius = 2.2f;
    public float boltWarn   = 0.75f;
    public float boltDamage = 0.6f;

    // ── 런타임 ───────────────────────────────────────────
    private LichReaperFlurryState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichReaperFlurryState(this);
    public override void OnRecycled()                       => _state = new LichReaperFlurryState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB == null || !lichBB.IsPhase2) return false;
        if (lichBB.ReaperFlurryCooldown > 0f) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxTriggerRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichReaperFlurryState — 중단 불가(피해는 받음)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichReaperFlurryState : UnInterruptibleState<LichReaperFlurryPatternSO>
{
    private enum Phase { Windup, Gap, End, Recovery }

    private const float SignalSeconds     = 0.15f;
    private const float SlamPoint         = 2f;     // 내려찍기 — 낫이 바닥에 닿는 자리(리치 앞 m)

    private Phase      _phase;
    private float      _timer;
    private float      _windup;
    private int        _strikes;
    private float      _yaw;
    private float      _endSeconds;
    private bool       _parryOpen;
    private bool       _signaled;
    private bool       _finisher;   // 이번 베기가 마무리 내려찍기
    private GameObject _guide;

    public LichReaperFlurryState(LichReaperFlurryPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _strikes    = 0;
        _endSeconds = Data.endDuration;
        LichPatternUtil.Mover(ctx)?.SetLocked(true);
        LichPatternUtil.Mover(ctx)?.RequestMovementState(LichMovementState.AltitudeDescend);
        LichPatternUtil.Lich(ctx)?.PulseBook(0.4f);
        BeginStrike(ctx, Data.firstWindup);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        FollowGuide(ctx);

        switch (_phase)
        {
            case Phase.Windup:
            {
                // 돌진하는 동안 부채꼴이 플레이어를 따라 돈다 — 빨강(신호)이 뜨면 방향이 고정된다(옆으로 피하면 산다).
                if (!_finisher && !_signaled)
                {
                    _yaw = ctx.Transform.eulerAngles.y;
                    if (_guide != null) _guide.transform.rotation = PatternGuideHelper.SectorRotation(_yaw);
                }
                if (LichPatternUtil.TickTelegraph(_guide, _timer, _windup, SignalSeconds, ref _signaled))
                    LichPatternUtil.HoldFacing(ctx, _yaw);

                if (Data.parryable && !_parryOpen && _windup - _timer <= LichPatternUtil.ParryWindow)
                {
                    _parryOpen = true;
                    LichPatternUtil.OpenParry(ctx);
                }
                if (_timer >= _windup) Strike(ctx);
                break;
            }

            case Phase.Gap:
                if (_timer >= Data.gapDuration) BeginStrike(ctx, Data.nextWindup);
                break;

            case Phase.End:
                if (_timer >= _endSeconds) Next(Phase.Recovery);
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
        var lich = LichPatternUtil.Lich(ctx);
        lich?.MovementController?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.ReaperFlurryCooldown = Data.patternCooldown;
    }

    private void Next(Phase phase)
    {
        _phase = phase;
        _timer = 0f;
    }

    /// <summary>베기 하나를 준비한다 — 플레이어를 보고 짧게 돌진하며 부채꼴 예고를 띄운다.</summary>
    private void BeginStrike(MonsterContext ctx, float windup)
    {
        _finisher  = Data.finisherOverhead && _strikes == Data.strikeCount - 1;
        _windup    = Mathf.Max(0.35f, _finisher ? Mathf.Max(windup, Data.finisherWindup) : windup);
        _parryOpen = false;
        _signaled  = false;

        Vector3 player = LichPatternUtil.PlayerFloorPos(ctx);
        LichPatternUtil.FaceInstant(ctx, player);
        _yaw = ctx.Transform.eulerAngles.y;

        var mc = LichPatternUtil.Mover(ctx);
        if (_finisher)
        {
            // 내려찍기는 제자리에서 들어 올린다 — 방향은 지금 고정(직선이라 옆으로 비키면 산다).
            LichPatternUtil.HoldFacing(ctx, _yaw);
        }
        else if (mc != null)
        {
            Vector3 to   = player - LichPatternUtil.OnFloor(ctx, ctx.Transform.position);
            float   dist = to.magnitude;
            float   step = Mathf.Clamp(dist - Data.lungeStopShort, 0f, Data.lungeDistance);
            if (step > 0.1f)
                mc.ScriptMove(ctx.Transform.position + to / Mathf.Max(0.01f, dist) * step, _windup * 0.6f, 0f, facePlayer: true);
        }

        PatternGuideHelper.SafeDestroy(ref _guide);
        Vector3 origin = LichPatternUtil.OnFloor(ctx, ctx.Transform.position);
        Vector3 dir    = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
        _guide = LichPatternUtil.PrepareTelegraph(
            _finisher
                ? PatternGuideHelper.Beam(origin, dir, Data.finisherLength, Data.finisherHalfWidth * 2f, LichPatternUtil.Crimson)
                : PatternGuideHelper.Sector(origin, Data.hitRadius, Data.hitHalfAngle * 2f, _yaw, LichPatternUtil.Crimson),
            LichPatternUtil.Crimson);

        // 접촉 프레임 = 판정 순간. 수평 베기는 방향을 번갈아, 마무리는 머리 위에서 내려찍기.
        LichPatternUtil.Swing(ctx, SwingOf(_strikes), _windup, _finisher ? 0.12f : 0.07f);
        LichSfx.Play(LichSfxSlot.CastShort, ctx.Transform.position, 0.5f);
        Next(Phase.Windup);
    }

    private LichSwing SwingOf(int strikeIndex)
    {
        if (_finisher) return LichSwing.Overhead;
        return strikeIndex % 2 == 0 ? LichSwing.LeftToRight : LichSwing.RightToLeft;
    }

    private void Strike(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _guide);
        _parryOpen = false;

        float stagger = Data.parryable ? LichPatternUtil.ConsumeParry(ctx) : 0f;
        if (stagger > 0f)
        {
            _endSeconds = stagger;
            Next(Phase.End);
            return;
        }

        Vector3 origin = LichPatternUtil.OnFloor(ctx, ctx.Transform.position);
        Vector3 dir    = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
        LichSwing swing = SwingOf(_strikes);
        _strikes++;

        if (_finisher)
        {
            // 내려찍기 — 낫이 바닥에 박히고 충격이 앞으로 뻗는다(직선 판정 · 발판이 떨린다).
            Vector3 slam = origin + dir * SlamPoint;
            LichPatternUtil.SlashVfx(slam, dir, SlamPoint * 1.3f, LichSwing.Overhead, LichPatternUtil.Crimson);
            LichVfx.Play(LichVfxSlot.SlamImpact, slam, Quaternion.identity, LichPatternUtil.NovaScale(Data.finisherHalfWidth * 1.5f));
            LichSfx.Play(LichSfxSlot.SlamImpact, slam);
            var grid = ArenaTileGrid.Active;
            if (grid != null)
                for (float d = SlamPoint; d <= Data.finisherLength; d += 2.5f)
                    grid.Tremble(origin + dir * d, 2f, 0.5f);
            bool hitLine = LichPatternUtil.HitBeam(ctx, origin, dir, Data.finisherLength, Data.finisherHalfWidth,
                                                   Data.finisherDamage, Data.knockbackMultiplier);
            LichPatternUtil.Impact(LichImpact.Heavy, hitLine);
        }
        else
        {
            LichPatternUtil.SlashVfx(origin, dir, Data.hitRadius, swing, LichPatternUtil.Crimson);
            LichSfx.Play(LichSfxSlot.ScytheSlash, origin);
            LichPatternUtil.Impact(LichImpact.Slash, HitArc(ctx, origin, dir));
        }

        // 마법 추격 — 지금 플레이어가 선 자리. 베기를 피한 자리에서 한 번 더 움직이게 한다.
        if (Data.boltRadius > 0f)
            LichHazards.DelayedBlast(ctx, LichPatternUtil.PlayerFloorPos(ctx), Data.boltRadius, Data.boltWarn,
                                     Data.boltDamage, LichVfxSlot.BoltImpact, LichPatternUtil.BoltScale(Data.boltRadius));

        if (_strikes < Data.strikeCount)
        {
            Next(Phase.Gap);
            return;
        }
        LichPatternUtil.Lich(ctx)?.NotifyVulnerableWindow(_endSeconds);
        Next(Phase.End);
    }

    private bool HitArc(MonsterContext ctx, Vector3 origin, Vector3 dir)
    {
        var target = ctx.Runtime.PlayerTarget;
        if (target == null) return false;
        Vector3 to = target.position - origin;
        to.y = 0f;
        if (to.magnitude > Data.hitRadius) return false;
        if (to.sqrMagnitude > 0.01f && Vector3.Angle(dir, to) > Data.hitHalfAngle) return false;
        return LichPatternUtil.HitCircle(ctx, origin, Data.hitRadius, Data.damageMultiplier, Data.knockbackMultiplier);
    }

    private void FollowGuide(MonsterContext ctx)
    {
        if (_guide == null || _finisher) return;   // 직선 예고는 제자리(리치도 움직이지 않는다)
        Vector3 p = LichPatternUtil.OnFloor(ctx, ctx.Transform.position);
        p.y += 0.03f;
        _guide.transform.position = p;
    }
}
}
