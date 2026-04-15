using UnityEngine;

namespace Abyss.Monster
{
[CreateAssetMenu(fileName = "DBBitePatternSO",
                 menuName = "Abyss/Boss/DragonBoss/Bite")]
public class DBBitePatternSO : BossPatternSO
{
    [Header("Animation")]
    [SerializeField] private string animName = "Attack02";
    [SerializeField] private float crossFade = 0.1f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxPrefab;

    [Header("Attack")]
    [SerializeField] private float flyDuration = 0.35f;
    [SerializeField] private float flyHeight = 3f;
    [SerializeField] private float warningDuration = 0.2f;
    [SerializeField] private float hitRadius = 2.5f;
    [SerializeField] private float hitInterval = 0.25f;
    [SerializeField] private int hitCount = 3;

    private BiteState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new BiteState(this);

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.DistToPlayer <= 3f;

    public override SpecialStateBase GetRuntimeState() => _state;

    private sealed class BiteState : FullLockState<DBBitePatternSO>
    {
        private float _timer;
        private int _phase;
        private int _hitIndex;
        private Vector3 _startPos;
        private Vector3 _targetPos;
        private bool _originalUpdatePosition;
        private bool _originalUpdateRotation;

        public BiteState(DBBitePatternSO data) : base(data) { }

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

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animName, Data.crossFade);

            _originalUpdatePosition = ctx.Agent.updatePosition;
            _originalUpdateRotation = ctx.Agent.updateRotation;
            _phase = 0;
            _timer = 0f;
            _hitIndex = 0;
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
                    if (_timer < Data.flyDuration) return;

                    ctx.Transform.position = _targetPos + Vector3.up * Data.flyHeight;
                    SpawnWarning(ctx);
                    _phase = 1;
                    _timer = 0f;
                    break;

                case 1:
                    if (_timer < Data.warningDuration) return;
                    ApplyHit(ctx);
                    _hitIndex++;
                    _timer = 0f;

                    if (_hitIndex < Data.hitCount)
                    {
                        SpawnWarning(ctx);
                    }
                    else
                    {
                        _phase = 2;
                    }
                    break;

                case 2:
                    if (_timer < Data.hitInterval) return;
                    ctx.Transform.position = _targetPos;
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
            float height = Mathf.Sin(t * Mathf.PI * 0.5f) * Data.flyHeight;
            ctx.Transform.position = horizontal + Vector3.up * height;
        }

        private void SpawnWarning(MonsterContext ctx)
        {
            MonsterGroundWarning.Spawn(
                _targetPos,
                Data.hitRadius,
                Data.warningDuration,
                new Color(0.9f, 0.3f, 0.1f));
        }

        private void ApplyHit(MonsterContext ctx)
        {
            int damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
            float kbForce = ctx.Stat.knockbackForce;

            if (Data.vfxPrefab != null)
            {
                BossEffectPool.SpawnOneShot(
                    Data.vfxPrefab,
                    _targetPos,
                    ctx.Transform.rotation);
            }

            var hits = Physics.OverlapSphere(_targetPos, Data.hitRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                    ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                Vector3 dir = (col.transform.position - _targetPos).normalized;
                dir.y = 0.2f;
                player.TakeDamage(damage);
                player.ApplyKnockback(dir.normalized * kbForce, 0.25f);
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
