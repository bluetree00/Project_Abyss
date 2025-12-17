using UnityEngine;

[CreateAssetMenu(menuName = "Monster/Abilities/Patrol", fileName = "New_Patrol")]
public class PatrolAbilitySO : MonsterAbilitySO
{
    [SerializeField] private Vector3[] waypoints;

    public override Define.MonsterAbilityType MonsterAbilityType => Define.MonsterAbilityType.Patrol;

    public void SetWaypoints(Vector3[] points)
    {
        waypoints = points;
    }

    public override IMonsterAbility ReturnAbilityInstance()
    {
        return new PatrolAbility(waypoints);
    }

    public Vector3[] GetWaypoints()
    {
        return waypoints;
    }
}
