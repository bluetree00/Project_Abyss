using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// AttackReadyState 팩토리 SO.
/// null 이면 기본 AttackReadyState 사용.
/// </summary>
[CreateAssetMenu(fileName = "AttackReadyState", menuName = "Abyss/Monster/States/AttackReady")]
public class AttackReadyStateSO : ScriptableObject
{
    public virtual AttackReadyState Create(MonsterBase monster) => new AttackReadyState();
}
}
