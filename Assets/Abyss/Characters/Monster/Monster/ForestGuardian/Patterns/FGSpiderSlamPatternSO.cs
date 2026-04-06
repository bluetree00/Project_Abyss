using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 스피드 폼 패턴 — 땅 찍기.
/// 빠르게 땅을 찍으며 충격파를 방출(지름 7m).
/// </summary>
[CreateAssetMenu(fileName = "FGSpiderSlamPatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/Spider/SpiderSlam")]
public class FGSpiderSlamPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animSlam  = "StompFeet";
    [SerializeField] private float  crossFade = 0.15f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxShockwave;

    [Header("땅 찍기 설정")]
    [SerializeField] private float slamRadius      = 3.5f;  // 지름 7m
    [SerializeField] private float warningDuration = 0.5f;
    [SerializeField] private float damageMult      = 1.2f;
    [SerializeField] private float knockbackMult   = 1.5f;

    private FGSpiderSlamState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new FGSpiderSlamState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ────────────────────────────────────────────────────

    private sealed class FGSpiderSlamState : FullLockState<FGSpiderSlamPatternSO>
    {
        private float _timer;
        private bool  _applied;

        public FGSpiderSlamState(FGSpiderSlamPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;
            _timer   = 0f;
            _applied = false;

            MonsterGroundWarning.Spawn(
                ctx.Transform.position, Data.slamRadius,
                Data.warningDuration, new Color(1f, 0.2f, 0.2f, 0.9f));

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animSlam))
                ctx.Animator.CrossFade(Data.animSlam, Data.crossFade);
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            if (!_applied && _timer >= Data.warningDuration)
            {
                _applied = true;
                ApplySlam(ctx);
            }

            if (_timer >= Data.warningDuration + 0.5f)
                ctx.Monster.ChangeState<PatrolState>();
        }

        public override void Exit(MonsterContext ctx) { }

        private void ApplySlam(MonsterContext ctx)
        {
            if (Data.vfxShockwave != null)
                BossEffectPool.SpawnOneShot(Data.vfxShockwave,
                    ctx.Transform.position, Quaternion.identity);

            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * Data.damageMult);
            float kbForce = ctx.Stat.knockbackForce * Data.knockbackMult;
            var hits = Physics.OverlapSphere(ctx.Transform.position, Data.slamRadius);
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
    }
}
}
