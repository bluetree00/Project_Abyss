using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 펀치 패턴.
///
/// 1페이즈 : 정박자 — 경고→타격, 전방 60°, 사정거리 3.5m, 지속 0.3초.
///           플레이어가 맞으면 크게 뒤로 밀려남(넉백 큰).
/// 2페이즈 : 엇박자 — 팔을 들고 랜덤(0.5~1.5초) 대기 후 타격, 전방 90°, 사정거리 4.5m, 지속 0.2초.
///           넉백 방향이 더 날카로워짐.
/// </summary>
[CreateAssetMenu(fileName = "FGFrontalStrikePatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/Punch")]
public class FGPunchPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animWindup = "PunchBig";
    [SerializeField] private string animHit    = "PunchSmall";
    [SerializeField] private float  crossFade  = 0.1f;

    [Header("Phase 1 설정")]
    [SerializeField] private float p1_warningDuration = 0.3f;
    [SerializeField] private float p1_attackRange     = 3.5f;
    [SerializeField] private float p1_attackAngle     = 60f;
    [SerializeField] private float p1_knockbackForce  = 12f;

    [Header("Phase 2 설정")]
    [SerializeField] private float p2_preDelayMin     = 0.5f;  // 엇박자 최소 딜레이
    [SerializeField] private float p2_preDelayMax     = 1.5f;  // 엇박자 최대 딜레이
    [SerializeField] private float p2_warningDuration = 0.2f;
    [SerializeField] private float p2_attackRange     = 4.5f;
    [SerializeField] private float p2_attackAngle     = 90f;
    [SerializeField] private float p2_knockbackForce  = 10f;

    [Header("쿨다운")]
    [SerializeField] private float patternCooldown = 4f;

    private float _cooldownEndTime = float.MinValue;
    private FGPunchState _state;

    public override void Initialize(BossPatternContext ctx)
    {
        _cooldownEndTime = float.MinValue;
        _state = new FGPunchState(this);
    }

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (Time.time < _cooldownEndTime) return false;
        return ctx.Ctx.Runtime.DistToPlayer <= 5f;
    }

    public override SpecialStateBase GetRuntimeState() => _state;

    internal void StartCooldown() => _cooldownEndTime = Time.time + patternCooldown;

    // ── 내부 상태 ───────────────────────────────────────────────────────

    private sealed class FGPunchState : FullLockState<FGPunchPatternSO>
    {
        private enum Sub { Windup, Warning, Hit, End }

        private Sub   _sub;
        private float _timer;
        private float _phaseDuration;
        private bool  _isPhase2;
        private float _attackRange;
        private float _attackAngle;
        private float _knockbackForce;

        public FGPunchState(FGPunchPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;

            var fg = (ctx.Monster as ForestGuardianMonster)?.FGBlackboard;
            _isPhase2     = fg?.IsPhase2 ?? false;
            _attackRange  = _isPhase2 ? Data.p2_attackRange : Data.p1_attackRange;
            _attackAngle  = _isPhase2 ? Data.p2_attackAngle : Data.p1_attackAngle;
            _knockbackForce = _isPhase2 ? Data.p2_knockbackForce : Data.p1_knockbackForce;

            FacePlayer(ctx);

            if (_isPhase2)
            {
                // 엇박자: 팔 들기 애니 후 랜덤 딜레이
                if (ctx.Animator != null)
                    ctx.Animator.CrossFade(Data.animWindup, Data.crossFade);
                _sub           = Sub.Windup;
                _timer         = 0f;
                _phaseDuration = Random.Range(Data.p2_preDelayMin, Data.p2_preDelayMax);
            }
            else
            {
                // 정박자: 즉시 경고 장판
                SpawnWarning(ctx, _attackRange, Data.p1_warningDuration);
                if (ctx.Animator != null)
                    ctx.Animator.CrossFade(Data.animWindup, Data.crossFade);
                _sub           = Sub.Warning;
                _timer         = 0f;
                _phaseDuration = Data.p1_warningDuration;
            }
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;
            if (_timer < _phaseDuration) return;
            _timer = 0f;

            switch (_sub)
            {
                case Sub.Windup:  // Phase2: 랜덤 딜레이 종료 → 경고
                    SpawnWarning(ctx, _attackRange, Data.p2_warningDuration);
                    if (ctx.Animator != null)
                        ctx.Animator.CrossFade(Data.animHit, Data.crossFade);
                    _sub           = Sub.Warning;
                    _phaseDuration = Data.p2_warningDuration;
                    break;

                case Sub.Warning:  // 경고 종료 → 타격 판정
                    ApplyHit(ctx);
                    _sub           = Sub.End;
                    _phaseDuration = 0.25f;
                    break;

                case Sub.End:
                    ctx.Monster.ChangeState<PatrolState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx) => Data.StartCooldown();

        // ── 헬퍼 ──────────────────────────────────────────────────

        private void ApplyHit(MonsterContext ctx)
        {
            int   damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
            float halfAngle = _attackAngle * 0.5f * Mathf.Deg2Rad;

            var hits = Physics.OverlapSphere(ctx.Transform.position, _attackRange);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                Vector3 toTarget = (col.transform.position - ctx.Transform.position).normalized;
                float angle = Mathf.Acos(Mathf.Clamp(
                    Vector3.Dot(ctx.Transform.forward, toTarget), -1f, 1f));
                if (angle > halfAngle) continue;

                player.TakeDamage(damage);
                Vector3 kbDir = toTarget;
                kbDir.y = 0.3f;
                player.ApplyKnockback(kbDir.normalized * _knockbackForce);
                break;
            }
        }

        private static void SpawnWarning(MonsterContext ctx, float radius, float duration)
        {
            Vector3 pos = ctx.Transform.position + ctx.Transform.forward * (radius * 0.5f);
            pos.y = ctx.Transform.position.y;
            MonsterGroundWarning.Spawn(pos, radius, duration, new Color(1f, 0.4f, 0.1f, 0.9f));
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
