using System.Collections.Generic;
using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// F4 「최후의 대마법」 — 악몽기 3페이지, HP 10%에서 한 번(완전설계 §4-6 · 임계는 LichMonster). 강제 실행(Lich_FinalMagicPending).
///
/// 리치가 코어 상공으로 떠올라 「제단째 떨어져라!」 — channelSeconds 동안 채널링(무적).
/// 코어 네 모서리에 봉인석이 내려앉는다(청록) — 세 번씩 쳐서 점화하면 금빛 사슬이 리치에게 감긴다.
/// 방해: interferenceInterval마다 하늘에서 잔해가 떨어진다(플레이어 자리 + 코어 어딘가).
/// **넷 다 점화** → 사슬이 리치를 코어로 끌어내린다 → groggySeconds 동안 무방비(받는 피해 ×groggyDamageTaken) = 결말 딜 구간.
/// **시간 초과** → 거대 폭발(플레이어 최대 체력 failDamageRatio, 즉사 아님) → 봉인석이 다시 떠 처음부터.
/// 서사: 봉인기에 무너졌던 봉인이 이번엔 끌어내리는 사슬로 성공한다 — 봉인 의식의 반전 짝.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_FinalMagicPattern", fileName = "Lich_FinalMagicPattern")]
public class LichFinalMagicPatternSO : BossPatternSO
{
    [Header("최후의 대마법 — 채널링")]
    public float channelSeconds   = 12f;
    public float riseOffset       = 8f;
    [Tooltip("봉인석 — 코어 중심에서 대각선 거리 (m)")]
    public float stoneDistance    = 7f;

    [Header("최후의 대마법 — 방해 (떨어지는 잔해)")]
    public float interferenceInterval = 3f;
    public float debrisRadius         = 2.5f;
    public float debrisWarn           = 1.2f;
    public float debrisDamage         = 0.8f;

    [Header("최후의 대마법 — 결과")]
    public float groggySeconds     = 5f;
    [Tooltip("그로기 동안 받는 피해 배율")]
    public float groggyDamageTaken = 1.3f;
    [Tooltip("실패 폭발 — 플레이어 최대 체력 비율(즉사 아님)")]
    public float failDamageRatio   = 0.6f;
    public float recoveryDuration  = 0.6f;

    // ── 런타임 ───────────────────────────────────────────
    private LichFinalMagicState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichFinalMagicState(this);
    public override void OnRecycled()                       => _state = new LichFinalMagicState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        var lich = ctx.Boss as LichMonster;
        return lich?.LichBB != null && lich.LichBB.Page >= 3 && !lich.LichBB.FinalMagicDone;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichFinalMagicState — 채널링 동안 무적, 그로기 동안 무방비
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichFinalMagicState : UnInterruptibleState<LichFinalMagicPatternSO>
{
    private enum Phase { Channel, PullDown, Groggy, Restart, Recovery }

    private const int   StoneCount   = 4;
    private const float PullSeconds  = 0.9f;
    private const float FailFlash    = 0.25f;   // 강 피격 붉은 비네트가 같이 읽히게(09-20 실측)
    private const float RestartDelay = 1.2f;    // 실패 폭발 뒤 다시 채널링하기까지(내뻗은 팔이 보이게)
    private const float ReleaseLead  = 0.35f;   // 폭발 직전에 두 팔을 내뻗는다
    private const float BossBarClearance = 70f; // 도전 HUD를 보스 체력바 아래로(1080 기준 px)

    private readonly List<LichSealStone> _stones = new(StoneCount);

    private Phase           _phase;
    private float           _timer;
    private float           _debrisTimer;
    private int             _ignited;
    private Vector3         _center;
    private UI_ChallengeHud _hud;
    private GameObject      _channelVfx;
    private Vector3         _pullFrom;

    public LichFinalMagicState(LichFinalMagicPatternSO data) : base(data) { }

    public override SpecialStateConstraint Constraints =>
        _phase == Phase.Channel || _phase == Phase.PullDown
            ? base.Constraints | SpecialStateConstraint.Invincible
            : base.Constraints;

    public override void Enter(MonsterContext ctx)
    {
        var lich = LichPatternUtil.Lich(ctx);
        var mc   = LichPatternUtil.Mover(ctx);
        mc?.SetLocked(true);
        mc?.RequestMovementState(LichMovementState.AltitudeRise);
        mc?.SetAltitudeOffset(Data.riseOffset);

        var grid = ArenaTileGrid.Active;
        if (grid == null || !grid.TryGetWorldCenter(out _center))
            _center = mc != null ? mc.ArenaCenter : LichPatternUtil.OnFloor(ctx, ctx.Transform.position);

        LichSkeletonMonster.DespawnAll();
        LichHazards.Clear();
        LichCinematics.Flash(new Color(0.7f, 0.4f, 1f), 0.3f, 0.15f);  // 보라는 채도가 높아 0.12에서도 화면 전체가 물든다(09-20 실측)
        LichPatternUtil.Impact(LichImpact.Transition);
        LichVfx.PlayScreen(LichVfxSlot.ScreenSpace, Data.channelSeconds);
        LichSfx.Play(LichSfxSlot.CastCharge, ctx.Transform.position);
        UI_BossBark.Show("제단째 떨어져라!", BossBarkType.PhaseAnnounce);
        lich?.SetRitualCamera(true);   // 떠오른 리치 · 네 봉인석 · 사슬을 한 화면에

        _hud = UI_ChallengeHud.Create(BossBarClearance);
        _hud.SetObjective("멀린: 봉인석 조각이 아직 빛나! 세 번씩 쳐서 끌어내려!");
        BeginChannel(ctx);
        Debug.Log("[Lich] 최후의 대마법 — 채널링 시작", ctx.Monster);
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;
        _timer += dt;

        switch (_phase)
        {
            case Phase.Channel:
                RefreshHud();
                _debrisTimer += dt;
                if (_debrisTimer >= Data.interferenceInterval)
                {
                    _debrisTimer = 0f;
                    DropDebris(ctx);
                }
                if (_ignited >= StoneCount) BeginPullDown(ctx);
                else if (_timer >= Data.channelSeconds) Fail(ctx);
                break;

            case Phase.PullDown:
            {
                float k = Mathf.Clamp01(_timer / PullSeconds);
                float e = k * k;
                ctx.Transform.position = Vector3.Lerp(_pullFrom, new Vector3(_center.x, _center.y + 0.6f, _center.z), e);
                if (k >= 1f) BeginGroggy(ctx);
                break;
            }

            case Phase.Restart:
                if (_timer >= RestartDelay) BeginChannel(ctx);
                break;

            case Phase.Groggy:
                if (_timer >= Data.groggySeconds)
                {
                    LichPatternUtil.Lich(ctx)?.SetDamageTakenMultiplier(1f);
                    foreach (var s in _stones) if (s != null) s.Explode();
                    _stones.Clear();
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
        foreach (var s in _stones) if (s != null) s.Dismiss();   // 디졸브로 스러진다(10-03)
        _stones.Clear();
        LichVfx.Stop(ref _channelVfx);
        _hud?.Close();
        _hud = null;

        var lich = LichPatternUtil.Lich(ctx);
        lich?.SetDamageTakenMultiplier(1f);
        lich?.SetRitualCamera(false);
        var mc = lich?.MovementController;
        mc?.HoldAltitude(false);
        mc?.SetAltitudeOffset(0f);
        mc?.RequestMovementState(LichMovementState.AltitudeDescend);
        mc?.SetLocked(false);
    }

    private void Next(Phase phase)
    {
        _phase = phase;
        _timer = 0f;
    }

    /// <summary>채널링(다시) 시작 — 봉인석 넷이 코어 모서리에 내려앉는다.</summary>
    private void BeginChannel(MonsterContext ctx)
    {
        foreach (var s in _stones) if (s != null) s.Dismiss();
        _stones.Clear();
        _ignited     = 0;
        _debrisTimer = 0f;
        for (int i = 0; i < StoneCount; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, 45f + 90f * i, 0f) * Vector3.forward;
            _stones.Add(LichSealStone.Create(_center + dir * Data.stoneDistance, ctx.Transform, OnStoneIgnited));
        }
        LichVfx.Stop(ref _channelVfx);
        _channelVfx = LichVfx.PlayLoop(LichVfxSlot.CastFlare, ctx.Transform.position + Vector3.up * 1.5f, Quaternion.identity, 2.2f, ctx.Transform);
        LichPatternUtil.Lich(ctx)?.PulseBook(Data.channelSeconds);
        LichPatternUtil.CastBeat(ctx, LichCast.DeathRayStart, Data.channelSeconds - ReleaseLead, 2.4f, ReleaseLead + 0.3f);
        Next(Phase.Channel);
    }

    private void OnStoneIgnited(LichSealStone stone)
    {
        _ignited++;
        LichSfx.Play(LichSfxSlot.SealComplete, stone.transform.position, 0.6f);
        LichPatternUtil.Impact(LichImpact.Heavy);
        UI_BossBark.Show($"봉인 사슬 ({_ignited} / {StoneCount})", BossBarkType.PatternAnnounce);
    }

    private void RefreshHud()
        => _hud?.SetStatus($"봉인석 {_ignited} / {StoneCount} · 남은 시간 {Mathf.Max(0f, Data.channelSeconds - _timer):0}초");

    /// <summary>방해 — 하늘에서 제단 잔해(플레이어 자리 + 코어 어딘가).</summary>
    private void DropDebris(MonsterContext ctx)
    {
        Vector3 player = LichPatternUtil.PlayerFloorPos(ctx);
        Vector2 r      = Random.insideUnitCircle * (Data.stoneDistance * 0.9f);
        Vector3 other  = LichPatternUtil.OnFloor(ctx, _center + new Vector3(r.x, 0f, r.y));
        foreach (var at in new[] { player, other })
            LichHazards.DelayedBlast(ctx, at, Data.debrisRadius, Data.debrisWarn, Data.debrisDamage,
                                     LichVfxSlot.SlamImpact, LichPatternUtil.NovaScale(Data.debrisRadius));
    }

    /// <summary>넷 다 점화 — 사슬이 팽팽해지며 리치를 코어로 끌어내린다.</summary>
    private void BeginPullDown(MonsterContext ctx)
    {
        _hud?.SetStatus("봉인 사슬 — 끌어내려라!");
        LichVfx.Stop(ref _channelVfx, 0.3f);
        foreach (var s in _stones) if (s != null) s.PulseChain();
        var mc = LichPatternUtil.Mover(ctx);
        mc?.HoldAltitude(true);
        _pullFrom = ctx.Transform.position;
        ctx.Animator?.CrossFade("GetHit", 0.05f);
        LichCinematics.Flash(new Color(1f, 0.9f, 0.6f), 0.3f, 0.25f);  // 끌어내림 성공 — 이 패턴의 절정이라 금빛 중 가장 세게
        LichCinematics.SlowMo(0.4f, 0.6f);
        LichVfx.Play(LichVfxSlot.SealComplete, _center + Vector3.up * 0.05f, Quaternion.identity);
        LichSfx.Play(LichSfxSlot.ChainPulse, ctx.Transform.position);
        UI_BossBark.Show("이, 이 사슬은…!", BossBarkType.PhaseAnnounce);
        Next(Phase.PullDown);
    }

    /// <summary>코어에 처박힘 — 무방비(받는 피해 증가).</summary>
    private void BeginGroggy(MonsterContext ctx)
    {
        var lich = LichPatternUtil.Lich(ctx);
        if (lich?.LichBB != null) lich.LichBB.FinalMagicDone = true;
        lich?.SetDamageTakenMultiplier(Data.groggyDamageTaken);
        lich?.NotifyVulnerableWindow(Data.groggySeconds);
        ctx.Animator?.CrossFade("Die", 0.1f);
        LichVfx.Play(LichVfxSlot.SlamImpact, _center, Quaternion.identity, LichPatternUtil.NovaScale(6f));
        LichSfx.Play(LichSfxSlot.SlamImpact, _center);
        LichPatternUtil.Impact(LichImpact.Transition);
        ArenaTileGrid.Active?.Tremble(_center, 12f, 0.8f);
        _hud?.Close();
        _hud = null;
        Debug.Log("[Lich] 최후의 대마법 저지 — 그로기", ctx.Monster);
        Next(Phase.Groggy);
    }

    /// <summary>시간 초과 — 거대 폭발(최대 체력 비율, 즉사 아님) 뒤 처음부터.</summary>
    private void Fail(MonsterContext ctx)
    {
        LichCinematics.Flash(Color.white, 0.2f, FailFlash);
        LichPatternUtil.Impact(LichImpact.Transition);
        LichVfx.Play(LichVfxSlot.PhaseBurst, _center + Vector3.up, Quaternion.identity, 3f);
        LichVfx.Play(LichVfxSlot.SlamImpact, _center, Quaternion.identity, LichPatternUtil.NovaScale(14f));
        LichSfx.Play(LichSfxSlot.SlamImpact, _center);

        var player = ctx.Runtime.CachedPlayer;
        if (player != null && player.RuntimeStats != null)
            player.TakeDamage(BossMaxHpDamage.Raw(player, Data.failDamageRatio), ctx.Monster.gameObject,   // 방어로 다시 깎이지 않게(10-01)
                              false, HitWeight.Heavy);

        UI_BossBark.Show("아직 끝나지 않았다 — 다시!", BossBarkType.PatternAnnounce);
        Debug.Log("[Lich] 최후의 대마법 — 시간 초과, 재시도", ctx.Monster);
        foreach (var s in _stones) if (s != null) s.Explode();
        _stones.Clear();
        LichVfx.Stop(ref _channelVfx, 0.3f);
        Next(Phase.Restart);
    }
}
}
