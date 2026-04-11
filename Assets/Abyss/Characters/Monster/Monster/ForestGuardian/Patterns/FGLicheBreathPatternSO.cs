using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 잡아 던지기 패턴 (가드 불가 특수).
///
/// 1페이즈 : 양손 벌리는 전조 모션이 매우 큼. 사정거리 2m, 전방 45°.
///           지속 2.5초. 잡기 → 좌우 내려찍기 → 앞으로 투척.
/// 2페이즈 : 발동 속도 대폭 상향. 사정거리 2m, 전방 60°. 지속 2.0초.
///           치명적인 데미지.
/// </summary>
[CreateAssetMenu(fileName = "FGLicheBreathPatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/GrabThrow")]
public class FGGrabThrowPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animWindup   = "GrabAndThrowAttack";
    [SerializeField] private string animGrab     = "GrabBiteShakeSpit";
    [SerializeField] private float  crossFade    = 0.1f;

    [Header("Phase 1 설정")]
    [SerializeField] private float p1_grabRange       = 2f;
    [SerializeField] private float p1_grabAngle       = 45f;
    [SerializeField] private float p1_windupDuration  = 1.0f;
    [SerializeField] private float p1_grabDuration    = 2.5f;
    [SerializeField] private float p1_throwForce      = 15f;
    [SerializeField] private float p1_damage          = 1.5f;  // attackPower 배수

    [Header("Phase 2 설정")]
    [SerializeField] private float p2_grabRange       = 2f;
    [SerializeField] private float p2_grabAngle       = 60f;
    [SerializeField] private float p2_windupDuration  = 0.3f;  // 발동 속도 대폭 상향
    [SerializeField] private float p2_grabDuration    = 2.0f;
    [SerializeField] private float p2_throwForce      = 20f;
    [SerializeField] private float p2_damage          = 2.5f;  // 치명적

    [Header("쿨다운")]
    [SerializeField] private float patternCooldown = 12f;

    private float _cooldownEndTime = float.MinValue;
    private FGGrabThrowState _state;

    public override void Initialize(BossPatternContext ctx)
    {
        _cooldownEndTime = float.MinValue;
        _state = new FGGrabThrowState(this);
    }

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (Time.time < _cooldownEndTime) return false;
        return ctx.Ctx.Runtime.DistToPlayer <= 3f;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
    internal void StartCooldown() => _cooldownEndTime = Time.time + patternCooldown;

    // ── 내부 상태 ─────────────────────────────────────────────────────

    private sealed class FGGrabThrowState : FullLockState<FGGrabThrowPatternSO>
    {
        private enum Sub { Windup, GrabActive, Throw }

        private Sub   _sub;
        private float _timer;
        private float _phaseDuration;
        private bool  _isPhase2;
        private float _grabRange;
        private float _grabAngle;
        private float _throwForce;
        private float _damageMult;
        private bool  _grabbed;
        private PlayerController _grabbedPlayer;
        private float _grabbedPlayerMoveScale = 1f;
        private bool  _leftSlamApplied;
        private bool  _rightSlamApplied;

        public FGGrabThrowState(FGGrabThrowPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;

            var fg = (ctx.Monster as ForestGuardianMonster)?.FGBlackboard;
            _isPhase2   = fg?.IsPhase2 ?? false;
            _grabRange  = _isPhase2 ? Data.p2_grabRange  : Data.p1_grabRange;
            _grabAngle  = _isPhase2 ? Data.p2_grabAngle  : Data.p1_grabAngle;
            _throwForce = _isPhase2 ? Data.p2_throwForce : Data.p1_throwForce;
            _damageMult = _isPhase2 ? Data.p2_damage     : Data.p1_damage;
            _grabbed    = false;
            _grabbedPlayer = null;
            _grabbedPlayerMoveScale = 1f;
            _leftSlamApplied = false;
            _rightSlamApplied = false;

            FacePlayer(ctx);

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animWindup, Data.crossFade);

            // 경고: 양손 넓이 암시
            float warnRadius = _grabRange * 0.7f;
            MonsterGroundWarning.Spawn(
                ctx.Transform.position + ctx.Transform.forward * _grabRange,
                warnRadius,
                _isPhase2 ? Data.p2_windupDuration : Data.p1_windupDuration,
                new Color(0.9f, 0.1f, 0.1f, 0.9f));

            _sub           = Sub.Windup;
            _timer         = 0f;
            _phaseDuration = _isPhase2 ? Data.p2_windupDuration : Data.p1_windupDuration;
        }

        public override void Update(MonsterContext ctx)
        {
            if (_grabbedPlayer != null)
                UpdateGrabbedPlayerPose(ctx);

            _timer += Time.deltaTime;
            if (_timer < _phaseDuration) return;
            _timer = 0f;

            switch (_sub)
            {
                case Sub.Windup:
                    // 잡기 판정
                    TryGrab(ctx);
                    if (ctx.Animator != null)
                        ctx.Animator.CrossFade(Data.animGrab, Data.crossFade);
                    _sub           = Sub.GrabActive;
                    _phaseDuration = _isPhase2 ? Data.p2_grabDuration : Data.p1_grabDuration;
                    break;

                case Sub.GrabActive:
                    // 던지기 데미지
                    if (_grabbed)
                        ApplyThrowDamage(ctx);
                    else
                        ReleaseGrabbedPlayer();
                    _sub           = Sub.Throw;
                    _phaseDuration = 0.3f;
                    break;

                case Sub.Throw:
                    ctx.Monster.ChangeState<PatrolState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            ReleaseGrabbedPlayer();
            Data.StartCooldown();
        }

        private void TryGrab(MonsterContext ctx)
        {
            float halfAngle = _grabAngle * 0.5f * Mathf.Deg2Rad;
            var hits = Physics.OverlapSphere(ctx.Transform.position, _grabRange);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                Vector3 toTarget = (col.transform.position - ctx.Transform.position).normalized;
                float angle = Mathf.Acos(Mathf.Clamp(
                    Vector3.Dot(ctx.Transform.forward, toTarget), -1f, 1f));
                if (angle > halfAngle) continue;

                // 초기 잡기 데미지
                int dmg = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * _damageMult * 0.5f);
                player.TakeDamage(dmg);
                _grabbedPlayer = player;
                _grabbedPlayerMoveScale = player.MoveScale;
                _grabbedPlayer.CancelActState();
                _grabbedPlayer.SetMoveScale(0f);
                _grabbedPlayer.StopHorizontalMovement();
                UpdateGrabbedPlayerPose(ctx);
                _grabbed = true;
                break;
            }
        }

        private void ApplyThrowDamage(MonsterContext ctx)
        {
            if (_grabbedPlayer == null) return;

            int dmg = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * _damageMult);
            var grabbedPlayer = _grabbedPlayer;
            float minThrowDistance = GetMinimumThrowDistance(ctx);

            grabbedPlayer.TakeDamage(dmg);
            ReleaseGrabbedPlayer();

            Vector3 releasePos = ctx.Transform.position + ctx.Transform.forward * Mathf.Max(1.4f, minThrowDistance * 0.3f);
            releasePos.y = ctx.Transform.position.y + 0.9f;

            if (grabbedPlayer.Rigid != null)
                grabbedPlayer.Rigid.position = releasePos;
            else
                grabbedPlayer.transform.position = releasePos;

            Vector3 throwVelocity = ctx.Transform.forward * Mathf.Max(_throwForce * 2.4f, minThrowDistance * 4.5f)
                                  + Vector3.up * Mathf.Max(4.5f, minThrowDistance * 0.9f);

            if (grabbedPlayer.Rigid != null)
                grabbedPlayer.Rigid.linearVelocity = throwVelocity;

            grabbedPlayer.ApplyKnockback(Vector3.zero, 0.9f);
        }

        private void UpdateGrabbedPlayerPose(MonsterContext ctx)
        {
            if (_grabbedPlayer == null) return;

            Vector3 holdPos = EvaluateGrabPosition(ctx);

            _grabbedPlayer.SetMoveScale(0f);
            _grabbedPlayer.StopHorizontalMovement();
            _grabbedPlayer.CancelActState();

            if (_grabbedPlayer.Rigid != null)
            {
                _grabbedPlayer.Rigid.linearVelocity = Vector3.zero;
                _grabbedPlayer.Rigid.position = holdPos;
            }
            else
            {
                _grabbedPlayer.transform.position = holdPos;
            }
        }

        private Vector3 EvaluateGrabPosition(MonsterContext ctx)
        {
            Vector3 basePos = ctx.Transform.position;
            Vector3 forward = ctx.Transform.forward.normalized;
            Vector3 right = ctx.Transform.right.normalized;

            Vector3 centerHold = basePos + forward * Mathf.Max(0.8f, _grabRange * 0.55f) + Vector3.up * 1.1f;
            Vector3 leftSlam   = basePos + forward * 0.35f - right * 0.95f + Vector3.up * 0.2f;
            Vector3 rightSlam  = basePos + forward * 0.35f + right * 0.95f + Vector3.up * 0.2f;
            Vector3 preThrow   = basePos + forward * 0.85f + Vector3.up * 0.85f;

            if (_sub != Sub.GrabActive || _phaseDuration <= 0f)
                return centerHold;

            float t = Mathf.Clamp01(_timer / _phaseDuration);
            ApplySlamDamage(ctx, t);

            if (t < 0.33f)
                return LerpArc(centerHold, leftSlam, t / 0.33f, 0.65f);

            if (t < 0.66f)
                return LerpArc(leftSlam, rightSlam, (t - 0.33f) / 0.33f, 0.45f);

            return Vector3.Lerp(rightSlam, preThrow, (t - 0.66f) / 0.34f);
        }

        private void ApplySlamDamage(MonsterContext ctx, float normalizedTime)
        {
            if (_grabbedPlayer == null) return;

            int slamDamage = Mathf.Max(1, Mathf.RoundToInt(
                ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * _damageMult * 0.25f));

            if (!_leftSlamApplied && normalizedTime >= 0.33f)
            {
                _leftSlamApplied = true;
                _grabbedPlayer.TakeDamage(slamDamage);
            }

            if (!_rightSlamApplied && normalizedTime >= 0.66f)
            {
                _rightSlamApplied = true;
                _grabbedPlayer.TakeDamage(slamDamage);
            }
        }

        private static Vector3 LerpArc(Vector3 from, Vector3 to, float t, float arcHeight)
        {
            Vector3 pos = Vector3.Lerp(from, to, Mathf.Clamp01(t));
            pos.y += Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * arcHeight;
            return pos;
        }

        private void ReleaseGrabbedPlayer()
        {
            if (_grabbedPlayer == null) return;

            _grabbedPlayer.SetMoveScale(_grabbedPlayerMoveScale);
            _grabbed = false;
            _grabbedPlayer = null;
        }

        private static float GetMinimumThrowDistance(MonsterContext ctx)
        {
            if (ctx.Config is BossConfigSO bossConfig)
                return Mathf.Max(6f, bossConfig.condDistFar + 1f);

            return 6f;
        }

        private static void FacePlayer(MonsterContext ctx)
        {
            if (ctx.Runtime.PlayerTarget == null) return;
            Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                ctx.Transform.rotation = Quaternion.LookRotation(dir.normalized);
        }
    }
}
}
