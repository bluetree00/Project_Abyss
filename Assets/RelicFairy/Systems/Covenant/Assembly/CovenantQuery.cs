using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 서약 적 탐색 — 원인 추적기(<see cref="CovenantCauseTracker"/>)와 절(<see cref="CovenantClause"/>)이 함께 쓰는 훑기.
/// 몬스터 타격 레이어(MonsterHit)만 훑는다. 방은 1m 칸마다 바닥 콜라이더를 깔아 반경 5m에도 80개가 넘는데,
/// 레이어 없이 훑으면 버퍼가 바닥으로 차서 몬스터가 한 마리도 안 담긴다(에디터 실측 — CombatQuery와 같은 사고).
/// <para>⚠️ 버퍼는 정적이라 서약 인스턴스끼리 공유한다 — 피해를 넣기 <b>전에</b> 훑기를 끝내고 목록에 옮겨 담는다
/// (피해가 적을 죽여 다른 서약의 원인이 발동하면 순회 도중에 통째로 덮어써진다).</para>
/// </summary>
public static class CovenantQuery
{
    private const int ProbeSize = 128;
    private static readonly Collider[] s_probe = new Collider[ProbeSize];

    /// <summary>반경 내 살아있는 적을 <paramref name="into"/>에 모은다(<paramref name="except"/> 제외). 목록은 먼저 비운다.</summary>
    public static void CollectLiveEnemies(Vector3 center, float radius, MonsterBase except, List<MonsterBase> into)
    {
        into.Clear();
        int n = Physics.OverlapSphereNonAlloc(center, radius, s_probe, MonsterBase.HitLayerMask,
                                              QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            var col = s_probe[i];
            if (col == null) continue;
            if (!col.TryGetComponent<MonsterBase>(out var mb) || mb.IsDead || mb == except) continue;
            into.Add(mb);
        }
    }

    /// <summary>반경 내 가장 가까운 살아있는 적(없으면 null).</summary>
    public static GameObject NearestLiveEnemy(Vector3 center, float radius, MonsterBase except = null)
    {
        int n = Physics.OverlapSphereNonAlloc(center, radius, s_probe, MonsterBase.HitLayerMask,
                                              QueryTriggerInteraction.Collide);
        GameObject best = null; float bestSq = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var col = s_probe[i];
            if (col == null) continue;
            if (!col.TryGetComponent<MonsterBase>(out var mb) || mb.IsDead || mb == except) continue;
            float sq = (col.transform.position - center).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = col.gameObject; }
        }
        return best;
    }

    /// <summary>반경 내 살아있는 적의 수.</summary>
    public static int CountLiveEnemies(Vector3 center, float radius)
    {
        int n = Physics.OverlapSphereNonAlloc(center, radius, s_probe, MonsterBase.HitLayerMask,
                                              QueryTriggerInteraction.Collide);
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            var col = s_probe[i];
            if (col == null) continue;
            if (col.TryGetComponent<MonsterBase>(out var mb) && !mb.IsDead) count++;
        }
        return count;
    }

    /// <summary>살아있는 몬스터면 그 MonsterBase, 아니면 null.</summary>
    public static MonsterBase Live(GameObject go)
    {
        if (go == null) return null;
        var mb = go.GetComponentInParent<MonsterBase>();
        return mb != null && !mb.IsDead ? mb : null;
    }
}

/// <summary>
/// 절(<see cref="CovenantClause"/>)을 품은 서약 — 조립 서약 · 서약서(문장)가 구현한다.
/// 절은 <see cref="CovenantBase"/>가 아니라서 문맥 · 광역 피해 · 스탯 재적용을 주인에게 빌린다.
/// </summary>
public interface ICovenantClauseHost
{
    CovenantContext Context { get; }
    Vector3 PlayerPosition { get; }
    /// <summary>스탯 레이어 즉시 재적용(박차 중첩이 바뀔 때).</summary>
    void RequestStatRefresh();
    /// <summary>반경 내 적에게 공격력 배수 피해(<see cref="CovenantBase"/>의 광역 헬퍼).</summary>
    int DealAoe(Vector3 center, float radius, float multiplier);
}
