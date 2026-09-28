using Cysharp.Threading.Tasks;
using UnityEngine;
using RelicFairy.Monster;

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

    // ── 수직 보정(지상 수평 발사) ───────────────────────────
    /// <summary>보정 대상을 찾는 거리(m). 화살 사거리(속도 30 × 수명 5)보다 짧은 실제 교전 거리.</summary>
    private const float PitchSearchRange = 25f;
    /// <summary>수평 조준에서 이 각도(도) 안의 적만 본다 — 옆에 선 적의 높이로 엉뚱하게 꺾이지 않게.</summary>
    private const float PitchConeHalfDeg = 12f;
    /// <summary>보정 기울기 상한(도).</summary>
    private const float MaxPitchDeg      = 35f;
    /// <summary>방향의 y가 이보다 크면 이미 기울인 발사(공중 조준 등)로 보고 손대지 않는다.</summary>
    private const float FlatDirMaxY      = 0.05f;

    private static readonly Collider[] s_pitchOverlap = new Collider[32];

    /// <summary>수직 보정 판단을 발사마다 로그로 남긴다(진단용, 기본 꺼짐 — 에디터 메뉴로 켠다).</summary>
    public static bool LogPitch { get; set; }

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
        req.direction = PitchTowardTarget(req.origin, req.direction, req.owner);

        // 주 투사체가 이미 있으면 그것부터 요청대로 세팅한다.
        if (primary != null) Configure(primary, in req, req.direction);

        int extra = req.count - 1;
        if (extra <= 0 || string.IsNullOrEmpty(req.prefabKey)) return;

        SpawnExtrasAsync(req, extra, primary != null, onSpawned).Forget();
    }

    /// <summary>파츠 폭발이 쓸 기본 이펙트 — 스킬 폭발(BowETier3Hit)과 같은 아트를 작게 쓴다.</summary>
    // 09-25: 활 E 3단계 착탄(BowETier3Hit)을 빌려 쓰던 것을 전용으로 — 의미가 다른 순간에 같은 이펙트를 쓰지 않는다.
    private const string PartsExplodeEffectKey   = "RangedPartsExplosion";   // 화살비 폭발(SkillVfxSeparationEditor가 등록)
    private const float  PartsExplodeEffectScale = 0.6f;

    /// <summary>투사체 1발에 요청값을 반영한다. 스폰 경로가 달라도 설정은 여기 한 곳.</summary>
    public static void Configure(BasicArrow arrow, in ProjectileRequest req, Vector3 dir)
    {
        if (arrow == null) return;

        arrow.Fire(dir, req.owner, req.damage * req.damageMult);
        arrow.SetActionType(req.actionType);
        arrow.SetFinisher(req.isFinisher);

        if (req.pierce > 0)        arrow.SetPierce(req.pierce);
        // 파츠가 붙인 폭발에도 이펙트를 준다 — 예전엔 키를 안 넘겨 <b>피해만 들어가고 아무것도 안 보였다</b>(09-21).
        // 스킬 폭발과 같은 아트를 작게 쓴다(사용자 결정). 스킬은 자기 키로 SetExplosion을 다시 불러 덮어쓴다.
        if (req.explodeRadius > 0f)
            arrow.SetExplosion(req.explodeRadius, req.damage * req.damageMult * req.explodeDamageRatio,
                               PartsExplodeEffectKey, PartsExplodeEffectScale);

        arrow.SetSpeedMultiplier(req.speedMult);
        arrow.SetHoming(req.homingStrength, req.returnOnPierce);
    }

    // ── 수직 보정 ───────────────────────────────────────────

    /// <summary>
    /// 지상 수평 발사를 정면에 있는 적의 <b>피격 콜라이더 중심 높이</b>로 기울인다(좌우 조준은 그대로).
    /// 수평으로만 쏘면 발사 높이(플레이어 루트+1m)와 판정 반경(0.35m) 때문에 지면 기준 약 0.8m 아래를
    /// 못 맞혀, 키 낮은 몹(슬라임·거미·벌 등 Ch1 일반)의 머리 위로 화살이 지나갔다(09-18 실측).
    /// 정면 원뿔 안에 적이 없거나 이미 기울어진 발사(공중 조준)는 그대로 둔다.
    /// 추가 갈래는 이 방향을 위쪽 축으로 돌려 만들므로 같은 기울기를 물려받는다.
    /// </summary>
    private static Vector3 PitchTowardTarget(Vector3 origin, Vector3 dir, GameObject owner)
    {
        if (dir.sqrMagnitude < 0.0001f) return dir;
        dir.Normalize();
        if (Mathf.Abs(dir.y) > FlatDirMaxY)
        {
            if (LogPitch) Debug.Log($"[Pitch] 이미 기울어진 발사 — 보정 생략 · 발사점 {origin} · 방향 {dir}");
            return dir;
        }

        Vector3 flatDir = new Vector3(dir.x, 0f, dir.z).normalized;
        float   cosCone = Mathf.Cos(PitchConeHalfDeg * Mathf.Deg2Rad);

        // 화살이 실제로 맞아야 하는 콜라이더(피격 레이어)를 기준으로 삼는다.
        int n = Physics.OverlapSphereNonAlloc(origin, PitchSearchRange, s_pitchOverlap,
                                              MonsterBase.HitLayerMask, QueryTriggerInteraction.Collide);
        float   bestDist   = float.MaxValue;
        Vector3 bestCenter = default;
        string  bestName   = null;
        float   nearestAnyAngle = -1f;   // 진단: 원뿔 밖이라도 가장 가까운 적의 각도
        for (int i = 0; i < n; i++)
        {
            var col = s_pitchOverlap[i];
            if (col == null) continue;
            var mb = col.GetComponentInParent<MonsterBase>();
            if (mb == null || mb.CurrentHp <= 0 || (owner != null && mb.gameObject == owner)) continue;

            Vector3 center = col.bounds.center;
            Vector3 flat   = new Vector3(center.x - origin.x, 0f, center.z - origin.z);
            float   dist   = flat.magnitude;
            if (dist < 0.01f || dist >= bestDist) continue;
            if (LogPitch && nearestAnyAngle < 0f) nearestAnyAngle = Vector3.Angle(flat, flatDir);
            if (Vector3.Dot(flat / dist, flatDir) < cosCone) continue;   // 정면 원뿔 밖

            bestDist   = dist;   // 가장 먼저 닿을(가까운) 적
            bestCenter = center;
            if (LogPitch) bestName = $"{mb.name}/{col.name}";
        }
        if (bestDist == float.MaxValue)
        {
            if (LogPitch) Debug.Log($"[Pitch] 정면 {PitchConeHalfDeg}° 안 적 없음 — 수평 유지 · 발사점 {origin} · 방향 {flatDir} · 후보 콜라이더 {n}개 · 가장 가까운 적 각도 {nearestAnyAngle:F1}°");
            return dir;
        }

        float pitch = Mathf.Clamp(Mathf.Atan2(bestCenter.y - origin.y, bestDist) * Mathf.Rad2Deg,
                                  -MaxPitchDeg, MaxPitchDeg) * Mathf.Deg2Rad;
        if (LogPitch) Debug.Log($"[Pitch] {bestName} 거리 {bestDist:F2} · 중심높이 {bestCenter.y:F2} · 발사높이 {origin.y:F2} → {pitch * Mathf.Rad2Deg:F1}°");
        return (flatDir * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch)).normalized;
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
