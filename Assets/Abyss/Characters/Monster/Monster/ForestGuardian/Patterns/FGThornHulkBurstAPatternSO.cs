using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 파워 폼 패턴 — 헐크 버스트 A.
/// 자신의 앞에 돌을 소환하고 양 옆으로 내리 찍은 후 플레이어에게 던진다.
/// 양 옆 찍기 공격 범위: 지름 8m, 던지기 공격 범위: 지름 8m.
/// </summary>
[CreateAssetMenu(fileName = "FGThornHulkBurstAPatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/Thorn/HulkBurstA")]
public class FGThornHulkBurstAPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animSmash = "SmashAttack";
    [SerializeField] private string animThrow = "GrabAndThrowAttack";
    [SerializeField] private float  crossFade = 0.15f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxSmash;
    [SerializeField] private GameObject vfxThrow;

    [Header("헐크 버스트 설정")]
    [SerializeField] private float smashRadius       = 4f;    // 양 옆 찍기 반경 (지름 8m)
    [SerializeField] private float throwRadius       = 4f;    // 던지기 반경 (지름 8m)
    [SerializeField] private float sideOffset        = 3f;    // 좌/우 오프셋
    [SerializeField] private float smashWarning      = 0.8f;  // 찍기 경고 시간
    [SerializeField] private float throwWarning      = 0.8f;  // 던지기 경고 시간

    private FGThornHulkBurstAState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new FGThornHulkBurstAState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ────────────────────────────────────────────────────

    private sealed class FGThornHulkBurstAState : FullLockState<FGThornHulkBurstAPatternSO>
    {
        private enum BurstPhase { SmashWarning, Smash, ThrowWarning, Throw, End }

        private BurstPhase _phase;
        private float      _timer;
        private Vector3    _targetPos;

        public FGThornHulkBurstAState(FGThornHulkBurstAPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;
            _phase = BurstPhase.SmashWarning;
            _timer = 0f;

            _targetPos = ctx.Runtime.PlayerTarget != null
                ? ctx.Runtime.PlayerTarget.position
                : ctx.Transform.position + ctx.Transform.forward * 5f;

            // 양 옆 경고 원
            Vector3 leftPos  = ctx.Transform.position - ctx.Transform.right * Data.sideOffset;
            Vector3 rightPos = ctx.Transform.position + ctx.Transform.right * Data.sideOffset;
            MonsterGroundWarning.Spawn(leftPos,  Data.smashRadius, Data.smashWarning, new Color(1f, 0.2f, 0.2f, 0.9f));
            MonsterGroundWarning.Spawn(rightPos, Data.smashRadius, Data.smashWarning, new Color(1f, 0.2f, 0.2f, 0.9f));

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animSmash))
                ctx.Animator.CrossFade(Data.animSmash, Data.crossFade);
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            switch (_phase)
            {
                case BurstPhase.SmashWarning:
                    if (_timer >= Data.smashWarning)
                    {
                        _phase = BurstPhase.Smash;
                        _timer = 0f;
                        ApplySmash(ctx);
                    }
                    break;

                case BurstPhase.Smash:
                    if (_timer >= 0.5f)
                    {
                        _phase = BurstPhase.ThrowWarning;
                        _timer = 0f;

                        // 플레이어 위치 갱신 + 던지기 경고
                        if (ctx.Runtime.PlayerTarget != null)
                            _targetPos = ctx.Runtime.PlayerTarget.position;
                        MonsterGroundWarning.Spawn(
                            _targetPos, Data.throwRadius, Data.throwWarning,
                            new Color(1f, 0.3f, 0f, 0.9f));

                        if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animThrow))
                            ctx.Animator.CrossFade(Data.animThrow, Data.crossFade);
                    }
                    break;

                case BurstPhase.ThrowWarning:
                    if (_timer >= Data.throwWarning)
                    {
                        _phase = BurstPhase.Throw;
                        _timer = 0f;
                        ApplyThrow(ctx);
                    }
                    break;

                case BurstPhase.Throw:
                    if (_timer >= 0.5f)
                        ctx.Monster.ChangeState<PatrolState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx) { }

        private void ApplySmash(MonsterContext ctx)
        {
            Vector3 leftPos  = ctx.Transform.position - ctx.Transform.right * Data.sideOffset;
            Vector3 rightPos = ctx.Transform.position + ctx.Transform.right * Data.sideOffset;

            if (Data.vfxSmash != null)
            {
                BossEffectPool.SpawnOneShot(Data.vfxSmash, leftPos, Quaternion.identity);
                BossEffectPool.SpawnOneShot(Data.vfxSmash, rightPos, Quaternion.identity);
            }

            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 1.2f);
            float kbForce = ctx.Stat.knockbackForce * 1.5f;

            // 좌측 판정
            ApplyArea(leftPos, Data.smashRadius, damage, kbForce);
            // 우측 판정
            ApplyArea(rightPos, Data.smashRadius, damage, kbForce);
        }

        private void ApplyThrow(MonsterContext ctx)
        {
            if (Data.vfxThrow != null)
                BossEffectPool.SpawnOneShot(Data.vfxThrow, _targetPos, Quaternion.identity);

            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 1.5f);
            float kbForce = ctx.Stat.knockbackForce * 2f;
            ApplyArea(_targetPos, Data.throwRadius, damage, kbForce);
        }

        private static void ApplyArea(Vector3 center, float radius, int damage, float kbForce)
        {
            var hits = Physics.OverlapSphere(center, radius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;
                player.TakeDamage(damage);
                Vector3 dir = (col.transform.position - center).normalized;
                dir.y = 0.5f;
                player.ApplyKnockback(dir.normalized * kbForce);
            }
        }
    }
}
}
