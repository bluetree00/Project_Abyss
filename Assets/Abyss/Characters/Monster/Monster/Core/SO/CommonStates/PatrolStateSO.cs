using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// PatrolState 팩토리 SO.
/// null 이면 기본 PatrolState 사용.
/// 파생 클래스에서 커스텀 PatrolState 를 반환할 수 있다.
///
/// 예) SlimeRegenPatrolSO : PatrolStateSO
///       내부에 RegenPatrolState : PatrolState 를 보유.
/// </summary>
[CreateAssetMenu(fileName = "PatrolState", menuName = "Abyss/Monster/States/Patrol")]
public class PatrolStateSO : ScriptableObject
{
    public virtual PatrolState Create(MonsterBase monster) => new PatrolState();
}
}
