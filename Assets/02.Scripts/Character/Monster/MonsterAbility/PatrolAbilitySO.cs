using UnityEngine;

[CreateAssetMenu(menuName = "Monster/Abilities/Patrol")]
public class PatrolAbilitySO : MonsterAbilitySO
{
    [SerializeField] private Vector3[] waypoints;

    public override Define.MonsterAbilityType MonsterAbilityType => Define.MonsterAbilityType.Patrol;

    public void SetWaypoints(Vector3[] points)
    {
        waypoints = points;
    }

    public override IMonsterAbility CreateAbilityInstance()
    {
        return new PatrolAbility(waypoints);
    }

    public Vector3[] GetWaypoints()
    {
        return waypoints;
    }
}
