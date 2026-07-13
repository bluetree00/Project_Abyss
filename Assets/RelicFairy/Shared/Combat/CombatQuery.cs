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

    /// <summary>
    /// center 반경 내 살아있는 몬스터를 가까운 순으로 최대 max마리 buffer에 채운다(exclude 제외).
    /// 반환 = 채운 개수. buffer는 호출자가 재사용(매 호출 Clear).
    /// </summary>
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
                                       int max, List<MonsterBase> buffer)
    {
        if (buffer == null) return 0;
        buffer.Clear();
        if (range <= 0f || max <= 0) return 0;

        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) return 0;
        forward.Normalize();
        float cosHalf = Mathf.Cos(halfAngleDeg * Mathf.Deg2Rad);

        // [가이드라인 비주얼] 원뿔 광역 질의 표시(통지만)
        GuidelineVisual.Cone(origin, forward, range, halfAngleDeg);

        int n = Physics.OverlapSphereNonAlloc(origin, range, s_overlap, MonsterBase.HitLayerMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            var col = s_overlap[i];
            if (col == null) continue;

            var mb = col.GetComponentInParent<MonsterBase>();
            if (mb == null || mb.CurrentHp <= 0 || buffer.Contains(mb)) continue;

            Vector3 to = mb.transform.position - origin;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) { buffer.Add(mb); continue; }
            if (Vector3.Dot(forward, to.normalized) >= cosHalf) buffer.Add(mb);
        }

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
    /// 시너지 즉발 피해. MonsterBase면 방어 경감을 defenseIgnore(0~1)만큼 우회한다(기본=완전 무시).
    /// 그 외 IDamageable은 일반 TakeDamage(넉백 0).
    /// </summary>
    public static void DealSynergyDamage(GameObject target, float amount, GameObject instigator,
                                         float defenseIgnore = 1f, bool isCrit = false)
    {
        if (target == null || amount <= 0f) return;

        var mb = target.GetComponentInParent<MonsterBase>();
        if (mb != null)
        {
            mb.TakeSynergyDamage(amount, instigator, defenseIgnore, isCrit);
            return;
        }

        if (target.TryGetComponent<IDamageable>(out var dmg))
            dmg.TakeDamage(amount, instigator, 0f, isCrit);
    }

    /// <summary>몬스터 직접 대상 즉발 피해(체인 등 이미 MonsterBase를 가진 경우).</summary>
    public static void DealSynergyDamage(MonsterBase target, float amount, GameObject instigator,
                                         float defenseIgnore = 1f, bool isCrit = false)
    {
        if (target == null || amount <= 0f) return;
        target.TakeSynergyDamage(amount, instigator, defenseIgnore, isCrit);
    }
}
