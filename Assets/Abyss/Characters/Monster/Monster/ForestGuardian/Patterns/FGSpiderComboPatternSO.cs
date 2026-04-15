using UnityEngine;
using UnityEngine.AI;

namespace Abyss.Monster
{
/// <summary>
/// 스파이더 폼 패턴 — 뒤엉킨 습격 (3단계 순서 진행).
/// Phase 0: 플레이어 방향 이동 + 회전 (범위 지름 5m, 3초)
/// Phase 1: 뒤로 날아 충격파 (지름 7m 즉발)
/// Phase 2: 정면 반동 + 충격파 (정면 2m + 충격파 반경 1m / 거리 3m, 즉발)
/// </summary>
[CreateAssetMenu(fileName = "FGSpiderComboPatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/Spider/SpiderCombo")]
public class FGSpiderComboPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animSpin     = "SpinAttack";
    [SerializeField] private string animJumpBack = "JumpBack";
    [SerializeField] private string animSlam     = "FrontSlam";
    [SerializeField] private float  crossFade    = 0.15f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxSpin;
    [SerializeField] private GameObject vfxShockwave;
    [SerializeField] private GameObject vfxFrontBlast;

    [Header("단계별 설정")]
    [SerializeField] private float phase0Duration    = 3f;    // 스핀 이동 지속
    [SerializeField] private float phase0Radius      = 2.5f;  // 스핀 반경 (지름 5m)
    [SerializeField] private float phase1Radius      = 3.5f;  // 충격파 반경 (지름 7m)
    [SerializeField] private float phase2FrontOffset = 2f;    // 정면 충격 거리
    [SerializeField] private float phase2BlastRadius = 1f;    // 반동 충격파 반경
    [SerializeField] private float phase2FrontRange  = 3f;    // 정면 반동 거리

    private FGSpiderComboState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new FGSpiderComboState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ────────────────────────────────────────────────────

    private sealed class FGSpiderComboState : FullLockState<FGSpiderComboPatternSO>
    {
        private int   _phase;
        private float _phaseTimer;

        public FGSpiderComboState(FGSpiderComboPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;
            _phase      = 0;
            _phaseTimer = 0f;

            EnterPhase0(ctx);
        }

        public override void Update(MonsterContext ctx)
        {
            _phaseTimer += Time.deltaTime;

            switch (_phase)
            {
                case 0:
                    // 스핀 이동 중 — 플레이어 추적 이동
                    if (ctx.Runtime.PlayerTarget != null && ctx.Agent.isOnNavMesh)
                        ctx.Agent.SetDestination(ctx.Runtime.PlayerTarget.position);

                    if (_phaseTimer >= Data.phase0Duration)
                    {
                        ApplyPhase0Hit(ctx);
                        _phase      = 1;
                        _phaseTimer = 0f;
                        EnterPhase1(ctx);
                    }
                    break;

                case 1:
                    // Phase1: 즉발이므로 짧은 딜레이 후 바로 진행
                    if (_phaseTimer >= 0.4f)
                    {
                        _phase      = 2;
                        _phaseTimer = 0f;
                        EnterPhase2(ctx);
                    }
                    break;

                case 2:
                    // Phase2: 즉발 후 종료
                    if (_phaseTimer >= 0.5f)
                        ctx.Monster.ChangeState<PatrolState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            if (ctx.Agent.isOnNavMesh)
            {
                ctx.Agent.ResetPath();
                ctx.Agent.velocity = Vector3.zero;
            }
        }

        // ── Phase 0: 스핀 이동 ──────────────────────────────────────

        private void EnterPhase0(MonsterContext ctx)
        {
            MonsterGroundWarning.Spawn(
                ctx.Transform.position, Data.phase0Radius,
                Data.phase0Duration, new Color(1f, 0.5f, 0f, 0.9f));

            if (Data.vfxSpin != null)
                BossEffectPool.SpawnOneShot(Data.vfxSpin,
                    ctx.Transform.position, ctx.Transform.rotation);

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animSpin))
                ctx.Animator.CrossFade(Data.animSpin, Data.crossFade);
        }

        private void ApplyPhase0Hit(MonsterContext ctx)
        {
            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
            float kbForce = ctx.Stat.knockbackForce;
            var hits = Physics.OverlapSphere(ctx.Transform.position, Data.phase0Radius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;
                player.TakeDamage(damage);
                Vector3 dir = (col.transform.position - ctx.Transform.position).normalized;
                dir.y = 0.3f;
                player.ApplyKnockback(dir.normalized * kbForce);
            }
        }

        // ── Phase 1: 뒤로 날아 충격파 ──────────────────────────────

        private void EnterPhase1(MonsterContext ctx)
        {
            // 뒤로 이동 (NavMesh 이동 중지 후 즉발 처리)
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;

            MonsterGroundWarning.Spawn(
                ctx.Transform.position, Data.phase1Radius,
                0.1f, new Color(1f, 0.2f, 0.2f, 0.9f));

            if (Data.vfxShockwave != null)
                BossEffectPool.SpawnOneShot(Data.vfxShockwave,
                    ctx.Transform.position, Quaternion.identity);

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animJumpBack))
                ctx.Animator.CrossFade(Data.animJumpBack, Data.crossFade);

            // 즉발 피해
            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 1.2f);
            float kbForce = ctx.Stat.knockbackForce * 1.5f;
            var hits = Physics.OverlapSphere(ctx.Transform.position, Data.phase1Radius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;
                player.TakeDamage(damage);
                Vector3 dir = (col.transform.position - ctx.Transform.position).normalized;
                dir.y = 0.4f;
                player.ApplyKnockback(dir.normalized * kbForce);
            }
        }

        // ── Phase 2: 정면 반동 충격파 ──────────────────────────────

        private void EnterPhase2(MonsterContext ctx)
        {
            Vector3 frontPos = ctx.Transform.position + ctx.Transform.forward * Data.phase2FrontOffset;

            MonsterGroundWarning.Spawn(
                frontPos, Data.phase2BlastRadius,
                0.1f, new Color(1f, 0.2f, 0.2f, 0.9f));

            if (Data.vfxFrontBlast != null)
                BossEffectPool.SpawnOneShot(Data.vfxFrontBlast,
                    frontPos, ctx.Transform.rotation);

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animSlam))
                ctx.Animator.CrossFade(Data.animSlam, Data.crossFade);

            // 정면 OverlapSphere (범위 phase2FrontRange)
            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
            float kbForce = ctx.Stat.knockbackForce;
            var hits = Physics.OverlapSphere(frontPos, Data.phase2FrontRange);
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
