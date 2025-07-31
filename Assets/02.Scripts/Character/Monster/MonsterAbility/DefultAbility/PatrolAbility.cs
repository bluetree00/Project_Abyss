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
        var agent = owner.agent;

        // 안정적인 도착 판정
        bool hasArrived =
            !agent.pathPending &&
            agent.remainingDistance <= stopDistance &&
            agent.velocity.sqrMagnitude < 0.01f;

        if (hasArrived)
        {
            currentIndex = (currentIndex + 1) % waypoints.Length;
            target = waypoints[currentIndex];
            Debug.Log($"[Patrol] Reached waypoint. Moving to next index {currentIndex}");
            owner.MoveTo(target);
        }
        else
        {
            owner.MoveTo(target);
        }
    }


}
