using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 몬스터 특수 상태 전환 조건 ScriptableObject 추상 베이스.
///
/// SpecialStateEntry.conditions 리스트에 파생 SO 를 드래그해 넣으면
/// Inspector 에서 해당 SO 의 파라미터를 인라인으로 바로 편집할 수 있다.
/// (MonsterConditionSODrawer 가 자동으로 SO 필드를 인라인 표시)
///
/// conditions 가 비어있으면 OnDamageTaken 에서 자동 평가하지 않는다 (코드에서 직접 발동).
///
/// 제공 구현체:
///   MonsterHpConditionSO — HP 비율 임계값
/// </summary>
public abstract class MonsterConditionSO : ScriptableObject
{
    /// <summary>
    /// 조건 평가. true 면 특수 상태 발동 가능.
    /// ctx 에서 HP·거리·타이머 등 필요한 데이터를 자유롭게 참조한다.
    /// </summary>
    public abstract bool Evaluate(MonsterContext ctx);
}
}
