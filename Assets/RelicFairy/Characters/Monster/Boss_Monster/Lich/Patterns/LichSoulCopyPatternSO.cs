using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// F1 「영혼 복제」 — 악몽기 3페이지(완전설계 §4-6).
///
/// O (openDuration = 녹화 시간) — 리치가 플레이어에게 손을 뻗는다. 코어 두 모서리에 보라 원(복제체가 설 자리)이 차오르고,
///   그동안 플레이어의 움직임 · 공격 박자가 기록된다.
/// → A — 플레이어 몸에서 보라 잔상이 뜯겨 나가 복제체 2체로 선다(<see cref="LichSoulCopy"/>).
///   복제체는 1.5초 전 플레이어의 움직임을 제 자리 기준으로 되풀이하고, 그 박자에 지금의 플레이어를 벤다(보라 예고선 0.4초).
///   리치는 떠서 약한 마력탄만 간간이 — 복제체가 모두 사라지거나 수명이 다하면 끝.
/// → R
/// 대응: 자기 리듬을 바꾼다(연타를 멈추면 복제체도 멈춘다) · 복제체는 두 번 치면 흩어진다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_SoulCopyPattern", fileName = "Lich_SoulCopyPattern")]
public class LichSoulCopyPatternSO : BossPatternSO
{
    [Header("영혼 복제 — 발동")]
    public float patternCooldown = 24f;

    [Header("영혼 복제 — 타이밍 (초)")]
    [Tooltip("손을 뻗는 시간 = 복제체가 되풀이할 기록이 쌓이는 시간(1.5초 이상)")]
    public float openDuration     = 1.5f;
    [Tooltip("복제체가 있는 동안 리치가 버티는 최대 시간")]
    public float activeDuration   = 10f;
    public float recoveryDuration = 0.5f;

    [Header("영혼 복제 — 복제체")]
    [Tooltip("복제체가 서는 자리 — 코어 중심에서 대각선 거리 (m)")]
    public float anchorDistance = 7f;
    [Tooltip("복제체가 움직일 수 있는 코어 반경 (m)")]
    public float coreRadius     = 9f;

    [Header("영혼 복제 — 리치의 약한 마력탄")]
    public float boltInterval   = 2.5f;
    public float boltRadius     = 1.8f;
    public float boltWarn       = 1.0f;
    public float boltDamage     = 0.5f;

    // ── 런타임 ───────────────────────────────────────────
    private LichSoulCopyState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichSoulCopyState(this);
    public override void OnRecycled()                       => _state = new LichSoulCopyState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        return lichBB != null && lichBB.Page >= 3 && lichBB.SoulCopyCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichSoulCopyState
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichSoulCopyState : UnInterruptibleState<LichSoulCopyPatternSO>
{
    private enum Phase { Open, Active, Recovery }

    private const int CopyCount = 2;

    private readonly LichSoulTape  _tape     = new();
    private readonly Vector3[]     _anchors  = new Vector3[CopyCount];
    private readonly GameObject[]  _marks    = new GameObject[CopyCount];
    private readonly bool[]        _signaled = new bool[CopyCount];

    private Phase   _phase;
    private float   _timer;
    private float   _boltTimer;
    private Vector3 _center;

    public LichSoulCopyState(LichSoulCopyPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase     = Phase.Open;
        _timer     = 0f;
        _boltTimer = 0f;

        var mc = LichPatternUtil.Mover(ctx);
        mc?.SetLocked(true);
        mc?.RequestMovementState(LichMovementState.AltitudeRise);

        var grid = ArenaTileGrid.Active;
        if (grid == null || !grid.TryGetWorldCenter(out _center))
            _center = mc != null ? mc.ArenaCenter : LichPatternUtil.OnFloor(ctx, ctx.Transform.position);

        var target = ctx.Runtime.PlayerTarget;
        PlayerController player = null;
        if (target != null) target.TryGetComponent(out player);
        if (player == null || !_tape.Begin(player))
        {
            Debug.LogWarning("[Lich] 영혼 복제 — 플레이어를 못 찾아 건너뛴다", ctx.Monster);
            _phase = Phase.Recovery;
            return;
        }

        PickAnchors(ctx);
        for (int i = 0; i < CopyCount; i++)
        {
            _signaled[i] = false;
            _marks[i] = LichPatternUtil.PrepareTelegraph(
                PatternGuideHelper.Disc(_anchors[i] + Vector3.up * 0.03f, 1.4f, LichPatternUtil.Arcane), LichPatternUtil.Arcane);
        }

        LichPatternUtil.FaceInstant(ctx, target.position);
        LichPatternUtil.CastBeat(ctx, LichCast.ArcaneOrb, Data.openDuration, 2.2f, 0.12f);
        LichPatternUtil.Lich(ctx)?.PulseBook(Data.openDuration);
        LichVfx.Play(LichVfxSlot.CastFlare, ctx.Transform.position + Vector3.up * 1.5f, Quaternion.identity, 1.4f);
        LichSfx.Play(LichSfxSlot.CastCharge, ctx.Transform.position, 0.8f);
        UI_BossBark.Show("네 영혼, 조금만 빌리지.", BossBarkType.PatternAnnounce);
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;
        _timer += dt;
        _tape.Record();

        switch (_phase)
        {
            case Phase.Open:
                for (int i = 0; i < CopyCount; i++)
                    LichPatternUtil.TickTelegraph(_marks[i], _timer, Data.openDuration, 0.2f, ref _signaled[i]);
                if (_timer >= Data.openDuration)
                {
                    SpawnCopies(ctx);
                    _phase = Phase.Active;
                    _timer = 0f;
                }
                break;

            case Phase.Active:
                _boltTimer += dt;
                if (_boltTimer >= Data.boltInterval)
                {
                    _boltTimer = 0f;
                    FireBolt(ctx);
                }
                if (LichSoulCopy.Live.Count == 0 || _timer >= Data.activeDuration)
                {
                    LichSoulCopy.ClearAll();
                    LichPatternUtil.Lich(ctx)?.NotifyVulnerableWindow(Data.recoveryDuration + 0.4f);
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
        for (int i = 0; i < CopyCount; i++) PatternGuideHelper.SafeDestroy(ref _marks[i]);
        LichSoulCopy.ClearAll();
        _tape.End();

        var lich = LichPatternUtil.Lich(ctx);
        var mc   = lich?.MovementController;
        mc?.RequestMovementState(LichMovementState.AltitudeDescend);
        mc?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.SoulCopyCooldown = Data.patternCooldown;
    }

    /// <summary>코어 네 모서리 중 플레이어 양옆 둘 — 가장 가깝지도 멀지도 않은 두 자리.</summary>
    private void PickAnchors(MonsterContext ctx)
    {
        Vector3 player = LichPatternUtil.PlayerFloorPos(ctx);
        var corners = new Vector3[4];
        var dist    = new float[4];
        for (int i = 0; i < 4; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, 45f + 90f * i, 0f) * Vector3.forward;
            corners[i] = LichPatternUtil.OnFloor(ctx, _center + dir * Data.anchorDistance);
            dist[i]    = LichPatternUtil.FlatDistance(corners[i], player);
        }
        System.Array.Sort(dist, corners);
        _anchors[0] = corners[1];
        _anchors[1] = corners[2];
    }

    private void SpawnCopies(MonsterContext ctx)
    {
        for (int i = 0; i < CopyCount; i++)
        {
            PatternGuideHelper.SafeDestroy(ref _marks[i]);
            // 플레이어 몸에서 연기가 뜯겨 나가 모서리에 선다(나타남 연기는 복제체 쪽이 튼다).
            LichSoulCopy.Create(ctx, _tape, _anchors[i], _center, Data.coreRadius);
        }
        LichVfx.Play(LichVfxSlot.TeleportVanish, _tape.Player.transform.position, Quaternion.identity, 0.5f);   // 플레이어 몸에서 뜯겨 나가는 연기
        LichSfx.Play(LichSfxSlot.Appear, _center);
        LichPatternUtil.Impact(LichImpact.Heavy);
        LichPatternProbe.Signal();
    }

    /// <summary>복제체가 싸우는 동안 리치의 견제 — 플레이어 자리에 작은 지연 폭발 하나.</summary>
    private void FireBolt(MonsterContext ctx)
    {
        Vector3 at = LichPatternUtil.PlayerFloorPos(ctx);
        LichHazards.DelayedBlast(ctx, at, Data.boltRadius, Data.boltWarn, Data.boltDamage,
                                 LichVfxSlot.BoltImpact, LichPatternUtil.BoltScale(Data.boltRadius), crack: false);
        LichVfx.Play(LichVfxSlot.BoltMuzzle, ctx.Transform.position + Vector3.up * 1.6f, Quaternion.identity, 0.8f);
        LichSfx.Play(LichSfxSlot.CastShort, ctx.Transform.position, 0.6f);
    }
}
}
