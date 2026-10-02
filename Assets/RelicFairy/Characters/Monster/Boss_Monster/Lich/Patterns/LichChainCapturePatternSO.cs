using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// C7 「사슬 포박」 — 2페이지 사슬 결박 + 마법 추격. 연출·UX 시나리오 §12-5 · §12-6.
///
/// O 조준(castDuration) — 리치 손에서 바닥을 따라 사슬 줄 chainCount개(가운데 = 플레이어, 좌우로 spreadAngle씩)가
///   진홍 직선 예고로 차오른다
/// → A 발사 — 금빛 사슬이 줄을 따라 뻗는다. 줄에 선 플레이어는 약한 피해 + 결박(bindSeconds, 이동·회피 불가)
///   → 묶인 자리에 폭발 예고가 바로 차오르고 풀린 뒤 followAfter초에 터진다(쌍둥이 폭발)
/// → 사슬이 되감긴다 → E 무방비(endDuration) → R 복귀
/// 결박은 연속으로 걸리지 않는다(풀린 뒤 4초 — <see cref="LichHazards.ChainBind"/>).
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_ChainCapturePattern", fileName = "Lich_ChainCapturePattern")]
public class LichChainCapturePatternSO : BossPatternSO
{
    [Header("사슬 포박 — 발동")]
    public float maxTriggerRange = 20f;
    public float patternCooldown = 14f;

    [Header("사슬 포박 — 타이밍 (초)")]
    public float castDuration     = 0.8f;
    [Tooltip("사슬이 끝까지 뻗는 시간")]
    public float shootSeconds     = 0.2f;
    [Tooltip("다 뻗은 뒤 되감기까지")]
    public float holdSeconds      = 0.35f;
    public float endDuration      = 0.8f;
    public float recoveryDuration = 0.4f;

    [Header("사슬 포박 — 사슬")]
    public int   chainCount   = 3;
    [Tooltip("사슬 사이 각도 (도)")]
    public float spreadAngle  = 25f;
    public float chainLength  = 18f;
    [Tooltip("판정 폭 (m) — 예고 직선 폭과 같다")]
    public float chainWidth   = 1.6f;
    public float hitDamage    = 0.5f;

    [Header("사슬 포박 — 결박 · 추격")]
    public float bindSeconds    = 0.9f;
    public float followRadius   = 2.8f;
    [Tooltip("풀린 뒤 이만큼 지나 폭발 (초)")]
    public float followAfter    = 0.5f;
    public float followDamage   = 1.0f;

    // ── 런타임 ───────────────────────────────────────────
    private LichChainCaptureState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichChainCaptureState(this);
    public override void OnRecycled()                       => _state = new LichChainCaptureState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB == null || !lichBB.IsPhase2) return false;
        if (lichBB.ChainCaptureCooldown > 0f || LichHazards.IsBinding) return false;
        // 악몽 모드 속박탄이 묶은 직후엔 시작하지 않는다 — 결박이 잇따르지 않게(10-02).
        if (Time.time - lichBB.LastBindBoltAt < LichBlackboard.CaptureAfterBindBolt) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxTriggerRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichChainCaptureState — 중단 불가(피해는 받음)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichChainCaptureState : UnInterruptibleState<LichChainCapturePatternSO>
{
    private enum Phase { Cast, Shoot, Hold, Retract, End, Recovery }

    private const int   MaxChains      = 7;
    private const float SignalSeconds  = 0.15f;
    private const float RetractSeconds = 0.25f;
    private const float ChainVisualWidth = 1.0f;   // 사슬 텍스처 — 보이는 고리 굵기 약 0.35 m(판정 폭은 예고 직선이 끝까지 보여 준다)

    private readonly Vector3[]       _dirs   = new Vector3[MaxChains];
    private readonly GameObject[]    _guides = new GameObject[MaxChains];
    private readonly LichChainLine[] _chains = new LichChainLine[MaxChains];

    private Phase   _phase;
    private float   _timer;
    private int     _count;
    private bool    _signaled;
    private bool    _caught;
    private Vector3 _origin;

    public LichChainCaptureState(LichChainCapturePatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase    = Phase.Cast;
        _timer    = 0f;
        _signaled = false;
        _caught   = false;
        _count    = Mathf.Clamp(Data.chainCount, 1, MaxChains);

        var mc = LichPatternUtil.Mover(ctx);
        mc?.SetLocked(true);
        mc?.RequestMovementState(LichMovementState.AltitudeDescend);

        Vector3 player = LichPatternUtil.PlayerFloorPos(ctx);
        LichPatternUtil.FaceInstant(ctx, player);
        LichPatternUtil.HoldFacing(ctx, ctx.Transform.eulerAngles.y);
        // 사슬을 내던지는 스윙 — 접촉 프레임 = 사슬이 손을 떠나는 순간(시전 끝).
        LichPatternUtil.Swing(ctx, LichSwing.RightToLeft, Data.castDuration, 0.05f);
        LichPatternUtil.Lich(ctx)?.PulseBook(Data.castDuration);
        LichSfx.Play(LichSfxSlot.ChainPulse, ctx.Transform.position, 0.8f);

        _origin = LichPatternUtil.OnFloor(ctx, ctx.Transform.position);
        Vector3 aim = player - _origin;
        aim.y = 0f;
        aim = aim.sqrMagnitude > 0.001f ? aim.normalized : ctx.Transform.forward;

        for (int i = 0; i < _count; i++)
        {
            // 0 = 가운데, 1 = 오른쪽, 2 = 왼쪽, 3.. = 바깥으로 번갈아
            int   ring  = (i + 1) / 2;
            float sign  = i % 2 == 1 ? 1f : -1f;
            _dirs[i]    = Quaternion.Euler(0f, sign * ring * Data.spreadAngle, 0f) * aim;
            float len   = Mathf.Min(Data.chainLength, LichPatternUtil.DistanceToArenaEdge(ctx, _origin, _dirs[i]));
            _guides[i]  = LichPatternUtil.PrepareTelegraph(
                PatternGuideHelper.Beam(_origin, _dirs[i], Mathf.Max(2f, len), Data.chainWidth, LichPatternUtil.Crimson),
                LichPatternUtil.Crimson);
        }
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Cast:
                for (int i = 0; i < _count; i++)
                {
                    bool s = _signaled;
                    LichPatternUtil.TickTelegraph(_guides[i], _timer, Data.castDuration, SignalSeconds, ref s);
                }
                if (!_signaled && _timer >= Data.castDuration - SignalSeconds) _signaled = true;
                if (_timer >= Data.castDuration)
                {
                    Shoot(ctx);
                    Next(Phase.Shoot);
                }
                break;

            case Phase.Shoot:
                StretchChains(ctx, Mathf.Clamp01(_timer / Mathf.Max(0.01f, Data.shootSeconds)));
                if (_timer >= Data.shootSeconds)
                {
                    StretchChains(ctx, 1f);
                    Resolve(ctx);
                    Next(Phase.Hold);
                }
                break;

            case Phase.Hold:
                StretchChains(ctx, 1f);
                if (_timer >= Data.holdSeconds) Next(Phase.Retract);
                break;

            case Phase.Retract:
                StretchChains(ctx, 1f - Mathf.Clamp01(_timer / RetractSeconds));
                if (_timer >= RetractSeconds)
                {
                    DisposeChains();
                    LichPatternUtil.Lich(ctx)?.NotifyVulnerableWindow(Data.endDuration);
                    Next(Phase.End);
                }
                break;

            case Phase.End:
                if (_timer >= Data.endDuration) Next(Phase.Recovery);
                break;

            case Phase.Recovery:
                if (_timer >= Data.recoveryDuration)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        for (int i = 0; i < MaxChains; i++) PatternGuideHelper.SafeDestroy(ref _guides[i]);
        DisposeChains();

        var lich = LichPatternUtil.Lich(ctx);
        lich?.MovementController?.SetLocked(false);
        if (lich?.LichBB != null)
        {
            lich.LichBB.ChainCaptureCooldown = Data.patternCooldown;
            lich.LichBB.LastChainCaptureAt   = Time.time;   // 속박탄(악몽 모드)은 이 뒤 6초 동안 나오지 않는다
        }
    }

    private void Next(Phase phase)
    {
        _phase = phase;
        _timer = 0f;
    }

    private void Shoot(MonsterContext ctx)
    {
        Vector3 hand = Hand(ctx);
        for (int i = 0; i < _count; i++)
        {
            // 예고 직선은 판정(다 뻗은 순간)까지 남긴다 — 사슬이 날아가는 동안에도 판정 폭이 보이게(09-19 감사).
            _chains[i] = LichChainLine.Create(hand, _origin, ChainVisualWidth, PatternGuideHelper.PlayerSeal);
            _chains[i].Tension();
        }
        LichSfx.Play(LichSfxSlot.ScytheThrow, _origin);
    }

    /// <summary>사슬 끝을 손에서 줄 끝까지 <paramref name="t"/>만큼 뻗는다(바닥을 긁으며).</summary>
    private void StretchChains(MonsterContext ctx, float t)
    {
        Vector3 hand = Hand(ctx);
        for (int i = 0; i < _count; i++)
        {
            if (_chains[i] == null) continue;
            float len = Mathf.Min(Data.chainLength, LichPatternUtil.DistanceToArenaEdge(ctx, _origin, _dirs[i]));
            _chains[i].SetEnds(hand, _origin + _dirs[i] * (len * t) + Vector3.up * 0.2f);
        }
    }

    /// <summary>다 뻗은 순간 판정 — 줄 하나에만 걸린다(여러 줄에 서 있어도 한 번).</summary>
    private void Resolve(MonsterContext ctx)
    {
        for (int i = 0; i < _count; i++) PatternGuideHelper.SafeDestroy(ref _guides[i]);
        for (int i = 0; i < _count && !_caught; i++)
        {
            float len = Mathf.Min(Data.chainLength, LichPatternUtil.DistanceToArenaEdge(ctx, _origin, _dirs[i]));
            if (!LichPatternUtil.HitBeam(ctx, _origin, _dirs[i], len, Data.chainWidth * 0.5f, Data.hitDamage)) continue;

            _caught = true;
            _chains[i]?.Flash(0.2f);
            LichPatternUtil.Impact(LichImpact.Slash, true);

            var lich = LichPatternUtil.Lich(ctx);
            if (!LichHazards.ChainBind(ctx, lich != null ? lich.CastPoint : ctx.Transform, Data.bindSeconds)) continue;
            LichHazards.DelayedBlast(ctx, LichPatternUtil.PlayerFloorPos(ctx), Data.followRadius,
                                     Data.bindSeconds + Data.followAfter, Data.followDamage,
                                     LichVfxSlot.TwinBlast, Data.followRadius / 3f);
        }
    }

    private void DisposeChains()
    {
        for (int i = 0; i < MaxChains; i++)
        {
            if (_chains[i] != null) _chains[i].Dispose();
            _chains[i] = null;
        }
    }

    private static Vector3 Hand(MonsterContext ctx)
    {
        var lich = LichPatternUtil.Lich(ctx);
        return lich != null ? lich.CastPoint.position : ctx.Transform.position + Vector3.up * 1.5f;
    }
}
}
