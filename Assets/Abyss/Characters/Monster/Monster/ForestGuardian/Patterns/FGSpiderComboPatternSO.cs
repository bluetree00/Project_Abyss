using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 돌진 패턴.
///
/// 1페이즈 : 직선 돌진. 사거리 15~20m, 폭 3m, 속도 12m/s.
///           끝난 뒤 1.5초간 중심잡는 모션(딜 타임).
/// 2페이즈 : 유도 돌진. 플레이어 위치를 추적하며 꺾어 들어옴. 속도 18m/s, 폭 4m.
///
/// 히트박스 보정: 이동 속도 비례 데미지 최대 2배.
/// 그로기 보정: 돌진 직후 적중 대상 강공격 시 그로기 감소 트리거 대상.
/// </summary>
[CreateAssetMenu(fileName = "FGSpiderComboPatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/Charge")]
public class FGChargePatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animCharge   = "ChargeTackleStart";
    [SerializeField] private string animRecover  = "ChargeTackleStop";
    [SerializeField] private string animIdle     = "IdleNormal";
    [SerializeField] private float  crossFade    = 0.1f;

    [Header("Phase 1 (직선)")]
    [SerializeField] private float p1_speed          = 12f;
    [SerializeField] private float p1_maxDist        = 18f;
    [SerializeField] private float p1_hitWidth       = 3f;
    [SerializeField] private float p1_recoverDuration = 1.5f;
    [SerializeField] private float p1_warningDuration = 0.5f;

    [Header("Phase 2 (유도)")]
    [SerializeField] private float p2_speed           = 18f;
    [SerializeField] private float p2_maxDist         = 22f;
    [SerializeField] private float p2_hitWidth        = 4f;
    [SerializeField] private float p2_recoverDuration = 0.8f;
    [SerializeField] private float p2_warningDuration = 0.35f;
    [SerializeField] private float p2_trackDuration   = 0.4f;   // 유도 지속 시간(이 이후는 직선)

    [Header("VFX")]
    [SerializeField] private GameObject vfxImpact;

    [Header("쿨다운")]
    [SerializeField] private float patternCooldown = 8f;

    private float _cooldownEndTime = float.MinValue;
    private FGChargeState _state;

    public override void Initialize(BossPatternContext ctx)
    {
        _cooldownEndTime = float.MinValue;
        _state = new FGChargeState(this);
    }

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (Time.time < _cooldownEndTime) return false;
        return ctx.Ctx.Runtime.DistToPlayer >= 5f;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
    internal void StartCooldown() => _cooldownEndTime = Time.time + patternCooldown;

    // ── 내부 상태 ─────────────────────────────────────────────────────

    private sealed class FGChargeState : FullLockState<FGChargePatternSO>
    {
        private enum Sub { Warning, Charging, Recover }

        private Sub     _sub;
        private float   _timer;
        private float   _phaseDuration;
        private Vector3 _chargeDir;
        private Vector3 _startPos;
        private float   _distTravelled;
        private float   _speed;
        private float   _maxDist;
        private float   _hitWidth;
        private bool    _isPhase2;
        private bool    _hitApplied;

        private bool    _originalUpdatePosition;
        private bool    _originalUpdateRotation;

        public FGChargeState(FGChargePatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            var fg = (ctx.Monster as ForestGuardianMonster)?.FGBlackboard;
            _isPhase2 = fg?.IsPhase2 ?? false;
            _speed    = _isPhase2 ? Data.p2_speed    : Data.p1_speed;
            _maxDist  = _isPhase2 ? Data.p2_maxDist  : Data.p1_maxDist;
            _hitWidth = _isPhase2 ? Data.p2_hitWidth : Data.p1_hitWidth;

            _hitApplied   = false;
            _distTravelled = 0f;
            _startPos     = ctx.Transform.position;

            // 초기 방향: 플레이어 방향
            if (ctx.Runtime.PlayerTarget != null)
            {
                Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
                dir.y = 0f;
                _chargeDir = dir.sqrMagnitude > 0.001f ? dir.normalized : ctx.Transform.forward;
            }
            else
            {
                _chargeDir = ctx.Transform.forward;
            }

            // 돌진 중 NavAgent 수동 제어
            _originalUpdatePosition = ctx.Agent.updatePosition;
            _originalUpdateRotation = ctx.Agent.updateRotation;
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;
            ctx.Agent.updatePosition = false;
            ctx.Agent.updateRotation = false;

            FaceDir(ctx, _chargeDir);

            // 경고 장판
            float warnDur = _isPhase2 ? Data.p2_warningDuration : Data.p1_warningDuration;
            SpawnChargeWarning(ctx, _chargeDir, _maxDist, _hitWidth, warnDur);

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animCharge, Data.crossFade);

            _sub           = Sub.Warning;
            _timer         = 0f;
            _phaseDuration = warnDur;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            switch (_sub)
            {
                // ── 경고 대기 ─────────────────────────────────────────
                case Sub.Warning:
                    if (_timer < _phaseDuration) return;
                    _sub   = Sub.Charging;
                    _timer = 0f;
                    break;

                // ── 돌진 이동 ─────────────────────────────────────────
                case Sub.Charging:
                {
                    // Phase2 유도: trackDuration 이내면 방향을 플레이어 쪽으로 보정
                    if (_isPhase2 && _timer <= Data.p2_trackDuration && ctx.Runtime.PlayerTarget != null)
                    {
                        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
                        toPlayer.y = 0f;
                        if (toPlayer.sqrMagnitude > 0.01f)
                        {
                            _chargeDir = Vector3.RotateTowards(
                                _chargeDir, toPlayer.normalized,
                                180f * Mathf.Deg2Rad * Time.deltaTime, 0f);
                            FaceDir(ctx, _chargeDir);
                        }
                    }

                    float step = _speed * Time.deltaTime;
                    ctx.Transform.position += _chargeDir * step;
                    _distTravelled += step;

                    // 히트 판정 (1회)
                    if (!_hitApplied)
                        TryApplyHit(ctx);

                    // 최대 거리 도달 or 벽 충돌
                    if (_distTravelled >= _maxDist)
                        StartRecover(ctx);

                    break;
                }

                // ── 경직 회복 ─────────────────────────────────────────
                case Sub.Recover:
                    if (_timer < _phaseDuration) return;
                    RestoreAgent(ctx);
                    ctx.Agent.Warp(ctx.Transform.position);
                    ctx.Monster.ChangeState<PatrolState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            RestoreAgent(ctx);
            Data.StartCooldown();
        }

        private void StartRecover(MonsterContext ctx)
        {
            float recoverDur = _isPhase2 ? Data.p2_recoverDuration : Data.p1_recoverDuration;
            if (Data.vfxImpact != null)
                BossEffectPool.SpawnOneShot(Data.vfxImpact,
                    ctx.Transform.position, ctx.Transform.rotation);

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animRecover, Data.crossFade);

            _sub           = Sub.Recover;
            _timer         = 0f;
            _phaseDuration = recoverDur;
        }

        private void TryApplyHit(MonsterContext ctx)
        {
            // BoxCast로 직선 폭 판정
            Vector3 center = ctx.Transform.position + Vector3.up * 0.8f;
            Vector3 halfExtents = new Vector3(_hitWidth * 0.5f, 0.8f, 0.5f);
            var hits = Physics.OverlapBox(center, halfExtents,
                Quaternion.LookRotation(_chargeDir));

            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                _hitApplied = true;
                int damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier
                                   * GetSpeedDamageMult(ctx));
                Vector3 dir = _chargeDir;
                dir.y = 0.2f;
                player.TakeDamage(damage);
                player.ApplyKnockback(dir.normalized * ctx.Stat.knockbackForce);
                break;
            }
        }

        private float GetSpeedDamageMult(MonsterContext ctx)
        {
            float baseSpeed = ctx.Stat.moveSpeed;
            if (baseSpeed <= 0f) return 1f;
            return Mathf.Clamp(_speed / baseSpeed, 1f, 2f);
        }

        private static void SpawnChargeWarning(MonsterContext ctx, Vector3 dir, float dist, float width, float duration)
        {
            Vector3 origin = ctx.Transform.position;
            origin.y = ctx.Transform.position.y;
            MonsterGroundWarning.SpawnRect(origin, dir, width, dist, duration, new Color(1f, 0.5f, 0.1f, 0.8f));
        }

        private static void FaceDir(MonsterContext ctx, Vector3 dir)
        {
            if (dir.sqrMagnitude > 0.001f)
                ctx.Transform.rotation = Quaternion.LookRotation(dir);
        }

        private void RestoreAgent(MonsterContext ctx)
        {
            ctx.Agent.updatePosition = _originalUpdatePosition;
            ctx.Agent.updateRotation = _originalUpdateRotation;
        }
    }
}
}
