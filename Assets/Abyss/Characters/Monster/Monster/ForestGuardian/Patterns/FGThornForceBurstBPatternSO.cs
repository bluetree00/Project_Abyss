using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 가시 폼 패턴 — 포스 버스트 B.
/// 정면으로 3회 폭기 발사(거리 10m). 회차마다 범위 확장: 1.5m → 2.5m → 3m.
/// 각 발사 전 경고 표시.
/// </summary>
[CreateAssetMenu(fileName = "FGThornForceBurstBPatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/Thorn/ForceBurstB")]
public class FGThornForceBurstBPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animBurst  = "BurstAttack";
    [SerializeField] private float  crossFade  = 0.15f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxBurst;

    [Header("폭기 설정")]
    [SerializeField] private float burstInterval     = 1f;     // 발사 간격
    [SerializeField] private float warningDuration   = 0.6f;   // 각 경고 시간
    [SerializeField] private float burstRange        = 10f;    // 최대 거리
    [SerializeField] private float frontOffset       = 5f;     // 중심점 오프셋 (보스에서 앞으로)

    // 회차별 반경 (1.5m / 2.5m / 3m)
    private static readonly float[] BurstRadii = { 1.5f, 2.5f, 3f };

    private FGThornForceBurstBState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new FGThornForceBurstBState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ────────────────────────────────────────────────────

    private sealed class FGThornForceBurstBState : FullLockState<FGThornForceBurstBPatternSO>
    {
        private int   _shotIndex;
        private float _timer;
        private bool  _inWarning;

        public FGThornForceBurstBState(FGThornForceBurstBPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;
            _shotIndex = 0;
            _timer     = 0f;
            _inWarning = true;
            StartWarning(ctx);
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            if (_inWarning && _timer >= Data.warningDuration)
            {
                _inWarning = false;
                _timer     = 0f;
                FireBurst(ctx, _shotIndex);
                _shotIndex++;

                if (_shotIndex >= BurstRadii.Length)
                {
                    // 모두 완료
                    ctx.Monster.ChangeState<PatrolState>();
                    return;
                }
            }

            if (!_inWarning && _timer >= Data.burstInterval - Data.warningDuration)
            {
                _inWarning = true;
                _timer     = 0f;
                StartWarning(ctx);
            }
        }

        public override void Exit(MonsterContext ctx) { }

        private void StartWarning(MonsterContext ctx)
        {
            float radius     = BurstRadii[_shotIndex];
            Vector3 burstPos = ctx.Transform.position
                             + ctx.Transform.forward * Data.frontOffset;

            MonsterGroundWarning.Spawn(burstPos, radius, Data.warningDuration,
                new Color(0.8f, 0.4f, 0f, 0.9f));

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animBurst))
                ctx.Animator.CrossFade(Data.animBurst, Data.crossFade);
        }

        private void FireBurst(MonsterContext ctx, int index)
        {
            float radius     = BurstRadii[index];
            Vector3 burstPos = ctx.Transform.position
                             + ctx.Transform.forward * Data.frontOffset;

            if (Data.vfxBurst != null)
                BossEffectPool.SpawnOneShot(Data.vfxBurst, burstPos, ctx.Transform.rotation);

            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * (0.8f + index * 0.2f));
            float kbForce = ctx.Stat.knockbackForce * (1f + index * 0.3f);

            var hits = Physics.OverlapSphere(burstPos, radius);
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
