using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// M5 「공간 도약」 — 사라졌다가 표적 머리 위 고공에서 내려찍는다. 리치 설계서 §3-2 · §13.
///
/// O 사라짐(vanishDuration) — 보라 연기, 몸이 사라진다(이 동안 무적)
/// → S 표적 원(targetDuration) — 발동 순간 플레이어 발밑에 반경 4 m 원이 차오른다(추적 없음). 끝 0.15초는 빨강.
///   끝나기 직전 표적 위 고공(dropHeight)에 나타난다
/// → A 낙하(dropDuration) — 착지 순간 원 판정 + 충격파 + 카메라 흔들림
/// → E 주저앉음(punishDuration) — 1페이지에서 가장 긴 반격창
/// → R 떠오름(recoveryDuration)
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_TeleportStrikePattern", fileName = "Lich_TeleportStrikePattern")]
public class LichTeleportStrikePatternSO : BossPatternSO
{
    [Header("M5 — 발동")]
    public float maxTriggerRange = 30f;
    public float patternCooldown = 6f;

    [Header("M5 — 타이밍 (초)")]
    public float vanishDuration   = 0.7f;
    [Tooltip("표적 원이 조여드는 시간 — 이만큼 피할 시간이 있다")]
    public float targetDuration   = 1.0f;
    [Tooltip("표적 원이 끝나기 이만큼 전에 고공에 나타난다")]
    public float appearLead       = 0.3f;
    public float dropDuration     = 0.25f;
    public float punishDuration   = 0.9f;
    public float recoveryDuration = 0.5f;

    [Header("M5 — 판정")]
    public float hitRadius           = 4f;
    public float damageMultiplier    = 1.4f;
    public float knockbackMultiplier = 1.8f;

    [Header("M5 — 움직임")]
    [Tooltip("나타나는 높이 (바닥 기준 m)")]
    public float dropHeight = 8f;

    [Header("M5 — 바닥 파괴 (연출·UX 시나리오 §12-4)")]
    [Tooltip("착지 순간 둘레 칸을 잠깐 부순다 — 칸 중심이 이 반경(m) 안. 0이면 없음. 리치가 선 칸은 남는다")]
    public float breakRadius  = 0f;
    [Tooltip("붉게 흔들리는 시간(초) — 이 사이에 떠나야 한다")]
    public float breakWarn    = 1.0f;
    [Tooltip("부서진 뒤 스스로 복구까지(초). 음수 = 복구 패턴(M8 원소 재편 · T1 전환)까지 구멍으로 남아 누적")]
    public float breakSeconds = 5f;

    [Header("M5⁺ — 악몽판(완전설계 §4-2): 연속 도약")]
    [Tooltip("내려찍기 횟수 — 1이면 한 번, 악몽판 2(두 번째 원은 첫 착지 뒤 nextLeapDelay초에 뜬다)")]
    [Range(1, 3)]
    public int   leapCount     = 1;
    public float nextLeapDelay = 0.4f;
    [Tooltip("다음 원이 뜬 뒤 내려찍기까지(예고 시간)")]
    public float nextLeapWarn  = 0.8f;

    // ── 런타임 ───────────────────────────────────────────
    private LichTeleportStrikeState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichTeleportStrikeState(this);
    public override void OnRecycled()                       => _state = new LichTeleportStrikeState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB != null && lichBB.TeleportStrikeCooldown > 0f) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxTriggerRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichTeleportStrikeState — 중단 불가 + 사라진 동안 무적
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichTeleportStrikeState : UnInterruptibleState<LichTeleportStrikePatternSO>
{
    private enum Phase { Vanish, Target, Leap, Drop, Punish, Recovery }

    private Phase      _phase;
    private float      _timer;
    private bool       _appeared;
    private bool       _hidden;
    private Vector3    _target;
    private float      _dropFromY;
    private GameObject _targetDisc;
    private bool       _signaled;
    private int        _leap;       // 지금 몇 번째 도약(1부터)
    private Vector3    _leapFrom;

    private const float SignalSeconds = 0.15f;

    public LichTeleportStrikeState(LichTeleportStrikePatternSO data) : base(data) { }

    /// <summary>사라져 있는 동안은 피해를 받지 않는다 — 보이지 않는 몸을 칠 수 없게.</summary>
    public override SpecialStateConstraint Constraints =>
        _hidden ? base.Constraints | SpecialStateConstraint.Invincible : base.Constraints;

    public override void Enter(MonsterContext ctx)
    {
        _phase    = Phase.Vanish;
        _timer    = 0f;
        _appeared = false;
        _hidden   = false;
        _signaled = false;
        _leap     = 1;

        var lich = LichPatternUtil.Lich(ctx);
        lich?.MovementController?.SetLocked(true);

        ctx.Animator?.CrossFade("TeleportStrike", 0.1f);
        LichVfx.Play(LichVfxSlot.TeleportVanish, ctx.Transform.position, ctx.Transform.rotation);
        LichSfx.Play(LichSfxSlot.Vanish, ctx.Transform.position);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        var lich = LichPatternUtil.Lich(ctx);
        var mc   = lich?.MovementController;

        switch (_phase)
        {
            case Phase.Vanish:
                // 연기가 몸을 덮는 순간(절반)에 사라진다.
                if (!_hidden && _timer >= Data.vanishDuration * 0.5f)
                {
                    _hidden = true;
                    lich?.SetBodyVisible(false);
                }
                if (_timer >= Data.vanishDuration)
                {
                    _target     = LichPatternUtil.PlayerFloorPos(ctx);
                    _targetDisc = LichPatternUtil.PrepareTelegraph(
                        PatternGuideHelper.Disc(_target, Data.hitRadius, LichPatternUtil.Arcane), LichPatternUtil.Arcane);
                    LichSfx.Play(LichSfxSlot.CircleSpawn, _target);
                    _phase      = Phase.Target;
                    _timer      = 0f;
                }
                break;

            case Phase.Target:
            {
                LichPatternUtil.TickTelegraph(_targetDisc, _timer, Data.targetDuration, SignalSeconds, ref _signaled);

                if (!_appeared && _timer >= Data.targetDuration - Data.appearLead)
                {
                    _appeared  = true;
                    _dropFromY = LichPatternUtil.FloorY(ctx) + Data.dropHeight;
                    mc?.SnapTo(_target);
                    mc?.RequestMovementState(LichMovementState.AltitudeDescend);   // 수평은 멈춰 서서 플레이어만 본다
                    mc?.HoldAltitude(true);      // 낙하·주저앉음 동안 고도는 패턴이 쥔다
                    mc?.SnapAltitude(_dropFromY);
                    LichPatternUtil.FaceInstant(ctx, ctx.Runtime.PlayerTarget != null ? ctx.Runtime.PlayerTarget.position : _target + ctx.Transform.forward);
                    _hidden = false;
                    lich?.SetBodyVisible(true);
                    LichVfx.Play(LichVfxSlot.TeleportAppear, ctx.Transform.position, ctx.Transform.rotation);
                    LichSfx.Play(LichSfxSlot.Appear, ctx.Transform.position);
                }

                if (_timer >= Data.targetDuration)
                {
                    _phase = Phase.Drop;
                    _timer = 0f;
                }
                break;
            }

            case Phase.Leap:
            {
                // 착지 자리 → 새 원으로 호를 그리며 떠올랐다가(Drop이 내려찍는다).
                float rise = Mathf.Max(0.05f, Data.nextLeapWarn - Data.dropDuration);
                LichPatternUtil.TickTelegraph(_targetDisc, _timer, Data.nextLeapWarn, SignalSeconds, ref _signaled);
                float k     = Mathf.Clamp01(_timer / rise);
                float floor = LichPatternUtil.FloorY(ctx);
                _dropFromY  = floor + Data.dropHeight * 0.6f;
                mc?.SnapTo(Vector3.Lerp(_leapFrom, _target, k));
                mc?.SnapAltitude(Mathf.Lerp(floor, _dropFromY, 1f - (1f - k) * (1f - k)));
                if (k >= 1f)
                {
                    _phase = Phase.Drop;
                    _timer = 0f;
                }
                break;
            }

            case Phase.Drop:
            {
                float t = Mathf.Clamp01(_timer / Data.dropDuration);
                float floor = LichPatternUtil.FloorY(ctx);
                mc?.SnapAltitude(Mathf.Lerp(_dropFromY, floor, t * t));

                if (t >= 1f)
                {
                    Land(ctx);
                    _phase = Phase.Punish;
                    _timer = 0f;
                }
                break;
            }

            case Phase.Punish:
                mc?.SnapAltitude(LichPatternUtil.FloorY(ctx));
                if (_leap < Data.leapCount && _timer >= Data.nextLeapDelay)
                {
                    BeginNextLeap(ctx);
                    break;
                }
                if (_timer >= Data.punishDuration)
                {
                    mc?.HoldAltitude(false);     // 떠오름 — 기본 고도로 부드럽게 복귀
                    _phase = Phase.Recovery;
                    _timer = 0f;
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
        PatternGuideHelper.SafeDestroy(ref _targetDisc);

        var lich = LichPatternUtil.Lich(ctx);
        if (_hidden || !_appeared)
        {
            // 도중에 끊겨도 몸은 반드시 돌아온다.
            _hidden = false;
            lich?.SetBodyVisible(true);
        }
        lich?.MovementController?.HoldAltitude(false);
        lich?.MovementController?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.TeleportStrikeCooldown = Data.patternCooldown;
    }

    /// <summary>M5⁺ — 다음 도약: 지금 플레이어 자리에 원, 리치는 호를 그리며 그 위로.</summary>
    private void BeginNextLeap(MonsterContext ctx)
    {
        _leap++;
        _leapFrom   = ctx.Transform.position;
        _target     = LichPatternUtil.PlayerFloorPos(ctx);
        _signaled   = false;
        _targetDisc = LichPatternUtil.PrepareTelegraph(
            PatternGuideHelper.Disc(_target, Data.hitRadius, LichPatternUtil.Arcane), LichPatternUtil.Arcane);
        LichSfx.Play(LichSfxSlot.CircleSpawn, _target);
        LichPatternUtil.FaceInstant(ctx, _target);
        ctx.Animator?.CrossFade("TeleportStrike", 0.05f, 0, 0.3f);
        _phase = Phase.Leap;
        _timer = 0f;
    }

    private void Land(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _targetDisc);
        ctx.Animator?.CrossFade("GetHit", 0.05f);   // 착지 반동 — 주저앉음 대용(전용 모션은 L7)

        LichVfx.Play(LichVfxSlot.SlamImpact, _target, Quaternion.identity, LichPatternUtil.NovaScale(Data.hitRadius));
        LichSfx.Play(LichSfxSlot.SlamImpact, _target);
        LichCrack.Spawn(_target, Data.hitRadius * 1.8f, 12f);

        bool hit = LichPatternUtil.HitCircle(ctx, _target, Data.hitRadius, Data.damageMultiplier, Data.knockbackMultiplier);
        LichPatternUtil.Impact(LichImpact.Heavy, hit);
        if (_leap >= Data.leapCount)
            LichPatternUtil.Lich(ctx)?.NotifyVulnerableWindow(Data.punishDuration);   // 1페이지의 보상 — 가장 긴 반격창(연속 도약은 마지막 착지에만)
        // 착지 충격에 둘레 바닥이 금 간다 — 반격하러 붙은 자리가 곧 꺼진다(리치 발밑 칸은 남는다).
        // 1페이지만 — 2페이지엔 바닥을 되살리는 패턴(M8)이 없어 구멍이 끝없이 쌓인다(09-19 실측).
        if ((LichPatternUtil.Lich(ctx)?.LichBB?.Page ?? 1) == 1)
            LichPatternUtil.BreakFloor(_target, Data.breakRadius, Data.breakWarn, Data.breakSeconds, keepCenterCell: true);
    }
}
}
