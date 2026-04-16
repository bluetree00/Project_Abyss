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
    [SerializeField] private float warningDuration = 1.0f;
    [SerializeField] private float diveDuration = 1.0f;
    [SerializeField] private float speedMultiplier = 3f;
    [SerializeField] private float hitBoxWidth = 2.5f;
    [SerializeField] private float hitBoxLength = 2f;
    [SerializeField] private float selfDamageRatioOnCrash = 0.03f;
    [SerializeField] private float maxDiveDistance = 20f;
    [SerializeField] private float recoverDuration = 0.8f;
    [SerializeField] private float wallProbeRadius = 0.7f;

    [Header("Cooldown")]
    [SerializeField] private float patternCooldown = 10f;

    private float _cooldownEndTime = -999f;
    private DiveState _state;

    public override void Initialize(BossPatternContext ctx) { _cooldownEndTime = float.MinValue; _state = new DiveState(this); }

    public override bool CanExecute(BossPatternContext ctx) => Time.time >= _cooldownEndTime;

    public override SpecialStateBase GetRuntimeState() => _state;

    internal void StartCooldown() => _cooldownEndTime = Time.time + patternCooldown;

    private sealed class DiveState : FullLockState<DBDivePatternSO>
    {
        private float _timer;
        private int _phase;
        private float _phaseDuration;
        private Vector3 _diveDir;
        private Vector3 _startPos;
        private Vector3 _targetPos;
        private float _speed;
        private float _distTravelled;
        private float _maxDist;
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

            // 지면 y에 스냅 — 이전 패턴이 공중에서 끝났더라도 수평 돌진 보장
            float groundY  = DragonBossVisualHelper.GetGroundY(ctx.Transform.position);
            _startPos       = ctx.Transform.position;
            _startPos.y     = groundY;
            ctx.Transform.position = _startPos;

            _targetPos = ctx.Runtime.PlayerTarget != null
                ? ctx.Runtime.PlayerTarget.position
                : ctx.Transform.position + ctx.Transform.forward * Data.hitBoxLength * 4f;
            _targetPos.y = groundY;

            _diveDir = _targetPos - _startPos;
            _diveDir.y = 0f;
            if (_diveDir.sqrMagnitude <= 0.001f)
                _diveDir = ctx.Transform.forward;
            else
                _diveDir.Normalize();

            float targetDist = Vector3.Distance(_startPos, _targetPos);
            _speed = Mathf.Max(0.01f, ctx.Stat.moveSpeed * Data.speedMultiplier);
            _maxDist = Mathf.Clamp(targetDist + Data.hitBoxLength * 2f, 6f, Data.maxDiveDistance);
            _distTravelled = 0f;

            // Enter 시점에서 즉시 off — Phase 0 동안 NavMesh가 transform을 오버라이드하지 않도록
            _originalUpdatePosition = ctx.Agent.updatePosition;
            _originalUpdateRotation = ctx.Agent.updateRotation;
            ctx.Agent.updatePosition = false;
            ctx.Agent.updateRotation = false;

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animName, Data.crossFade);

            // 돌진 경로 전체 직사각형 경고
            float diveLen = Vector3.Distance(_startPos, _targetPos);
            MonsterGroundWarning.SpawnRect(
                _startPos,
                _diveDir,
                Data.hitBoxWidth * 2f,
                Mathf.Max(6f, Mathf.Min(Data.maxDiveDistance, diveLen + 2f)),
                Data.warningDuration,
                new Color(1f, 0.5f, 0f));

            _hitDealt      = false;
            _crashResolved = false;
            _phase         = 0;
            _timer         = 0f;
            _phaseDuration = Data.warningDuration;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            switch (_phase)
            {
                case 0:
                    if (_timer < _phaseDuration) return;

                    _timer         = 0f;
                    _phase         = 1;
                    _phaseDuration = Data.diveDuration;
                    break;

                case 1:
                    if (TryResolveWallCrash(ctx))
                    {
                        StartRecover();
                        return;
                    }

                    UpdateDivePose(ctx);

                    if (!_hitDealt)
                        CheckDiveHit(ctx);

                    if (_distTravelled < _maxDist && _timer < _phaseDuration) return;

                    StartRecover();
                    break;

                case 2:
                    if (_timer < _phaseDuration) return;

                    RestoreAgentTracking(ctx);
                    ctx.Agent.Warp(ctx.Transform.position);
                    ctx.Agent.ResetPath();
                    ctx.Monster.ChangeState<PatrolState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            RestoreAgentTracking(ctx);
            Data.StartCooldown();
        }

        private void StartRecover()
        {
            _timer         = 0f;
            _phase         = 2;
            _phaseDuration = Data.recoverDuration;
        }

        private void UpdateDivePose(MonsterContext ctx)
        {
            float step = _speed * Time.deltaTime;
            ctx.Transform.position += _diveDir * step;
            _distTravelled += step;

            if (_diveDir.sqrMagnitude > 0.001f)
                ctx.Transform.rotation = Quaternion.LookRotation(_diveDir);
        }

        private void CheckDiveHit(MonsterContext ctx)
        {
            // 현재 위치 기준 전방 박스로 플레이어 판정
            Vector3 center = ctx.Transform.position + _diveDir * Data.hitBoxLength;
            var hits = Physics.OverlapBox(
                center,
                new Vector3(Data.hitBoxWidth * 0.5f, 1f, Data.hitBoxLength),
                ctx.Transform.rotation);

            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                    ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                int   damage   = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
                float kbForce  = ctx.Stat.knockbackForce;

                if (Data.vfxPrefab != null)
                    BossEffectPool.SpawnOneShot(Data.vfxPrefab, ctx.Transform.position, ctx.Transform.rotation);

                ApplyElementalDamage(ctx, player, damage, kbForce, _diveDir);
                _hitDealt = true;
                break;
            }
        }

        private bool TryResolveWallCrash(MonsterContext ctx)
        {
            if (_crashResolved) return true;

            float probeDistance = Mathf.Max(0.8f, _speed * Time.deltaTime + Data.hitBoxLength);

            var ray = new Ray(ctx.Transform.position + Vector3.up * 0.5f, _diveDir);
            if (!Physics.SphereCast(ray, Data.wallProbeRadius, out RaycastHit hit, probeDistance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;

            if (hit.collider.transform == ctx.Transform ||
                hit.collider.transform.IsChildOf(ctx.Transform))
                return false;

            var player = hit.collider.GetComponent<PlayerController>()
                ?? hit.collider.GetComponentInParent<PlayerController>();
            if (player != null) return false;

            _crashResolved = true;
            int currentHp = Mathf.Max(1, ctx.Runtime.CurrentHp);
            float rawSelfDamage = Mathf.Ceil(currentHp * Data.selfDamageRatioOnCrash + ctx.Stat.defense);
            ctx.Monster.TakeDamage(rawSelfDamage, ctx.Monster.gameObject, 0f);
            return true;
        }

        private static void ApplyElementalDamage(
            MonsterContext ctx,
            PlayerController player,
            int damage,
            float kbForce,
            Vector3 dir)
        {
            var dragon  = ctx.Monster as DragonBossMonster;
            var element = dragon?.DBBlackboard?.CurrentElement
                ?? DragonBossBlackboard.DragonElement.Ice;

            player.TakeDamage(damage);
            switch (element)
            {
                case DragonBossBlackboard.DragonElement.Ice:
                    player.ApplyKnockback(Vector3.zero, 2f);
                    break;
                case DragonBossBlackboard.DragonElement.Thunder:
                    player.ApplyKnockback(Vector3.zero, 0.5f);
                    break;
                default: // Fire
                    player.ApplySlow(0.4f, 2f);
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
