using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 내려 찍기 패턴.
///
/// 1페이즈 : 보스 중심 360°, 반경 5m, 충격파 속도 10m/s.
/// 2페이즈 : 반경 7m(1.3배), 충격파 속도 15m/s, 지면 파열 이펙트.
///
/// 히트박스 보정: GroundSlam 직후 플레이어 강공격 적중 시 그로기 게이지 크게 감소
/// (감소는 외부 호출 - 기획 확정 후 연결).
/// </summary>
[CreateAssetMenu(fileName = "FGFrontalDefensePatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/GroundSlam")]
public class FGGroundSlamPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animAttack = "SmashAttack";
    [SerializeField] private float  crossFade  = 0.1f;

    [Header("Phase 1 설정")]
    [SerializeField] private float p1_radius            = 5f;
    [SerializeField] private float p1_warningDuration   = 0.5f;
    [SerializeField] private float p1_shockwaveDuration = 0.8f;

    [Header("Phase 2 설정 (1.3x 강화)")]
    [SerializeField] private float p2_radius            = 7f;
    [SerializeField] private float p2_warningDuration   = 0.4f;
    [SerializeField] private float p2_shockwaveDuration = 1.0f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxImpact;

    [Header("쿨다운")]
    [SerializeField] private float patternCooldown = 6f;

    private float _cooldownEndTime = float.MinValue;
    private FGGroundSlamState _state;

    public override void Initialize(BossPatternContext ctx)
    {
        _cooldownEndTime = float.MinValue;
        _state = new FGGroundSlamState(this);
    }

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (Time.time < _cooldownEndTime) return false;
        return ctx.Ctx.Runtime.DistToPlayer <= 6f;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
    internal void StartCooldown() => _cooldownEndTime = Time.time + patternCooldown;

    // ── 내부 상태 ──────────────────────────────────────────────────────

    private sealed class FGGroundSlamState : FullLockState<FGGroundSlamPatternSO>
    {
        private enum Sub { Warning, Shockwave }

        private Sub   _sub;
        private float _timer;
        private float _phaseDuration;
        private float _radius;
        private bool  _isPhase2;
        private bool  _hitApplied;

        public FGGroundSlamState(FGGroundSlamPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;

            var fg = (ctx.Monster as ForestGuardianMonster)?.FGBlackboard;
            _isPhase2 = fg?.IsPhase2 ?? false;
            _radius   = _isPhase2 ? Data.p2_radius : Data.p1_radius;
            _hitApplied = false;

            FacePlayer(ctx);

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animAttack, Data.crossFade);

            float warnDur = _isPhase2 ? Data.p2_warningDuration : Data.p1_warningDuration;
            MonsterGroundWarning.Spawn(ctx.Transform.position, _radius, warnDur,
                new Color(1f, 0.3f, 0f, 0.8f));

            _sub           = Sub.Warning;
            _timer         = 0f;
            _phaseDuration = warnDur;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;
            if (_timer < _phaseDuration) return;
            _timer = 0f;

            switch (_sub)
            {
                case Sub.Warning:
                    ApplyHit(ctx);
                    if (Data.vfxImpact != null)
                        BossEffectPool.SpawnOneShot(Data.vfxImpact,
                            ctx.Transform.position, ctx.Transform.rotation);
                    _sub           = Sub.Shockwave;
                    _phaseDuration = _isPhase2 ? Data.p2_shockwaveDuration : Data.p1_shockwaveDuration;
                    break;

                case Sub.Shockwave:
                    ctx.Monster.ChangeState<PatrolState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx) => Data.StartCooldown();

        private void ApplyHit(MonsterContext ctx)
        {
            if (_hitApplied) return;
            _hitApplied = true;

            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
            float kbForce = ctx.Stat.knockbackForce;

            var hits = Physics.OverlapSphere(ctx.Transform.position, _radius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                Vector3 dir = (col.transform.position - ctx.Transform.position);
                dir.y = 0.2f;
                player.TakeDamage(damage);
                player.ApplyKnockback(dir.normalized * kbForce);
                break;
            }
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
