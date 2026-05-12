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
    // CrossFade 방식: Enter에서 확정 / Trigger 방식: 전환 완료 후 Update에서 확정 (0 = 미확정)
    private int _attackStateHash;

    public virtual void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();

        _cooldownTimer = 1f / Mathf.Max(0.01f, ctx.Stat.attackRate);
        _damageTimer = ctx.Combat.damageApplyDelay;
        _damageDealt = false;
        _attackStateHash = 0;

        string key = ctx.Animation.attackTrigger;
        bool hasAnim = ctx.Animator != null && !string.IsNullOrEmpty(key);
        _waitForAttackAnimFinish = hasAnim;

        // CrossFade 방식이면 상태 해시를 바로 확정
        if (hasAnim && HasState(ctx.Animator, key))
            _attackStateHash = Animator.StringToHash(key);

        ctx.Runtime.AttackHitDealt = false;
        ctx.Runtime.AttackHitCount = 0;
        PlayAttackAnim(ctx);
        FacePlayer(ctx);
    }

    public virtual void Update(MonsterContext ctx)
    {
        // Trigger 방식: 전환이 끝난 첫 프레임에 실제 공격 상태 해시 확정
        if (_waitForAttackAnimFinish && _attackStateHash == 0 && ctx.Animator != null
            && !ctx.Animator.IsInTransition(0))
        {
            _attackStateHash = ctx.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        }

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
        if (ctx.Runtime.IsExecutingAttackSequence) return;

        int maxHits = ctx.Combat.maxHitsPerAttack;
        bool allHitsDealt = maxHits > 0 && ctx.Runtime.AttackHitCount >= maxHits;

        // maxHitsPerAttack이 설정된 경우 모든 히트 완료 시 쿨다운 무시, 애니메이션 끝만 대기
        if (!allHitsDealt && _cooldownTimer > 0f) return;

        // 애니메이션 1사이클이 끝날 때까지 대기 (루핑 방지)
        if (_waitForAttackAnimFinish && !IsAttackAnimFinished(ctx))
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

    private bool IsAttackAnimFinished(MonsterContext ctx)
    {
        if (ctx.Animator == null) return true;
        if (_attackStateHash == 0) return false;  // 아직 공격 상태 미확정 → 대기

        var state = ctx.Animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash != _attackStateHash && state.fullPathHash != _attackStateHash)
            return true;  // 공격 상태를 벗어났으면 완료

        return (state.normalizedTime % 1f) >= 0.92f;
    }
}
}
