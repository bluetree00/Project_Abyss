using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// HP 비율 임계값 조건.
/// 현재 HP 비율이 threshold 이하이면 true.
/// </summary>
[CreateAssetMenu(fileName  = "MonsterCond_Hp",
                 menuName  = "Abyss/Monster/Condition/HpThreshold")]
public class MonsterHpConditionSO : MonsterConditionSO
{
    [Range(0f, 1f)]
    [Tooltip("HP 비율 임계값 (0~1). 이 값 이하로 떨어지면 참.")]
    public float threshold = 0.5f;

    public override bool Evaluate(MonsterContext ctx)
    {
        if (ctx.Stat.maxHp <= 0) return false;
        float hpRatio = (float)ctx.Runtime.CurrentHp / ctx.Stat.maxHp;
        return hpRatio <= threshold;
    }
}
}
