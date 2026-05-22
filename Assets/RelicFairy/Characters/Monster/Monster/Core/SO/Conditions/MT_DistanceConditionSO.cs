using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 플레이어와의 거리 조건.
/// minDistance 이상 maxDistance 이하일 때 참.
/// 0 = 해당 방향 제한 없음.
/// </summary>
[CreateAssetMenu(fileName = "MonsterCond_Distance", menuName = "RelicFairy/Monster/Condition/Distance")]
public class MT_DistanceConditionSO : MonsterConditionSO
{
    [Tooltip("최소 거리 (이상). 0 = 제한 없음")]
    public float minDistance = 0f;

    [Tooltip("최대 거리 (이하). 0 = 제한 없음")]
    public float maxDistance = 0f;

    public override bool Evaluate(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return false;

        float dist = ctx.Runtime.DistToPlayer;

        if (minDistance > 0f && dist < minDistance) return false;
        if (maxDistance > 0f && dist > maxDistance) return false;

        return true;
    }
}
}
