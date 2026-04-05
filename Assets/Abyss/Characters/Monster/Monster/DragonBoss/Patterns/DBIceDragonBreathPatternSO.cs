using UnityEngine;

namespace Abyss.Monster
{
[CreateAssetMenu(fileName = "DBIceDragonBreathPatternSO",
                 menuName = "Abyss/Boss/DragonBoss/IceDragonBreath")]
public class DBIceDragonBreathPatternSO : BossPatternSO
{
    [Header("Animation")]
    [SerializeField] private string animName = "Attack01";
    [SerializeField] private float crossFade = 0.1f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxPrefab;
    [SerializeField] private GameObject iceColumnVfxPrefab;

    [Header("Attack")]
    [SerializeField] private float warningDuration = 0.8f;
    [SerializeField] private float breathDuration = 3f;
    [SerializeField] private float breathTickInterval = 0.3f;
    [SerializeField] private float columnTickInterval = 0.4f;
    [SerializeField] private float columnRadius = 1.5f;
    [SerializeField] private float columnRingRadius = 4f;
    [SerializeField] private float breathCastRadius = 1.2f;
    [SerializeField] private float breathCastMaxDist = 20f;

    [Header("Flight")]
    [SerializeField] private float riseHeight = 8f;

    private IceDragonBreathState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new IceDragonBreathState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    private sealed class IceDragonBreathState : FullLockState<DBIceDragonBreathPatternSO>
    {
        private float _timer;
        private float _breathTick;
        private float _columnTick;
        private float _columnAngle;
        private int _phase;
        private GameObject _activeVfx;
        private Vector3 _anchorPosition;
        private Vector3 _flyPosition;
        private bool _originalUpdatePosition;
        private bool _originalUpdateRotation;

        public IceDragonBreathState(DBIceDragonBreathPatternSO data) : base(data) { }

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

            var iceColor = DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Ice);


            _phase = 0;
            _timer = 0f;
            _breathTick = 0f;
            _columnTick = 0f;
            _columnAngle = 0f;
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

                // 브레스 VFX 생성
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
                            DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Ice));
                    }
                }

                _phase = 1;
                _timer = 0f;
                return;
            }

            // phase 1: 브레스
            _breathTick += Time.deltaTime;
            _columnTick += Time.deltaTime;
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

            if (_columnTick >= Data.columnTickInterval)
            {
                _columnTick -= Data.columnTickInterval;
                SpawnColumn(ctx);
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

        private void SpawnColumn(MonsterContext ctx)
        {
            if (ctx.Runtime.PlayerTarget == null) return;

            Vector3 center = ctx.Runtime.PlayerTarget.position;
            center.y = ctx.Transform.position.y;
            Vector3 dir = Quaternion.Euler(0f, _columnAngle, 0f) * Vector3.forward;
            Vector3 pos = center + dir * Data.columnRingRadius;
            _columnAngle -= 45f;

            if (Data.iceColumnVfxPrefab != null)
            {
                var column = BossEffectPool.SpawnOneShot(Data.iceColumnVfxPrefab, pos, Quaternion.identity, null, 3f);
                DragonBossVisualHelper.ApplyEffectTint(
                    column,
                    DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Ice));
            }

            var hits = Physics.OverlapSphere(pos, Data.columnRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                    ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                player.TakeDamage((int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier));
                player.ApplyKnockback(Vector3.zero, 2f);
            }
        }

        private void DoBreathHit(MonsterContext ctx)
        {
            if (ctx.Runtime.PlayerTarget == null) return;
            int damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 0.8f);

            var hits = Physics.OverlapSphere(ctx.Runtime.PlayerTarget.position, Data.breathCastRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                    ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;
                player.TakeDamage(damage);
                player.ApplyKnockback(Vector3.zero, 2f);
                break;
            }
        }
    }
}
}
