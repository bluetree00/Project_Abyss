using System.Collections.Generic;
using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// R8 「낫의 대마법」 — 악몽기 2페이지(완전설계 §4-4, 0913 N-3 계승).
///
/// O 충전(windupDuration) — 책이 펼쳐져 떠오르고 리치가 낫을 뒤로 크게 당긴다. 플레이어를 지나 제단 끝까지 폭발 자리 원(판정 그대로)이 차오른다.
/// → A 투척 — 낫이 궤적을 따라 날아가고(초속 flySpeed), 지나간 자리가 <b>0.4초 뒤 연쇄 폭발</b>(반경 blastRadius).
///   낫을 피해도 폭발이 따라온다 — 궤적 밖으로 빠져야 산다. 폭발이 겹친 칸은 두 번 맞아 영구히 무너진다(누적 한계 안에서).
///   플레이어는 한 줄에서 한 번만 맞는다(원이 겹친 띠에서 두 번 맞던 것, 10-08 점검).
/// → E 무방비 — 낫이 돌아올 때까지 빈손(endDuration) → R 복귀
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_ScytheMagicPattern", fileName = "Lich_ScytheMagicPattern")]
public class LichScytheMagicPatternSO : BossPatternSO
{
    [Header("낫의 대마법 — 발동")]
    public float patternCooldown = 16f;

    [Header("낫의 대마법 — 타이밍 (초)")]
    public float windupDuration   = 1.8f;
    public float signalDuration   = 0.2f;
    [Tooltip("낫이 지나간 뒤 폭발까지")]
    public float blastDelay       = 0.4f;
    [Tooltip("폭발 사이 간격(초) — 낫이 이만큼 날 때마다 한 번")]
    public float blastInterval    = 0.2f;
    public float endDuration      = 1.0f;
    public float recoveryDuration = 0.4f;

    [Header("낫의 대마법 — 궤적 · 판정")]
    public float flySpeed         = 18f;
    public float maxLength        = 34f;
    [Tooltip("폭발 반경 = 예고 원 반경")]
    public float blastRadius      = 2f;
    public float damageMultiplier = 1.2f;
    [Tooltip("겹친 폭발에 두 번 맞은 칸은 영구히 무너진다(0이면 안 무너진다)")]
    public int   hitsToCollapse   = 2;
    [Tooltip("이번 전투에 강한 패턴류가 영구히 무너뜨릴 최대 칸 수(C8 · C9와 같은 예산)")]
    public int   maxErodedCells   = 36;

    // ── 런타임 ───────────────────────────────────────────
    private LichScytheMagicState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichScytheMagicState(this);
    public override void OnRecycled()                       => _state = new LichScytheMagicState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB == null || !lichBB.IsPhase2) return false;
        return lichBB.ScytheMagicCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichScytheMagicState — 중단 불가(피해는 받음)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichScytheMagicState : UnInterruptibleState<LichScytheMagicPatternSO>
{
    private enum Phase { Windup, Signal, Fly, End, Recovery }

    private const float SpinHeight = 1.2f;

    private readonly Dictionary<Vector2Int, int> _cellHits = new();
    private readonly List<Vector2Int>            _cells    = new();
    private readonly List<Vector2Int>            _doomed   = new();
    // 폭발 자리마다 판정 크기 원 — 직선 예고(폭 = 지름)는 원 사이 빈 귀퉁이까지 위험해 보였다(10-08 점검).
    private readonly List<GameObject>            _dots     = new();

    private Phase      _phase;
    private float      _timer;
    private Vector3    _start;
    private Vector3    _dir;
    private float      _length;
    private float      _travelled;
    private float      _nextBlast;
    private int        _blastIndex;   // 다음 폭발 = _dots 순번
    private GameObject _spin;
    private LichHazards.BlastRow _row;   // 이번 투척의 폭발 묶음 — 한 줄에서 한 번만 맞는다(10-08 점검)

    /// <summary>폭발 간격(m) — 낫이 blastInterval초 동안 나는 거리.</summary>
    private float BlastStep => Mathf.Max(0.5f, Data.flySpeed * Data.blastInterval);

    public LichScytheMagicState(LichScytheMagicPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase     = Phase.Windup;
        _timer     = 0f;
        _travelled = 0f;
        _nextBlast = 0f;
        _blastIndex = 0;
        _cellHits.Clear();

        var lich = LichPatternUtil.Lich(ctx);
        var mc   = LichPatternUtil.Mover(ctx);
        mc?.SetLocked(true);
        mc?.RequestMovementState(LichMovementState.AltitudeDescend);

        _start = LichPatternUtil.OnFloor(ctx, ctx.Transform.position);
        Vector3 to = LichPatternUtil.PlayerFloorPos(ctx) - _start;
        to.y = 0f;
        _dir    = to.sqrMagnitude > 0.01f ? to.normalized : ctx.Transform.forward;
        _length = Mathf.Min(Data.maxLength, LichPatternUtil.DistanceToArenaEdge(ctx, _start, _dir));

        LichPatternUtil.FaceInstant(ctx, _start + _dir);
        LichPatternUtil.HoldFacing(ctx, Quaternion.LookRotation(_dir).eulerAngles.y);
        // 뒤로 크게 당겼다가 가로로 내던진다 — 접촉 프레임 = 낫이 손을 떠나는 순간.
        LichPatternUtil.Swing(ctx, LichSwing.RightToLeft, Data.windupDuration + Data.signalDuration, 0.1f);

        // 예고 = 실제로 터질 원들(Fly와 같은 간격 · 같은 자리).
        ClearDots();
        for (float d = 0f; d <= _length; d += BlastStep)
            _dots.Add(LichPatternUtil.PrepareTelegraph(
                PatternGuideHelper.Disc(LichPatternUtil.OnFloor(ctx, _start + _dir * d), Data.blastRadius, LichPatternUtil.Arcane),
                LichPatternUtil.Arcane));
        _row = new LichHazards.BlastRow();

        lich?.PulseBook(Data.windupDuration + Data.signalDuration);
        LichVfx.Play(LichVfxSlot.CastFlare, ctx.Transform.position + Vector3.up * 1.4f, Quaternion.identity, 1.2f);
        LichSfx.Play(LichSfxSlot.CastCharge, ctx.Transform.position);
        UI_BossBark.Show("낫이여 — 길을 불태워라!", BossBarkType.PatternAnnounce);
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;
        _timer += dt;

        switch (_phase)
        {
            case Phase.Windup:
                // 원들이 함께 차오르고 다 차는 순간 빨강(신호).
                for (int i = 0; i < _dots.Count; i++)
                {
                    bool signaled = false;
                    LichPatternUtil.TickTelegraph(_dots[i], _timer, Data.windupDuration, 0f, ref signaled);
                }
                if (_timer >= Data.windupDuration) Next(Phase.Signal);
                break;

            case Phase.Signal:
                if (_timer >= Data.signalDuration) Throw(ctx);
                break;

            case Phase.Fly:
                Fly(ctx, dt);
                break;

            case Phase.End:
                if (_timer >= Data.endDuration)
                {
                    LichPatternUtil.Lich(ctx)?.SetScytheVisible(true);
                    LichSfx.Play(LichSfxSlot.ScytheCatch, ctx.Transform.position);
                    Next(Phase.Recovery);
                }
                break;

            case Phase.Recovery:
                if (_timer >= LichPatternUtil.Recovery(ctx, Data.recoveryDuration))
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        ClearDots();
        LichVfx.Stop(ref _spin);
        var lich = LichPatternUtil.Lich(ctx);
        lich?.SetScytheVisible(true);
        lich?.MovementController?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.ScytheMagicCooldown = Data.patternCooldown;
    }

    private void Next(Phase phase)
    {
        _phase = phase;
        _timer = 0f;
    }

    private void Throw(MonsterContext ctx)
    {
        LichPatternUtil.Lich(ctx)?.SetScytheVisible(false);
        Vector3 at = _start + Vector3.up * SpinHeight;
        _spin = LichVfx.PlayLoop(LichVfxSlot.ScytheSpin, at, Quaternion.LookRotation(Vector3.up, _dir), 1.6f);
        LichSfx.Play(LichSfxSlot.ScytheThrow, _start);
        LichPatternUtil.Impact(LichImpact.Slash);
        Next(Phase.Fly);
    }

    /// <summary>낫이 날아가며 지나간 자리에 폭발을 건다 — 0.4초 뒤 터진다(예고 원이 차오른다).</summary>
    private void Fly(MonsterContext ctx, float dt)
    {
        _travelled += Data.flySpeed * dt;
        float along = Mathf.Min(_travelled, _length);
        if (_spin != null) _spin.transform.position = _start + _dir * along + Vector3.up * SpinHeight;

        while (_nextBlast <= along)
        {
            Vector3 at = LichPatternUtil.OnFloor(ctx, _start + _dir * _nextBlast);
            LichHazards.DelayedBlast(ctx, at, Data.blastRadius, Data.blastDelay, Data.damageMultiplier,
                                     LichVfxSlot.BoltImpact, LichPatternUtil.BoltScale(Data.blastRadius), row: _row, armed: true);
            // 이 자리 예고 원은 폭발이 제 원(차오름 → 빨강)으로 이어받는다.
            if (_blastIndex < _dots.Count)
            {
                var dot = _dots[_blastIndex];
                PatternGuideHelper.SafeDestroy(ref dot);
                _dots[_blastIndex] = null;
            }
            _blastIndex++;
            CountCells(ctx, at);
            _nextBlast += BlastStep;
        }

        if (_travelled < _length) return;
        ClearDots();
        LichVfx.Stop(ref _spin, 0.3f);
        CollapseDoubleHit(ctx);
        LichPatternUtil.Lich(ctx)?.NotifyVulnerableWindow(Data.endDuration);
        Next(Phase.End);
    }

    private void ClearDots()
    {
        for (int i = 0; i < _dots.Count; i++)
        {
            var dot = _dots[i];
            PatternGuideHelper.SafeDestroy(ref dot);
        }
        _dots.Clear();
    }

    /// <summary>폭발이 덮는 칸마다 맞은 횟수를 센다.</summary>
    private void CountCells(MonsterContext ctx, Vector3 at)
    {
        var grid = ArenaTileGrid.Active;
        if (grid == null || Data.hitsToCollapse <= 0) return;
        grid.CellsInRadius(at, Data.blastRadius, _cells);
        foreach (var c in _cells)
            _cellHits[c] = _cellHits.TryGetValue(c, out int n) ? n + 1 : 1;
    }

    /// <summary>두 번 이상 맞은 칸 — 마지막 폭발이 터질 즈음 무너진다(예산 안에서, 리치 가까운 칸부터).</summary>
    private void CollapseDoubleHit(MonsterContext ctx)
    {
        var grid = ArenaTileGrid.Active;
        var bb   = LichPatternUtil.Lich(ctx)?.LichBB;
        if (grid == null || bb == null || Data.hitsToCollapse <= 0) return;
        int budget = Data.maxErodedCells - bb.ErodedCells;
        if (budget <= 0) return;

        _doomed.Clear();
        foreach (var kv in _cellHits)
            if (kv.Value >= Data.hitsToCollapse) _doomed.Add(kv.Key);
        if (_doomed.Count > budget) _doomed.RemoveRange(budget, _doomed.Count - budget);
        bb.ErodedCells += grid.CollapseCells(_doomed, Data.blastDelay + 0.3f);
    }
}
}
