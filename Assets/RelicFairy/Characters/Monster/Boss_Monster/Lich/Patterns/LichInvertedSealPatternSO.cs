using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// N1 「뒤집힌 봉인술」 — 악몽기 1페이지 특수(완전설계 §4-2). 악몽 규칙(무기 스킬 재사용 +25%)을 몸으로 체감시킨다.
///
/// O(openDuration) — 리치가 손을 치켜들면 손끝의 금빛 사슬이 보라로 물든다(역류)
/// → S 신호 → A — 보라 사슬 chainCount줄이 간격을 두고 뻗어 플레이어를 천천히 쫓는다(<see cref="LichSealChainBolt"/>, 초속 chainSpeed)
///   · 발밑 청록 고리 = 끊을 수 있음 — 한 번 치면 끊기고, 끊긴 조각이 리치에게 튀어 짧게 휘청(brokenStagger)
///   · 맞으면 무기 스킬 E 또는 R(아직 안 묶인 쪽) sealSeconds초 봉인 — HUD 칸이 잠김
///   · 세 줄 다 맞으면 둘 다 봉인 + 이동 −15% slowSeconds초
/// → E(endDuration, 무방비) → R
/// 바크 — 「네 봉인술이다. 돌려주지.」
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_InvertedSealPattern", fileName = "Lich_InvertedSealPattern")]
public class LichInvertedSealPatternSO : BossPatternSO
{
    [Header("뒤집힌 봉인술 — 발동")]
    public float maxTriggerRange = 22f;
    public float patternCooldown = 18f;

    [Header("뒤집힌 봉인술 — 타이밍 (초)")]
    public float openDuration     = 1.2f;
    public float signalDuration   = 0.2f;
    public float activeDuration   = 2.4f;
    public float endDuration      = 0.6f;
    public float recoveryDuration = 0.4f;

    [Header("뒤집힌 봉인술 — 사슬")]
    public int   chainCount     = 3;
    [Tooltip("사슬 사이 발사 간격")]
    public float chainInterval  = 0.25f;
    [Tooltip("가운데 = 플레이어, 좌우로 이만큼(도) 벌려 뻗는다")]
    public float spreadAngle    = 35f;
    public float chainSpeed     = 7f;
    [Tooltip("쫓는 선회 속도(도/초) — 느려서 옆으로 비키면 지나간다")]
    public float chainTurn      = 70f;
    [Tooltip("사슬 수명 — 활성 시간이 끝나도 날던 사슬은 이만큼까지")]
    public float chainLifetime  = 3.2f;

    [Header("뒤집힌 봉인술 — 효과")]
    public float sealSeconds    = 8f;
    [Tooltip("세 줄 다 맞았을 때 — 이동 배율 · 시간")]
    public float slowScale      = 0.85f;
    public float slowSeconds    = 3f;
    [Tooltip("사슬을 끊으면 리치가 휘청이는 시간")]
    public float brokenStagger  = 0.5f;

    // ── 런타임 ───────────────────────────────────────────
    private LichInvertedSealState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichInvertedSealState(this);
    public override void OnRecycled()                       => _state = new LichInvertedSealState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        var lich = ctx.Boss as LichMonster;
        var bb   = lich?.LichBB;
        if (bb == null || !bb.IsNightmare || bb.InvertedSealCooldown > 0f) return false;
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxTriggerRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichInvertedSealState
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichInvertedSealState : UnInterruptibleState<LichInvertedSealPatternSO>
{
    private enum Phase { Open, Signal, Active, End, Recovery }

    private Phase         _phase;
    private float         _timer;
    private int           _launched;
    private int           _hits;
    private int           _resolved;
    private float         _staggerLeft;
    private Vector3       _aim;
    private GameObject    _handGlow;
    private LichChainLine _gather;     // 치켜든 손의 사슬 — 금 → 보라로 물든다
    private MonsterContext _ctx;

    public LichInvertedSealState(LichInvertedSealPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _ctx         = ctx;
        _phase       = Phase.Open;
        _timer       = 0f;
        _launched    = 0;
        _hits        = 0;
        _resolved    = 0;
        _staggerLeft = 0f;

        var lich = LichPatternUtil.Lich(ctx);
        var mc   = lich?.MovementController;
        mc?.SetLocked(true);
        mc?.RequestMovementState(LichMovementState.AltitudeDescend);

        _aim = LichPatternUtil.PlayerFloorPos(ctx);
        LichPatternUtil.FaceInstant(ctx, _aim);
        LichPatternUtil.CastBeat(ctx, LichCast.ArcaneOrb, Data.openDuration + Data.signalDuration, 2.2f, 0.1f);

        Transform hand = lich != null ? lich.CastPoint : ctx.Transform;
        _handGlow = LichVfx.PlayLoopTinted(LichVfxSlot.CastFlare, hand.position, hand.rotation, 1.1f, PatternGuideHelper.PlayerSeal);
        if (_handGlow != null) _handGlow.transform.SetParent(hand, true);
        _gather = LichChainLine.Create(hand.position, hand.position + Vector3.up * 2.2f, 0.3f, PatternGuideHelper.PlayerSeal);
        lich?.PulseBook(Data.openDuration);
        LichSfx.Play(LichSfxSlot.ChainPulse, hand.position, 0.8f);
        UI_BossBark.Show("네 봉인술이다. 돌려주지.", BossBarkType.PatternAnnounce);
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;
        _timer += dt;

        var lich = LichPatternUtil.Lich(ctx);
        Transform hand = lich != null ? lich.CastPoint : ctx.Transform;

        switch (_phase)
        {
            case Phase.Open:
            {
                // 금빛 사슬이 손끝에서 보라로 물든다(역류).
                float k = Mathf.Clamp01(_timer / Data.openDuration);
                if (_gather != null)
                {
                    _gather.SetEnds(hand.position, hand.position + Vector3.up * (1.2f + k * 1.2f));
                    _gather.SetColor(Color.Lerp(PatternGuideHelper.PlayerSeal, PatternGuideHelper.Reversed, k));
                    _gather.Tension(k);
                }
                if (_timer >= Data.openDuration)
                {
                    _gather?.Flash(0.2f);
                    LichPatternProbe.Signal();
                    Next(Phase.Signal);
                }
                break;
            }

            case Phase.Signal:
                if (_timer >= Data.signalDuration)
                {
                    DisposeGather();
                    LichVfx.Stop(ref _handGlow, 0.2f);
                    _aim = LichPatternUtil.PlayerFloorPos(ctx);
                    Next(Phase.Active);
                }
                break;

            case Phase.Active:
                if (_launched < Data.chainCount && _timer >= _launched * Data.chainInterval)
                    LaunchChain(ctx, hand, _launched++);
                if (_staggerLeft > 0f) _staggerLeft -= dt;
                if (_launched >= Data.chainCount && (_resolved >= _launched || _timer >= Data.activeDuration))
                {
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
        DisposeGather();
        LichVfx.Stop(ref _handGlow);
        LichSealChainBolt.ClearAll();

        var lich = LichPatternUtil.Lich(ctx);
        lich?.MovementController?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.InvertedSealCooldown = Data.patternCooldown;
        _ctx = null;
    }

    private void Next(Phase phase)
    {
        _phase = phase;
        _timer = 0f;
    }

    /// <summary>i번째 사슬 — 가운데 줄은 플레이어 쪽, 나머지는 좌우로 벌려 뻗어 휘어 들어온다.</summary>
    private void LaunchChain(MonsterContext ctx, Transform hand, int i)
    {
        var target = ctx.Runtime.PlayerTarget;
        if (target == null) { _resolved++; return; }

        Vector3 fwd = _aim - ctx.Transform.position;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : ctx.Transform.forward;
        int   n     = Mathf.Max(1, Data.chainCount);
        float angle = n > 1 ? Mathf.Lerp(-Data.spreadAngle, Data.spreadAngle, (float)i / (n - 1)) : 0f;
        Vector3 dir = Quaternion.Euler(0f, angle, 0f) * fwd;

        var bolt = LichSealChainBolt.Launch(ctx, hand, target, dir, Data.chainSpeed, Data.chainTurn,
                                            Data.chainLifetime, LichPatternUtil.FloorY(ctx));
        bolt.Hit    += HandleChainHit;
        bolt.Broken += HandleChainBroken;
        LichSfx.Play(LichSfxSlot.ChainPulse, hand.position, 0.6f);
        LichVfx.Play(LichVfxSlot.CastFlare, hand.position, Quaternion.LookRotation(dir), 0.5f);
    }

    private void DisposeGather()
    {
        if (_gather == null) return;
        _gather.Dispose();
        _gather = null;
    }

    /// <summary>
    /// 기술 봉인 — 아직 안 묶인 E · R 중 무작위 하나. 세 줄째면 둘 다 + 이동 감속.
    /// (봉인 · 감속 API는 플레이어 담당이 제공: SealSkill · IsSkillSealed · ApplySlow)
    /// </summary>
    private void HandleChainHit(LichSealChainBolt bolt)
    {
        _hits++;
        _resolved++;
        var player = _ctx?.Runtime.CachedPlayer;
        if (player == null) return;

        if (_hits >= Data.chainCount)
        {
            player.SealSkill(SkillType.E, Data.sealSeconds);
            player.SealSkill(SkillType.R, Data.sealSeconds);
            player.ApplySlow(Data.slowScale, Data.slowSeconds);
            UI_BossBark.Show($"E · R 기술 봉인 — {Data.sealSeconds:0}초 · 둔화", BossBarkType.PatternAnnounce);
            return;
        }

        bool eSealed = player.IsSkillSealed(SkillType.E);
        bool rSealed = player.IsSkillSealed(SkillType.R);
        SkillType slot = eSealed == rSealed ? (Random.value < 0.5f ? SkillType.E : SkillType.R)
                                            : (eSealed ? SkillType.R : SkillType.E);
        player.SealSkill(slot, Data.sealSeconds);
        UI_BossBark.Show($"{slot} 기술 봉인 — {Data.sealSeconds:0}초", BossBarkType.PatternAnnounce);
    }

    /// <summary>사슬을 끊었다 — 조각이 리치에게 튀어 짧게 휘청.</summary>
    private void HandleChainBroken(LichSealChainBolt bolt)
    {
        _resolved++;
        var lich = _ctx != null ? LichPatternUtil.Lich(_ctx) : null;
        if (lich == null || bolt == null) return;
        Vector3 from = bolt.transform.position;
        LichVfx.PlayBeam(LichVfxSlot.ArcaneBeam, from, lich.transform.position + Vector3.up * 1.4f, 0.3f, 0.25f);
        if (_staggerLeft > 0f) return;
        _staggerLeft = Data.brokenStagger;
        _ctx.Animator?.CrossFade("GetHit", 0.05f);
        lich.NotifyVulnerableWindow(Data.brokenStagger);
        LichPatternUtil.Impact(LichImpact.Light, true);
    }
}
}
