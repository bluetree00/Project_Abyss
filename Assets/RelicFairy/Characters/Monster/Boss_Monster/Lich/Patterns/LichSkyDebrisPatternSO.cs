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
/// 첫 낙하점은 늘 플레이어 자리, 나머지는 코어 안 무작위. 마지막 파는 코어를 거의 덮고 <b>흰 안전 칸 safeSpots개</b>만 남긴다.
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

    // ── 런타임 ───────────────────────────────────────────
    private LichSkyDebrisState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichSkyDebrisState(this);
    public override void OnRecycled()                       => _state = new LichSkyDebrisState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        return lichBB != null && lichBB.Page >= 3 && lichBB.SkyDebrisCooldown <= 0f;
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

    private readonly List<Vector3>    _points = new();
    private readonly List<GameObject> _safe   = new();

    private Phase   _phase;
    private float   _timer;
    private int     _wave;
    private Vector3 _center;
    private CancellationToken _ct;

    public LichSkyDebrisState(LichSkyDebrisPatternSO data) : base(data) { }

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
        int count = Mathf.Max(1, Data.waveCounts[wave]);
        _points.Clear();
        _points.Add(LichPatternUtil.PlayerFloorPos(ctx));

        if (last && Data.safeSpots > 0)
        {
            // 코어를 고리처럼 덮되 안전 칸 방위는 비운다.
            ClearSafe();
            var   safeYaws = new List<float>();
            var   safeAt   = new List<Vector3>();
            float safeR    = Data.debrisRadius * 0.9f;
            for (int i = 0; i < Data.safeSpots; i++)
            {
                float yaw = Random.Range(0f, 360f);
                safeYaws.Add(yaw);
                Vector3 at = LichPatternUtil.OnFloor(ctx, _center + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * (Data.coreRadius * 0.6f));
                safeAt.Add(at);
                _safe.Add(PatternGuideHelper.Disc(at + Vector3.up * 0.03f, safeR, LichPatternUtil.SafeWhite));
            }
            // 흰 안전 칸은 안전해야 한다 — 플레이어가 안전 칸에 서 있으면 그 자리엔 안 떨어뜨리고,
            // 다른 낙하점도 낙하 반경이 안전 칸에 닿으면 뺀다(방위 ±40°만으로는 가운데 쪽 낙하점이 안전 칸을 쳤다 · 10-08 점검)
            if (NearAny(_points[0], safeAt, safeR + Data.debrisRadius)) _points.RemoveAt(0);   // 낙하 원이 안전 칸에 닿으면 빼기 — 다른 낙하점과 같은 조건(10-13 검증)
            for (int i = 0; _points.Count < count + 3 && i < 40; i++)
            {
                float yaw  = Random.Range(0f, 360f);
                bool  near = false;
                foreach (float s in safeYaws) if (Mathf.Abs(Mathf.DeltaAngle(s, yaw)) < 40f) near = true;
                if (near) continue;
                Vector3 p = _center + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * Random.Range(0f, Data.coreRadius);
                if (NearAny(p, safeAt, safeR + Data.debrisRadius)) continue;
                _points.Add(p);
            }
        }
        else
        {
            for (int i = 1; i < count; i++)
            {
                Vector2 r = Random.insideUnitCircle * Data.coreRadius;
                _points.Add(_center + new Vector3(r.x, 0f, r.y));
            }
        }

        foreach (var p in _points)
        {
            Vector3 at = LichPatternUtil.OnFloor(ctx, p);
            LichHazards.DelayedBlast(ctx, at, Data.debrisRadius, Data.debrisWarn, Data.damageMultiplier,
                                     LichVfxSlot.DebrisImpact, 1f, crack: true);
            DropRockAsync(at, Data.debrisWarn, _ct).Forget();
        }
        LichSfx.Play(LichSfxSlot.Collapse, _center, 0.6f);
    }

    /// <summary>제단 잔해(룬석) 하나 — 예고 끝에 하늘에서 떨어져 박히고, 잠시 남았다가 사라진다.</summary>
    private static async UniTaskVoid DropRockAsync(Vector3 at, float warn, CancellationToken ct)
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
