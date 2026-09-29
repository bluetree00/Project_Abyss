using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 낫 휘두르기 — 해방판 R1 「사신의 호」 / 봉인판 C1 「사슬 낫 휘두르기」. 리치 설계서 §3-4 · §4-4 · §13.
/// 같은 클래스, 에셋 두 개(값만 다르다).
///
/// O 끌어당김(windupDuration) — 플레이어 쪽으로 짧게 돌진하며 낫을 뒤로 당긴다. 발밑 앞쪽 반원이 채워진다
/// → S 신호(signalDuration) — 반원이 빨갛게
/// → A 휘두르기 × swingCount — 휘두를 때마다 반원 판정. 두 번째는 <b>반대편</b> 반원이 첫 판정 직후 뜬다(등 뒤로 피하면 맞는다)
/// → E 반격창(endDuration) — 해방판: 낫이 바닥에 박힘 / 봉인판: 사슬이 당겨져 뒤로 끌려가며 멈칫(chainYank)
/// → R 복귀
///
/// 패링(§12-3): 휘두를 때마다 판정 0.25초 전 낫이 금빛으로 빛난다 — 그 사이에 맞받아치면 남은 휘두르기가 취소되고 리치가 휘청한다.
/// 마법 추격(§12-6): 휘두르기가 다 끝나면 보라탄이 플레이어가 피한 자리 둘레로 떨어진다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_ScytheSweepPattern", fileName = "Lich_ScytheSweepPattern")]
public class LichScytheSweepPatternSO : BossPatternSO
{
    [Header("낫 휘두르기 — 발동")]
    public float minTriggerRange = 0f;
    public float maxTriggerRange = 20f;
    public float patternCooldown = 7f;

    [Header("낫 휘두르기 — 타이밍 (초)")]
    public float windupDuration   = 0.9f;
    public float signalDuration   = 0.15f;
    [Tooltip("한 번 휘두르기 — 다음 반원이 뜨고 판정까지")]
    public float swingActive      = 0.35f;
    public int   swingCount       = 2;
    public float endDuration      = 0.6f;
    public float recoveryDuration = 0.4f;

    [Header("낫 휘두르기 — 판정")]
    public float sweepRadius         = 8f;
    [Tooltip("반원 판정 반각 (도). 90 = 앞 180°")]
    public float sweepHalfAngle      = 90f;
    public float damageMultiplier    = 1.6f;
    public float knockbackMultiplier = 2.0f;

    [Header("낫 휘두르기 — 움직임")]
    [Tooltip("휘두르기 전에 플레이어 쪽으로 돌진하는 최대 거리 (m)")]
    public float lungeDistance = 3f;
    [Tooltip("돌진 뒤 플레이어와 남기는 거리 (m)")]
    public float lungeStopShort = 1.5f;
    [Tooltip("봉인판: 휘두른 뒤 사슬에 끌려 뒤로 밀리는 거리 (m). 0이면 없음")]
    public float chainYank = 0f;

    [Header("낫 휘두르기 — 패링 · 마법 추격 (연출·UX 시나리오 §12)")]
    [Tooltip("판정 직전 패링 창을 연다(낫이 금빛으로 빛남)")]
    public bool  parryable        = true;
    [Tooltip("휘두르기가 끝난 뒤 떨어지는 보라탄 수(0 = 없음) — 플레이어 자리 + 좌우")]
    public int   followBolts      = 0;
    public float followBoltWarn   = 0.8f;
    public float followBoltRadius = 2.2f;
    [Tooltip("좌우 탄이 플레이어 자리에서 떨어진 거리 (m)")]
    public float followBoltSpread = 3.5f;
    public float followBoltDamage = 0.6f;

    // ── 런타임 ───────────────────────────────────────────
    private LichScytheSweepState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichScytheSweepState(this);
    public override void OnRecycled()                       => _state = new LichScytheSweepState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB == null || !lichBB.IsPhase2) return false;
        if (lichBB.ScytheSweepCooldown > 0f) return false;
        float dist = Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position);
        return dist >= minTriggerRange && dist <= maxTriggerRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichScytheSweepState — 중단 불가(피해는 받음)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichScytheSweepState : UnInterruptibleState<LichScytheSweepPatternSO>
{
    private enum Phase { Windup, Signal, Swing, End, Recovery }

    private Phase      _phase;
    private float      _timer;
    private int        _swings;
    private float      _yaw;          // 이번 휘두르기가 향하는 방위(도)
    private GameObject _guide;
    private bool       _parryOpen;    // 다음 휘두르기의 패링 창을 열었다
    private float      _endSeconds;   // 반격창 길이 — 튕겨내면 휘청 시간으로 바뀐다

    public LichScytheSweepState(LichScytheSweepPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase      = Phase.Windup;
        _timer      = 0f;
        _swings     = 0;
        _parryOpen  = false;
        _endSeconds = Data.endDuration;

        // 첫 휘두름(왼쪽 → 오른쪽)의 접촉 프레임이 첫 판정 순간에 오게 한다 — 끌어당김이 늘어지고 베기는 빠르게.
        LichPatternUtil.Swing(ctx, SwingOf(1), Data.windupDuration + Data.signalDuration);

        Vector3 player = LichPatternUtil.PlayerFloorPos(ctx);
        LichPatternUtil.FaceInstant(ctx, player);
        _yaw = ctx.Transform.eulerAngles.y;

        var mc = LichPatternUtil.Mover(ctx);
        if (mc != null)
        {
            mc.SetLocked(true);
            Vector3 to   = player - LichPatternUtil.OnFloor(ctx, ctx.Transform.position);
            float   dist = to.magnitude;
            float   step = Mathf.Clamp(dist - Data.lungeStopShort, 0f, Data.lungeDistance);
            if (step > 0.1f)
                mc.ScriptMove(ctx.Transform.position + to / Mathf.Max(0.01f, dist) * step,
                              Data.windupDuration * 0.6f, 0f, facePlayer: true);
        }

        ShowGuide(ctx, _yaw);
        LichSfx.Play(LichSfxSlot.CastShort, ctx.Transform.position, 0.6f);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        FollowGuide(ctx);

        switch (_phase)
        {
            case Phase.Windup:
            {
                // 판정 크기의 진홍 반원이 안에서부터 차오른다 → 끝나면 빨강(신호).
                float t = Mathf.Clamp01(_timer / Data.windupDuration);
                PatternGuideHelper.SetProgress(_guide, t);
                _yaw = ctx.Transform.eulerAngles.y;
                if (_guide != null) _guide.transform.rotation = PatternGuideHelper.SectorRotation(_yaw);
                TryOpenParry(ctx, Data.windupDuration - _timer + Data.signalDuration);
                if (t >= 1f)
                {
                    Signal(_guide);
                    LichPatternUtil.HoldFacing(ctx, _yaw);   // 빨강이 뜬 뒤엔 몸도 판정 방향에 고정
                    Next(Phase.Signal);
                }
                break;
            }

            case Phase.Signal:
                TryOpenParry(ctx, Data.signalDuration - _timer);
                if (_timer >= Data.signalDuration && Swing(ctx))
                    Next(Phase.Swing);
                break;

            case Phase.Swing:
                if (_swings < Data.swingCount) TryOpenParry(ctx, Data.swingActive - _timer);
                if (_timer >= Data.swingActive)
                {
                    if (_swings < Data.swingCount)
                    {
                        if (Swing(ctx)) _timer = 0f;
                    }
                    else
                    {
                        PatternGuideHelper.SafeDestroy(ref _guide);
                        EndSwings(ctx);
                        SpawnFollowBolts(ctx);
                        LichPatternUtil.Lich(ctx)?.NotifyVulnerableWindow(_endSeconds);
                        Next(Phase.End);
                    }
                }
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
            lich.LichBB.ScytheSweepCooldown = Data.patternCooldown;
    }

    private void Next(Phase phase)
    {
        _phase = phase;
        _timer = 0f;
    }

    /// <summary>
    /// 지금 반원을 판정하고, 남은 휘두르기가 있으면 반대편 반원을 띄운다.
    /// 튕겨냈으면 판정 없이 휘청으로 넘어가고 false(남은 휘두르기 취소).
    /// </summary>
    private bool Swing(MonsterContext ctx)
    {
        _parryOpen = false;
        float stagger = Data.parryable ? LichPatternUtil.ConsumeParry(ctx) : 0f;
        if (stagger > 0f)
        {
            PatternGuideHelper.SafeDestroy(ref _guide);
            _endSeconds = stagger;
            Next(Phase.End);
            return false;
        }

        _swings++;
        Vector3 origin = LichPatternUtil.OnFloor(ctx, ctx.Transform.position);
        Vector3 dir    = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;

        LichPatternUtil.SlashVfx(origin, dir, Data.sweepRadius, SwingOf(_swings));
        LichPatternUtil.Impact(LichImpact.Slash, HitArc(ctx, origin, dir));

        if (_swings < Data.swingCount)
        {
            // 다음 휘두름은 반대편 반원 — 몸을 빠르게 돌려 등 뒤를 베는 동작으로 보이게(반대 방향 스윙).
            _yaw += 180f;
            LichPatternUtil.HoldFacing(ctx, _yaw, 900f);
            LichPatternUtil.Swing(ctx, SwingOf(_swings + 1), Data.swingActive);
            ShowGuide(ctx, _yaw);
            PatternGuideHelper.SetProgress(_guide, 1f);
            Signal(_guide);
        }
        return true;
    }

    /// <summary>n번째 휘두름의 동작 — 왼쪽→오른쪽, 오른쪽→왼쪽을 번갈아(왕복 베기).</summary>
    private static LichSwing SwingOf(int n) => n % 2 == 1 ? LichSwing.LeftToRight : LichSwing.RightToLeft;

    /// <summary>다음 판정까지 남은 시간이 패링 창 안으로 들어오면 창을 연다(휘두르기마다 한 번).</summary>
    private void TryOpenParry(MonsterContext ctx, float secondsUntilSwing)
    {
        if (!Data.parryable || _parryOpen || secondsUntilSwing > LichPatternUtil.ParryWindow) return;
        _parryOpen = true;
        LichPatternUtil.OpenParry(ctx);
    }

    /// <summary>마법 추격 — 플레이어 자리와 좌우에 보라탄(예고가 차오른 뒤 착탄).</summary>
    private void SpawnFollowBolts(MonsterContext ctx)
    {
        if (Data.followBolts <= 0) return;
        Vector3 player = LichPatternUtil.PlayerFloorPos(ctx);
        Vector3 side   = Quaternion.Euler(0f, _yaw + 90f, 0f) * Vector3.forward;
        for (int i = 0; i < Data.followBolts; i++)
        {
            // 0 = 자리, 1 = 오른쪽, 2 = 왼쪽, 3.. = 바깥으로 번갈아
            int   ring   = (i + 1) / 2;
            float sign   = i % 2 == 1 ? 1f : -1f;
            Vector3 at   = player + side * (sign * ring * Data.followBoltSpread);
            LichHazards.DelayedBlast(ctx, LichPatternUtil.OnFloor(ctx, at), Data.followBoltRadius,
                                     Data.followBoltWarn + i * 0.12f, Data.followBoltDamage,
                                     LichVfxSlot.BoltImpact, LichPatternUtil.BoltScale(Data.followBoltRadius));
        }
        LichSfx.Play(LichSfxSlot.CastShort, ctx.Transform.position, 0.7f);
    }

    private bool HitArc(MonsterContext ctx, Vector3 origin, Vector3 dir)
    {
        var target = ctx.Runtime.PlayerTarget;
        if (target == null) return false;
        Vector3 to = target.position - origin;
        to.y = 0f;
        if (to.magnitude > Data.sweepRadius) return false;
        if (to.sqrMagnitude > 0.01f && Vector3.Angle(dir, to) > Data.sweepHalfAngle) return false;
        return LichPatternUtil.HitCircle(ctx, origin, Data.sweepRadius, Data.damageMultiplier, Data.knockbackMultiplier);
    }

    private static void Signal(GameObject guide)
    {
        PatternGuideHelper.SetColor(guide, LichPatternUtil.Lethal);
        PatternGuideHelper.SetFlow(guide, LichPatternUtil.Lethal);
        PatternGuideHelper.SetIntensity(guide, 2f);
    }

    private void EndSwings(MonsterContext ctx)
    {
        if (Data.chainYank <= 0f) return;
        // 봉인판 — 사슬이 팽팽해지며 리치를 뒤로 끌어당긴다.
        var mc = LichPatternUtil.Mover(ctx);
        mc?.ScriptMove(ctx.Transform.position - ctx.Transform.forward * Data.chainYank, 0.35f, 0f, facePlayer: true);
        LichVfx.Play(LichVfxSlot.SealBurst, ctx.Transform.position + Vector3.up, Quaternion.identity, 0.5f);
        LichHazards.PulseBoundChains();
        LichSfx.Play(LichSfxSlot.ChainPulse, ctx.Transform.position);
        ctx.Animator?.CrossFade("GetHit", 0.05f);
    }

    private void ShowGuide(MonsterContext ctx, float yaw)
    {
        PatternGuideHelper.SafeDestroy(ref _guide);
        _guide = LichPatternUtil.PrepareTelegraph(
            PatternGuideHelper.Sector(LichPatternUtil.OnFloor(ctx, ctx.Transform.position),
                                      Data.sweepRadius, Data.sweepHalfAngle * 2f, yaw, LichPatternUtil.Crimson),
            LichPatternUtil.Crimson);
    }

    private void FollowGuide(MonsterContext ctx)
    {
        if (_guide == null) return;
        Vector3 p = LichPatternUtil.OnFloor(ctx, ctx.Transform.position);
        p.y += 0.03f;
        _guide.transform.position = p;
    }
}
}
