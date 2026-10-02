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
/// </summary>
public class DragonAirMeteorPassiveRunner
{
    private readonly MonsterContext              _ctx;
    private readonly DragonFireballRainPatternSO _so;
    private readonly float                       _cooldownMin;
    private readonly float                       _cooldownMax;
    private readonly float                       _initialDelay;
    private readonly float                       _burstDurationMin;
    private readonly float                       _burstDurationMax;

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
        if (bb.PassiveMeteorSuppressed) return false;

        var element = GetCurrentElement();
        // Ice: 지상 상태만 아니면 발동
        if (element == DragonBossBlackboard.DragonElement.Ice)
            return bb.BodyState != BodyState.Grounded;
        // Thunder, Fire, Abyss: 항상 발동
        return true;
    }

    private bool IsSuppressed()
        => (_ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard bb && bb.PassiveMeteorSuppressed;

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

    // ── 버스트 (5~10초간 연속 발사) ──────────────────────────

    private async UniTask FireBurstAsync(CancellationToken ct)
    {
        float burstEnd       = Time.time + UnityEngine.Random.Range(_burstDurationMin, _burstDurationMax);
        float launchInterval = Mathf.Max(0.1f, _so.SpawnInterval);

        while (Time.time < burstEnd)
        {
            if (_ctx?.Runtime == null || _ctx.Runtime.IsDead) break;   // 처치 뒤 운석이 계속 떨어지지 않게
            if (IsSuppressed()) break;                                  // 공허 낙하 · 검은 태양이 시작되면 버스트를 끊는다
            LaunchOneMeteor(ct);
            await UniTask.Delay(TimeSpan.FromSeconds(launchInterval), cancellationToken: ct);
        }
    }

    private async UniTaskVoid LaunchOneMeteor(CancellationToken ct)
    {
        try { await FireMeteorAsync(ct); }
        catch (OperationCanceledException) { }
        catch (Exception e) { Debug.LogError($"[DragonAirMeteor] {e}"); }
    }

    // ── 메테오 1발 ────────────────────────────────────────────

    private async UniTask FireMeteorAsync(CancellationToken ct)
    {
        int minX = 2, maxX = DragonBossRoomContext.Width  - 3;
        int minZ = 2, maxZ = DragonBossRoomContext.Height - 3;
        if (maxX < minX || maxZ < minZ) return;

        int cx = UnityEngine.Random.Range(minX, maxX + 1);
        int cz = UnityEngine.Random.Range(minZ, maxZ + 1);

        Vector3 cellBase = DragonBossRoomContext.CellToWorld(cx, cz, 0f);
        float   floorY   = GetFloorY(cellBase.x, cellBase.z);
        Vector3 landPos  = new Vector3(cellBase.x, floorY + 0.05f, cellBase.z);

        int halfR = UnityEngine.Random.Range(1, 3); // 1=3×3, 2=5×5

        // ── 경고 타일 생성 ──────────────────────────────────────
        var warnTiles = new List<GameObject>();
        var warnMats  = new List<Material>();
        var warnMrs   = new List<MeshRenderer>();
        // 예고 색 — 1페이지는 예전대로 불, 2페이지는 심연(검보라)
        Color fireBase = DragonBossVisualHelper.GetElementColor(
            GetCurrentElement() == DragonBossBlackboard.DragonElement.Abyss
                ? DragonBossBlackboard.DragonElement.Abyss
                : DragonBossBlackboard.DragonElement.Fire);

        for (int dx = -halfR; dx <= halfR; dx++)
        for (int dz = -halfR; dz <= halfR; dz++)
        {
            int tx = cx + dx, tz = cz + dz;
            if (!DragonBossRoomContext.IsInterior(tx, tz)) continue;

            Vector3 tileBase = DragonBossRoomContext.CellToWorld(tx, tz, 0f);
            float   tileY    = GetFloorY(tileBase.x, tileBase.z);
            Vector3 tilePos  = new Vector3(tileBase.x, tileY + 0.1f, tileBase.z);
            var (go, mr, mat) = QuadTilePool.Rent();
            go.name = "PassiveMeteorWarn";
            go.transform.position   = tilePos;
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
            float warnDur = Mathf.Max(0.01f, _so.WarningDuration);
            while (elapsed < warnDur)
            {
                float t = Mathf.Clamp01(elapsed / warnDur);
                foreach (var mat in warnMats)
                {
                    if (mat == null) continue;
                    Color c = mat.color;
                    c.a = Mathf.Lerp(0f, _so.WarningColor.a, t);
                    mat.color = c;
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
                projectile.transform.localScale = Vector3.one * _so.FireballScale;
        }
        Managers.Sound?.PlayEffectAt(_so.FireRainSfx, landPos);

        // ── 충돌 판정 → 이펙트 소멸 ────────────────────────────
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(_so.ImpactDelay), cancellationToken: ct);

            ApplyImpact(landPos, halfR);
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

    // ── 충돌 처리 ──────────────────────────────────────────────

    private void ApplyImpact(Vector3 landPos, int halfR)
    {
        if (_so.ExplosionPrefab != null)
        {
            var exp = BossEffectPool.SpawnOneShot(
                _so.ExplosionPrefab, landPos, Quaternion.identity, fallbackLifetime: 3f);
            if (exp != null)
                exp.transform.localScale = Vector3.one * _so.ExplosionScale;
        }

        if (_ctx?.Runtime?.PlayerTarget == null) return;

        Vector3 playerPos  = _ctx.Runtime.PlayerTarget.position;
        float   halfExtent = (halfR + 0.5f) * DragonBossRoomContext.CellSize;
        Vector3 d          = playerPos - landPos;
        if (Mathf.Abs(d.x) <= halfExtent && Mathf.Abs(d.z) <= halfExtent)
            _ctx.Runtime.PlayerTarget.GetComponent<PlayerController>()
                ?.TakeDamage(Mathf.RoundToInt(_ctx.Config.stat.attackPower * _so.DamageMultiplier), _ctx.Monster.gameObject,
                             false, HitWeight.Auto);   // 수동 운석 — 직격(293)이라 약이 아니다: 피격 연출 · 날아감은 피해량대로(10-01)
    }
}
}
