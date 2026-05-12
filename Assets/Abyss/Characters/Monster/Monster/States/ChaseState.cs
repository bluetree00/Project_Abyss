using UnityEngine;


namespace Abyss.Monster
{
/// <summary>
/// 추격 상태.
/// 플레이어를 향해 NavMesh로 이동.
/// - 공격 사정거리 이내 → AttackReady
/// - 추격 포기 거리 초과 또는 플레이어 사망 → Patrol
/// </summary>
public class ChaseState : IMonsterState
{
    // SetDestination은 플레이어가 이 거리 이상 이동했을 때만 재호출 (매 프레임 경로 재계산 방지)
    private const float DestinationUpdateThresholdSq = 0.09f; // 0.3m²
    private Vector3 _lastDestination = Vector3.positiveInfinity;

    public virtual void Enter(MonsterContext ctx)
    {
        ctx.Agent.speed = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier;
        ctx.Agent.stoppingDistance = ctx.Monster.GetCombatStopDistance(ctx);
        _lastDestination = Vector3.positiveInfinity; // 진입 시 즉시 경로 계산 보장
        PlayAnim(ctx, ctx.Animation.chaseStateName);
    }

    public virtual void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        // 공격 사정거리 이내 → 공격 준비
        if (ctx.Monster.ShouldEnterAttackReady(ctx))
        {
            ctx.Monster.ChangeState<AttackReadyState>();
            return;
        }

        // 추격 포기 → 배회 복귀
        if (ctx.Monster.ShouldGiveUpChase(ctx))
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        // 추격 이동 + 회전 — 플레이어가 충분히 이동했을 때만 경로 재계산
        Vector3 targetPos = ctx.Runtime.PlayerTarget.position;
        if ((targetPos - _lastDestination).sqrMagnitude > DestinationUpdateThresholdSq)
        {
            ctx.Agent.SetDestination(targetPos);
            _lastDestination = targetPos;
        }
        FaceTarget(ctx);
        KeepChaseAnimation(ctx);

        // 이동 속도 파라미터 갱신 — 댐핑으로 블렌드 트리 부드럽게 전환
        if (!string.IsNullOrEmpty(ctx.Animation.speedParam) && ctx.Animator != null)
            ctx.Animator.SetFloat(ctx.Animation.speedParam, ctx.Agent.velocity.magnitude,
                ctx.Animation.speedDampTime, Time.deltaTime);
    }

    public virtual void Exit(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
    }

    // ── 헬퍼 ──────────────────────────────────────────────

    protected static void FaceTarget(MonsterContext ctx)
    {
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        Quaternion target = Quaternion.LookRotation(dir);
        ctx.Transform.rotation = Quaternion.Slerp(
            ctx.Transform.rotation, target, Time.deltaTime * 10f);
    }

    protected static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;

        string fallback = ctx.Animation.patrolStateName;
        string finalState = !string.IsNullOrEmpty(stateName) && ctx.Animator.HasState(0, Animator.StringToHash(stateName))
            ? stateName
            : (!string.IsNullOrEmpty(fallback) && ctx.Animator.HasState(0, Animator.StringToHash(fallback))
                ? fallback
                : null);

        if (string.IsNullOrEmpty(finalState)) return;
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(finalState, ctx.Animation.crossFadeDuration);
    }

    protected static void KeepChaseAnimation(MonsterContext ctx)
    {
        if (ctx.Animator == null) return;
        if (ctx.Animator.IsInTransition(0)) return;
        if (string.IsNullOrEmpty(ctx.Animation.chaseStateName)) return;

        int chaseHash = Animator.StringToHash(ctx.Animation.chaseStateName);
        if (!ctx.Animator.HasState(0, chaseHash)) return;

        var current = ctx.Animator.GetCurrentAnimatorStateInfo(0);
        if (current.shortNameHash == chaseHash) return;

        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(ctx.Animation.chaseStateName, Mathf.Min(0.08f, ctx.Animation.crossFadeDuration));
    }
}
}
