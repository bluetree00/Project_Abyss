using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// Dragon monster that chooses between melee and ranged attacks by distance.
/// </summary>
public class DragonMonster : MonsterBase
{
    public const string PrefabAddress = "Dragon/Dragon";
    protected override string ConfigAddress   => "Dragon/DragonConfig";
    protected override string DataAddress     => string.Empty;
    protected override float  HPBarHeadOffset => 0.18f;
    protected override string HeadBoneName    => null;

    [Header("Ranged Attack")]
    [SerializeField] private MonsterProjectile _rangedProjectile;
    [SerializeField] private float _rangedAttackRange = 10f;
    [SerializeField] private float _rangedAttackSpeed = 14f;
    [SerializeField] private float _rangedAttackMaxRange = 16f;
    [SerializeField] private float _rangedLaunchHeightOffset = 1.4f;
    [SerializeField] private string _rangedAttackAnim = "Attack02";
    private float _legacyRageTimer;

    protected override void RegisterStates()
    {
        base.RegisterStates();
        _fsm.RegisterAs<AttackReadyState>(new DragonAttackReadyState(this));
        _fsm.RegisterAs<AttackState>(new DragonDualAttackState(this));
    }

    public override bool ShouldEnterAttackReady(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return false;
        return ctx.Runtime.DistToPlayer <= GetMaxAttackRange();
    }

    private float GetMaxAttackRange()
        => Mathf.Max(_config.stat.attackRange, _rangedProjectile != null ? _rangedAttackRange : 0f);

    private bool ShouldUseRangedAttack(MonsterContext ctx)
        => _rangedProjectile != null
        && ctx.Runtime.PlayerTarget != null
        && ctx.Runtime.DistToPlayer > ctx.Stat.attackRange * 1.1f;

    private void FireRangedAttack(MonsterContext ctx)
    {
        if (_rangedProjectile == null || ctx.Runtime.PlayerTarget == null) return;

        var damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
        var knockback = ctx.Stat.knockbackForce;
        var origin = ctx.Transform.position + Vector3.up * _rangedLaunchHeightOffset;
        var targetPos = ctx.Runtime.PlayerTarget.position + Vector3.up * _rangedLaunchHeightOffset;
        var direction = (targetPos - origin).normalized;

        var proj = Object.Instantiate(_rangedProjectile, origin, Quaternion.LookRotation(direction));
        proj.Init(direction, _rangedAttackSpeed, _rangedAttackMaxRange, damage, knockback);
    }

    protected override void Update()
    {
        if (_legacyRageTimer > 0f)
        {
            _legacyRageTimer -= Time.deltaTime;
            if (_legacyRageTimer <= 0f && _runtime != null && _agent != null && _config != null)
            {
                _runtime.SpeedMultiplier = 1f;
                _agent.speed = _config.stat.moveSpeed;
            }
        }

        base.Update();
    }

    // Legacy compatibility for DragonRoarState while that file still exists in the project.
    public void StartRageChase(float duration)
    {
        _legacyRageTimer = duration;

        if (_runtime != null)
            _runtime.SpeedMultiplier = 1.6f;

        if (_agent != null && _config != null)
            _agent.speed = _config.stat.moveSpeed * 1.6f;
    }

    protected override void OnEnable()
    {
        _legacyRageTimer = 0f;
        base.OnEnable();
    }

    private static void FacePlayer(MonsterContext ctx, float turnSpeed)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        var dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        var target = Quaternion.LookRotation(dir);
        ctx.Transform.rotation = Quaternion.Slerp(ctx.Transform.rotation, target, Time.deltaTime * turnSpeed);
    }

    private sealed class DragonAttackReadyState : IMonsterState
    {
        private readonly DragonMonster _owner;

        public DragonAttackReadyState(DragonMonster owner)
        {
            _owner = owner;
        }

        public void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();
            ctx.Runtime.StateTimer = ctx.Runtime.IsFirstAttack ? 0f : ctx.Stat.attackDelay;
            ctx.Runtime.IsFirstAttack = false;

            if (ctx.Animator != null && !string.IsNullOrEmpty(ctx.Animation.attackReadyStateName))
                ctx.Animator.CrossFade(ctx.Animation.attackReadyStateName, ctx.Animation.crossFadeDuration);

            FacePlayer(ctx, 15f);
        }

        public void Update(MonsterContext ctx)
        {
            if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
            {
                ctx.Monster.ChangeState<PatrolState>();
                return;
            }

            if (ctx.Runtime.DistToPlayer > _owner.GetMaxAttackRange() * 1.15f)
            {
                ctx.Monster.ChangeState<ChaseState>();
                return;
            }

            FacePlayer(ctx, 15f);

            ctx.Runtime.StateTimer -= Time.deltaTime;
            if (ctx.Runtime.StateTimer <= 0f)
                ctx.Monster.ChangeState<AttackState>();
        }

        public void Exit(MonsterContext ctx) { }
    }

    private sealed class DragonDualAttackState : IMonsterState
    {
        private readonly DragonMonster _owner;
        private float _cooldownTimer;
        private float _damageTimer;
        private bool _damageDealt;
        private bool _useRangedAttack;

        public DragonDualAttackState(DragonMonster owner)
        {
            _owner = owner;
        }

        public void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();

            _cooldownTimer = 1f / Mathf.Max(0.01f, ctx.Stat.attackRate);
            _damageTimer = ctx.Combat.damageApplyDelay;
            _damageDealt = false;
            _useRangedAttack = _owner.ShouldUseRangedAttack(ctx);

            ctx.Runtime.AttackHitDealt = false;
            FacePlayer(ctx, 100f);

            var attackAnim = _useRangedAttack ? _owner._rangedAttackAnim : ctx.Animation.attackTrigger;
            if (ctx.Animator != null && !string.IsNullOrEmpty(attackAnim))
            ctx.Animator.CrossFade(attackAnim, Mathf.Max(0.08f, ctx.Animation.crossFadeDuration));
        }

        public void Update(MonsterContext ctx)
        {
            if (!_damageDealt)
            {
                _damageTimer -= Time.deltaTime;
                if (_damageTimer <= 0f)
                {
                    _damageDealt = true;
                    if (_useRangedAttack)
                        _owner.FireRangedAttack(ctx);
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

            if (ctx.Runtime.DistToPlayer <= _owner.GetMaxAttackRange())
                ctx.Monster.ChangeState<AttackReadyState>();
            else
                ctx.Monster.ChangeState<ChaseState>();
        }

        public void Exit(MonsterContext ctx) { }
    }
}
}
