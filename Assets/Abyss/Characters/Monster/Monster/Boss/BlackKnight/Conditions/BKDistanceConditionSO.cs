using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 플레이어와의 거리 범위 조건.
/// minDistance <= 거리 <= maxDistance 이면 참.
/// </summary>
[CreateAssetMenu(fileName = "BK_Cond_Distance",
                 menuName  = "Abyss/Boss/BlackKnight/Conditions/Distance")]
public class BKDistanceConditionSO : BossConditionSO
{
    [Tooltip("최소 거리 (m). 이 값 이상이어야 조건 충족.")]
    public float minDistance = 0f;
    [Tooltip("최대 거리 (m). 이 값 이하이어야 조건 충족.")]
    public float maxDistance = 999f;

    public override bool Evaluate(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        float dist = Vector3.Distance(
            ctx.Ctx.Transform.position,
            ctx.Ctx.Runtime.PlayerTarget.position);
        return dist >= minDistance && dist <= maxDistance;
    }
}
