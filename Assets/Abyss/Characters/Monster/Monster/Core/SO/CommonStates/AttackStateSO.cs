using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// AttackState 팩토리 SO.
/// null 이면 기본 AttackState 사용.
/// </summary>
[CreateAssetMenu(fileName = "AttackState", menuName = "Abyss/Monster/States/Attack")]
public class AttackStateSO : ScriptableObject
{
    public virtual AttackState Create(MonsterBase monster) => new AttackState();
}
}
