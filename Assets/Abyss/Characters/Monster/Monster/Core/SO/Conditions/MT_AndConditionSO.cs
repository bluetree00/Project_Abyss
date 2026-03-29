using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 조건 AND 조합기.
/// 모든 하위 조건이 참일 때만 참.
/// </summary>
[CreateAssetMenu(fileName = "MonsterCond_And", menuName = "Abyss/Monster/Condition/And")]
public class MT_AndConditionSO : MonsterConditionSO
{
    public MonsterConditionSO[] conditions;

    public override bool Evaluate(MonsterContext ctx)
    {
        if (conditions == null) return true;
        foreach (var c in conditions)
        {
            if (c != null && !c.Evaluate(ctx)) return false;
        }
        return true;
    }
}
}
