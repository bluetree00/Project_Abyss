using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// GetHitState 팩토리 SO.
/// null 이면 기본 GetHitState 사용.
/// </summary>
[CreateAssetMenu(fileName = "GetHitState", menuName = "Abyss/Monster/States/GetHit")]
public class GetHitStateSO : ScriptableObject
{
    public virtual GetHitState Create(MonsterBase monster) => new GetHitState();
}
}
