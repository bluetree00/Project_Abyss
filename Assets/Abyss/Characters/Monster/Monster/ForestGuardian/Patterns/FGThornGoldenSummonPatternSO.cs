using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 가시 폼 패턴 — 골든 소환.
/// 플레이어 위치에 미니 골렘 소환(범위 지름 5m → 반경 2.5m, 1.5초 경고 후 피해).
/// </summary>
[CreateAssetMenu(fileName = "FGThornGoldenSummonPatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/Thorn/GoldenSummon")]
public class FGThornGoldenSummonPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animStateName = "Summon";
    [SerializeField] private float  crossFade     = 0.15f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxSummon;

    [Header("소환 설정")]
    [SerializeField] private float summonRadius      = 2.5f;  // 지름 5m → 반경 2.5m
    [SerializeField] private float warningDuration   = 1.5f;

    private FGThornGoldenSummonState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new FGThornGoldenSummonState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ────────────────────────────────────────────────────

    private sealed class FGThornGoldenSummonState : FullLockState<FGThornGoldenSummonPatternSO>
    {
        private float   _timer;
        private bool    _attacked;
        private Vector3 _summonPos;

        public FGThornGoldenSummonState(FGThornGoldenSummonPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;
            _timer   = 0f;
            _attacked = false;

            _summonPos = ctx.Runtime.PlayerTarget != null
                ? ctx.Runtime.PlayerTarget.position
                : ctx.Transform.position + ctx.Transform.forward * 3f;

            MonsterGroundWarning.Spawn(
                _summonPos, Data.summonRadius, Data.warningDuration,
                new Color(1f, 0.85f, 0f, 0.9f));

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animStateName))
                ctx.Animator.CrossFade(Data.animStateName, Data.crossFade);
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            if (!_attacked && _timer >= Data.warningDuration)
            {
                _attacked = true;
                ApplySummon(ctx);
            }

            if (_timer >= Data.warningDuration + 0.5f)
                ctx.Monster.ChangeState<PatrolState>();
        }

        public override void Exit(MonsterContext ctx) { }

        private void ApplySummon(MonsterContext ctx)
        {
            if (Data.vfxSummon != null)
                BossEffectPool.SpawnOneShot(Data.vfxSummon, _summonPos, Quaternion.identity);

            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
            float kbForce = ctx.Stat.knockbackForce;
            var hits = Physics.OverlapSphere(_summonPos, Data.summonRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;
                player.TakeDamage(damage);
                Vector3 dir = (col.transform.position - _summonPos).normalized;
                dir.y = 0.3f;
                player.ApplyKnockback(dir.normalized * kbForce);
            }
        }
    }
}
}
