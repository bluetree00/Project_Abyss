using UnityEngine;
using UnityEngine.AI;

namespace Abyss.Monster
{
/// <summary>
/// 가시 폼 패턴 — 포스 버스트 A.
/// 위로 날아 폭발 2회(각 지름 8m) + 낙하(지름 8m).
/// </summary>
[CreateAssetMenu(fileName = "FGThornForceBurstAPatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/Thorn/ForceBurstA")]
public class FGThornForceBurstAPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animRise     = "JumpUp";
    [SerializeField] private string animExplode  = "Explode";
    [SerializeField] private string animFall     = "FallDown";
    [SerializeField] private string animLand     = "Land";
    [SerializeField] private float  crossFade    = 0.15f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxExplosion;
    [SerializeField] private GameObject vfxLand;

    [Header("폭발 설정")]
    [SerializeField] private float explosionRadius  = 4f;    // 지름 8m → 반경 4m
    [SerializeField] private float landRadius       = 4f;    // 낙하 반경
    [SerializeField] private float riseHeight       = 6f;
    [SerializeField] private float riseDuration     = 0.6f;
    [SerializeField] private float explodeDuration  = 1.5f;  // 경고+폭발 1회 소요
    [SerializeField] private float fallDuration     = 0.8f;

    private FGThornForceBurstAState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new FGThornForceBurstAState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ────────────────────────────────────────────────────

    private sealed class FGThornForceBurstAState : FullLockState<FGThornForceBurstAPatternSO>
    {
        private enum BurstPhase { Rise, Explode1, Explode2, Fall, Land }

        private BurstPhase _phase;
        private float      _phaseTimer;
        private Vector3    _riseStart;
        private Vector3    _fallStart;
        private Vector3    _targetPos;
        private int        _explodeDone;

        public FGThornForceBurstAState(FGThornForceBurstAPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            _phase       = BurstPhase.Rise;
            _phaseTimer  = 0f;
            _riseStart   = ctx.Transform.position;
            _explodeDone = 0;
            _targetPos   = ctx.Runtime.PlayerTarget != null
                ? ctx.Runtime.PlayerTarget.position
                : ctx.Transform.position;

            ctx.Agent.enabled = false;

            MonsterGroundWarning.Spawn(_targetPos, Data.landRadius,
                Data.riseDuration + Data.explodeDuration * 2 + Data.fallDuration,
                new Color(1f, 0.2f, 0f, 0.9f));

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animRise))
                ctx.Animator.CrossFade(Data.animRise, Data.crossFade);
        }

        public override void Update(MonsterContext ctx)
        {
            _phaseTimer += Time.deltaTime;

            switch (_phase)
            {
                case BurstPhase.Rise:
                {
                    float t = Mathf.Clamp01(_phaseTimer / Data.riseDuration);
                    ctx.Transform.position = _riseStart + Vector3.up * (Data.riseHeight * t);
                    if (_phaseTimer >= Data.riseDuration)
                    {
                        _phase      = BurstPhase.Explode1;
                        _phaseTimer = 0f;
                        StartExplode(ctx, 1);
                    }
                    break;
                }

                case BurstPhase.Explode1:
                    if (_phaseTimer >= Data.explodeDuration)
                    {
                        ApplyExplosion(ctx);
                        _phase      = BurstPhase.Explode2;
                        _phaseTimer = 0f;
                        StartExplode(ctx, 2);
                    }
                    break;

                case BurstPhase.Explode2:
                    if (_phaseTimer >= Data.explodeDuration)
                    {
                        ApplyExplosion(ctx);
                        _phase      = BurstPhase.Fall;
                        _phaseTimer = 0f;
                        _fallStart  = ctx.Transform.position;

                        if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animFall))
                            ctx.Animator.CrossFade(Data.animFall, Data.crossFade);
                    }
                    break;

                case BurstPhase.Fall:
                {
                    float t = Mathf.Clamp01(_phaseTimer / Data.fallDuration);
                    ctx.Transform.position = Vector3.Lerp(_fallStart, _targetPos, t);
                    if (_phaseTimer >= Data.fallDuration)
                    {
                        _phase      = BurstPhase.Land;
                        _phaseTimer = 0f;
                        ApplyLanding(ctx);
                    }
                    break;
                }

                case BurstPhase.Land:
                    if (_phaseTimer >= 0.5f)
                        ctx.Monster.ChangeState<PatrolState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            ctx.Agent.enabled = true;
            if (ctx.Agent.isOnNavMesh)
            {
                ctx.Agent.Warp(ctx.Transform.position);
                if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
            }
        }

        private void StartExplode(MonsterContext ctx, int index)
        {
            Vector3 pos = ctx.Transform.position;
            pos.y = _targetPos.y;

            MonsterGroundWarning.Spawn(pos, Data.explosionRadius,
                Data.explodeDuration, new Color(1f, 0.4f, 0f, 0.9f));

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animExplode))
                ctx.Animator.CrossFade(Data.animExplode, Data.crossFade);
        }

        private void ApplyExplosion(MonsterContext ctx)
        {
            Vector3 pos = ctx.Transform.position;
            pos.y = _targetPos.y;

            if (Data.vfxExplosion != null)
                BossEffectPool.SpawnOneShot(Data.vfxExplosion, pos, Quaternion.identity);

            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 1.2f);
            float kbForce = ctx.Stat.knockbackForce * 1.5f;
            var hits = Physics.OverlapSphere(pos, Data.explosionRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;
                player.TakeDamage(damage);
                Vector3 dir = (col.transform.position - pos).normalized;
                dir.y = 0.4f;
                player.ApplyKnockback(dir.normalized * kbForce);
            }
        }

        private void ApplyLanding(MonsterContext ctx)
        {
            if (Data.vfxLand != null)
                BossEffectPool.SpawnOneShot(Data.vfxLand,
                    ctx.Transform.position, Quaternion.identity);

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animLand))
                ctx.Animator.CrossFade(Data.animLand, Data.crossFade);

            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 1.5f);
            float kbForce = ctx.Stat.knockbackForce * 2f;
            var hits = Physics.OverlapSphere(ctx.Transform.position, Data.landRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;
                player.TakeDamage(damage);
                Vector3 dir = (col.transform.position - ctx.Transform.position).normalized;
                dir.y = 0.5f;
                player.ApplyKnockback(dir.normalized * kbForce);
            }
        }
    }
}
}
