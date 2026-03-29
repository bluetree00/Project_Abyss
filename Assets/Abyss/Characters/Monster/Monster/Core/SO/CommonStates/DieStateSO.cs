using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// DieState 팩토리 SO.
/// null 이면 기본 DieState 사용.
/// </summary>
[CreateAssetMenu(fileName = "DieState", menuName = "Abyss/Monster/States/Die")]
public class DieStateSO : ScriptableObject
{
    public virtual DieState Create(MonsterBase monster) => new DieState();
}
}
