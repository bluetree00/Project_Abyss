using UnityEngine;


namespace Abyss.Monster
{
/// <summary>
/// 배회 상태.
/// 스폰 위치 기준으로 PatrolSO에 설정된 방식(가로/세로/랜덤)으로 왕복 이동.
/// 플레이어가 감지 거리 안에 들어오면 Chase 상태로 전환.
/// </summary>
public class PatrolState : IMonsterState
{
    // ── 내부 상태 ─────────────────────────────────────────
    private Vector3 _waypointA;
    private Vector3 _waypointB;

    public void Enter(MonsterContext ctx)
    {
        // 배회 복귀 시 다음 조우에서 다시 즉시 공격
        ctx.Runtime.IsFirstAttack = true;

        float speed = ctx.Patrol.patrolSpeed > 0f
            ? ctx.Patrol.patrolSpeed
            : ctx.Stat.moveSpeed;

        ctx.Agent.speed = speed;

        // 배회 웨이포인트 계산 (스폰 위치 기준)
        CalcWaypoints(ctx);

        // 현재 방향에 맞는 목적지 설정
        MoveToNextWaypoint(ctx);
        PlayAnim(ctx, ctx.Animation.patrolStateName);
    }

    public void Update(MonsterContext ctx)
    {
        // 플레이어 감지 → 즉시 Chase
        if (ctx.Monster.ShouldStartChase(ctx))
        {
            ctx.Monster.ChangeState<ChaseState>();
            return;
        }

        // 웨이포인트 대기 중
        if (ctx.Runtime.IsWaitingAtWaypoint)
        {
            ctx.Runtime.PatrolWaitTimer -= Time.deltaTime;
            if (ctx.Runtime.PatrolWaitTimer <= 0f)
            {
                ctx.Runtime.IsWaitingAtWaypoint = false;
                ctx.Runtime.PatrolDirection    *= -1;
                MoveToNextWaypoint(ctx);
                PlayAnim(ctx, ctx.Animation.patrolStateName);
            }
            return;
        }

        // Blend 파라미터 갱신 (BlendTree 이동 애니메이션 구동)
        if (!string.IsNullOrEmpty(ctx.Animation.speedParam) && ctx.Animator != null)
            ctx.Animator.SetFloat(ctx.Animation.speedParam, ctx.Agent.velocity.magnitude);

        // 목적지 도착 판정
        if (ctx.Agent.isOnNavMesh &&
            !ctx.Agent.pathPending &&
            ctx.Agent.remainingDistance <= ctx.Agent.stoppingDistance + 0.25f)
        {
            ctx.Runtime.IsWaitingAtWaypoint = true;
            ctx.Runtime.PatrolWaitTimer     = ctx.Patrol.waypointWaitTime;
            PlayAnim(ctx, ctx.Animation.idleStateName);
        }
    }

    public void Exit(MonsterContext ctx)
    {
        ctx.Agent.ResetPath();
    }

    // ── 내부 헬퍼 ─────────────────────────────────────────

    private void CalcWaypoints(MonsterContext ctx)
    {
        float   r      = ctx.Patrol.patrolRange;
        Vector3 origin = ctx.Runtime.SpawnPosition;

        switch (ctx.Patrol.patrolType)
        {
            case PatrolType.Vertical:
                _waypointA = origin + Vector3.forward * r;
                _waypointB = origin - Vector3.forward * r;
                break;
            case PatrolType.Random:
                // 랜덤은 매번 새로 계산 (MoveToNextWaypoint에서 처리)
                _waypointA = origin + Random.insideUnitSphere.normalized * r;
                _waypointB = origin + Random.insideUnitSphere.normalized * r;
                _waypointA.y = origin.y;
                _waypointB.y = origin.y;
                break;
            default: // Horizontal
                _waypointA = origin + Vector3.right  * r;
                _waypointB = origin + Vector3.left   * r;
                break;
        }
    }

    private void MoveToNextWaypoint(MonsterContext ctx)
    {
        if (!ctx.Agent.isActiveAndEnabled || !ctx.Agent.isOnNavMesh) return;

        // Random 패턴은 목적지를 새로 뽑는다
        if (ctx.Patrol.patrolType == PatrolType.Random)
        {
            float   r      = ctx.Patrol.patrolRange;
            Vector3 origin = ctx.Runtime.SpawnPosition;
            Vector3 target = origin + Random.insideUnitSphere.normalized * r;
            target.y = origin.y;
            ctx.Agent.SetDestination(target);
            return;
        }

        Vector3 dest = ctx.Runtime.PatrolDirection > 0 ? _waypointA : _waypointB;
        ctx.Agent.SetDestination(dest);
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        ctx.Animator.CrossFade(stateName, ctx.Animation.crossFadeDuration);
    }
}
}
