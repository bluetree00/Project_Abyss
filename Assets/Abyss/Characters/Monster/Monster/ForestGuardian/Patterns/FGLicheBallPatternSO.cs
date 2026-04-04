using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 리체 폼 패턴 — 리체 볼.
/// 주변 회전 마법진(반경 7m) 생성 후 2초 경고, 피해 판정.
/// </summary>
[CreateAssetMenu(fileName = "FGLicheBallPatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/Liche/LicheBall")]
public class FGLicheBallPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animStateName = "Magic01";
    [SerializeField] private float  crossFade     = 0.15f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxPrefab;

    [Header("공격 설정")]
    [SerializeField] private float attackRadius    = 7f;
    [SerializeField] private float warningDuration = 2f;

    private FGLicheBallState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new FGLicheBallState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ────────────────────────────────────────────────────

    private sealed class FGLicheBallState : FullLockState<FGLicheBallPatternSO>
    {
        private float _timer;
        private bool  _attacked;

        public FGLicheBallState(FGLicheBallPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;
            _timer   = 0f;
            _attacked = false;

            // 경고 원
            MonsterGroundWarning.Spawn(
                ctx.Transform.position,
                Data.attackRadius,
                Data.warningDuration,
                new Color(0.5f, 0f, 1f, 0.9f));

            // 애니
            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animStateName))
                ctx.Animator.CrossFade(Data.animStateName, Data.crossFade);
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            if (!_attacked && _timer >= Data.warningDuration)
            {
                _attacked = true;
                ApplyHit(ctx);
            }

            if (_timer >= Data.warningDuration + 0.5f)
                ctx.Monster.ChangeState<PatrolState>();
        }

        public override void Exit(MonsterContext ctx) { }

        private void ApplyHit(MonsterContext ctx)
        {
            if (Data.vfxPrefab != null)
                BossEffectPool.SpawnOneShot(Data.vfxPrefab,
                    ctx.Transform.position, Quaternion.identity);

            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
            float kbForce = ctx.Stat.knockbackForce;

            var hits = Physics.OverlapSphere(ctx.Transform.position, Data.attackRadius);
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
