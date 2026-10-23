using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// F3 「천공 낙하」 — 악몽기 3페이지(완전설계 §4-6). 무너진 제단의 잔해가 하늘에서 떨어진다.
///
/// 파 × waveCounts(기본 3 → 4 → 5개) — 파마다 낙하점(반경 debrisRadius)이 붉게 차오르고(debrisWarn) 떨어진다.
/// 첫 낙하점은 늘 플레이어 자리, 나머지는 그 둘레에 빽빽이(가장자리 사이 틈 1.0~2.4 m 무작위 · 10-22 E5 페이지2 — 코어 안 흩뿌림은 큰 빈자리가 남았다).
/// 마지막 파는 <b>흰 안전 칸 safeSpots개</b>(플레이어 몇 걸음 곁)를 비우고 깐다.
/// → E · R
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_SkyDebrisPattern", fileName = "Lich_SkyDebrisPattern")]
public class LichSkyDebrisPatternSO : BossPatternSO
{
    [Header("천공 낙하 — 발동")]
    public float patternCooldown = 12f;

    [Header("천공 낙하 — 파")]
    public int[] waveCounts       = { 3, 4, 5 };
    public float waveInterval     = 1.2f;
    public float debrisWarn       = 1.2f;
    public float debrisRadius     = 2.5f;
    public float damageMultiplier = 1.0f;
    [Tooltip("마지막 파에서 남기는 흰 안전 칸 수(0이면 없음)")]
    public int   safeSpots        = 2;
    [Tooltip("코어 반경 (m) — 낙하점이 이 안에 뿌려진다")]
    public float coreRadius       = 9f;
    public float endDuration      = 0.5f;
    public float recoveryDuration = 0.4f;

    [Header("천공 낙하 — 촘촘히 (10-22 E5 페이지2)")]
    [Tooltip("낙하 원 가장자리 사이 빈틈 (m, 최소 · 최대) — 점마다 무작위라 좁고 매번 다르다. waveCounts는 파마다 상한")]
    [SerializeField] private Vector2 _packGap  = new Vector2(1.0f, 2.4f);
    [Tooltip("흰 안전 칸이 뜨는 자리 — 플레이어에서 이 거리(m) 사이")]
    [SerializeField] private Vector2 _safeNear = new Vector2(3f, 6f);

    // ── 런타임 ───────────────────────────────────────────
    private LichSkyDebrisState _state;

    public Vector2 PackGap  => _packGap;
    public Vector2 SafeNear => _safeNear;

    public override void Initialize(BossPatternContext ctx) => _state = new LichSkyDebrisState(this);
    public override void OnRecycled()                       => _state = new LichSkyDebrisState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        return lichBB != null && lichBB.Page >= 3 && lichBB.SkyDebrisCooldown <= 0f;
    }

    /// <summary>연계(10-22 E5 페이지2 「낫 휘두르기 → 천공 낙하」) — 앞 패턴의 일부라 자기 쿨다운은 보지 않는다.</summary>
    public override bool CanFollowUp(BossPatternContext ctx)
    {
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        return lichBB != null && lichBB.Page >= 3;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichSkyDebrisState
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichSkyDebrisState : UnInterruptibleState<LichSkyDebrisPatternSO>
{
    private enum Phase { Waves, End, Recovery }

    private const float RockHeight      = 18f;
    private const float RockFallSeconds = 0.45f;   // 예고 끝 이만큼 동안 떨어진다
    private const float RockLinger      = 2.5f;    // 떨어진 뒤 남아 있는 시간
    private const float RockScale       = 0.9f;
    private const float RockFadeSeconds = 0.5f;    // 남았다가 디졸브로 사라진다(10-12 디졸브 원칙)
    private const int   SafeTries       = 12;      // 흰 안전 칸 자리 찾기(10-22 E5 페이지2)

    private readonly List<Vector3>    _points = new();
    private readonly List<GameObject> _safe   = new();
    private readonly List<Vector3>    _safeAt = new();   // 흰 안전 칸 자리(10-22 E5 페이지2)
    private readonly System.Predicate<Vector3> _blocked;

    private Phase   _phase;
    private float   _timer;
    private int     _wave;
    private Vector3 _center;
    private CancellationToken _ct;

    public LichSkyDebrisState(LichSkyDebrisPatternSO data) : base(data) => _blocked = Blocked;

    public override void Enter(MonsterContext ctx)
    {
        _phase = Phase.Waves;
        _timer = 0f;
        _wave  = 0;

        var mc = LichPatternUtil.Mover(ctx);
        mc?.SetLocked(true);
        var lich = LichPatternUtil.Lich(ctx);
        _ct = lich != null ? lich.ActivationToken : CancellationToken.None;
        var grid = ArenaTileGrid.Active;
        if (grid == null || !grid.TryGetWorldCenter(out _center))
            _center = mc != null ? mc.ArenaCenter : LichPatternUtil.OnFloor(ctx, ctx.Transform.position);

        LichPatternUtil.CastBeat(ctx, LichCast.MagicBolt, Data.debrisWarn, 2f, 0.1f);
        LichSfx.Play(LichSfxSlot.CastCharge, ctx.Transform.position, 0.8f);
        UI_BossBark.Show("무너진 하늘이 네 위로.", BossBarkType.PatternAnnounce);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Waves:
            {
                int waves = Data.waveCounts != null ? Data.waveCounts.Length : 0;
                if (_wave < waves && _timer >= _wave * Data.waveInterval)
                    DropWave(ctx, _wave++, _wave == waves);
                if (_wave >= waves && _timer >= (waves - 1) * Data.waveInterval + Data.debrisWarn + 0.2f)
                {
                    ClearSafe();
                    LichPatternUtil.Lich(ctx)?.NotifyVulnerableWindow(Data.endDuration);
                    Next(Phase.End);
                }
                break;
            }

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
        ClearSafe();
        var lich = LichPatternUtil.Lich(ctx);
        lich?.MovementController?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.SkyDebrisCooldown = Data.patternCooldown;
    }

    private void Next(Phase phase)
    {
        _phase = phase;
        _timer = 0f;
    }

    /// <summary>한 파 — 첫 낙하점은 플레이어 자리. 마지막 파는 안전 칸을 남기고 코어를 덮는다.</summary>
    private static bool NearAny(Vector3 p, List<Vector3> centers, float radius)
    {
        foreach (var c in centers)
        {
            float dx = p.x - c.x, dz = p.z - c.z;
            if (dx * dx + dz * dz < radius * radius) return true;
        }
        return false;
    }

    private void DropWave(MonsterContext ctx, int wave, bool last)
    {
        int     count  = Mathf.Max(1, Data.waveCounts[wave]);
        Vector3 player = LichPatternUtil.PlayerFloorPos(ctx);
        _points.Clear();
        _safeAt.Clear();

        if (last && Data.safeSpots > 0)
        {
            // 흰 안전 칸 — 플레이어 몇 걸음 곁(10-22 E5 페이지2 · 예전엔 코어 어디든 방위만 무작위)
            ClearSafe();
            float safeR = Data.debrisRadius * 0.9f;
            for (int i = 0; i < Data.safeSpots; i++)
            {
                Vector3 at = SafeSpot(ctx, player, safeR);
                _safeAt.Add(at);
                _safe.Add(PatternGuideHelper.Disc(at + Vector3.up * 0.03f, safeR, LichPatternUtil.SafeWhite));
            }
        }
        // 첫 점 = 플레이어 자리 · 나머지는 둘레에 빽빽이 — 틈 1.0~2.4 m 무작위(10-22 E5 페이지2).
        // 흰 안전 칸은 안전해야 한다 — 낙하 원이 안전 칸에 닿는 자리는 비운다(플레이어 자리 포함 · 10-08 점검 · 10-13 검증 규칙 그대로)
        LichPatternUtil.PackDiscs(_points, player, Data.coreRadius, Data.debrisRadius, Data.PackGap, count, player, _blocked);

        foreach (var p in _points)
        {
            Vector3 at = LichPatternUtil.OnFloor(ctx, p);
            LichHazards.DelayedBlast(ctx, at, Data.debrisRadius, Data.debrisWarn, Data.damageMultiplier,
                                     LichVfxSlot.DebrisImpact, 1f, crack: true);
            DropRockAsync(at, Data.debrisWarn, _ct).Forget();
        }
        LichSfx.Play(LichSfxSlot.Collapse, _center, 0.6f);
    }

    /// <summary>제단 잔해(룬석) 하나 — 예고 끝에 하늘에서 떨어져 박히고, 잠시 남았다가 사라진다. 영혼 복제의 잔해 한 파도 쓴다(10-22 E2 L1).</summary>
    internal static async UniTaskVoid DropRockAsync(Vector3 at, float warn, CancellationToken ct)
    {
        GameObject rock = null;
        try
        {
            await UniTask.Delay(System.TimeSpan.FromSeconds(Mathf.Max(0f, warn - RockFallSeconds)), cancellationToken: ct);
            Vector3 from = at + Vector3.up * RockHeight;
            var tilt = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-25f, 25f));
            rock = LichVfx.InstantiateModel(LichVfxSlot.SealStoneModel, from, tilt);
            if (rock == null) return;
            rock.transform.localScale *= RockScale;
            float t = 0f;
            while (t < RockFallSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / RockFallSeconds);
                rock.transform.position = Vector3.LerpUnclamped(from, at, k * k);
                await UniTask.Yield(ct);
            }
            rock.transform.position = at;
            await UniTask.Delay(System.TimeSpan.FromSeconds(RockLinger), cancellationToken: ct);
        }
        catch (System.OperationCanceledException) { }
        finally
        {
            // 잔해는 디졸브로 거둔다 — 예전엔 RockLinger 뒤 그냥 사라졌다
            if (rock != null)
            {
                var r = rock;
                DissolveEffect.PlayDisappear(r, RockFadeSeconds, () => { if (r != null) Object.Destroy(r); });
            }
        }
    }

    /// <summary>낙하점을 두지 않는 자리 — 코어 밖 · 흰 안전 칸에 닿는 자리(10-22 E5 페이지2).</summary>
    private bool Blocked(Vector3 p)
        => LichPatternUtil.FlatDistance(p, _center) > Data.coreRadius
           || NearAny(p, _safeAt, Data.debrisRadius * 0.9f + Data.debrisRadius);

    /// <summary>흰 안전 칸 자리 — 플레이어에서 SafeNear 거리 · 코어 안 · 다른 안전 칸과 겹치지 않게(10-22 E5 페이지2). 못 찾으면 코어 안 무작위.</summary>
    private Vector3 SafeSpot(MonsterContext ctx, Vector3 player, float safeR)
    {
        float room = Mathf.Max(0f, Data.coreRadius - safeR);
        for (int tries = 0; tries < SafeTries; tries++)
        {
            Vector3 p = player + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward
                                 * Random.Range(Data.SafeNear.x, Data.SafeNear.y);
            if (LichPatternUtil.FlatDistance(p, _center) <= room && !NearAny(p, _safeAt, safeR * 2f))
                return LichPatternUtil.OnFloor(ctx, p);
        }
        Vector2 r = Random.insideUnitCircle * room;
        return LichPatternUtil.OnFloor(ctx, _center + new Vector3(r.x, 0f, r.y));
    }

    private void ClearSafe()
    {
        for (int i = 0; i < _safe.Count; i++)
        {
            var g = _safe[i];
            PatternGuideHelper.SafeDestroy(ref g);
        }
        _safe.Clear();
    }
}
}
