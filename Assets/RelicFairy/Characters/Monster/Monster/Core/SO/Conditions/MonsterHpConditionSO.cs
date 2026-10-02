using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// HP 비율 임계값 조건.
/// 현재 HP 비율이 threshold 이하이면 true.
/// </summary>
[CreateAssetMenu(fileName  = "MonsterCond_Hp",
                 menuName  = "RelicFairy/Monster/Condition/HpThreshold")]
public class MonsterHpConditionSO : MonsterConditionSO
{
    [Range(0f, 1f)]
    [Tooltip("HP 비율 임계값 (0~1). 이 값 이하로 떨어지면 참.")]
    public float threshold = 0.5f;

    public override bool Evaluate(MonsterContext ctx)
    {
        // 챕터 난이도 배율이 곱해진 유효 최대 HP 기준 — 기본 maxHp로 나누면 Ch2(×1.5)에서 「50%」가 실제 33%에 걸렸다.
        int maxHp = ctx.Monster.EffectiveMaxHp;
        if (maxHp <= 0) return false;
        float hpRatio = (float)ctx.Runtime.CurrentHp / maxHp;
        return hpRatio <= threshold;
    }
}
}
