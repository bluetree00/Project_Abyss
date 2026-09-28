using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// N2 「봉인 조각」 — 악몽기 2페이지 특수(완전설계 §4-4).
///
/// O — 리치가 떠오르며 손을 뻗으면 봉인석 조각 넷이 제단 네 방위에서 솟아오른다(처음 한 번 — 그 뒤로 남아 떠 있다).
/// → A — 조각이 <b>시계 방향 순서로</b> 간격마다 달아올라, 제단 중심을 가로지르는 광선을 쏜다(폭 beamWidth).
///   순서가 곧 예고 — 다음 조각 쪽 선이 먼저 보라로 차오르고 0.2초 전 빨강.
///   청록 조각은 두 번 치면 5초 꺼진다 → 꺼진 조각은 쏘지 않는다(가까운 조각부터 끄며 버틴다).
/// → E · R
/// 서사: 「봉인의 잔해가 이제 그의 무기다」 — P3의 최후의 대마법(F4)에서 뒤집힌다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_SealShardsPattern", fileName = "Lich_SealShardsPattern")]
public class LichSealShardsPatternSO : BossPatternSO
{
    [Header("봉인 조각 — 발동")]
    public float patternCooldown = 20f;

    [Header("봉인 조각 — 타이밍 (초)")]
    [Tooltip("조각이 솟아오르는 · 손을 뻗는 시간")]
    public float openDuration     = 1.0f;
    [Tooltip("광선 사이 간격 — 조각 하나씩")]
    public float beamInterval     = 1.5f;
    [Tooltip("광선 예고(선이 차오르는 시간)")]
    public float beamWarn         = 1.1f;
    public float endDuration      = 0.8f;
    public float recoveryDuration = 0.5f;

    [Header("봉인 조각 — 광선")]
    [Tooltip("조각이 떠 있는 거리(제단 중심 기준, m)")]
    public float shardDistance    = 20f;
    public float beamWidth        = 3f;
    public float damageMultiplier = 1.3f;
    public float knockbackMultiplier = 1.2f;

    // ── 런타임 ───────────────────────────────────────────
    private LichSealShardsState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichSealShardsState(this);
    public override void OnRecycled()                       => _state = new LichSealShardsState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB == null || !lichBB.IsPhase2) return false;
        return lichBB.SealShardsCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichSealShardsState — 중단 불가(피해는 받음)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichSealShardsState : UnInterruptibleState<LichSealShardsPatternSO>
{
    private enum Phase { Open, Beams, End, Recovery }

    private const int   ShardCount    = 4;
    private const float SignalSeconds = 0.2f;
    private const float BeamSpeed     = 70f;   // 광선 앞머리가 뻗는 속도(맞는 순간 = 보이는 순간)

    private readonly GameObject[] _guides   = new GameObject[ShardCount];
    private readonly bool[]       _signaled = new bool[ShardCount];
    private readonly bool[]       _fired    = new bool[ShardCount];

    private Phase   _phase;
    private float   _timer;
    private int     _first;     // 첫 조각(시계 방향으로 이어진다)
    private Vector3 _center;

    public LichSealShardsState(LichSealShardsPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase = Phase.Open;
        _timer = 0f;
        for (int i = 0; i < ShardCount; i++) { _signaled[i] = false; _fired[i] = false; }

        var lich = LichPatternUtil.Lich(ctx);
        var mc   = LichPatternUtil.Mover(ctx);
        mc?.SetLocked(true);
        mc?.RequestMovementState(LichMovementState.AltitudeRise);

        var grid = ArenaTileGrid.Active;
        if (grid == null || !grid.TryGetWorldCenter(out _center))
            _center = mc != null ? mc.ArenaCenter : LichPatternUtil.OnFloor(ctx, ctx.Transform.position);

        LichSealShard.EnsureSpawned(_center, Data.shardDistance);
        _first = Random.Range(0, ShardCount);

        LichPatternUtil.CastBeat(ctx, LichCast.ArcaneOrb, Data.openDuration + Data.beamWarn, 2f, 0.1f);
        lich?.PulseBook(Data.openDuration + ShardCount * Data.beamInterval);
        LichSfx.Play(LichSfxSlot.CastCharge, ctx.Transform.position);
        UI_BossBark.Show("네 봉인의 잔해다 — 이제 내 것이지.", BossBarkType.PatternAnnounce);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Open:
                if (_timer >= Data.openDuration) Next(Phase.Beams);
                break;

            case Phase.Beams:
                for (int k = 0; k < ShardCount; k++) TickShard(ctx, k);
                if (_timer >= (ShardCount - 1) * Data.beamInterval + Data.beamWarn + 0.3f)
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
        for (int i = 0; i < ShardCount; i++) PatternGuideHelper.SafeDestroy(ref _guides[i]);
        var lich = LichPatternUtil.Lich(ctx);
        var mc   = lich?.MovementController;
        mc?.RequestMovementState(LichMovementState.AltitudeDescend);
        mc?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.SealShardsCooldown = Data.patternCooldown;
    }

    private void Next(Phase phase)
    {
        _phase = phase;
        _timer = 0f;
    }

    /// <summary>k번째 광선(시계 방향 순서) — 예고가 차오르고, 신호, 발사. 조각이 꺼져 있으면 예고가 스러지고 쏘지 않는다.</summary>
    private void TickShard(MonsterContext ctx, int k)
    {
        if (_fired[k]) return;
        float fireAt = k * Data.beamInterval + Data.beamWarn;
        float openAt = fireAt - Data.beamWarn;
        if (_timer < openAt) return;

        var shard = Shard(k);
        if (shard == null) { _fired[k] = true; return; }

        Vector3 origin = new Vector3(shard.BeamOrigin.x, _center.y, shard.BeamOrigin.z);
        Vector3 dir    = _center - origin;
        dir.y = 0f;
        dir = dir.normalized;
        float length = Data.shardDistance * 2f;

        if (_guides[k] == null && !shard.IsDisabled)
        {
            _guides[k] = LichPatternUtil.PrepareTelegraph(
                PatternGuideHelper.Beam(origin, dir, length, Data.beamWidth, LichPatternUtil.Arcane), LichPatternUtil.Arcane);
            shard.Charge();
        }
        if (_guides[k] != null)
            LichPatternUtil.TickTelegraph(_guides[k], _timer - openAt, Data.beamWarn, SignalSeconds, ref _signaled[k]);

        if (_timer < fireAt) return;
        _fired[k] = true;
        PatternGuideHelper.SafeDestroy(ref _guides[k]);

        if (shard.IsDisabled)
        {
            // 꺼진 조각 — 불발(연기만)
            LichVfx.Play(LichVfxSlot.TeleportVanish, shard.BeamOrigin, Quaternion.identity, 0.4f);
            return;
        }

        Vector3 from = shard.BeamOrigin;
        Vector3 to   = origin + dir * length + Vector3.up * 0.4f;
        LichVfx.PlayBeam(LichVfxSlot.ArcaneBeam, from, to, Data.beamWidth, 0.7f);
        LichHazards.BeamFront(ctx, origin, dir, length, Data.beamWidth * 0.5f, BeamSpeed,
                              Data.damageMultiplier, Data.knockbackMultiplier);
        LichSfx.Play(LichSfxSlot.FireBeam, from);
        LichPatternUtil.Impact(LichImpact.Heavy);
        shard.Settle();
    }

    /// <summary>시계 방향 k번째 조각.</summary>
    private LichSealShard Shard(int k)
    {
        var live = LichSealShard.Live;
        if (live.Count == 0) return null;
        return live[(_first + k) % live.Count];
    }
}
}
