using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// C9 「무너지는 별」 — 2페이지 강한 패턴(09-19 사용자 지시: 낫 패턴보다 강력한 패턴들 · 필드를 영구히 갈아먹되 한계를 두고).
///
/// O 시전 — 리치가 떠올라 책을 치켜든다.
/// → 별 × starCount — 간격마다 지금 플레이어 자리에 보라 원이 차오르고(warnSeconds), 그 칸들이 붉게 흔들린다.
///   마지막 0.5초에 거대한 어둠의 별이 하늘에서 떨어져 → 큰 피해 + 충돌 자리 칸이 영구히 무너진다(작은 구덩이).
///   계속 움직이면 산다 — 멈춰 서면 발밑이 사라진다.
/// → E 무방비 → R 복귀
///
/// 한계: 심연 절단(C8)과 같은 예산(이번 전투 누적 maxErodedCells칸)을 나눠 쓴다. 코어는 절대 안 무너진다.
///       예산이 바닥나도 별은 떨어진다(피해만).
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_FallingStarsPattern", fileName = "Lich_FallingStarsPattern")]
public class LichFallingStarsPatternSO : BossPatternSO
{
    [Header("무너지는 별 — 발동")]
    public float patternCooldown = 22f;

    [Header("무너지는 별 — 타이밍 (초)")]
    public float castDuration     = 0.5f;
    public int   starCount        = 3;
    [Tooltip("별 사이 간격")]
    public float starInterval     = 0.9f;
    [Tooltip("원이 차오르는 시간 = 발판이 흔들리는 시간")]
    public float warnSeconds      = 1.6f;
    [Tooltip("별이 하늘에서 떨어지는 시간(예고 끝부분)")]
    public float fallSeconds      = 0.5f;
    public float endDuration      = 1.0f;
    public float recoveryDuration = 0.5f;

    [Header("무너지는 별 — 판정 · 붕괴")]
    public float blastRadius      = 4.5f;
    public float damageMultiplier = 1.6f;
    [Tooltip("이 반경(m) 안 칸 중심이 영구히 무너진다")]
    public float collapseRadius   = 3.2f;
    [Tooltip("이번 전투에 강한 패턴류가 영구히 무너뜨릴 최대 칸 수(C8과 같은 예산)")]
    public int   maxErodedCells   = 36;

    [Header("무너지는 별 — 연출")]
    public float riseHeight       = 3f;
    [Tooltip("떨어지는 별 크기(DarkOrb 배율)")]
    public float starScale        = 4f;
    public float starDropHeight   = 25f;

    // ── 런타임 ───────────────────────────────────────────
    private LichFallingStarsState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichFallingStarsState(this);
    public override void OnRecycled()                       => _state = new LichFallingStarsState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB == null || !lichBB.IsPhase2) return false;
        return lichBB.FallingStarsCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichFallingStarsState — 중단 불가(피해는 받음)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichFallingStarsState : UnInterruptibleState<LichFallingStarsPatternSO>
{
    private enum Phase { Cast, Stars, End, Recovery }

    private readonly List<Vector2Int> _cells = new();

    private Phase _phase;
    private float _timer;
    private int   _spawned;
    private CancellationTokenSource _cts;

    public LichFallingStarsState(LichFallingStarsPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase   = Phase.Cast;
        _timer   = 0f;
        _spawned = 0;

        var lich = LichPatternUtil.Lich(ctx);
        var mc   = LichPatternUtil.Mover(ctx);
        mc?.SetLocked(true);
        mc?.RequestMovementState(LichMovementState.AltitudeRise);
        mc?.SetAltitudeOffset(Data.riseHeight);

        _cts = lich != null ? CancellationTokenSource.CreateLinkedTokenSource(lich.ActivationToken) : new CancellationTokenSource();

        LichPatternUtil.CastBeat(ctx, LichCast.ArcaneOrb, Data.castDuration + Data.warnSeconds - Data.fallSeconds, 2.2f, 0.12f);
        lich?.PulseBook(Data.castDuration + Data.starCount * Data.starInterval);
        LichVfx.Play(LichVfxSlot.CastFlare, ctx.Transform.position + Vector3.up * 1.5f, Quaternion.identity, 1.4f);
        LichSfx.Play(LichSfxSlot.CastCharge, ctx.Transform.position);
        UI_BossBark.Show("별이 떨어진다 — 설 곳은 남지 않으리라!", BossBarkType.PatternAnnounce);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Cast:
                if (_timer >= Data.castDuration) Next(Phase.Stars);
                break;

            case Phase.Stars:
                if (_spawned < Data.starCount && _timer >= _spawned * Data.starInterval)
                    SpawnStar(ctx);
                // 마지막 별이 떨어진 뒤 무방비
                if (_spawned >= Data.starCount && _timer >= (Data.starCount - 1) * Data.starInterval + Data.warnSeconds)
                {
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
        // 이미 예고된 별은 끝까지 떨어진다(예고를 보고 피한 플레이어의 판단을 지킨다). 하늘의 별 연출만 걷는다.
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        var lich = LichPatternUtil.Lich(ctx);
        var mc   = lich?.MovementController;
        mc?.SetAltitudeOffset(0f);
        mc?.RequestMovementState(LichMovementState.AltitudeDescend);
        mc?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.FallingStarsCooldown = Data.patternCooldown;
    }

    private void Next(Phase phase)
    {
        _phase = phase;
        _timer = 0f;
    }

    /// <summary>별 하나 — 지금 플레이어 자리에 예고(피해는 LichHazards가 맡는다) + 발판 흔들림(영구 붕괴 확정) + 떨어지는 별.</summary>
    private void SpawnStar(MonsterContext ctx)
    {
        _spawned++;
        Vector3 at = LichPatternUtil.PlayerFloorPos(ctx);

        LichHazards.DelayedBlast(ctx, at, Data.blastRadius, Data.warnSeconds, Data.damageMultiplier,
                                 LichVfxSlot.SlamImpact, LichPatternUtil.NovaScale(Data.blastRadius));
        CollapseCrater(ctx, at);
        if (_cts != null) DropStarAsync(at, _cts.Token).Forget();
        LichSfx.Play(LichSfxSlot.CastShort, ctx.Transform.position, 0.8f);
    }

    /// <summary>충돌 자리 칸 — 예고 내내 붉게 흔들리다 별이 닿는 순간 떨어진다(예산 안에서, 가까운 칸부터).</summary>
    private void CollapseCrater(MonsterContext ctx, Vector3 at)
    {
        var grid = ArenaTileGrid.Active;
        var bb   = LichPatternUtil.Lich(ctx)?.LichBB;
        if (grid == null || bb == null) return;
        int budget = Data.maxErodedCells - bb.ErodedCells;
        if (budget <= 0) return;

        grid.CellsInRadius(at, Data.collapseRadius, _cells);
        if (_cells.Count > budget)
        {
            _cells.Sort((a, b) => Dist(grid, a, at).CompareTo(Dist(grid, b, at)));
            _cells.RemoveRange(budget, _cells.Count - budget);
        }
        bb.ErodedCells += grid.CollapseCells(_cells, Data.warnSeconds);
    }

    /// <summary>하늘에서 떨어지는 어둠의 별 — 예고 끝 fallSeconds 동안 내려와 충돌 순간 사라진다.</summary>
    private async UniTaskVoid DropStarAsync(Vector3 at, CancellationToken ct)
    {
        GameObject star = null;
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, Data.warnSeconds - Data.fallSeconds)), cancellationToken: ct);
            Vector3 from = at + Vector3.up * Data.starDropHeight;
            star = LichVfx.PlayLoop(LichVfxSlot.DarkOrb, from, Quaternion.identity, Data.starScale);
            float t = 0f;
            while (t < Data.fallSeconds && star != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / Data.fallSeconds);
                star.transform.position = Vector3.Lerp(from, at + Vector3.up, k * k);   // 점점 빨라진다
                await UniTask.Yield(ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (star != null) LichVfx.Stop(ref star);
        }
    }

    private static float Dist(ArenaTileGrid grid, Vector2Int idx, Vector3 at)
        => grid.TryGetCellCenter(idx, out var c) ? (c - at).sqrMagnitude : float.MaxValue;
}
}
