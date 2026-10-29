using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 룬 시너지 광역/체인 타겟 질의(AQ) + 시너지 즉발 피해(DM) 공용 헬퍼.
///
/// • 타겟 질의: PlayerController 에임어시스트(OverlapSphere + IDamageable 필터)와 동일 패턴.
///   핫패스 alloc 방지를 위해 OverlapSphereNonAlloc + 재사용 버퍼 사용.
/// • 즉발 피해: MonsterBase면 방어 경감을 우회하는 TakeSynergyDamage, 그 외 IDamageable은 일반 TakeDamage.
/// </summary>
public static class CombatQuery
{
    // ⚠️ 이 버퍼가 작으면 "이펙트는 나가는데 데미지가 0"이 된다.
    //
    // OverlapSphereNonAlloc은 레이어 `~0`(전부) + 트리거 포함으로 훑는데, 반경 안에는 몬스터 말고도
    // 바닥·벽·기둥·소품·VFX 트리거·픽업·배리어·존 트리거·플레이어·몬스터 다중 콜라이더가 잔뜩 있다.
    // 버퍼가 차면 물리엔진은 거기서 끊고, 그 순서는 <b>거리순이 아니라 내부 임의 순서</b>다.
    // → 환경 콜라이더가 버퍼를 다 차지하면 몬스터가 한 마리도 안 잡힌다.
    //   (아래 거리 정렬은 '필터링이 끝난 뒤'라 이미 잘린 것을 되살리지 못한다.)
    //
    // 몬스터 콜라이더는 Default 레이어라(가시성 레이어는 렌더러에만 적용) 레이어 필터로 줄일 수 없다.
    // 따라서 버퍼를 넉넉히 잡는 것이 현실적인 해법.
    private const int MaxOverlap = 256;
    private static readonly Collider[] s_overlap = new Collider[MaxOverlap];

#if UNITY_EDITOR
    /// <summary>실측 99 L — 추가타(시너지 즉발)가 들어갈 때마다(대상 · 양 · 속성). 에디터에서만.</summary>
    public static System.Action<GameObject, float, RuneElement?> ProbeSynergyHit;
#endif

    /// <summary>
    /// center 반경 내 살아있는 몬스터를 가까운 순으로 최대 max마리 buffer에 채운다(exclude 제외).
    /// 반환 = 채운 개수. buffer는 호출자가 재사용(매 호출 Clear).
    /// </summary>
    /// <summary>직선 추가 피해의 몸 여유(m) — 중심점이 선에서 「반폭 + 이 값」 안이면 맞는다. 작은 적 0.4 · 큰 적은 몸 반경(10-16).</summary>
    public static float BodyReach(MonsterBase e) => e != null ? Mathf.Max(0.4f, e.BodyRadius) : 0.4f;

    /// <summary>
    /// 원뿔 안인가 — 중심 각이 반각 안이거나, 몸이 원뿔 가장자리에 걸치면(몸 반경만큼 각 여유 · 10-16 「이펙트는 닿았는데 판정이 안 된다」).
    /// 원점이 몸 안이면(겹침) 통과.
    /// </summary>
    private static bool InConeBody(Vector3 forward, Vector3 to, float cosHalf, float halfAngleDeg, float bodyRadius)
    {
        float d = to.magnitude;
        if (d < 0.0001f || d <= bodyRadius) return true;
        if (Vector3.Dot(forward, to / d) >= cosHalf) return true;
        if (bodyRadius <= 0f) return false;
        return Vector3.Angle(forward, to) <= halfAngleDeg + Mathf.Asin(Mathf.Clamp01(bodyRadius / d)) * Mathf.Rad2Deg;
    }

    public static int GetNearbyEnemies(Vector3 center, float radius, GameObject exclude,
                                       int max, List<MonsterBase> buffer)
    {
        if (buffer == null) return 0;
        buffer.Clear();
        if (radius <= 0f || max <= 0) return 0;

        int n = Physics.OverlapSphereNonAlloc(center, radius, s_overlap, MonsterBase.HitLayerMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            var col = s_overlap[i];
            if (col == null) continue;

            var mb = col.GetComponentInParent<MonsterBase>();
            if (mb == null || mb.CurrentHp <= 0) continue;
            if (exclude != null && mb.gameObject == exclude) continue;
            if (buffer.Contains(mb)) continue;   // 다중 콜라이더 1몹 중복 방지

            buffer.Add(mb);
        }
        AddRelays(center, radius, exclude, buffer, null);

        // 가까운 순 정렬 후 max로 절단
        buffer.Sort((a, b) =>
        {
            float da = (a.transform.position - center).sqrMagnitude;
            float db = (b.transform.position - center).sqrMagnitude;
            return da.CompareTo(db);
        });
        if (buffer.Count > max) buffer.RemoveRange(max, buffer.Count - max);
        return buffer.Count;
    }

    /// <summary>
    /// origin에서 forward 방향 원뿔(halfAngleDeg) 내 살아있는 몬스터를 buffer에 채운다(가까운 순, 최대 max).
    /// 빛 광폭발/원뿔 등에 사용. 반환 = 채운 개수.
    /// </summary>
    public static int GetEnemiesInCone(Vector3 origin, Vector3 forward, float range, float halfAngleDeg,
                                       int max, List<MonsterBase> buffer, bool showGuide = true)
    {
        if (buffer == null) return 0;
        buffer.Clear();
        if (range <= 0f || max <= 0) return 0;

        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) return 0;
        forward.Normalize();
        float cosHalf = Mathf.Cos(halfAngleDeg * Mathf.Deg2Rad);

        // [가이드라인 비주얼] 원뿔 광역 질의 표시(개발용). 자체 VFX가 범위를 이미 보여주는
        // 스킬은 showGuide:false로 끈다 — 겹쳐 그리면 화면만 지저분해진다.
        if (showGuide) GuidelineVisual.Cone(origin, forward, range, halfAngleDeg);

        int n = Physics.OverlapSphereNonAlloc(origin, range, s_overlap, MonsterBase.HitLayerMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            var col = s_overlap[i];
            if (col == null) continue;

            var mb = col.GetComponentInParent<MonsterBase>();
            if (mb == null || mb.CurrentHp <= 0 || buffer.Contains(mb)) continue;

            Vector3 to = mb.transform.position - origin;
            to.y = 0f;
            if (InConeBody(forward, to, cosHalf, halfAngleDeg, mb.BodyRadius)) buffer.Add(mb);
        }
        AddRelays(origin, range, null, buffer, null, true, forward, cosHalf, halfAngleDeg);

        buffer.Sort((a, b) =>
        {
            float da = (a.transform.position - origin).sqrMagnitude;
            float db = (b.transform.position - origin).sqrMagnitude;
            return da.CompareTo(db);
        });
        if (buffer.Count > max) buffer.RemoveRange(max, buffer.Count - max);
        return buffer.Count;
    }

    /// <summary>
    /// 반경 내 <b>피해를 받을 수 있는 모든 것</b>(IDamageable)을 가까운 순으로 채운다.
    ///
    /// GetNearbyEnemies는 MonsterBase만 돌려주므로 <b>훈련용 허수아비가 광역기에 전혀 안 맞는다</b>.
    /// 더미로 스킬 피해를 검증하려면 몬스터로 좁히지 않은 이 경로가 필요하다.
    /// 몬스터 전용 처리(낙인·시너지 피해 등)는 호출부에서 MonsterBase 여부를 다시 확인하면 된다.
    /// </summary>
    public static int GetNearbyDamageables(Vector3 center, float radius, GameObject exclude,
                                           int max, List<GameObject> buffer)
    {
        if (buffer == null) return 0;
        buffer.Clear();
        if (radius <= 0f || max <= 0) return 0;

        int n = Physics.OverlapSphereNonAlloc(center, radius, s_overlap, MonsterBase.HitLayerMask, QueryTriggerInteraction.Collide);
        CollectDamageables(n, exclude, buffer);
        AddRelays(center, radius, exclude, null, buffer);

        SortByDistance(buffer, center);
        if (buffer.Count > max) buffer.RemoveRange(max, buffer.Count - max);
        return buffer.Count;
    }

    /// <summary>원뿔(halfAngleDeg) 내 IDamageable을 가까운 순으로 채운다. <see cref="GetNearbyDamageables"/>의 원뿔판.</summary>
    public static int GetDamageablesInCone(Vector3 origin, Vector3 forward, float range, float halfAngleDeg,
                                           GameObject exclude, int max, List<GameObject> buffer, bool showGuide = true)
    {
        if (buffer == null) return 0;
        buffer.Clear();
        if (range <= 0f || max <= 0) return 0;

        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) return 0;
        forward.Normalize();
        float cosHalf = Mathf.Cos(halfAngleDeg * Mathf.Deg2Rad);

        if (showGuide) GuidelineVisual.Cone(origin, forward, range, halfAngleDeg);

        int n = Physics.OverlapSphereNonAlloc(origin, range, s_overlap, MonsterBase.HitLayerMask, QueryTriggerInteraction.Collide);
        CollectDamageables(n, exclude, buffer);

        // 원뿔 밖 제거(수집 후 각도 필터 — 중복 제거가 끝난 뒤라야 대상당 1회만 판정한다)
        for (int i = buffer.Count - 1; i >= 0; i--)
        {
            Vector3 to = buffer[i].transform.position - origin;
            to.y = 0f;
            var body = buffer[i].GetComponentInParent<MonsterBase>();
            if (!InConeBody(forward, to, cosHalf, halfAngleDeg, body != null ? body.BodyRadius : 0f)) buffer.RemoveAt(i);
        }
        AddRelays(origin, range, exclude, null, buffer, true, forward, cosHalf, halfAngleDeg);

        SortByDistance(buffer, origin);
        if (buffer.Count > max) buffer.RemoveRange(max, buffer.Count - max);
        return buffer.Count;
    }

    /// <summary>
    /// 중계 물체(공명 성흔석 · 10-22 A1b) — 몬스터 층 겹침에 안 잡히는 중계 몸이 반경(원뿔이면 원뿔도) 안이면 넘겨받는 대상을 더한다.
    /// monsters가 있으면 MonsterBase로, 없으면 objects에 대상 GameObject로(×0.8 · 중계 두 번이 안 걸리게 성흔석이 아니라 대상). 식음은 안 따진다.
    /// </summary>
    private static void AddRelays(Vector3 center, float radius, GameObject exclude, List<MonsterBase> monsters, List<GameObject> objects,
                                  bool cone = false, Vector3 forward = default, float cosHalf = 0f, float halfAngleDeg = 0f)
    {
        var all = DamageRelays.All;
        for (int i = 0; i < all.Count; i++)
        {
            var relay = all[i];
            var to    = relay?.RelayTarget;
            var body  = relay?.RelayBody;
            if (to == null || body == null || !body.enabled || !body.gameObject.activeInHierarchy) continue;
            if (exclude != null && to == exclude) continue;
            if ((body.ClosestPoint(center) - center).sqrMagnitude > radius * radius) continue;
            if (cone)
            {
                Vector3 d = body.transform.position - center;
                d.y = 0f;
                Vector3 ext = body.bounds.extents;
                if (!InConeBody(forward, d, cosHalf, halfAngleDeg, Mathf.Max(ext.x, ext.z))) continue;
            }
            if (monsters != null)
            {
                var mb = to.GetComponentInParent<MonsterBase>();
                if (mb != null && mb.CurrentHp > 0 && !monsters.Contains(mb)) monsters.Add(mb);
            }
            else if (objects != null && !objects.Contains(to)) objects.Add(to);
        }
    }

    /// <summary>s_overlap[0..n) 에서 살아있는 IDamageable의 GameObject를 중복 없이 모은다.</summary>
    private static void CollectDamageables(int n, GameObject exclude, List<GameObject> buffer)
    {
        for (int i = 0; i < n; i++)
        {
            var col = s_overlap[i];
            if (col == null) continue;

            var dmg = col.GetComponentInParent<IDamageable>();
            if (dmg is not Component comp) continue;          // 인터페이스만으론 GameObject를 못 얻는다

            var go = comp.gameObject;
            if (exclude != null && go == exclude) continue;
            if (dmg is MonsterBase mb && mb.CurrentHp <= 0) continue;   // 시체는 제외(더미는 죽지 않는다)
            if (buffer.Contains(go)) continue;                // 다중 콜라이더 1대상 중복 방지

            buffer.Add(go);
        }
    }

    private static void SortByDistance(List<GameObject> buffer, Vector3 from)
    {
        buffer.Sort((a, b) =>
        {
            float da = (a.transform.position - from).sqrMagnitude;
            float db = (b.transform.position - from).sqrMagnitude;
            return da.CompareTo(db);
        });
    }

    /// <summary>
    /// 시너지 즉발 피해. MonsterBase면 방어 경감을 defenseIgnore(0~1)만큼 우회한다(기본=완전 무시).
    /// 그 외 IDamageable은 일반 TakeDamage(넉백 0).
    /// </summary>
    public static void DealSynergyDamage(GameObject target, float amount, GameObject instigator,
                                         float defenseIgnore = 1f, bool isCrit = false,
                                         RuneElement? element = null)
    {
        if (target == null || amount <= 0f) return;

        // 중계 물체(공명 성흔석) — 추가타는 넘겨받는 대상(기사)으로 바로, 과열은 세지 않는다(10-22 A1b)
        if (target.TryGetComponent<IDamageRelay>(out var relay))
        {
            target = relay.RelayTarget;
            if (target == null) return;
            amount *= relay.RelayRatio;
        }
#if UNITY_EDITOR
        ProbeSynergyHit?.Invoke(target, amount, element);
#endif

        var mb = target.GetComponentInParent<MonsterBase>();
        if (mb != null)
        {
            mb.TakeSynergyDamage(amount, instigator, defenseIgnore, isCrit, DamageKind.Synergy, element);
            return;
        }

        if (target.TryGetComponent<IDamageable>(out var dmg))
            dmg.TakeDamage(amount, instigator, 0f, isCrit);
    }

    /// <summary>몬스터 직접 대상 즉발 피해(체인 등 이미 MonsterBase를 가진 경우). element=데미지 텍스트 속성색.</summary>
    public static void DealSynergyDamage(MonsterBase target, float amount, GameObject instigator,
                                         float defenseIgnore = 1f, bool isCrit = false,
                                         RuneElement? element = null)
    {
        if (target == null || amount <= 0f) return;
#if UNITY_EDITOR
        ProbeSynergyHit?.Invoke(target.gameObject, amount, element);
#endif
        target.TakeSynergyDamage(amount, instigator, defenseIgnore, isCrit, DamageKind.Synergy, element);
    }
}
