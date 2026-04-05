using UnityEngine;

namespace Abyss.Monster
{
[CreateAssetMenu(fileName = "DBThunderShieldPatternSO",
                 menuName = "Abyss/Boss/DragonBoss/ThunderShield")]
public class DBThunderShieldPatternSO : BossPatternSO
{
    [Header("Animation")]
    [SerializeField] private string animName = "Attack01";
    [SerializeField] private float crossFade = 0.1f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxPrefab;

    [Header("Shield")]
    [SerializeField] private float shieldDuration = 3f;
    [SerializeField] private float reflectRadius = 2f;
    [SerializeField] private float reflectTickInterval = 0.5f;

    private ThunderShieldState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new ThunderShieldState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    private sealed class ThunderShieldState : InvincibleState<DBThunderShieldPatternSO>
    {
        private float _timer;
        private float _shieldTimer;
        private float _reflectTick;
        private int _phase;
        private float _phaseDuration;
        private GameObject _activeVfx;

        public ThunderShieldState(DBThunderShieldPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            if (ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animName, Data.crossFade);

            var dragon = ctx.Monster as DragonBossMonster;
            if (dragon != null)
                dragon.DBBlackboard.ThunderShieldActive = true;

            MonsterGroundWarning.SpawnGrid(
                ctx.Transform.position,
                ctx.Transform.forward,
                MonsterGroundWarning.GridShape.Around8,
                0.6f,
                new Color(1f, 0.9f, 0.1f));

            if (Data.vfxPrefab != null)
            {
                _activeVfx = BossEffectPool.Spawn(
                    Data.vfxPrefab,
                    ctx.Transform.position,
                    ctx.Transform.rotation);
            }

            _phase = 0;
            _timer = 0f;
            _shieldTimer = 0f;
            _reflectTick = 0f;
            _phaseDuration = Data.shieldDuration;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;
            _shieldTimer += Time.deltaTime;
            _reflectTick += Time.deltaTime;

            if (_activeVfx != null)
                _activeVfx.transform.position = ctx.Transform.position;

            if (_reflectTick >= Data.reflectTickInterval)
            {
                _reflectTick -= Data.reflectTickInterval;
                DoReflectHit(ctx);
            }

            if (_phase == 0 && _shieldTimer >= Data.shieldDuration)
            {
                _phase = 1;
                _phaseDuration = 0.3f;
                _timer = 0f;
            }

            if (_phase == 1 && _timer >= _phaseDuration)
            {
                EndShield(ctx);
                ctx.Monster.ChangeState<PatrolState>();
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            EndShield(ctx);
        }

        private void EndShield(MonsterContext ctx)
        {
            var dragon = ctx.Monster as DragonBossMonster;
            if (dragon != null)
                dragon.DBBlackboard.ThunderShieldActive = false;

            if (_activeVfx != null)
            {
                BossEffectPool.Release(_activeVfx);
                _activeVfx = null;
            }
        }

        private void DoReflectHit(MonsterContext ctx)
        {
            var hits = Physics.OverlapSphere(ctx.Transform.position, Data.reflectRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                    ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                player.TakeDamage((int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 0.3f));
                player.ApplyKnockback(Vector3.zero, 0.5f);
            }
        }
    }
}
}
