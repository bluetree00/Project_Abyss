using UnityEngine;

namespace Abyss.Monster
{
[CreateAssetMenu(fileName = "DBClawSwipePatternSO",
                 menuName = "Abyss/Boss/DragonBoss/ClawSwipe")]
public class DBClawSwipePatternSO : BossPatternSO
{
    [Header("Animation")]
    [SerializeField] private string animHit1 = "Attack01";
    [SerializeField] private string animHit2 = "Attack02";
    [SerializeField] private float crossFade = 0.1f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxPrefab;

    [Header("Attack")]
    [SerializeField] private float flyDuration = 0.45f;
    [SerializeField] private float flyHeight = 2f;
    [SerializeField] private float warningDuration = 0.25f;
    [SerializeField] private float hitRadius = 2f;
    [SerializeField] private float betweenDelay = 0.35f;

    private ClawSwipeState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new ClawSwipeState(this);

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.DistToPlayer <= 3f;

    public override SpecialStateBase GetRuntimeState() => _state;

    private sealed class ClawSwipeState : FullLockState<DBClawSwipePatternSO>
    {
        private float _timer;
        private int _phase;
        private float _phaseDuration;
        private Vector3 _startPos;
        private Vector3 _targetPos;
        private bool _originalUpdatePosition;
        private bool _originalUpdateRotation;

        public ClawSwipeState(DBClawSwipePatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            if (ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;

            _startPos = ctx.Transform.position;
            _targetPos = ctx.Runtime.PlayerTarget != null
                ? ctx.Runtime.PlayerTarget.position
                : ctx.Transform.position + ctx.Transform.forward * 2f;
            _targetPos.y = _startPos.y;

            Vector3 dir = (_targetPos - _startPos);
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                ctx.Transform.rotation = Quaternion.LookRotation(dir.normalized);

            _originalUpdatePosition = ctx.Agent.updatePosition;
            _originalUpdateRotation = ctx.Agent.updateRotation;

            _phase = 0;
            _timer = 0f;
            _phaseDuration = Data.flyDuration;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            switch (_phase)
            {
                case 0:
                    ctx.Agent.updatePosition = false;
                    ctx.Agent.updateRotation = false;
                    UpdateFlyPose(ctx, _timer / Mathf.Max(0.01f, Data.flyDuration));
                    if (_timer < _phaseDuration) return;

                    ctx.Transform.position = _targetPos;
                    SpawnWarning(ctx);
                    if (ctx.Animator != null)
                        ctx.Animator.CrossFade(Data.animHit1, Data.crossFade);
                    _phase = 1;
                    _timer = 0f;
                    _phaseDuration = Data.warningDuration;
                    break;

                case 1:
                    if (_timer < _phaseDuration) return;
                    ApplyHit(ctx);
                    _phase = 2;
                    _timer = 0f;
                    _phaseDuration = Data.betweenDelay;
                    break;

                case 2:
                    if (_timer < _phaseDuration) return;
                    SpawnWarning(ctx);
                    if (ctx.Animator != null)
                        ctx.Animator.CrossFade(Data.animHit2, Data.crossFade);
                    _phase = 3;
                    _timer = 0f;
                    _phaseDuration = Data.warningDuration;
                    break;

                case 3:
                    if (_timer < _phaseDuration) return;
                    ApplyHit(ctx);
                    RestoreAgentTracking(ctx);
                    ctx.Agent.Warp(ctx.Transform.position);
                    ctx.Monster.ChangeState<PatrolState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            RestoreAgentTracking(ctx);
        }

        private void UpdateFlyPose(MonsterContext ctx, float t)
        {
            t = Mathf.Clamp01(t);
            Vector3 horizontal = Vector3.Lerp(_startPos, _targetPos, t);
            float height = Mathf.Sin(t * Mathf.PI) * Data.flyHeight;
            ctx.Transform.position = horizontal + Vector3.up * height;
        }

        private void SpawnWarning(MonsterContext ctx)
        {
            MonsterGroundWarning.Spawn(
                ctx.Transform.position + ctx.Transform.forward * (Data.hitRadius * 0.5f),
                Data.hitRadius,
                Data.warningDuration,
                new Color(0.8f, 0.2f, 0.2f));
        }

        private void ApplyHit(MonsterContext ctx)
        {
            int damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
            float kbForce = ctx.Stat.knockbackForce;

            if (Data.vfxPrefab != null)
            {
                BossEffectPool.SpawnOneShot(
                    Data.vfxPrefab,
                    ctx.Transform.position + ctx.Transform.forward,
                    ctx.Transform.rotation);
            }

            var hits = Physics.OverlapSphere(ctx.Transform.position, Data.hitRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                    ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                Vector3 dir = (col.transform.position - ctx.Transform.position).normalized;
                if (Vector3.Dot(ctx.Transform.forward, dir) < 0f) continue;

                dir.y = 0.2f;
                player.TakeDamage(damage);
                player.ApplyKnockback(dir.normalized * kbForce, 0.3f);
            }
        }

        private void RestoreAgentTracking(MonsterContext ctx)
        {
            ctx.Agent.updatePosition = _originalUpdatePosition;
            ctx.Agent.updateRotation = _originalUpdateRotation;
        }
    }
}
}
