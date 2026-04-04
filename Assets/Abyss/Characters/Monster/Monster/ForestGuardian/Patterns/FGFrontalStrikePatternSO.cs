using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 공통 패턴 ① — 정면 타격.
/// 정면으로 주먹 2회 후려친다.
/// 2타 사이 딜레이: 리체 1초 / 스파이더 0.5초 / 가시 2초
/// 공격 범위: 180°, 반경 리체 2m / 스파이더 1m / 가시 4m
/// </summary>
[CreateAssetMenu(fileName = "FGFrontalStrikePatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/FrontalStrike")]
public class FGFrontalStrikePatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animHit1 = "Attack01";
    [SerializeField] private string animHit2 = "Attack02";
    [SerializeField] private float  crossFade = 0.1f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxPrefab;

    [Header("공격 설정")]
    [SerializeField] private float warningDuration = 0.4f;

    private FGFrontalStrikeState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new FGFrontalStrikeState(this);

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.DistToPlayer <= 2f;

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ────────────────────────────────────────────────────

    private sealed class FGFrontalStrikeState : FullLockState<FGFrontalStrikePatternSO>
    {
        private float _timer;
        private int   _phase;        // 0=경고, 1=1타, 2=대기, 3=2타, 4=완료
        private float _phaseDuration;
        private ForestGuardianBlackboard.BossForm _form;

        public FGFrontalStrikeState(FGFrontalStrikePatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = UnityEngine.Vector3.zero;

            // 폼 확인
            _form = (ctx.Monster as ForestGuardianMonster)?.FGBlackboard.CurrentForm
                    ?? ForestGuardianBlackboard.BossForm.Liche;

            float attackRadius = GetRadius(_form);

            // 1타 전 경고
            MonsterGroundWarning.Spawn(
                ctx.Transform.position + ctx.Transform.forward * (attackRadius * 0.5f),
                attackRadius, Data.warningDuration,
                new Color(1f, 0.2f, 0.2f, 0.9f));

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animHit1))
                ctx.Animator.CrossFade(Data.animHit1, Data.crossFade);

            _phase         = 0;
            _timer         = 0f;
            _phaseDuration = Data.warningDuration;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;
            if (_timer < _phaseDuration) return;
            _timer = 0f;

            switch (_phase)
            {
                case 0:  // 경고 끝 → 1타 판정
                    ApplyHit(ctx);
                    _phase = 1;
                    _phaseDuration = GetBetweenDelay(_form);
                    break;

                case 1:  // 대기 끝 → 2타 애니 + 경고
                {
                    float attackRadius = GetRadius(_form);
                    MonsterGroundWarning.Spawn(
                        ctx.Transform.position + ctx.Transform.forward * (attackRadius * 0.5f),
                        attackRadius, Data.warningDuration,
                        new Color(1f, 0.2f, 0.2f, 0.9f));

                    if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animHit2))
                        ctx.Animator.CrossFade(Data.animHit2, Data.crossFade);

                    _phase         = 2;
                    _phaseDuration = Data.warningDuration;
                    break;
                }

                case 2:  // 2타 판정
                    ApplyHit(ctx);
                    _phase         = 3;
                    _phaseDuration = 0.3f;
                    break;

                case 3:  // 종료
                    ctx.Monster.ChangeState<PatrolState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx) { }

        private void ApplyHit(MonsterContext ctx)
        {
            float radius = GetRadius(_form);
            int   damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
            float kbForce = ctx.Stat.knockbackForce;

            if (Data.vfxPrefab != null)
                BossEffectPool.SpawnOneShot(Data.vfxPrefab,
                    ctx.Transform.position + ctx.Transform.forward,
                    ctx.Transform.rotation);

            var hits = Physics.OverlapSphere(ctx.Transform.position, radius);
            foreach (var col in hits)
            {
                // 180° 정면 체크
                Vector3 toTarget = (col.transform.position - ctx.Transform.position).normalized;
                if (Vector3.Dot(ctx.Transform.forward, toTarget) < 0f) continue;

                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                player.TakeDamage(damage);
                Vector3 dir = toTarget;
                dir.y = 0.3f;
                player.ApplyKnockback(dir.normalized * kbForce);
            }
        }

        private static float GetRadius(ForestGuardianBlackboard.BossForm form)
        {
            return form switch
            {
                ForestGuardianBlackboard.BossForm.Spider => 1f,
                ForestGuardianBlackboard.BossForm.Thorn  => 4f,
                _                                        => 2f,  // Liche
            };
        }

        private static float GetBetweenDelay(ForestGuardianBlackboard.BossForm form)
        {
            return form switch
            {
                ForestGuardianBlackboard.BossForm.Spider => 0.5f,
                ForestGuardianBlackboard.BossForm.Thorn  => 2.0f,
                _                                        => 1.0f,  // Liche
            };
        }
    }
}
}
