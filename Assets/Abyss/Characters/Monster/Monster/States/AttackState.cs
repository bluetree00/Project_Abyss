using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// Common melee attack state.
/// Applies hit after delay and then transitions back to ready/chase.
/// </summary>
public class AttackState : IMonsterState
{
    private float _cooldownTimer;
    private float _damageTimer;
    private bool _damageDealt;
    private bool _waitForAttackAnimFinish;

    public virtual void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();

        _cooldownTimer = 1f / Mathf.Max(0.01f, ctx.Stat.attackRate);
        _damageTimer = ctx.Combat.damageApplyDelay;
        _damageDealt = false;
        _waitForAttackAnimFinish = ShouldWaitForAttackAnimation(ctx);

        ctx.Runtime.AttackHitDealt = false;
        PlayAttackAnim(ctx);
        FacePlayer(ctx);
    }

    public virtual void Update(MonsterContext ctx)
    {
        if (!_damageDealt && _damageTimer > 0f)
        {
            _damageTimer -= Time.deltaTime;
            if (_damageTimer <= 0f)
            {
                _damageDealt = true;
                ctx.Monster.DealDamageToPlayer();
            }
        }

        _cooldownTimer -= Time.deltaTime;
        if (_cooldownTimer > 0f) return;

        // If the attack animation exists as a state, wait until it nearly finishes
        // to avoid stiff snapping at the end of the motion.
        if (_waitForAttackAnimFinish && !IsAttackAnimNearlyFinished(ctx))
            return;

        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        if (dist <= ctx.Monster.GetCombatStopDistance(ctx))
            ctx.Monster.ChangeState<AttackReadyState>();
        else
            ctx.Monster.ChangeState<ChaseState>();
    }

    public virtual void Exit(MonsterContext ctx) { }

    protected static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }

    private static void PlayAttackAnim(MonsterContext ctx)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(ctx.Animation.attackTrigger)) return;

        var animator = ctx.Animator;
        animator.speed = 1f;
        string key = ctx.Animation.attackTrigger;

        if (HasState(animator, key))
        {
            float fade = Mathf.Max(0.1f, ctx.Animation.crossFadeDuration);
            animator.CrossFade(key, fade, 0, 0f);
            return;
        }

        if (HasTrigger(animator, key))
            animator.SetTrigger(key);
    }

    private static bool HasState(Animator animator, string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return false;
        return animator.HasState(0, Animator.StringToHash(stateName));
    }

    private static bool HasTrigger(Animator animator, string triggerName)
    {
        if (animator == null || string.IsNullOrEmpty(triggerName)) return false;

        var parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == triggerName)
                return true;
        }
        return false;
    }

    private static bool ShouldWaitForAttackAnimation(MonsterContext ctx)
    {
        return ctx.Animator != null
               && !string.IsNullOrEmpty(ctx.Animation.attackTrigger)
               && HasState(ctx.Animator, ctx.Animation.attackTrigger);
    }

    private static bool IsAttackAnimNearlyFinished(MonsterContext ctx)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(ctx.Animation.attackTrigger))
            return true;

        int attackHash = Animator.StringToHash(ctx.Animation.attackTrigger);
        if (!ctx.Animator.HasState(0, attackHash))
            return true;

        var state = ctx.Animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash != attackHash && state.fullPathHash != attackHash)
            return true;

        float normalized = state.normalizedTime % 1f;
        return normalized >= 0.92f;
    }
}
}
