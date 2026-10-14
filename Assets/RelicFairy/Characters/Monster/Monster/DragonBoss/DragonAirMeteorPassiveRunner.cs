using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 드래곤 패시브 메테오 러너.
/// 전투 시작 시 상시 가동되며, 드래곤 속성에 따라 발동 조건이 달라진다.
/// Ice: 지상이 아닐 때 / Thunder·Fire·Abyss(2페이지): 항상.
/// 공허 낙하 · 검은 태양 동안은 쉰다(<see cref="DragonBossBlackboard.PassiveMeteorSuppressed"/>).
///
/// 10-14 사용자 「같은 판정에 무작위로 내려 의도적인 위협이 없다 — 양 · 크기 · 범위 불규칙 · 플레이어를 노리는 메테오」:
///   · 흩뿌림 — 크기 셋(반경 1 · 2 · 3칸 원, 바깥 테두리 칸 일부를 빼 모양이 불규칙) · 넷 중 하나 남짓은 플레이어 둘레 7 m 안.
///     크기가 클수록 예고 · 피해 · 이펙트도 크다(예고 = 판정).
///   · 노림 — 버스트 중 약 1.4초(2페이지 1.0초)마다 플레이어 자리(움직이면 조금 앞) · 두 번에 한 번은 세 발이 0.35초 간격으로 쫓아온다.
///   · 큰 메테오 — 버스트마다 한 번, 플레이어 자리에 반경 4칸 원 · 예고를 길게.
/// 예고(바닥 칸) → 낙하(<see cref="DragonFireballRainPatternSO.ImpactDelay"/>) → 착탄. 앞섬은 모두 0.45초 이상.
/// </summary>
public class DragonAirMeteorPassiveRunner
{
    // ── 10-14 메테오 개편 ─────────────────────────────────────
    private enum Kind { Scatter, Aimed, Big }

    private const float ScatterGap      = 0.09f;   // 흩뿌림 간격(예전 0.1)
    private const float ScatterNearShare = 0.25f;  // 흩뿌림 중 플레이어 둘레에 떨어지는 몫(0.4는 둘레가 빈틈없이 덮였다 — 10-14 실측)
    private const float NearRadius      = 7f;
    private const float ScatterWarn     = 1.0f;    // 바닥 예고(예전 SO 1.5 — 착탄까지 2.1초라 걸어서 비켜났다)
    private const float AimedWarn       = 0.6f;    // 노림 — 착탄까지 0.6 + 낙하
    private const float AimedEvery      = 1.4f;    // ± 0.3
    private const float AimedEveryAbyss = 1.0f;    // 2페이지
    private const int   AimedChain      = 3;
    private const float AimedChainGap   = 0.35f;
    private const float AimedLead       = 0.35f;   // 첫 발 — 움직이는 방향으로 이만큼(초) 앞
    private const float BigWarn         = 1.3f;
    private const float BigAt           = 2f;      // 버스트 시작 뒤 이때 한 번
    // 크기 · 모양 · 피해 배율 · 이펙트 배율은 화룡 공용 규칙(DragonThreat — 화염구 비와 같다)

    private readonly MonsterContext              _ctx;
    private readonly DragonFireballRainPatternSO _so;
    private readonly float                       _cooldownMin;
    private readonly float                       _cooldownMax;
    private readonly float                       _initialDelay;
    private readonly float                       _burstDurationMin;
    private readonly float                       _burstDurationMax;

    private int _aimedCount;

    private static int s_groundLayerMask = -1;

    public DragonAirMeteorPassiveRunner(
        MonsterContext ctx,
        DragonFireballRainPatternSO so,
        float cooldownMin,
        float cooldownMax,
        float initialDelay,
        float burstDurationMin,
        float burstDurationMax)
    {
        _ctx              = ctx;
        _so               = so;
        _cooldownMin      = cooldownMin;
        _cooldownMax      = cooldownMax;
        _initialDelay     = initialDelay;
        _burstDurationMin = burstDurationMin;
        _burstDurationMax = burstDurationMax;
    }

    public void Start(CancellationToken ct) => RunLoop(ct).Forget();

    // ── 메인 루프 ─────────────────────────────────────────────

    private async UniTaskVoid RunLoop(CancellationToken ct)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(_initialDelay), cancellationToken: ct);
            while (true)
            {
                if (IsAliveAndShouldFire())
                {
                    try { await FireBurstAsync(ct); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { Debug.LogError($"[DragonAirMeteor] {e}"); }
                }

                float cd = UnityEngine.Random.Range(_cooldownMin, _cooldownMax);
                await UniTask.Delay(TimeSpan.FromSeconds(cd), cancellationToken: ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private bool IsAliveAndShouldFire()
    {
        if (_ctx?.Runtime == null || _ctx.Runtime.IsDead) return false;
        if (!((_ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard bb)) return false;
        if (bb.PassiveMeteorSuppressed || InPageTransition()) return false;

        var element = GetCurrentElement();
        // Ice: 지상 상태만 아니면 발동
        if (element == DragonBossBlackboard.DragonElement.Ice)
            return bb.BodyState != BodyState.Grounded;
        // Thunder, Fire, Abyss: 항상 발동
        return true;
    }

    private bool IsSuppressed()
        => ((_ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard bb && bb.PassiveMeteorSuppressed) || InPageTransition();

    /// <summary>2페이지 전환 컷신 동안은 쉰다 — 10-06 연출 점검: 체력바가 차오르는 전경 샷 내내 보라 칸 · 운석이 떨어졌다.</summary>
    private bool InPageTransition() => (_ctx.Monster as IPagedBoss)?.Pages?.Transitioning ?? false;

    /// <summary>드래곤이 정하는 지금 원소 — 2페이지는 Abyss(체력 비율로 불을 읽지 않는다).</summary>
    private DragonBossBlackboard.DragonElement GetCurrentElement()
        => (_ctx.Monster as DragonBossMonster)?.CurrentElement ?? DragonBossBlackboard.DragonElement.Ice;

    private float GetFloorY(float x, float z)
    {
        if (s_groundLayerMask < 0)
            s_groundLayerMask = 1 << LayerMask.NameToLayer("Ground");

        float originY = _ctx.Runtime.SpawnPosition.y + 50f;
        if (Physics.Raycast(new Vector3(x, originY, z), Vector3.down, out RaycastHit hit, 100f,
                s_groundLayerMask, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return _ctx.Runtime.SpawnPosition.y;
    }

    // ── 버스트 (5~10초) — 흩뿌림 · 노림 · 큰 메테오 ────────────────

    private async UniTask FireBurstAsync(CancellationToken ct)
    {
        float start    = Time.time;
        float burstEnd = start + UnityEngine.Random.Range(_burstDurationMin, _burstDurationMax);
        bool  abyss    = GetCurrentElement() == DragonBossBlackboard.DragonElement.Abyss;
        float aimedAt  = start + 0.8f;
        bool  bigDone  = false;
        int   scatter = 0, aimed = 0;

        while (Time.time < burstEnd)
        {
            if (_ctx?.Runtime == null || _ctx.Runtime.IsDead) break;   // 처치 뒤 운석이 계속 떨어지지 않게
            if (IsSuppressed()) break;                                  // 공허 낙하 · 검은 태양이 시작되면 버스트를 끊는다
            Launch(PickScatterCell(), DragonThreat.PickScatterRadius(out int tier), Kind.Scatter, tier, ct);
            scatter++;

            if (Time.time >= aimedAt)
            {
                aimedAt = Time.time + (abyss ? AimedEveryAbyss : AimedEvery) + UnityEngine.Random.Range(-0.3f, 0.3f);
                aimed += (_aimedCount++ & 1) == 1 ? AimedChain : 1;
                if ((_aimedCount & 1) == 0) AimedChainAsync(ct).Forget();
                else Launch(AimCell(AimedLead), DragonThreat.AimedRadius, Kind.Aimed, 0, ct);
            }
            if (!bigDone && Time.time - start >= BigAt)
            {
                bigDone = true;
                Launch(AimCell(0f), DragonThreat.BigRadius, Kind.Big, 0, ct);
            }
            await UniTask.Delay(TimeSpan.FromSeconds(ScatterGap), cancellationToken: ct);
        }
        Debug.Log($"[DragonAirMeteor] 버스트 {Time.time - start:0.0}초 — 흩뿌림 {scatter} · 노림 {aimed} · 큰 {(bigDone ? 1 : 0)}{(abyss ? " · 2페이지" : "")}");
    }

    // 노림 연타 — 세 발이 플레이어를 쫓는다(각 발은 그 순간 플레이어 자리)
    private async UniTaskVoid AimedChainAsync(CancellationToken ct)
    {
        try
        {
            for (int i = 0; i < AimedChain; i++)
            {
                if (_ctx?.Runtime == null || _ctx.Runtime.IsDead || IsSuppressed()) return;
                Launch(AimCell(i == 0 ? AimedLead : 0f), DragonThreat.AimedRadius, Kind.Aimed, 0, ct);
                await UniTask.Delay(TimeSpan.FromSeconds(AimedChainGap), cancellationToken: ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void Launch(Vector2Int cell, int radius, Kind kind, int tier, CancellationToken ct)
        => LaunchAsync(cell, radius, kind, tier, ct).Forget();

    private async UniTaskVoid LaunchAsync(Vector2Int cell, int radius, Kind kind, int tier, CancellationToken ct)
    {
        try { await FireMeteorAsync(cell, radius, kind, tier, ct); }
        catch (OperationCanceledException) { }
        catch (Exception e) { Debug.LogError($"[DragonAirMeteor] {e}"); }
    }

    // ── 자리 · 크기 고르기 ─────────────────────────────────────

    private Vector2Int PickScatterCell()
    {
        var t = _ctx?.Runtime?.PlayerTarget;
        if (t != null && UnityEngine.Random.value < ScatterNearShare)
        {
            Vector2 r = UnityEngine.Random.insideUnitCircle * NearRadius;
            return DragonThreat.ClampInterior(DragonBossRoomContext.WorldToCell(t.position + new Vector3(r.x, 0f, r.y)));
        }
        return new Vector2Int(UnityEngine.Random.Range(2, DragonBossRoomContext.Width - 2),
                              UnityEngine.Random.Range(2, DragonBossRoomContext.Height - 2));
    }

    private Vector2Int AimCell(float lead)
    {
        if (_ctx?.Runtime?.PlayerTarget == null) return PickScatterCell();
        return DragonThreat.ClampInterior(DragonBossRoomContext.WorldToCell(DragonThreat.PredictPlayer(_ctx, lead)));
    }

    // ── 메테오 1발 ────────────────────────────────────────────

    private async UniTask FireMeteorAsync(Vector2Int c, int radius, Kind kind, int tier, CancellationToken ct)
    {
        var cells = DragonThreat.Footprint(c, radius, kind == Kind.Big ? DragonThreat.EdgeDrop * 0.5f : DragonThreat.EdgeDrop);
        if (cells.Count == 0) return;

        Vector3 cellBase = DragonBossRoomContext.CellToWorld(c.x, c.y, 0f);
        float   floorY   = GetFloorY(cellBase.x, cellBase.z);
        Vector3 landPos  = new Vector3(cellBase.x, floorY + 0.05f, cellBase.z);
        float   warnDur  = kind == Kind.Aimed ? AimedWarn : kind == Kind.Big ? BigWarn : ScatterWarn;
        float   dmgMul   = kind == Kind.Aimed ? DragonThreat.AimedDamage : kind == Kind.Big ? DragonThreat.BigDamage : DragonThreat.ScatterDamage[tier];
        float   sizeMul  = DragonThreat.MeteorScale(radius);   // 이펙트 = 판정 크기

        // ── 경고 타일 생성 ──────────────────────────────────────
        var warnTiles = new List<GameObject>(cells.Count);
        var warnMats  = new List<Material>(cells.Count);
        var warnMrs   = new List<MeshRenderer>(cells.Count);
        // 예고 색 — 1페이지는 예전대로 불, 2페이지는 심연(검보라)
        Color fireBase = DragonBossVisualHelper.GetElementColor(
            GetCurrentElement() == DragonBossBlackboard.DragonElement.Abyss
                ? DragonBossBlackboard.DragonElement.Abyss
                : DragonBossBlackboard.DragonElement.Fire);

        foreach (var cell in cells)
        {
            Vector3 tileBase = DragonBossRoomContext.CellToWorld(cell.x, cell.y, 0f);
            float   tileY    = GetFloorY(tileBase.x, tileBase.z);
            var (go, mr, mat) = QuadTilePool.Rent();
            go.name = "PassiveMeteorWarn";
            go.transform.position   = new Vector3(tileBase.x, tileY + 0.1f, tileBase.z);
            go.transform.rotation   = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = Vector3.one;
            mat.color = new Color(fireBase.r, fireBase.g, fireBase.b, 0f);
            warnTiles.Add(go);
            warnMats.Add(mat);
            warnMrs.Add(mr);
        }

        // ── 경고 페이드인 ──────────────────────────────────────
        try
        {
            float elapsed = 0f;
            float alpha   = Mathf.Min(1f, _so.WarningColor.a * (kind == Kind.Scatter ? 1f : 1.4f));   // 노림 · 큰 메테오는 더 진하게
            while (elapsed < warnDur)
            {
                float t = Mathf.Clamp01(elapsed / warnDur);
                foreach (var mat in warnMats)
                {
                    if (mat == null) continue;
                    Color col = mat.color;
                    col.a = Mathf.Lerp(0f, alpha, t);
                    mat.color = col;
                }
                elapsed += Time.deltaTime;
                await UniTask.Yield(ct);
            }
        }
        catch
        {
            ReturnWarnTiles();   // 취소 · 예외 — 착탄 정리까지 못 가니 여기서 돌려준다
            throw;
        }

        // ── 낙하 VFX + 사운드 ─────────────────────────────────
        GameObject projectile = null;
        if (_so.FireballPrefab != null)
        {
            projectile = BossEffectPool.Spawn(_so.FireballPrefab, landPos, Quaternion.identity);
            if (projectile != null)
                projectile.transform.localScale = Vector3.one * _so.FireballScale * sizeMul;
        }
        Managers.Sound?.PlayEffectAt(_so.FireRainSfx, landPos);

        // ── 충돌 판정 → 이펙트 소멸 ────────────────────────────
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(_so.ImpactDelay), cancellationToken: ct);

            ApplyImpact(landPos, cells, sizeMul, dmgMul, kind);
            ReturnWarnTiles();   // 예고는 착탄 순간까지 남긴다(10-02 — 예전엔 낙하 0.6초 동안 바닥 예고가 비어 있었다)

            if (projectile != null)
                foreach (var ps in projectile.GetComponentsInChildren<ParticleSystem>(true))
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            float remaining = Mathf.Max(0f, _so.MeteorHitDuration - _so.ImpactDelay);
            if (remaining > 0f)
                await UniTask.Delay(TimeSpan.FromSeconds(remaining), cancellationToken: ct);
        }
        finally
        {
            ReturnWarnTiles();
            if (projectile != null) BossEffectPool.Release(projectile);
        }

        void ReturnWarnTiles()
        {
            for (int i = 0; i < warnTiles.Count; i++)
                QuadTilePool.Return(warnTiles[i], warnMrs[i], warnMats[i]);
            warnTiles.Clear();
            warnMats.Clear();
            warnMrs.Clear();
        }
    }

    // ── 충돌 처리 — 예고 칸에 선 플레이어만 · 폭발은 판정 크기만큼 ─────────

    private void ApplyImpact(Vector3 landPos, List<Vector2Int> cells, float sizeMul, float dmgMul, Kind kind)
    {
        if (_so.ExplosionPrefab != null)
        {
            var exp = BossEffectPool.SpawnOneShot(
                _so.ExplosionPrefab, landPos, Quaternion.identity, fallbackLifetime: 3f);
            if (exp != null)
                exp.transform.localScale = Vector3.one * _so.ExplosionScale * sizeMul;
        }
        if (kind == Kind.Big) BossImpactFeedback.TriggerCameraShake(0.08f, 0.25f);   // 살짝

        if (_ctx?.Runtime?.PlayerTarget == null || _ctx.Runtime.IsDead) return;   // 처치 뒤 떨어지는 운석은 이펙트만(10-08 점검)

        Vector2Int pc = DragonBossRoomContext.WorldToCell(_ctx.Runtime.PlayerTarget.position);
        if (!cells.Contains(pc) || !DragonThreat.TryMeteorHit()) return;
        _ctx.Runtime.PlayerTarget.GetComponent<PlayerController>()
            ?.TakeDamage(Mathf.RoundToInt(_ctx.Config.stat.attackPower * _so.DamageMultiplier * dmgMul), _ctx.Monster.gameObject,
                         false, HitWeight.Auto);   // 수동 운석 — 직격(293)이라 약이 아니다: 피격 연출 · 날아감은 피해량대로(10-01)
    }
}
}
