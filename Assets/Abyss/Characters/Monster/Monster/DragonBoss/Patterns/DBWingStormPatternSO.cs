using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 드래곤 보스 — 윙 스톰.
/// 양 날개로 주변 충격파, 반경 6m 피해.
/// 2초 경고 후 판정.
/// </summary>
[CreateAssetMenu(fileName = "DBWingStormPatternSO",
                 menuName  = "Abyss/Boss/DragonBoss/WingStorm")]
public class DBWingStormPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animName  = "Attack01";
    [SerializeField] private float  crossFade = 0.1f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxPrefab;

    [Header("공격 설정")]
    [SerializeField] private float warningDuration = 2f;
    [SerializeField] private float blastRadius     = 6f;

    private WingStormState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new WingStormState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ───────────────────────────────────────────────

    private sealed class WingStormState : FullLockState<DBWingStormPatternSO>
    {
        private float _timer;
        private int   _phase;
        private float _phaseDuration;

        public WingStormState(DBWingStormPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            if (ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animName, Data.crossFade);

            // 전방위 경고
            MonsterGroundWarning.SpawnGrid(
                ctx.Transform.position,
                ctx.Transform.forward,
                MonsterGroundWarning.GridShape.Around8,
                Data.warningDuration,
                new Color(0.9f, 0.7f, 0f));

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
                case 0: // 경고 끝 → 판정
                    DoBlast(ctx);
                    _phase         = 1;
                    _phaseDuration = 0.4f;
                    break;

                case 1: // 종료
                    ctx.Monster.ChangeState<PatrolState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx) { }

        private void DoBlast(MonsterContext ctx)
        {
            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
            float kbForce = ctx.Stat.knockbackForce;

            if (Data.vfxPrefab != null)
                BossEffectPool.SpawnOneShot(Data.vfxPrefab,
                    ctx.Transform.position, ctx.Transform.rotation);

            var hits = Physics.OverlapSphere(ctx.Transform.position, Data.blastRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                Vector3 dir = (col.transform.position - ctx.Transform.position).normalized;
                ApplyElementalDamage(ctx, player, damage, kbForce, dir);
            }
        }

        private static void ApplyElementalDamage(MonsterContext ctx,
            PlayerController player, int damage, float kbForce, Vector3 dir)
        {
            var dragon  = ctx.Monster as DragonBossMonster;
            var element = dragon?.DBBlackboard?.CurrentElement
                          ?? DragonBossBlackboard.DragonElement.Ice;

            player.TakeDamage(damage);
            switch (element)
            {
                case DragonBossBlackboard.DragonElement.Ice:
                    player.ApplyKnockback(Vector3.zero, 2f); // 빙결 2초
                    break;
                case DragonBossBlackboard.DragonElement.Thunder:
                    player.ApplyKnockback(Vector3.zero, 0.5f); // 그로기 0.5초
                    break;
                default: // Fire
                    player.ApplySlow(0.4f, 2f); // 이동속도 감소 2초
                    break;
            }
        }
    }
}
}
