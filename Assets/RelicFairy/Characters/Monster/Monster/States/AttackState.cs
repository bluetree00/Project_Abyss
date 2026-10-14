using UnityEngine;

namespace RelicFairy.Monster
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
    private MonsterTelegraph _telegraph;   // 이 공격의 범위 예고(끊기면 지운다 · 10-12)

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
        _telegraph = SpawnAttackTelegraph(ctx, _damageTimer);

        // 근접 windup(예고) 노출 — 데미지 지연 동안 아이템 저스트가드/섬광 판정용.
        // 투사체(원거리)는 비행이 예고이므로 제외.
        if (!(ctx.Stat.attackShape is MonsterRangedAttackSO))
            ctx.Monster.BeginAttackTelegraph();
        ctx.Monster.NotifyAttackIncoming(_damageTimer);
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
            ctx.Monster.NotifyAttackIncoming(_damageTimer);
            if (_damageTimer <= 0f)
            {
                _damageDealt = true;
                // 아이템 섬광의 순간: windup 중 적중당해 취소되었으면 데미지 스킵.
                bool canceled = ctx.Monster.ConsumeAttackCancel();
                ctx.Monster.EndAttackTelegraph();
                if (canceled && _telegraph != null) _telegraph.Cancel();
                _telegraph = null;
                if (!canceled)
                    ctx.Monster.DealDamageToPlayer();
                // 판정이 끝난 <b>뒤에</b> 세운다 — DealDamageToPlayer는 이 플래그가 서 있으면 곧바로 돌아가므로(애니 이벤트 중복 방지),
                // 먼저 세우면 타이머 경로의 피해가 한 번도 나가지 않았다(07-10 ~ 10-01: 달팽이 · 리치 해골 등 기본 공격 상태를 쓰는 몬스터 피해 0).
                ctx.Runtime.AttackHitDealt = true;
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

    public virtual void Exit(MonsterContext ctx)
    {
        ctx.Monster.EndAttackTelegraph();
        ctx.Monster.ResetAttackGlint();
        if (_telegraph != null) _telegraph.Cancel();   // 피해 전에 끊겼다 — 헛예고를 지운다
        _telegraph = null;
    }

    protected static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }

    // 공격 전 암시 판정 — 기본 근접 몹의 히트 존을 데미지보다 먼저 바닥에 표시.
    // 데미지는 damageApplyDelay 후 발동하므로 경고가 그만큼 선행한다.
    // 투사체(원거리)는 비행 자체가 예고이므로 제외. 패턴 몹은 PatternAttackState가 별도 처리.
    private static MonsterTelegraph SpawnAttackTelegraph(MonsterContext ctx, float secondsUntilHit)
    {
        // 판정 모양 그대로(DealDamageToPlayer → attackShape.Execute · 없으면 거리 원) — 부채꼴 몹이 원을 그리던 것(10-12 P2)
        var shape = ctx.Stat.attackShape;
        if (shape is MonsterRangedAttackSO) return null;
        Color color = ctx.Monster.Grade == MonsterGrade.Elite ? MonsterTelegraph.Heavy : MonsterTelegraph.Normal;
        float secs = secondsUntilHit > 0f ? secondsUntilHit : 0.2f;   // 애니 이벤트로 때리는 몹(타이머 0)은 짧게
        if (shape is MonsterConeAttackSO cone)
            return MonsterTelegraph.Cone(ctx.Transform.position, ctx.Transform.forward, cone.range, cone.halfAngle, secs, color);
        float radius = shape is MonsterSphereAttackSO sphere
            ? (sphere.radiusOverride > 0f ? sphere.radiusOverride : ctx.Stat.attackRadius)
            : ctx.Monster.GetCombatHitDistance(ctx);
        return radius > 0.01f ? MonsterTelegraph.Circle(ctx.Transform.position, radius, secs, color) : null;
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
