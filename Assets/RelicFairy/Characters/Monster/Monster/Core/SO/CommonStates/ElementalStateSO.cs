using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 원소 누적치 임계값 도달 시 발동하는 상태 팩토리 SO.
/// null 이면 기본 DefaultElementalState 사용.
/// 파생 클래스에서 몬스터별 커스텀 원소 반응 상태를 반환한다.
///
/// 패턴: PatrolStateSO 와 동일.
///   null               → DefaultElementalState (공통 원소 효과, 추후 구현)
///   GolemFireImmuneStateSO → 골렘 전용 불 내성 상태 등
/// </summary>
public abstract class ElementalStateSO : ScriptableObject
{
    public abstract SpecialStateBase Create(MonsterBase monster);
}
}
