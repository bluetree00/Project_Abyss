using System.Collections.Generic;
using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// C8 「심연 절단」 — 2페이지 강한 패턴(09-19 사용자 지시: 낫 패턴보다 강력한 패턴 · 필드를 영구히 갈아먹되 한계를 두고).
///
/// O 상승(riseSeconds) — 리치가 떠오르며 낫을 치켜든다. 제단 가운데에서 플레이어 쪽으로 거대한 부채꼴이 펼쳐진다.
///   부채꼴 안 발판(코어 밖)이 붉게 흔들리기 시작한다(=곧 무너진다). 코어는 흰 원으로 「여기는 안전」.
/// → A 절단 — 예고가 다 차는 순간 거대한 초승달이 부채꼴을 가르고, 그 칸들이 심연으로 떨어진다(영구).
///   부채꼴 안(코어 밖)에 있으면 큰 피해.
/// → E 무방비(endDuration) → R 복귀
///
/// 한계: 이번 전투에 영구히 무너뜨린 칸이 maxErodedCells를 넘지 않는다(바깥 칸부터 — 남는 바닥이 코어와 이어지게).
///       코어(영구 코어 링)는 절대 무너지지 않는다. 예산이 minCells보다 적게 남으면 이 패턴은 고르지 않는다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_AbyssalRendPattern", fileName = "Lich_AbyssalRendPattern")]
public class LichAbyssalRendPatternSO : BossPatternSO
{
    [Header("심연 절단 — 발동")]
    public float patternCooldown = 26f;

    [Header("심연 절단 — 타이밍 (초)")]
    [Tooltip("떠올라 부채꼴이 차오르는 시간 = 발판이 흔들리는 시간(길수록 공정)")]
    public float telegraphSeconds = 1.8f;
    public float endDuration      = 1.4f;
    public float recoveryDuration = 0.5f;

    [Header("심연 절단 — 모양")]
    [Tooltip("부채꼴 반각 (도)")]
    public float halfAngle       = 30f;
    [Tooltip("이 반경(m, 제단 중심 기준) 안은 안전 — 무너지지도 맞지도 않는다")]
    public float safeRadius      = 10f;
    [Tooltip("리치가 떠오르는 높이 (m)")]
    public float riseHeight      = 4f;

    [Header("심연 절단 — 피해")]
    public float damageMultiplier    = 2.2f;
    public float knockbackMultiplier = 1.5f;

    [Header("심연 절단 — 영구 붕괴 한계")]
    [Tooltip("이번 전투에 이 패턴류가 영구히 무너뜨릴 수 있는 최대 칸 수(144칸 제단 기준 약 25%)")]
    public int   maxErodedCells  = 36;
    [Tooltip("남은 예산이 이보다 적으면 고르지 않는다")]
    public int   minCells        = 6;

    // ── 런타임 ───────────────────────────────────────────
    private LichAbyssalRendState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichAbyssalRendState(this);
    public override void OnRecycled()                       => _state = new LichAbyssalRendState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB == null || !lichBB.IsPhase2) return false;
        if (lichBB.AbyssalRendCooldown > 0f) return false;
        if (maxErodedCells - lichBB.ErodedCells < minCells) return false;
        var grid = ArenaTileGrid.Active;
        return grid != null && grid.TryGetCell(ctx.Ctx.Transform.position, out _);
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichAbyssalRendState — 중단 불가(피해는 받음)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichAbyssalRendState : UnInterruptibleState<LichAbyssalRendPatternSO>
{
    private enum Phase { Telegraph, End, Recovery }

    private const float SignalSeconds = 0.2f;
    private const float WedgeReach    = 45f;   // 제단 모서리까지 닿는 부채꼴 반경

    private readonly List<Vector2Int> _cells = new();

    private Phase      _phase;
    private float      _timer;
    private bool       _signaled;
    private Vector3    _center;
    private float      _yaw;
    private GameObject _wedge;
    private GameObject _safe;

    public LichAbyssalRendState(LichAbyssalRendPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase    = Phase.Telegraph;
        _timer    = 0f;
        _signaled = false;

        var grid = ArenaTileGrid.Active;
        var lich = LichPatternUtil.Lich(ctx);
        var mc   = LichPatternUtil.Mover(ctx);
        mc?.SetLocked(true);
        mc?.RequestMovementState(LichMovementState.AltitudeRise);
        mc?.SetAltitudeOffset(Data.riseHeight);

        if (grid == null || !grid.TryGetWorldCenter(out _center)) _center = LichPatternUtil.OnFloor(ctx, ctx.Transform.position);
        Vector3 toPlayer = LichPatternUtil.PlayerFloorPos(ctx) - _center;
        toPlayer.y = 0f;
        _yaw = toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer).eulerAngles.y : ctx.Transform.eulerAngles.y;

        // 부채꼴(위험) + 코어 흰 원(안전).
        _wedge = LichPatternUtil.PrepareTelegraph(
            PatternGuideHelper.Sector(_center, WedgeReach, Data.halfAngle * 2f, _yaw, LichPatternUtil.Crimson),
            LichPatternUtil.Crimson);
        _safe  = PatternGuideHelper.Disc(_center + Vector3.up * 0.02f, Data.safeRadius, LichPatternUtil.SafeWhite);

        // 무너질 칸 — 예고 내내 붉게 흔들리다 절단 순간 떨어진다(바깥 칸부터 예산만큼).
        CollectWedgeCells(grid);
        int budget = lich?.LichBB != null ? Data.maxErodedCells - lich.LichBB.ErodedCells : 0;
        if (grid != null && budget > 0 && _cells.Count > 0)
        {
            if (_cells.Count > budget)
            {
                _cells.Sort((a, b) => DistSq(grid, b).CompareTo(DistSq(grid, a)));
                _cells.RemoveRange(budget, _cells.Count - budget);
            }
            int n = grid.CollapseCells(_cells, Data.telegraphSeconds);
            if (lich?.LichBB != null) lich.LichBB.ErodedCells += n;
        }

        // 낫을 머리 위로 — 내려찍는 순간이 절단.
        LichPatternUtil.HoldFacing(ctx, _yaw, 360f);
        LichPatternUtil.Swing(ctx, LichSwing.Overhead, Data.telegraphSeconds, 0.18f);
        lich?.PulseBook(Data.telegraphSeconds);
        LichVfx.Play(LichVfxSlot.CastFlare, ctx.Transform.position + Vector3.up * 1.5f, Quaternion.identity, 1.5f);
        LichSfx.Play(LichSfxSlot.CastCharge, ctx.Transform.position);
        UI_BossBark.Show("심연이 너희의 땅을 갈라 삼킨다!", BossBarkType.PatternAnnounce);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Telegraph:
                LichPatternUtil.TickTelegraph(_wedge, _timer, Data.telegraphSeconds, SignalSeconds, ref _signaled);
                if (_timer >= Data.telegraphSeconds)
                {
                    Rend(ctx);
                    LichPatternUtil.Lich(ctx)?.NotifyVulnerableWindow(Data.endDuration);
                    Next(Phase.End);
                }
                break;

            case Phase.End:
                if (_timer >= Data.endDuration)
                {
                    LichPatternUtil.Mover(ctx)?.SetAltitudeOffset(0f);
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
        PatternGuideHelper.SafeDestroy(ref _wedge);
        PatternGuideHelper.SafeDestroy(ref _safe);
        var lich = LichPatternUtil.Lich(ctx);
        var mc   = lich?.MovementController;
        mc?.SetAltitudeOffset(0f);
        mc?.RequestMovementState(LichMovementState.AltitudeDescend);
        mc?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.AbyssalRendCooldown = Data.patternCooldown;
    }

    private void Next(Phase phase)
    {
        _phase = phase;
        _timer = 0f;
    }

    /// <summary>절단 — 거대한 초승달이 부채꼴 가운데 선을 따라 떨어지고, 부채꼴 안(코어 밖)을 벤다.</summary>
    private void Rend(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _wedge);
        PatternGuideHelper.SafeDestroy(ref _safe);

        Vector3 dir = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
        float   mid = (Data.safeRadius + WedgeReach * 0.6f) * 0.5f;
        LichPatternUtil.SlashVfx(_center + dir * mid, dir, mid, LichSwing.Overhead, LichPatternUtil.Crimson);
        for (float d = Data.safeRadius + 3f; d < WedgeReach * 0.7f; d += 7f)
            LichVfx.Play(LichVfxSlot.SlamImpact, _center + dir * d, Quaternion.identity, LichPatternUtil.NovaScale(3f));
        LichSfx.Play(LichSfxSlot.SlamImpact, _center + dir * mid);
        LichSfx.Play(LichSfxSlot.Collapse, _center + dir * mid);

        bool hit = InWedge(ctx) && LichPatternUtil.HitPlayer(ctx, _center, Data.damageMultiplier, Data.knockbackMultiplier);
        LichPatternUtil.Impact(LichImpact.Transition, hit);
    }

    private bool InWedge(MonsterContext ctx)
    {
        var target = ctx.Runtime.PlayerTarget;
        if (target == null) return false;
        Vector3 to = target.position - _center;
        to.y = 0f;
        float d = to.magnitude;
        if (d < Data.safeRadius || d > WedgeReach) return false;
        return Vector3.Angle(Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward, to) <= Data.halfAngle;
    }

    /// <summary>부채꼴 안(코어 밖) 칸 — 칸 중심이 부채꼴에 드는 것.</summary>
    private void CollectWedgeCells(ArenaTileGrid grid)
    {
        _cells.Clear();
        if (grid == null) return;
        Vector3 fwd = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
        int n = grid.GridSize;
        for (int x = 0; x < n; x++)
        for (int y = 0; y < n; y++)
        {
            var idx = new Vector2Int(x, y);
            if (!grid.TryGetCellCenter(idx, out var c)) continue;
            Vector3 to = c - _center;
            to.y = 0f;
            float d = to.magnitude;
            if (d < Data.safeRadius || d > WedgeReach) continue;
            if (Vector3.Angle(fwd, to) <= Data.halfAngle) _cells.Add(idx);
        }
    }

    private float DistSq(ArenaTileGrid grid, Vector2Int idx)
        => grid.TryGetCellCenter(idx, out var c) ? (c - _center).sqrMagnitude : 0f;
}
}
