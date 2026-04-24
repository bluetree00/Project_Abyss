using UnityEngine;
using UnityEngine.AI;

namespace Abyss.Monster
{
/// <summary>
/// 스피드 폼 패턴 — 나뭇잎 선풍.
/// 플레이어 방향으로 이동하며 회전 킥(지름 5m, 3m/초, 3초).
/// </summary>
[CreateAssetMenu(fileName = "FGSpiderWhirlwindPatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/Spider/SpiderWhirlwind")]
public class FGSpiderWhirlwindPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animSpin  = "SpinAttack";
    [SerializeField] private float  crossFade = 0.15f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxSpin;

    [Header("선풍 설정")]
    [SerializeField] private float whirlwindRadius   = 2.5f;  // 지름 5m
    [SerializeField] private float whirlwindDuration = 3f;
    [SerializeField] private float moveSpeed         = 3f;    // 기획서: 초당 3m

    private FGSpiderWhirlwindState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new FGSpiderWhirlwindState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ────────────────────────────────────────────────────

    private sealed class FGSpiderWhirlwindState : FullLockState<FGSpiderWhirlwindPatternSO>
    {
        private float _elapsed;
        private float _originalSpeed;

        public FGSpiderWhirlwindState(FGSpiderWhirlwindPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            _elapsed       = 0f;
            _originalSpeed = ctx.Agent.speed;
            ctx.Agent.speed = Data.moveSpeed;

            MonsterGroundWarning.Spawn(
                ctx.Transform.position, Data.whirlwindRadius,
                Data.whirlwindDuration, new Color(1f, 0.5f, 0f, 0.9f));

            if (Data.vfxSpin != null)
                BossEffectPool.SpawnOneShot(Data.vfxSpin,
                    ctx.Transform.position, ctx.Transform.rotation);

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animSpin))
                ctx.Animator.CrossFade(Data.animSpin, Data.crossFade);
        }

        public override void Update(MonsterContext ctx)
        {
            _elapsed += Time.deltaTime;

            // 플레이어 추적 이동
            if (ctx.Runtime.PlayerTarget != null && ctx.Agent.isOnNavMesh)
                ctx.Agent.SetDestination(ctx.Runtime.PlayerTarget.position);

            // 지속 판정
            ApplyHit(ctx);

            if (_elapsed >= Data.whirlwindDuration)
                ctx.Monster.ChangeState<PatrolState>();
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

        private void ApplyHit(MonsterContext ctx)
        {
            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
            float kbForce = ctx.Stat.knockbackForce;
            var hits = Physics.OverlapSphere(ctx.Transform.position, Data.whirlwindRadius);
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
    }
}
}
