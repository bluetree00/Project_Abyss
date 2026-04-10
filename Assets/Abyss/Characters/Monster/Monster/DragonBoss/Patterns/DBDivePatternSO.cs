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
    [SerializeField] private float diveDuration = 0.55f;   // 빠른 돌진
    [SerializeField] private float hitBoxWidth = 2.5f;
    [SerializeField] private float hitBoxLength = 2f;
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

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animName, Data.crossFade);

            // 돌진 경로 전체 직사각형 경고
            float diveLen = Vector3.Distance(_startPos, _targetPos);
            MonsterGroundWarning.SpawnRect(
                _startPos,
                _diveDir,
                Data.hitBoxWidth * 2f,
                Mathf.Max(2f, diveLen + 2f),
                Data.warningDuration,
                new Color(1f, 0.5f, 0f));

            _originalUpdatePosition = ctx.Agent.updatePosition;
            _originalUpdateRotation = ctx.Agent.updateRotation;
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

                    _timer = 0f;
                    ctx.Agent.updatePosition = false;
                    ctx.Agent.updateRotation = false;
                    _phase         = 1;
                    _phaseDuration = Data.diveDuration;
                    break;

                case 1:
                    UpdateDivePose(ctx);

                    if (!_hitDealt && _timer >= Data.diveDuration * 0.85f)
                        CheckDiveHit(ctx);

                    if (!_crashResolved)
                        CheckCrash(ctx);

                    if (_timer < _phaseDuration) return;

                    _timer         = 0f;
                    _phase         = 2;
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

        // 완전 수평 돌진 — arc 없음
        private void UpdateDivePose(MonsterContext ctx)
        {
            float t     = Mathf.Clamp01(_timer / Mathf.Max(0.01f, Data.diveDuration));
            float easeT = 1f - (1f - t) * (1f - t); // ease-out: 초반 빠르게 감속
            ctx.Transform.position = Vector3.Lerp(_startPos, _targetPos, easeT);

            if (_diveDir.sqrMagnitude > 0.001f)
                ctx.Transform.rotation = Quaternion.LookRotation(_diveDir);
        }

        private void CheckDiveHit(MonsterContext ctx)
        {
            // 현재 위치 기준 전방 박스로 플레이어 판정
            Vector3 center = ctx.Transform.position + _diveDir * Data.hitBoxLength;
            var hits = Physics.OverlapBox(
                center,
                new Vector3(Data.hitBoxWidth, 1f, Data.hitBoxLength),
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
                _hitDealt      = true;
                _crashResolved = true;
                break;
            }
        }

        // 벽 충돌 시에만 보스 자기 데미지 (플레이어 충돌은 제외)
        private void CheckCrash(MonsterContext ctx)
        {
            float dist = Vector3.Distance(_startPos, _targetPos);
            if (dist <= 0.5f) return;

            // 현재 위치 기준 전방 SphereCast
            var ray = new Ray(ctx.Transform.position + Vector3.up * 0.5f, _diveDir);
            if (!Physics.SphereCast(ray, 0.6f, out RaycastHit hit, 2f)) return;

            var player = hit.collider.GetComponent<PlayerController>()
                ?? hit.collider.GetComponentInParent<PlayerController>();
            if (player != null) return; // 플레이어 충돌은 무시

            // 벽/맵 오브젝트에 박힌 경우 → 자기 데미지
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
