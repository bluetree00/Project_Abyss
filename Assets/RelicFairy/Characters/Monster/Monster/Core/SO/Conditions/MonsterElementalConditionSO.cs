using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 원소 누적치 임계값 조건.
/// 지정한 원소의 누적치가 threshold 이상이면 참.
///
/// MonsterHpConditionSO 와 동일한 파이프라인에서 동작한다:
///   TakeDamage → OnDamageTaken → SpecialStateEntry.conditions 평가
///
/// 현재 단계에서는 원소 효과 구현 없음.
/// SpecialStateEntry.state 에 효과 SO 가 연결되면 자동으로 동작한다.
/// </summary>
[CreateAssetMenu(fileName = "MonsterCond_Elemental",
                 menuName  = "Abyss/Monster/Condition/Elemental")]
public class MonsterElementalConditionSO : MonsterConditionSO
{
    [Tooltip("누적치를 체크할 원소 속성")]
    public ElementType element = ElementType.Lightning;

    [Tooltip("이 값 이상 누적되면 참. 0이면 항상 참 (디버그용).")]
    public float threshold = 100f;

    public override bool Evaluate(MonsterContext ctx)
    {
        if (!element.IsValid()) return false;
        return ctx.Runtime.ElementAccumulation[element.ToIndex()] >= threshold;
    }
}
