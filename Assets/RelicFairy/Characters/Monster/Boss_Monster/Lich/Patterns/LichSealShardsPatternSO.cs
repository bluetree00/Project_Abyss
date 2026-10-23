using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// N2 「봉인 조각」 — 악몽기 2페이지 특수(완전설계 §4-4).
///
/// O — 리치가 떠오르며 손을 뻗으면 봉인석 조각 넷이 제단 네 방위에서 솟아오른다(처음 한 번 — 그 뒤로 남아 떠 있다).
/// → A — 조각이 차례로(시계 · 반시계 무작위) 간격마다 달아올라 광선을 쏜다(폭 beamWidth). 광선마다 예고가 뜰 때 그때 플레이어를 지나게 굳힌다
///   (10-22 E2 L3 — 고정 4방위 · 늘 시계 방향이라 외우면 끝이었다 · 실측 0/3 → 10-22 E5 페이지2 — 첫 광선만 겨누고 나머지는 같은 비껴 각이라
///   한 번 비키면 끝이었다).
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
    private const string ShardHintKey  = "Lich_Shard_Hint";   // 처음 솟을 때 멀린 — 끊는 법(10-03). CSV에 없으면 아래 줄
    private const string ShardHintLine = "청록 조각은 두 번 치면 잠시 꺼져!";

    private readonly GameObject[] _guides   = new GameObject[ShardCount];
    private readonly bool[]       _signaled = new bool[ShardCount];
    private readonly bool[]       _fired    = new bool[ShardCount];
    private readonly float[]      _guideEnd = new float[ShardCount];   // 쏜 뒤 예고를 지울 시각(Time.time) — 앞머리가 끝까지 지난 뒤
    private readonly Vector3[]    _dirs     = new Vector3[ShardCount]; // 광선 방향 — 예고가 뜰 때 그때 플레이어를 지나게 굳힌다(10-22 E5 페이지2)

    private Phase   _phase;
    private float   _timer;
    private int     _first;     // 첫 조각 — 플레이어에서 가장 먼 조각(10-22 E2 L3)
    private int     _dirSign;   // 이어 쏘는 방향(+1 시계 · −1 반시계, 무작위)
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

        bool risen = LichSealShard.EnsureSpawned(_center, Data.shardDistance);
        _first     = 0;   // 광선 단계 시작 때 AimFirst가 정한다
        _dirSign   = 1;

        LichPatternUtil.CastBeat(ctx, LichCast.ArcaneOrb, Data.openDuration + Data.beamWarn, 2f, 0.1f);
        lich?.PulseBook(Data.openDuration + ShardCount * Data.beamInterval);
        LichSfx.Play(LichSfxSlot.CastCharge, ctx.Transform.position);
        UI_BossBark.Show("네 봉인의 잔해다 — 이제 내 것이지.", BossBarkType.PatternAnnounce);
        // 처음 솟을 때 한 번(조각은 T3까지 남는다 = 전투당 한 번) — 끄고 나서야 칠 수 있다는 걸 알았다(10-03).
        if (risen && !UI_BossBark.ShowDialogue(ShardHintKey))
            UI_BossBark.Show(ShardHintLine, BossBarkType.MerlinNarration, DialogueSpeaker.Merlin);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        ReleaseSpentGuides();

        switch (_phase)
        {
            case Phase.Open:
                if (_timer >= Data.openDuration)
                {
                    AimFirst(ctx);
                    Next(Phase.Beams);
                }
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
                if (_timer >= LichPatternUtil.Recovery(ctx, Data.recoveryDuration))
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

    /// <summary>
    /// 첫 조각 = 플레이어에서 가장 먼 조각(10-22 E2 L3) · 이어 쏘는 방향(시계 · 반시계)은 무작위.
    /// 광선 방향은 조각마다 예고가 뜰 때 정한다(<see cref="AimDir"/> · 10-22 E5 페이지2).
    /// </summary>
    private void AimFirst(MonsterContext ctx)
    {
        _first     = 0;
        _dirSign   = Random.value < 0.5f ? 1 : -1;
        var live = LichSealShard.Live;
        if (live.Count == 0) return;

        Vector3 player = LichPatternUtil.PlayerFloorPos(ctx);
        float   far    = -1f;
        for (int i = 0; i < live.Count; i++)
        {
            if (live[i] == null) continue;
            float d = LichPatternUtil.FlatDistance(live[i].BeamOrigin, player);
            if (d <= far) continue;
            far    = d;
            _first = i;
        }
    }

    /// <summary>조각 → 지금 플레이어를 지나는 방향(수평 · 10-22 E5 페이지2). 플레이어가 조각 바로 곁이면 중심 쪽.</summary>
    private Vector3 AimDir(MonsterContext ctx, Vector3 origin)
    {
        Vector3 d = LichPatternUtil.PlayerFloorPos(ctx) - origin;
        d.y = 0f;
        if (d.sqrMagnitude < 0.25f) d = _center - origin;
        d.y = 0f;
        return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.forward;
    }

    private void ReleaseSpentGuides()
    {
        for (int k = 0; k < ShardCount; k++)
            if (_fired[k] && _guides[k] != null && Time.time >= _guideEnd[k])
                PatternGuideHelper.SafeDestroy(ref _guides[k]);
    }

    /// <summary>k번째 광선(첫 조각에서 이어 쏘는 방향으로) — 예고가 차오르고, 신호, 발사. 조각이 꺼져 있으면 예고가 스러지고 쏘지 않는다.</summary>
    private void TickShard(MonsterContext ctx, int k)
    {
        if (_fired[k]) return;
        float fireAt = k * Data.beamInterval + Data.beamWarn;
        float openAt = fireAt - Data.beamWarn;
        if (_timer < openAt) return;

        var shard = Shard(k);
        if (shard == null) { _fired[k] = true; return; }

        Vector3 origin = new Vector3(shard.BeamOrigin.x, _center.y, shard.BeamOrigin.z);
        float length = Data.shardDistance * 2f;

        // 예고가 뜰 때 그때 플레이어를 지나게 굳힌다(10-22 E5 페이지2) — 판정까지 MinLead가 안 남으면 이번 광선은 띄우지 않는다(예고 = 판정)
        if (_guides[k] == null && !shard.IsDisabled && fireAt - _timer >= LichPatternUtil.MinLead)
        {
            _dirs[k]   = AimDir(ctx, origin);
            _guides[k] = LichPatternUtil.PrepareTelegraph(
                PatternGuideHelper.Beam(origin, _dirs[k], length, Data.beamWidth, LichPatternUtil.Arcane), LichPatternUtil.Arcane);
            shard.Charge();
        }
        if (_guides[k] != null)
            LichPatternUtil.TickTelegraph(_guides[k], _timer - openAt, Data.beamWarn, SignalSeconds, ref _signaled[k]);

        if (_timer < fireAt) return;
        _fired[k] = true;

        if (shard.IsDisabled || _guides[k] == null)   // 꺼졌거나 예고 없이 판정할 뻔한 광선(10-22 E5 페이지2)
        {
            PatternGuideHelper.SafeDestroy(ref _guides[k]);
            // 꺼진 조각 — 불발(연기만)
            LichVfx.Play(LichVfxSlot.TeleportVanish, shard.BeamOrigin, Quaternion.identity, 0.4f);
            return;
        }

        _guideEnd[k] = Time.time + length / BeamSpeed;   // 예고는 광선 앞머리가 끝까지 지날 때 지운다

        Vector3 from = shard.BeamOrigin;
        Vector3 to   = origin + _dirs[k] * length + Vector3.up * 0.4f;
        LichVfx.PlayBeam(LichVfxSlot.ArcaneBeam, from, to, Data.beamWidth, 0.7f);
        LichHazards.BeamFront(ctx, origin, _dirs[k], length, Data.beamWidth * 0.5f, BeamSpeed,
                              Data.damageMultiplier, Data.knockbackMultiplier);
        LichSfx.Play(LichSfxSlot.FireBeam, from);
        LichPatternUtil.Impact(LichImpact.Heavy);
        shard.Settle();
    }

    /// <summary>첫 조각에서 이어 쏘는 방향으로 k번째 조각.</summary>
    private LichSealShard Shard(int k)
    {
        var live = LichSealShard.Live;
        if (live.Count == 0) return null;
        int n = live.Count;
        return live[((_first + _dirSign * k) % n + n) % n];
    }
}
}
