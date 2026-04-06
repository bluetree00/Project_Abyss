using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 스피드 폼 패턴 — 박수 충격파.
/// 정면으로 박수를 치며 충격파를 날린다.
/// 공격 범위: 정면 2m, 충격파 범위 1m, 충격파 거리 3m.
/// </summary>
[CreateAssetMenu(fileName = "FGSpiderClapPatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/Spider/SpiderClap")]
public class FGSpiderClapPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animClap  = "ClapAttack";
    [SerializeField] private float  crossFade = 0.15f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxClap;
    [SerializeField] private GameObject vfxBlast;

    [Header("박수 충격파 설정")]
    [SerializeField] private float frontRange     = 2f;    // 정면 타격 범위
    [SerializeField] private float blastRadius    = 1f;    // 충격파 반경
    [SerializeField] private float blastDistance  = 3f;    // 충격파 도달 거리
    [SerializeField] private float warningTime    = 0.4f;  // 경고 시간

    private FGSpiderClapState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new FGSpiderClapState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ────────────────────────────────────────────────────

    private sealed class FGSpiderClapState : FullLockState<FGSpiderClapPatternSO>
    {
        private enum ClapPhase { Warning, Clap, Blast, End }

        private ClapPhase _phase;
        private float     _timer;

        public FGSpiderClapState(FGSpiderClapPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;
            _phase = ClapPhase.Warning;
            _timer = 0f;

            // 정면 부채꼴 경고
            Vector3 frontPos = ctx.Transform.position + ctx.Transform.forward * Data.frontRange;
            MonsterGroundWarning.Spawn(
                frontPos, Data.frontRange,
                Data.warningTime, new Color(1f, 0.3f, 0.3f, 0.9f));

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animClap))
                ctx.Animator.CrossFade(Data.animClap, Data.crossFade);
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            switch (_phase)
            {
                case ClapPhase.Warning:
                    if (_timer >= Data.warningTime)
                    {
                        _phase = ClapPhase.Clap;
                        _timer = 0f;
                        ApplyFrontClap(ctx);
                    }
                    break;

                case ClapPhase.Clap:
                    if (_timer >= 0.2f)
                    {
                        _phase = ClapPhase.Blast;
                        _timer = 0f;
                        ApplyBlast(ctx);
                    }
                    break;

                case ClapPhase.Blast:
                    if (_timer >= 0.4f)
                        ctx.Monster.ChangeState<PatrolState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx) { }

        private void ApplyFrontClap(MonsterContext ctx)
        {
            if (Data.vfxClap != null)
                BossEffectPool.SpawnOneShot(Data.vfxClap,
                    ctx.Transform.position + ctx.Transform.forward * Data.frontRange,
                    ctx.Transform.rotation);

            // 정면 범위 판정
            Vector3 center = ctx.Transform.position + ctx.Transform.forward * Data.frontRange;
            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
            float kbForce = ctx.Stat.knockbackForce;
            var hits = Physics.OverlapSphere(center, Data.frontRange);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;
                player.TakeDamage(damage);
                Vector3 dir = ctx.Transform.forward;
                dir.y = 0.3f;
                player.ApplyKnockback(dir.normalized * kbForce);
            }
        }

        private void ApplyBlast(MonsterContext ctx)
        {
            // 충격파: 정면 방향으로 blastDistance 거리에 blastRadius 범위
            Vector3 blastCenter = ctx.Transform.position
                + ctx.Transform.forward * (Data.frontRange + Data.blastDistance);

            if (Data.vfxBlast != null)
                BossEffectPool.SpawnOneShot(Data.vfxBlast,
                    blastCenter, ctx.Transform.rotation);

            // OverlapBox로 직선 충격파 판정
            Vector3 boxCenter = ctx.Transform.position
                + ctx.Transform.forward * (Data.frontRange + Data.blastDistance * 0.5f);
            Vector3 halfExtents = new Vector3(Data.blastRadius, 1f, Data.blastDistance * 0.5f);
            var hits = Physics.OverlapBox(boxCenter, halfExtents, ctx.Transform.rotation);

            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
            float kbForce = ctx.Stat.knockbackForce;
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;
                player.TakeDamage(damage);
                Vector3 dir = ctx.Transform.forward;
                dir.y = 0.3f;
                player.ApplyKnockback(dir.normalized * kbForce);
            }
        }
    }
}
}
