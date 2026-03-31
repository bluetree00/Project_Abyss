using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// ChaseState 팩토리 SO.
///
/// MonsterConfigSO.chaseState 슬롯에 설정하지 않으면 기본 ChaseState 가 사용된다.
/// 파생 클래스에서 Create() 를 오버라이드해 커스텀 ChaseState 를 반환한다.
///
/// 진정한 모듈화 구조:
///   SO 계층  : ChaseStateSO → BKChaseStateSO
///   State 계층: ChaseState  → BKOrbitalChaseState (BKChaseStateSO 내부 클래스)
/// </summary>
[CreateAssetMenu(fileName = "ChaseState", menuName = "Abyss/Monster/States/Chase")]
public class ChaseStateSO : ScriptableObject
{
    /// <summary>
    /// ChaseState 인스턴스를 생성한다.
    /// monster: 풀 재사용 콜백 등 MonsterBase 기능이 필요한 파생 클래스에서 사용.
    /// </summary>
    public virtual ChaseState Create(MonsterBase monster) => new ChaseState();
}
}
