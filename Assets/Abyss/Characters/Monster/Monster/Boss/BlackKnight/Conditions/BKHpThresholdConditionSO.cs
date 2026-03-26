using Abyss.Monster;
using UnityEngine;

/// <summary>
/// HP 비율 임계값 조건.
/// 현재 HP / MaxHP 가 hpThreshold 이하이면 참.
///
/// 단순 임계값 체크이므로 조건이 한 번 충족되면 계속 참이 된다.
/// "이 HP 구간에서 딱 한 번만 발동"이 필요하면 BKSpinPhaseConditionSO 를 사용할 것.
/// </summary>
[CreateAssetMenu(fileName = "BK_Cond_HpThreshold",
                 menuName  = "Abyss/Boss/BlackKnight/Conditions/HpThreshold")]
public class BKHpThresholdConditionSO : BossConditionSO
{
    [Range(0f, 1f)]
    [Tooltip("HP 비율 임계값. HP 가 이 값 이하일 때 조건 충족.")]
    public float hpThreshold = 0.5f;

    public override bool Evaluate(BossPatternContext ctx)
    {
        int max = ctx.Ctx.Config.stat.maxHp;
        if (max <= 0) return false;
        float ratio = (float)ctx.Ctx.Runtime.CurrentHp / max;
        return ratio <= hpThreshold;
    }
}
