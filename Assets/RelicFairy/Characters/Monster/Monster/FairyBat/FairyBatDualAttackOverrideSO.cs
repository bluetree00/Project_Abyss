using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// Fairy bat override that supports true melee or ranged attacks depending on
/// player distance, while also widening chase/attack-ready logic to cover the
/// sonic attack range.
/// </summary>
[CreateAssetMenu(fileName = "FairyBatDualAttackOverride", menuName = "Lee/Monster/Override/FairyBatDualAttack")]
public class FairyBatDualAttackOverrideSO : MonsterStateOverrideSO
{
    [Tooltip("Projectile prefab used for the sonic wave attack.")]
    public MonsterProjectile sonicWaveProjectile;

    [Tooltip("Optional VFX-only projectile setup used instead of the legacy temporary projectile.")]
    public GameObject sonicVfxProjectilePrefab;

    [Tooltip("Scale applied to the VFX projectile.")]
    public float sonicVfxProjectileScale = 1f;

    [Tooltip("Distance from the player at which the VFX projectile counts as a hit.")]
    public float sonicVfxHitRadius = 0.6f;

    [Tooltip("Optional hit effect spawned when the VFX projectile lands.")]
    public GameObject sonicVfxHitEffectPrefab;

    [Tooltip("Scale applied to the VFX hit effect.")]
    public float sonicVfxHitEffectScale = 1f;

    [Tooltip("Projectile speed in meters per second.")]
    public float sonicSpeed = 12f;

    [Tooltip("Maximum distance where the sonic attack can be used.")]
    public float sonicMaxRange = 15f;

    [Tooltip("Vertical offset applied to projectile spawn and target positions.")]
    public float launchHeightOffset = 0.8f;

    [Tooltip("Animation state name used for the sonic attack.")]
    public string sonicAnimTrigger = "Attack02";

    public override void RegisterOverrides(MonsterFSM fsm, MonsterBase monster)
    {
        fsm.RegisterAs<ChaseState>(new FairyBatDualChaseState(this));
        fsm.RegisterAs<AttackReadyState>(new FairyBatDualAttackReadyState(this));
        fsm.RegisterAs<AttackState>(new FairyBatDualAttackState(this));
    }

    private float GetMaxAttackRange(MonsterContext ctx)
    {
        bool hasRangedAttack = sonicWaveProjectile != null || sonicVfxProjectilePrefab != null;
        return Mathf.Max(ctx.Stat.attackRange, hasRangedAttack ? sonicMaxRange : 0f);
    }

    private bool ShouldUseRangedAttack(MonsterContext ctx)
        => (sonicWaveProjectile != null || sonicVfxProjectilePrefab != null)
        && ctx.Runtime.PlayerTarget != null
        && ctx.Runtime.DistToPlayer > ctx.Stat.attackRange;

    private void FireSonicWave(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        var damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
        var knockback = ctx.Stat.knockbackForce;
        var origin = ctx.Transform.position + Vector3.up * launchHeightOffset;
        var targetPos = ctx.Runtime.PlayerTarget.position + Vector3.up * launchHeightOffset;
        var direction = (targetPos - origin).normalized;

        if (sonicVfxProjectilePrefab != null)
        {
            var go = Object.Instantiate(sonicVfxProjectilePrefab, origin, Quaternion.LookRotation(direction));
            go.transform.localScale = Vector3.one * Mathf.Max(0.001f, sonicVfxProjectileScale);

            if (!go.TryGetComponent<MonsterVfxProjectile>(out var vfxProjectile))
                vfxProjectile = go.AddComponent<MonsterVfxProjectile>();

            vfxProjectile.Init(
                direction,
                sonicSpeed,
                sonicMaxRange,
                damage,
                knockback,
                0f,
                0f,
                sonicVfxHitRadius,
                ctx.Runtime.PlayerTarget,
                sonicVfxHitEffectPrefab,
                sonicVfxHitEffectScale);
            vfxProjectile.Owner = ctx.Transform.gameObject;
            return;
        }

        if (sonicWaveProjectile == null) return;

        var proj = Object.Instantiate(sonicWaveProjectile, origin, Quaternion.LookRotation(direction));
        proj.Init(direction, sonicSpeed, sonicMaxRange, damage, knockback);
        proj.Owner = ctx.Transform.gameObject;
    }

    private static void RotateTowardPlayer(MonsterContext ctx, float turnSpeed = 10f)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        var dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        var target = Quaternion.LookRotation(dir);
        ctx.Transform.rotation = Quaternion.Slerp(ctx.Transform.rotation, target, Time.deltaTime * turnSpeed);
    }

    private static void UpdateMoveBlend(MonsterContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.Animation.speedParam) || ctx.Animator == null) return;

        ctx.Animator.SetFloat(
            ctx.Animation.speedParam,
            ctx.Agent.velocity.magnitude,
            ctx.Animation.speedDampTime,
            Time.deltaTime);
    }

    private class FairyBatDualChaseState : ChaseState
    {
        private readonly FairyBatDualAttackOverrideSO _data;
        private const float DestinationUpdateThresholdSq = 0.09f;
        private Vector3 _lastDestination = Vector3.positiveInfinity;

        public FairyBatDualChaseState(FairyBatDualAttackOverrideSO data)
        {
            _data = data;
        }

        public override void Enter(MonsterContext ctx)
        {
            ctx.Agent.speed = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier;
            ctx.Agent.stoppingDistance = ctx.Stat.attackRange * 0.9f;
            _lastDestination = Vector3.positiveInfinity;
            PlayAnim(ctx, ctx.Animation.chaseStateName);
        }

        public override void Update(MonsterContext ctx)
        {
            if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
            {
                ctx.Monster.ChangeState<PatrolState>();
                return;
            }

            if (ctx.Runtime.DistToPlayer <= _data.GetMaxAttackRange(ctx))
            {
                ctx.Monster.ChangeState<AttackReadyState>();
                return;
            }

            if (ctx.Monster.ShouldGiveUpChase(ctx))
            {
                ctx.Monster.ChangeState<PatrolState>();
                return;
            }

            var targetPos = ctx.Runtime.PlayerTarget.position;
            if ((targetPos - _lastDestination).sqrMagnitude > DestinationUpdateThresholdSq)
            {
                ctx.Agent.SetDestination(targetPos);
                _lastDestination = targetPos;
            }

            RotateTowardPlayer(ctx);
            UpdateMoveBlend(ctx);
        }
    }

    private class FairyBatDualAttackReadyState : AttackReadyState
    {
        private readonly FairyBatDualAttackOverrideSO _data;

        public FairyBatDualAttackReadyState(FairyBatDualAttackOverrideSO data)
        {
            _data = data;
        }

        public override void Update(MonsterContext ctx)
        {
            if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
            {
                ctx.Monster.ChangeState<PatrolState>();
                return;
            }

            if (ctx.Runtime.DistToPlayer > _data.GetMaxAttackRange(ctx) * 1.1f)
            {
                ctx.Monster.ChangeState<ChaseState>();
                return;
            }

            RotateTowardPlayer(ctx, 15f);

            ctx.Runtime.StateTimer -= Time.deltaTime;
            ctx.Monster.NotifyAttackIncoming(ctx.Runtime.StateTimer + ctx.Combat.damageApplyDelay);
            if (ctx.Runtime.StateTimer <= 0f)
                ctx.Monster.ChangeState<AttackState>();
        }
    }

    private class FairyBatDualAttackState : IMonsterState
    {
        private readonly FairyBatDualAttackOverrideSO _data;
        private float _cooldownTimer;
        private float _damageTimer;
        private bool _damageDealt;
        private bool _useRangedAttack;
        private MonsterTelegraph _telegraph;   // 근접 원 예고(10-13)

        public FairyBatDualAttackState(FairyBatDualAttackOverrideSO data)
        {
            _data = data;
        }

        public void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();

            _cooldownTimer = 1f / Mathf.Max(0.01f, ctx.Stat.attackRate);
            _damageTimer = ctx.Combat.damageApplyDelay;
            _damageDealt = false;
            _useRangedAttack = _data.ShouldUseRangedAttack(ctx);

            ctx.Runtime.AttackHitDealt = false;
            RotateTowardPlayer(ctx, 100f);

            var meleeAnim = !string.IsNullOrEmpty(ctx.Animation.attackStateName)
                ? ctx.Animation.attackStateName
                : ctx.Animation.attackTrigger;
            var attackAnim = _useRangedAttack ? _data.sonicAnimTrigger : meleeAnim;
            // 컨트롤러에 없는 상태면 공격 클립(attackTrigger = Attack01)으로 — 요정 박쥐 컨트롤러엔 Attack01만 있어
            // 근접 「Attack」 · 음파 「Attack02」가 둘 다 안 나오고 모션 없이 피해만 들어갔다(10-07)
            if (ctx.Animator != null && !string.IsNullOrEmpty(attackAnim)
                && !ctx.Animator.HasState(0, Animator.StringToHash(attackAnim)))
                attackAnim = ctx.Animation.attackTrigger;
            if (ctx.Animator != null && !string.IsNullOrEmpty(attackAnim))
                ctx.Animator.CrossFade(attackAnim, Mathf.Max(0.08f, ctx.Animation.crossFadeDuration));
            ctx.Monster.NotifyAttackIncoming(_damageTimer);
            // 근접은 판정 원(DealDamageToPlayer = 거리 원)을 예고한다 — 예고 없이 1.8 m 피해가 들어갔다(10-13 이펙트 = 판정)
            if (!_useRangedAttack)
                _telegraph = MonsterTelegraph.Circle(ctx.Transform.position, ctx.Monster.GetCombatHitDistance(ctx), Mathf.Max(0.05f, _damageTimer), MonsterTelegraph.Normal);
        }

        public void Update(MonsterContext ctx)
        {
            if (!_damageDealt)
            {
                _damageTimer -= Time.deltaTime;
                ctx.Monster.NotifyAttackIncoming(_damageTimer);
                if (_damageTimer <= 0f)
                {
                    _damageDealt = true;
                    _telegraph = null;   // 맞는 순간까지 그렸다
                    if (_useRangedAttack)
                        _data.FireSonicWave(ctx);
                    else
                        ctx.Monster.DealDamageToPlayer();
                }
            }

            _cooldownTimer -= Time.deltaTime;
            if (_cooldownTimer > 0f) return;

            if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
            {
                ctx.Monster.ChangeState<PatrolState>();
                return;
            }

            if (ctx.Runtime.DistToPlayer <= _data.GetMaxAttackRange(ctx))
                ctx.Monster.ChangeState<AttackReadyState>();
            else
                ctx.Monster.ChangeState<ChaseState>();
        }

        public void Exit(MonsterContext ctx)
        {
            ctx.Monster.ResetAttackGlint();
            if (_telegraph != null) _telegraph.Cancel();   // 피해 전에 끊겼다 — 헛예고를 지운다
            _telegraph = null;
        }
    }
}
