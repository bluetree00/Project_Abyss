using UnityEngine;


namespace RelicFairy.Monster
{
/// <summary>
/// 공격 준비 상태.
/// - 이동 정지, attackDelay(1초) 대기 후 Attack 전환.
/// - 대기 중 플레이어가 사정거리 밖으로 나가면 Chase 전환.
/// - 플레이어 사망 시 Patrol 전환.
/// </summary>
public class AttackReadyState : IMonsterState
{
    // 첫 공격 준비 — 등급별 하한(10-21 사용자 「첫 공격 모션까지 조금 느리게 — 플레이어가 연속 공격할 때 성취감」).
    // 예전엔 min(attackDelay, 0.3)이라 사거리에 닿자마자 0.5~0.8초 안에 첫 타가 나왔다. 두 번째부터는 attackDelay 그대로.
    // 예고(맞기 0.5초 전 바닥 예고 · 최소 앞섬 0.45)는 PatternAttack 쪽이 맡아 그대로 — 「예고 = 판정」 유지.
    private const float FirstAttackCommon = 0.7f;
    private const float FirstAttackRare   = 0.85f;
    private const float FirstAttackElite  = 1.0f;

    public virtual void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;   // 추적 속도가 남아 전투 대기 자세로 0.2~0.4초 미끄러졌다(10-14 정예 애니 점검)
        }

        // 첫 조우 — 등급별 하한만큼은 기다린다(그 사이 플레이어가 먼저 연타를 넣을 틈).
        ctx.Runtime.StateTimer = ctx.Runtime.IsFirstAttack
            ? Mathf.Max(ctx.Stat.attackDelay, FirstAttackFloor(ctx.Monster.Grade))
            : ctx.Stat.attackDelay;
        ctx.Runtime.IsFirstAttack = false;
        ctx.Monster.ResetAttackGlint();

        PlayAnim(ctx, ctx.Animation.attackReadyStateName);
        FacePlayer(ctx);
    }

    private static float FirstAttackFloor(MonsterGrade grade) => grade switch
    {
        MonsterGrade.Common => FirstAttackCommon,
        MonsterGrade.Rare   => FirstAttackRare,
        MonsterGrade.Boss   => 0f,   // 보스는 보스 패턴이 박자를 정한다
        _                   => FirstAttackElite,
    };

    public virtual void Update(MonsterContext ctx)
    {
        // 플레이어 사망
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);

        // 플레이어가 사정거리 밖으로 이탈 (1.3배 여유 허용)
        if (dist > ctx.Monster.GetCombatStopDistance(ctx) * 1.3f)
        {
            ctx.Monster.ChangeState<ChaseState>();
            return;
        }

        FacePlayer(ctx);

        ctx.Runtime.StateTimer -= Time.deltaTime;
        // 공격 예고 반짝임 — 준비가 끝나고 공격 상태가 맞히기까지(damageApplyDelay)를 더한 남은 초
        ctx.Monster.NotifyAttackIncoming(ctx.Runtime.StateTimer + ctx.Combat.damageApplyDelay);
        if (ctx.Runtime.StateTimer <= 0f)
            ctx.Monster.ChangeState<AttackState>();
    }

    public virtual void Exit(MonsterContext ctx) { }

    // ── 헬퍼 ──────────────────────────────────────────────

    protected static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        ctx.Transform.rotation = Quaternion.Slerp(
            ctx.Transform.rotation,
            Quaternion.LookRotation(dir),
            Time.deltaTime * 15f);
    }

    protected static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;

        string fallback = ctx.Animation.idleStateName;
        string finalState = !string.IsNullOrEmpty(stateName) && ctx.Animator.HasState(0, Animator.StringToHash(stateName))
            ? stateName
            : (!string.IsNullOrEmpty(fallback) && ctx.Animator.HasState(0, Animator.StringToHash(fallback))
                ? fallback
                : null);

        if (string.IsNullOrEmpty(finalState)) return;
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(finalState, ctx.Animation.crossFadeDuration);
    }
}
}
