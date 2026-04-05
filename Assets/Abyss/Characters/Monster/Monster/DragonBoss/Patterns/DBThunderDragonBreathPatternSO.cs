using UnityEngine;

namespace Abyss.Monster
{
[CreateAssetMenu(fileName = "DBThunderDragonBreathPatternSO",
                 menuName = "Abyss/Boss/DragonBoss/ThunderDragonBreath")]
public class DBThunderDragonBreathPatternSO : BossPatternSO
{
    [Header("Animation")]
    [SerializeField] private string animName = "Attack01";
    [SerializeField] private float crossFade = 0.1f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxPrefab;
    [SerializeField] private GameObject lightningVfxPrefab;

    [Header("Attack")]
    [SerializeField] private float warningDuration = 0.8f;
    [SerializeField] private float breathDuration = 3f;
    [SerializeField] private float breathTickInterval = 0.3f;
    [SerializeField] private float lightningTickInterval = 0.4f;
    [SerializeField] private float lightningRadius = 1.5f;
    [SerializeField] private float lightningRingRadius = 4f;
    [SerializeField] private float castRadius = 1.2f;
    [SerializeField] private float castMaxDist = 20f;

    [Header("Flight")]
    [SerializeField] private float riseHeight = 8f;

    private ThunderDragonBreathState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new ThunderDragonBreathState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    private sealed class ThunderDragonBreathState : FullLockState<DBThunderDragonBreathPatternSO>
    {
        private float _timer;
        private float _breathTick;
        private float _lightningTick;
        private float _lightningAngle;
        private int _phase;
        private GameObject _activeVfx;
        private Vector3 _anchorPosition;
        private Vector3 _flyPosition;
        private bool _originalUpdatePosition;
        private bool _originalUpdateRotation;

        public ThunderDragonBreathState(DBThunderDragonBreathPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            if (ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;
            _anchorPosition = ctx.Transform.position;
            _flyPosition = _anchorPosition + Vector3.up * Data.riseHeight;
            _originalUpdatePosition = ctx.Agent.updatePosition;
            _originalUpdateRotation = ctx.Agent.updateRotation;
            ctx.Agent.updatePosition = false;
            ctx.Agent.updateRotation = false;

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animName, Data.crossFade);

            _phase = 0;
            _timer = 0f;
            _breathTick = 0f;
            _lightningTick = 0f;
            _lightningAngle = 0f;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            // phase 0: 경고 대기 + 부상
            if (_phase == 0)
            {
                float riseT = Mathf.Clamp01(_timer / Data.warningDuration);
                ctx.Transform.position = Vector3.Lerp(_anchorPosition, _flyPosition, riseT);
                if (_timer < Data.warningDuration) return;

                if (Data.vfxPrefab != null)
                {
                    _activeVfx = BossEffectPool.Spawn(
                        Data.vfxPrefab,
                        _anchorPosition,
                        ctx.Transform.rotation,
                        ctx.Transform,
                        false);
                    if (_activeVfx != null)
                    {
                        _activeVfx.transform.localPosition = new Vector3(0f, 1.2f, 2.2f);
                        _activeVfx.transform.localRotation = Quaternion.identity;
                        _activeVfx.transform.localScale = new Vector3(1.6f, 1.6f, 5.5f);
                        DragonBossVisualHelper.ApplyEffectTint(
                            _activeVfx,
                            DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Thunder));
                    }
                }

                _phase = 1;
                _timer = 0f;
                return;
            }

            // phase 1: 브레스
            _breathTick += Time.deltaTime;
            _lightningTick += Time.deltaTime;
            ctx.Transform.position = _flyPosition;

            if (ctx.Runtime.PlayerTarget != null)
            {
                Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
                if (toPlayer.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(toPlayer.normalized);
                    ctx.Transform.rotation = Quaternion.RotateTowards(ctx.Transform.rotation, targetRot, 60f * Time.deltaTime);
                }
            }

            if (_lightningTick >= Data.lightningTickInterval)
            {
                _lightningTick -= Data.lightningTickInterval;
                SpawnLightning(ctx);
            }

            if (_breathTick >= Data.breathTickInterval)
            {
                _breathTick -= Data.breathTickInterval;
                DoBreathHit(ctx);
            }

            if (_timer >= Data.breathDuration)
                ctx.Monster.ChangeState<PatrolState>();
        }

        public override void Exit(MonsterContext ctx)
        {
            ctx.Transform.position = _anchorPosition;
            ctx.Agent.updatePosition = _originalUpdatePosition;
            ctx.Agent.updateRotation = _originalUpdateRotation;
            ctx.Agent.Warp(_anchorPosition);

            if (_activeVfx != null)
            {
                BossEffectPool.Release(_activeVfx);
                _activeVfx = null;
            }

        }

        private void SpawnLightning(MonsterContext ctx)
        {
            if (ctx.Runtime.PlayerTarget == null) return;

            Vector3 center = ctx.Runtime.PlayerTarget.position;
            center.y = ctx.Transform.position.y;
            Vector3 dir = Quaternion.Euler(0f, _lightningAngle, 0f) * Vector3.forward;
            Vector3 pos = center + dir * Data.lightningRingRadius;
            _lightningAngle -= 45f;

            if (Data.lightningVfxPrefab != null)
            {
                var lightning = BossEffectPool.SpawnOneShot(Data.lightningVfxPrefab, pos, Quaternion.identity, null, 3f);
                DragonBossVisualHelper.ApplyEffectTint(
                    lightning,
                    DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Thunder));
            }

            var hits = Physics.OverlapSphere(pos, Data.lightningRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                    ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                player.TakeDamage((int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier));
                player.ApplyKnockback(Vector3.zero, 0.5f); // 번개: 2초 그로기
            }
        }

        private void DoBreathHit(MonsterContext ctx)
        {
            if (ctx.Runtime.PlayerTarget == null) return;
            int damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 0.5f);

            var hits = Physics.OverlapSphere(ctx.Runtime.PlayerTarget.position, Data.castRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                    ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;
                player.TakeDamage(damage);
                player.ApplyKnockback(Vector3.zero, 0.5f);
                break;
            }
        }
    }
}
}
