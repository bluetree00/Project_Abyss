using UnityEngine;

namespace Abyss.Monster
{
[CreateAssetMenu(fileName = "DBDivePatternSO",
                 menuName = "Abyss/Boss/DragonBoss/Dive")]
public class DBDivePatternSO : BossPatternSO
{
    [Header("Animation")]
    [SerializeField] private string animName = "Attack02";
    [SerializeField] private float crossFade = 0.1f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxPrefab;

    [Header("Dive")]
    [SerializeField] private float warningDuration = 0.8f;
    [SerializeField] private float diveDuration = 1.1f;
    [SerializeField] private float jumpHeight = 6f;
    [SerializeField] private float hitBoxWidth = 2f;
    [SerializeField] private float hitBoxLength = 1f;
    [SerializeField] private float selfDamageRatioOnCrash = 0.08f;

    private DiveState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new DiveState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    private sealed class DiveState : FullLockState<DBDivePatternSO>
    {
        private float _timer;
        private int _phase;
        private float _phaseDuration;
        private Vector3 _diveDir;
        private Vector3 _startPos;
        private Vector3 _targetPos;
        private bool _hitDealt;
        private bool _crashResolved;
        private bool _originalUpdatePosition;
        private bool _originalUpdateRotation;

        public DiveState(DBDivePatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            if (ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;

            _startPos = ctx.Transform.position;
            _targetPos = ctx.Runtime.PlayerTarget != null
                ? ctx.Runtime.PlayerTarget.position
                : ctx.Transform.position + ctx.Transform.forward * Data.hitBoxLength * 4f;
            _targetPos.y = _startPos.y;

            _diveDir = _targetPos - _startPos;
            _diveDir.y = 0f;
            if (_diveDir.sqrMagnitude <= 0.001f)
                _diveDir = ctx.Transform.forward;
            else
                _diveDir.Normalize();

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animName, Data.crossFade);

            MonsterGroundWarning.SpawnGrid(
                _targetPos,
                _diveDir,
                MonsterGroundWarning.GridShape.Front2,
                Data.warningDuration,
                new Color(1f, 0.5f, 0f));

            _originalUpdatePosition = ctx.Agent.updatePosition;
            _originalUpdateRotation = ctx.Agent.updateRotation;
            _hitDealt = false;
            _crashResolved = false;
            _phase = 0;
            _timer = 0f;
            _phaseDuration = Data.warningDuration;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            switch (_phase)
            {
                case 0:
                    if (_timer < _phaseDuration) return;

                    _timer = 0f;
                    ctx.Agent.updatePosition = false;
                    ctx.Agent.updateRotation = false;
                    _phase = 1;
                    _phaseDuration = Data.diveDuration;
                    break;

                case 1:
                    UpdateDivePose(ctx);

                    if (!_hitDealt && _timer >= Data.diveDuration * 0.85f)
                        CheckDiveHit(ctx);

                    if (!_crashResolved)
                        CheckCrash(ctx);

                    if (_timer < _phaseDuration) return;

                    _timer = 0f;
                    _phase = 2;
                    _phaseDuration = 0.25f;
                    break;

                case 2:
                    if (_timer < _phaseDuration) return;

                    RestoreAgentTracking(ctx);
                    ctx.Agent.Warp(_targetPos);
                    ctx.Agent.ResetPath();
                    ctx.Monster.ChangeState<PatrolState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            RestoreAgentTracking(ctx);
        }

        private void UpdateDivePose(MonsterContext ctx)
        {
            float t = Mathf.Clamp01(_timer / Mathf.Max(0.01f, Data.diveDuration));
            Vector3 horizontal = Vector3.Lerp(_startPos, _targetPos, t);
            float height = Mathf.Sin(t * Mathf.PI) * Data.jumpHeight;
            ctx.Transform.position = horizontal + Vector3.up * height;

            if (_diveDir.sqrMagnitude > 0.001f)
                ctx.Transform.rotation = Quaternion.LookRotation(_diveDir);
        }

        private void CheckDiveHit(MonsterContext ctx)
        {
            Vector3 center = _targetPos + _diveDir * Data.hitBoxLength;
            var hits = Physics.OverlapBox(
                center,
                new Vector3(Data.hitBoxWidth, 1f, Data.hitBoxLength),
                ctx.Transform.rotation);

            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                    ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                int damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
                float kbForce = ctx.Stat.knockbackForce;

                if (Data.vfxPrefab != null)
                {
                    BossEffectPool.SpawnOneShot(
                        Data.vfxPrefab,
                        _targetPos,
                        ctx.Transform.rotation);
                }

                ApplyElementalDamage(ctx, player, damage, kbForce, _diveDir);
                _hitDealt = true;
                _crashResolved = true;
                break;
            }
        }

        private void CheckCrash(MonsterContext ctx)
        {
            float dist = Vector3.Distance(_startPos, _targetPos);
            if (dist <= 0.5f) return;

            var ray = new Ray(_startPos + Vector3.up * 0.5f, _diveDir);
            if (!Physics.SphereCast(ray, 0.6f, out RaycastHit hit, dist)) return;

            var player = hit.collider.GetComponent<PlayerController>()
                ?? hit.collider.GetComponentInParent<PlayerController>();
            if (player != null) return;

            _crashResolved = true;
            int selfDamage = Mathf.Max(1, Mathf.RoundToInt(ctx.Stat.maxHp * Data.selfDamageRatioOnCrash));
            ctx.Monster.TakeDamage(selfDamage, ctx.Monster.gameObject, 0f);
        }

        private static void ApplyElementalDamage(
            MonsterContext ctx,
            PlayerController player,
            int damage,
            float kbForce,
            Vector3 dir)
        {
            var dragon = ctx.Monster as DragonBossMonster;
            var element = dragon?.DBBlackboard?.CurrentElement
                ?? DragonBossBlackboard.DragonElement.Ice;

            player.TakeDamage(damage);
            switch (element)
            {
                case DragonBossBlackboard.DragonElement.Ice:
                    player.ApplyKnockback(Vector3.zero, 2f); // 빙결 2초
                    break;
                case DragonBossBlackboard.DragonElement.Thunder:
                    player.ApplyKnockback(Vector3.zero, 0.5f); // 그로기 0.5초
                    break;
                default: // Fire
                    player.ApplySlow(0.4f, 2f); // 이동속도 감소 2초
                    break;
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
