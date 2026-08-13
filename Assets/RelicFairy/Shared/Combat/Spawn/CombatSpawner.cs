using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 플레이어 공격 생성의 <b>단일 통로</b>. 모든 투사체는 여기를 지나며, 그 사이에서
/// 수정자 계층이 요청을 고친다. 새 효과가 생겨도 스폰 코드는 건드리지 않는다.
///
/// 수정자 순서(스코프가 좁은 것부터):
///   ① 원본        — 호출자가 채운 값
///   ② 무기 파츠    — <b>그 슬롯으로 쏜 것에만</b>(sourceSlot 게이트)
///   ③ 아이템·룬    — 플레이어 공격 전역
///   ④ 방 버프      — 전역
///   ⑤ 클램프      — 상한·재귀 방지
///
/// 예전엔 부채꼴(15° 하드코딩)·추가탄(±8°, 80ms)이 각자 스폰 코드를 복제하고 있어
/// 투사체 증가 소스가 4갈래로 흩어져 있었다. 여기로 합친다.
/// </summary>
public static class CombatSpawner
{
    // ── 안전장치 ────────────────────────────────────────────
    /// <summary>파츠+아이템이 겹쳐도 투사체가 폭증하지 않게 하는 상한.</summary>
    private const int   MaxProjectiles = 20;
    /// <summary>재귀 스폰 최대 깊이.</summary>
    private const int   MaxDepth       = 2;
    /// <summary>갈래가 여럿일 때 기본 확산각(도). 요청이 0이면 이 값을 쓴다.</summary>
    private const float DefaultSpread  = 30f;
    /// <summary>갈래끼리 겹쳐 보이지 않게 옆으로 벌리는 거리 계수.</summary>
    private const float LateralSpread  = 0.3f;

    /// <summary>
    /// 요청을 수정자에 통과시킨 뒤 투사체를 만든다.
    /// </summary>
    /// <param name="req">발사 요청(수정자가 ref로 고친다).</param>
    /// <param name="primary">이미 스폰된 주 투사체(어빌리티 이펙트 경로). null이면 전부 풀에서 새로 뽑는다.</param>
    /// <param name="onSpawned">추가로 만들어진 투사체 게임오브젝트 통지(어빌리티 실행 등록용).</param>
    public static void SpawnProjectile(ref ProjectileRequest req,
                                       BasicArrow primary = null,
                                       System.Action<GameObject> onSpawned = null)
    {
        ApplyModifiers(ref req);

        // 주 투사체가 이미 있으면 그것부터 요청대로 세팅한다.
        if (primary != null) Configure(primary, in req, req.direction);

        int extra = req.count - 1;
        if (extra <= 0 || string.IsNullOrEmpty(req.prefabKey)) return;

        SpawnExtrasAsync(req, extra, primary != null, onSpawned).Forget();
    }

    /// <summary>투사체 1발에 요청값을 반영한다. 스폰 경로가 달라도 설정은 여기 한 곳.</summary>
    public static void Configure(BasicArrow arrow, in ProjectileRequest req, Vector3 dir)
    {
        if (arrow == null) return;

        arrow.Fire(dir, req.owner, req.damage * req.damageMult);

        if (req.pierce > 0)        arrow.SetPierce(req.pierce);
        if (req.explodeRadius > 0f) arrow.SetExplosion(req.explodeRadius, req.damage * req.damageMult * req.explodeDamageRatio);

        arrow.SetSpeedMultiplier(req.speedMult);
        arrow.SetHoming(req.homingStrength, req.returnOnPierce);
    }

    // ── 수정자 계층 ─────────────────────────────────────────

    private static void ApplyModifiers(ref ProjectileRequest req)
    {
        // ② 무기 파츠 — 그 슬롯으로 쏜 것에만 걸린다.
        RangedParts.Apply(ref req);

        // ③④ 아이템·룬·방 버프 — 흩어져 있던 투사체 증가 소스를 여기로 모은다.
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player != null && req.owner == player.gameObject)
        {
            var stats = player.RuntimeStats;
            if (stats != null)
            {
                req.count  += Mathf.Max(0, stats.BonusProjectile);        // 방 버프 + 아이템 추가 투사체
                req.pierce += Mathf.Max(0, stats.ProjectilePierceBonus);  // 아이템 관통
            }
            req.count += Mathf.Max(0, player.ExtraShotCount);             // 스킬 버프 추가탄
        }

        // ⑤ 클램프
        req.count      = Mathf.Clamp(req.count, 1, MaxProjectiles);
        req.damageMult = Mathf.Max(0f, req.damageMult);
        req.sizeMult   = Mathf.Max(0.05f, req.sizeMult);
        req.speedMult  = Mathf.Max(0.05f, req.speedMult);
        req.pierce     = Mathf.Max(0, req.pierce);
        if (req.count > 1 && req.spreadDeg <= 0f) req.spreadDeg = DefaultSpread;
    }

    // ── 스폰 ────────────────────────────────────────────────

    /// <summary>
    /// 갈래를 부채꼴로 펼쳐 추가 투사체를 만든다.
    /// 각도는 전체 확산각을 갈래 수로 균등 분할 — 예전 하드코딩(15°/±8°)을 대체한다.
    /// </summary>
    private static async UniTaskVoid SpawnExtrasAsync(ProjectileRequest req, int extra, bool hasPrimary,
                                                      System.Action<GameObject> onSpawned)
    {
        if (req.depth >= MaxDepth) return;

        Vector3 baseDir = req.direction.sqrMagnitude > 0.0001f ? req.direction.normalized : Vector3.forward;
        Vector3 right   = Vector3.Cross(Vector3.up, baseDir).normalized;

        // 주 투사체가 중앙(0도)을 차지하므로 나머지를 좌우 교대로 배치한다.
        for (int i = 1; i <= extra; i++)
        {
            float angle = FanAngle(i, req.count, req.spreadDeg, hasPrimary);
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * baseDir;
            Vector3 pos = req.origin + right * (Mathf.Sin(angle * Mathf.Deg2Rad) * LateralSpread);

            var go = await Managers.ObjectPooler.SpawnAsync(
                req.prefabKey, ObjectPoolerManager.PoolType.Effect, pos, Quaternion.LookRotation(dir));
            if (go == null) return;

            go.transform.localScale = Vector3.one * (req.baseScale * req.sizeMult);

            if (go.TryGetComponent<BasicArrow>(out var arrow))
                Configure(arrow, in req, dir);

            onSpawned?.Invoke(go);
        }
    }

    /// <summary>i번째 추가 갈래의 각도. 좌우 교대로 벌어진다.</summary>
    private static float FanAngle(int index, int total, float spreadDeg, bool hasPrimary)
    {
        if (total <= 1) return 0f;

        // 주 투사체가 중앙을 쓰면 나머지는 ±step, ±2step … 로 교대 배치.
        float step = spreadDeg / Mathf.Max(1, total - (hasPrimary ? 1 : 0));
        int   rank = (index + 1) / 2;
        return (index % 2 == 1 ? -1f : 1f) * step * rank;
    }
}
