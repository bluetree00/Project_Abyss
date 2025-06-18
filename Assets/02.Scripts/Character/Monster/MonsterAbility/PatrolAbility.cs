using UnityEngine;

public class PatrolAbility : IMonsterAbility
{
    private MonsterController owner;
    private Vector3[] waypoints;
    private int currentIndex = 0;
    private float stopDistance = 0.2f;

    public Define.MonsterAbilityType Type => Define.MonsterAbilityType.Patrol;

    public PatrolAbility(Vector3[] waypoints)
    {
        this.waypoints = waypoints;
    }

    public void Init(MonsterController owner)
    {
        this.owner = owner;
    }

    public void Execute()
    {
        if (waypoints == null || waypoints.Length == 0)
            return;

        Vector3 target = waypoints[currentIndex];

        if (Vector3.Distance(owner.transform.position, target) < stopDistance)
        {
            // 다음 웨이포인트로 갱신
            currentIndex = (currentIndex + 1) % waypoints.Length;
            target = waypoints[currentIndex]; // 바로 다음 타겟으로 이동하도록 갱신
        }

        owner.MoveTo(target);
    }

}
