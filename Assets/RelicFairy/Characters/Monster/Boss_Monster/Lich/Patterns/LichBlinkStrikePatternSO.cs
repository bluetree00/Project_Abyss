using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 순간 베기 — 해방판 R4 「순간 베기」(3연) / 봉인판 C4 「사슬 순간 베기」(1회). 리치 설계서 §3-4 · §4-4 · §13.
/// 같은 클래스, 에셋 두 개(값만 다르다).
///
/// 한 번의 베기:
///   예고(markDuration) — 그 순간 플레이어 <b>등 뒤</b>에 흰 잔상 표식(베기 반원) — 거기로 온다
///   → 점멸 — 리치가 사라졌다 잔상 자리에 나타나 플레이어를 본다
///   → 베기(slashDelay) — 반원 판정
///   → 다음 베기까지 blinkInterval
/// 마지막 베기 뒤 E 반격창(endDuration) → R 복귀
///
/// 패링(§12-3): 나타나서 베기까지(slashDelay)가 곧 패링 창 — 낫이 금빛으로 빛날 때 맞받아치면 남은 베기가 취소되고 휘청.
/// 여운(§12-5): 벤 자리에 어둠 장판이 남는다(0.4초마다 피해 — 회피 한 번으로 못 버틴다).
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_BlinkStrikePattern", fileName = "Lich_BlinkStrikePattern")]
public class LichBlinkStrikePatternSO : BossPatternSO
{
    [Header("순간 베기 — 발동")]
    public float maxTriggerRange = 25f;
    public float patternCooldown = 9f;

    [Header("순간 베기 — 타이밍 (초)")]
    public int   strikeCount      = 3;
    public float markDuration     = 0.45f;
    public float slashDelay       = 0.25f;
    public float blinkInterval    = 0.2f;
    public float endDuration      = 0.6f;
    public float recoveryDuration = 0.4f;

    [Header("순간 베기 — 판정")]
    [Tooltip("플레이어 뒤로 나타나는 거리 (m)")]
    public float backOffset          = 2.2f;
    public float hitRadius           = 3.2f;
    public float hitHalfAngle        = 75f;
    public float damageMultiplier    = 1.0f;
    public float knockbackMultiplier = 1.5f;

    [Header("순간 베기 — 패링 · 여운 (연출·UX 시나리오 §12)")]
    public bool  parryable     = true;
    [Tooltip("벤 자리에 남는 어둠 장판 반경 (m). 0이면 없음")]
    public float lingerRadius  = 0f;
    public float lingerSeconds = 1.8f;
    [Tooltip("장판 한 틱 피해 배율")]
    public float lingerDamage  = 0.15f;

    // ── 런타임 ───────────────────────────────────────────
    private LichBlinkStrikeState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichBlinkStrikeState(this);
    public override void OnRecycled()                       => _state = new LichBlinkStrikeState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB == null || !lichBB.IsPhase2) return false;
        if (lichBB.BlinkStrikeCooldown > 0f) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxTriggerRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichBlinkStrikeState — 중단 불가 + 사라진 동안 무적
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichBlinkStrikeState : UnInterruptibleState<LichBlinkStrikePatternSO>
{
    private enum Phase { Mark, Slash, Interval, End, Recovery }

    private static readonly Color AfterimageColor = new Color(0.9f, 0.9f, 1f);   // 흰 잔상

    private Phase      _phase;
    private float      _timer;
    private int        _strikes;
    private bool       _hidden;
    private Vector3    _dest;
    private float      _yaw;
    private GameObject _mark;
    private bool       _signaled;
    private bool       _parryOpen;
    private float      _endSeconds;

    public LichBlinkStrikeState(LichBlinkStrikePatternSO data) : base(data) { }

    public override SpecialStateConstraint Constraints =>
        _hidden ? base.Constraints | SpecialStateConstraint.Invincible : base.Constraints;

    public override void Enter(MonsterContext ctx)
    {
        _strikes    = 0;
        _hidden     = false;
        _signaled   = false;
        _parryOpen  = false;
        _endSeconds = Data.endDuration;
        LichPatternUtil.Mover(ctx)?.SetLocked(true);
        LichPatternUtil.Mover(ctx)?.RequestMovementState(LichMovementState.AltitudeDescend);
        ctx.Animator?.CrossFade("TeleportStrike", 0.1f);   // 사라지는 동작 — 베기는 나타날 때 휘두름 드라이버가 튼다
        BeginMark(ctx);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        var lich = LichPatternUtil.Lich(ctx);

        switch (_phase)
        {
            case Phase.Mark:
                PatternGuideHelper.SetProgress(_mark, _timer / Mathf.Max(0.01f, Data.markDuration));
                // 표식 절반에서 사라진다 — 나타나기 전까지 무적.
                if (!_hidden && _timer >= Data.markDuration * 0.5f)
                {
                    _hidden = true;
                    LichVfx.Play(LichVfxSlot.TeleportVanish, ctx.Transform.position + Vector3.up, Quaternion.identity, 0.5f);
                    LichSfx.Play(LichSfxSlot.Vanish, ctx.Transform.position);
                    lich?.SetBodyVisible(false);
                }
                if (_timer >= Data.markDuration)
                {
                    Appear(ctx);
                    Next(Phase.Slash);
                }
                break;

            case Phase.Slash:
                if (Data.parryable && !_parryOpen && Data.slashDelay - _timer <= LichPatternUtil.ParryWindow)
                {
                    _parryOpen = true;
                    LichPatternUtil.OpenParry(ctx);
                }
                if (_timer >= Data.slashDelay)
                {
                    if (Slash(ctx)) Next(_strikes < Data.strikeCount ? Phase.Interval : Phase.End);
                }
                break;

            case Phase.Interval:
                if (_timer >= Data.blinkInterval) BeginMark(ctx);
                break;

            case Phase.End:
                if (!_signaled)
                {
                    _signaled = true;   // 마지막 베기 뒤 반격창 — 한 번만 알린다
                    lich?.NotifyVulnerableWindow(_endSeconds);
                }
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
        PatternGuideHelper.SafeDestroy(ref _mark);
        var lich = LichPatternUtil.Lich(ctx);
        if (_hidden)
        {
            _hidden = false;
            lich?.SetBodyVisible(true);
        }
        lich?.MovementController?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.BlinkStrikeCooldown = Data.patternCooldown;
    }

    private void Next(Phase phase)
    {
        _phase = phase;
        _timer = 0f;
    }

    /// <summary>지금 플레이어 등 뒤 — 그 자리에 흰 잔상 반원(플레이어를 향한)을 띄운다.</summary>
    private void BeginMark(MonsterContext ctx)
    {
        var     target = ctx.Runtime.PlayerTarget;
        Vector3 player = LichPatternUtil.PlayerFloorPos(ctx);
        Vector3 back   = target != null ? -target.forward : -ctx.Transform.forward;
        back.y = 0f;
        back   = back.sqrMagnitude > 0.001f ? back.normalized : Vector3.back;

        var mc = LichPatternUtil.Mover(ctx);
        _dest = player + back * Data.backOffset;
        if (mc != null) _dest = LichPatternUtil.OnFloor(ctx, mc.ClampToArena(_dest));
        _yaw  = Quaternion.LookRotation(-back).eulerAngles.y;   // 잔상 자리에서 플레이어 쪽

        PatternGuideHelper.SafeDestroy(ref _mark);
        _mark = LichPatternUtil.PrepareTelegraph(
            PatternGuideHelper.Sector(_dest, Data.hitRadius, Data.hitHalfAngle * 2f, _yaw, AfterimageColor), AfterimageColor);
        // 흰 잔상 — 거기로 온다
        LichVfx.PlayTinted(LichVfxSlot.BlinkAfterimage, _dest, Quaternion.Euler(0f, _yaw, 0f), 0.35f, AfterimageColor);
        Next(Phase.Mark);
    }

    private void Appear(MonsterContext ctx)
    {
        var lich = LichPatternUtil.Lich(ctx);
        lich?.MovementController?.SnapTo(_dest);
        ctx.Transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        _hidden = false;
        lich?.SetBodyVisible(true);
        LichVfx.Play(LichVfxSlot.TeleportAppear, _dest + Vector3.up, Quaternion.identity, 0.5f);
        LichSfx.Play(LichSfxSlot.Appear, _dest);
        PatternGuideHelper.SetColor(_mark, LichPatternUtil.Lethal);
        PatternGuideHelper.SetFlow(_mark, LichPatternUtil.Lethal);
        PatternGuideHelper.SetIntensity(_mark, 2f);
        // 나타나자마자 휘두른다 — 접촉 프레임 = 베기 판정 순간. 몸은 잔상 방향에 고정(플레이어를 따라 돌지 않게).
        LichPatternUtil.HoldFacing(ctx, _yaw, 1440f);
        LichPatternUtil.Swing(ctx, SwingOf(_strikes), Data.slashDelay);
    }

    /// <summary>베기 동작 — 수평 부채꼴 판정이라 수평 스윙만(왼쪽→오른쪽, 오른쪽→왼쪽 번갈아).</summary>
    private static LichSwing SwingOf(int strikeIndex) => strikeIndex % 2 == 0 ? LichSwing.LeftToRight : LichSwing.RightToLeft;

    /// <summary>베기 한 번. 튕겨냈으면 판정 없이 휘청으로 넘어가고 false(남은 베기 취소).</summary>
    private bool Slash(MonsterContext ctx)
    {
        _parryOpen = false;
        PatternGuideHelper.SafeDestroy(ref _mark);
        float stagger = Data.parryable ? LichPatternUtil.ConsumeParry(ctx) : 0f;
        if (stagger > 0f)
        {
            _endSeconds = stagger;
            _signaled   = true;   // 휘청이 이미 알렸다
            Next(Phase.End);
            return false;
        }

        Vector3 dir = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
        LichPatternUtil.SlashVfx(_dest, dir, Data.hitRadius, SwingOf(_strikes));
        LichSfx.Play(LichSfxSlot.ScytheSlash, _dest);
        _strikes++;

        // 여운은 벤 자리(반원 가운데) — 리치가 선 자리가 아니라 플레이어가 서 있던 쪽에 남는다.
        if (Data.lingerRadius > 0f)
            LichHazards.LingerPool(ctx, LichPatternUtil.OnFloor(ctx, _dest + dir * (Data.hitRadius * 0.5f)),
                                   Data.lingerRadius, Data.lingerSeconds, Data.lingerDamage);

        bool hit    = false;
        var  target = ctx.Runtime.PlayerTarget;
        if (target != null)
        {
            Vector3 to = target.position - _dest;
            to.y = 0f;
            if (to.magnitude <= Data.hitRadius && (to.sqrMagnitude <= 0.01f || Vector3.Angle(dir, to) <= Data.hitHalfAngle))
                hit = LichPatternUtil.HitCircle(ctx, _dest, Data.hitRadius, Data.damageMultiplier, Data.knockbackMultiplier);
        }
        LichPatternUtil.Impact(LichImpact.Slash, hit);   // 빗나가도 베기의 무게는 느껴지게
        return true;
    }
}
}
