using UnityEngine;
using UnityEngine.AI;

namespace Abyss.Monster
{
/// <summary>
/// 스파이더 폼 패턴 — 대시.
/// 플레이어 방향으로 빠르게 돌진(1~3회 반복). 폭 3m OverlapBox 판정.
/// 대시 속도: stat.moveSpeed * 2
/// </summary>
[CreateAssetMenu(fileName = "FGSpiderDashPatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/Spider/SpiderDash")]
public class FGSpiderDashPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animDash  = "Dash";
    [SerializeField] private float  crossFade = 0.1f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxDash;

    [Header("대시 설정")]
    [SerializeField] private float dashWidthHalf  = 1.5f;  // 폭 3m → 반 1.5m
    [SerializeField] private float dashTimeout    = 0.8f;  // 대시 1회 최대 시간
    [SerializeField] private float speedMult      = 2f;    // moveSpeed 배율

    private FGSpiderDashState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new FGSpiderDashState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ────────────────────────────────────────────────────

    private sealed class FGSpiderDashState : FullLockState<FGSpiderDashPatternSO>
    {
        private int   _totalDashes;
        private int   _dashCount;
        private float _dashTimer;
        private float _originalSpeed;
        private bool  _hit;

        public FGSpiderDashState(FGSpiderDashPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            _totalDashes   = Random.Range(1, 4);  // 1~3회
            _dashCount     = 0;
            _originalSpeed = ctx.Agent.speed;
            StartDash(ctx);
        }

        public override void Update(MonsterContext ctx)
        {
            _dashTimer += Time.deltaTime;

            // 판정 (지속적으로)
            if (!_hit)
                CheckHit(ctx);

            // 대시 완료 조건: 도착 or 타임아웃
            bool arrived = ctx.Agent.isOnNavMesh
                && !ctx.Agent.pathPending
                && ctx.Agent.remainingDistance <= ctx.Agent.stoppingDistance + 0.1f;

            if (arrived || _dashTimer >= Data.dashTimeout)
            {
                _hit = false;
                _dashCount++;
                if (_dashCount < _totalDashes && ctx.Runtime.PlayerTarget != null)
                    StartDash(ctx);
                else
                    EndDash(ctx);
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            if (ctx.Agent.isOnNavMesh)
            {
                ctx.Agent.speed = _originalSpeed;
                ctx.Agent.ResetPath();
                ctx.Agent.velocity = Vector3.zero;
            }
        }

        private void StartDash(MonsterContext ctx)
        {
            _dashTimer = 0f;
            _hit       = false;

            ctx.Agent.speed = ctx.Stat.moveSpeed * Data.speedMult;

            Vector3 target = ctx.Runtime.PlayerTarget != null
                ? ctx.Runtime.PlayerTarget.position
                : ctx.Transform.position + ctx.Transform.forward * 5f;

            if (ctx.Agent.isOnNavMesh)
            {
                NavMeshHit navHit;
                if (NavMesh.SamplePosition(target, out navHit, 2f, NavMesh.AllAreas))
                    ctx.Agent.SetDestination(navHit.position);
            }

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animDash))
                ctx.Animator.CrossFade(Data.animDash, Data.crossFade);

            if (Data.vfxDash != null)
                BossEffectPool.SpawnOneShot(Data.vfxDash,
                    ctx.Transform.position, ctx.Transform.rotation);
        }

        private void EndDash(MonsterContext ctx)
        {
            ctx.Agent.speed = _originalSpeed;
            ctx.Monster.ChangeState<PatrolState>();
        }

        private void CheckHit(MonsterContext ctx)
        {
            Vector3 center = ctx.Transform.position + ctx.Transform.forward * Data.dashWidthHalf;
            Vector3 halfExtents = new Vector3(Data.dashWidthHalf, 1f, Data.dashWidthHalf);
            var hits = Physics.OverlapBox(center, halfExtents, ctx.Transform.rotation);

            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
            float kbForce = ctx.Stat.knockbackForce;

            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                _hit = true;
                player.TakeDamage(damage);
                Vector3 dir = ctx.Transform.forward;
                dir.y = 0.3f;
                player.ApplyKnockback(dir.normalized * kbForce);
            }
        }
    }
}
}
