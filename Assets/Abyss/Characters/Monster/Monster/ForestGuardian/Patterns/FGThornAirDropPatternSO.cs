using UnityEngine;
using UnityEngine.AI;

namespace Abyss.Monster
{
/// <summary>
/// 가시 폼 패턴 — 에어 드롭.
/// 위로 날아 플레이어에게 낙하(착지 범위 지름 8m → 반경 4m).
/// 경고 표시 후 착지.
/// </summary>
[CreateAssetMenu(fileName = "FGThornAirDropPatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/Thorn/AirDrop")]
public class FGThornAirDropPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animJump    = "JumpUp";
    [SerializeField] private string animFall    = "FallDown";
    [SerializeField] private string animLand    = "Land";
    [SerializeField] private float  crossFade   = 0.15f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxLand;

    [Header("에어 드롭 설정")]
    [SerializeField] private float landRadius       = 4f;    // 착지 반경 (지름 8m)
    [SerializeField] private float riseHeight       = 5f;    // 상승 높이
    [SerializeField] private float riseDuration     = 0.5f;  // 상승 시간
    [SerializeField] private float warningDuration  = 1.5f;  // 경고 표시 시간
    [SerializeField] private float fallDuration     = 0.7f;  // 낙하 시간

    private FGThornAirDropState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new FGThornAirDropState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ────────────────────────────────────────────────────

    private sealed class FGThornAirDropState : FullLockState<FGThornAirDropPatternSO>
    {
        private enum AirPhase { Rise, Warning, Fall, Land }

        private AirPhase _phase;
        private float    _phaseTimer;
        private Vector3  _riseStartPos;
        private Vector3  _targetPos;
        private Rigidbody _rb;

        public FGThornAirDropState(FGThornAirDropPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            _phase      = AirPhase.Rise;
            _phaseTimer = 0f;
            _riseStartPos = ctx.Transform.position;
            _targetPos  = ctx.Runtime.PlayerTarget != null
                ? ctx.Runtime.PlayerTarget.position
                : ctx.Transform.position;

            _rb = ctx.Monster.GetComponent<Rigidbody>();

            // Agent 일시 비활성
            ctx.Agent.enabled = false;

            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.useGravity  = false;
            }

            // 경고 미리 표시
            MonsterGroundWarning.Spawn(
                _targetPos, Data.landRadius, Data.riseDuration + Data.warningDuration,
                new Color(1f, 0.3f, 0f, 0.9f));

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animJump))
                ctx.Animator.CrossFade(Data.animJump, Data.crossFade);
        }

        public override void Update(MonsterContext ctx)
        {
            _phaseTimer += Time.deltaTime;

            switch (_phase)
            {
                case AirPhase.Rise:
                {
                    float t = Mathf.Clamp01(_phaseTimer / Data.riseDuration);
                    Vector3 risePos = _riseStartPos + Vector3.up * (Data.riseHeight * t);
                    ctx.Transform.position = risePos;

                    if (_phaseTimer >= Data.riseDuration)
                    {
                        _phase      = AirPhase.Warning;
                        _phaseTimer = 0f;

                        if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animFall))
                            ctx.Animator.CrossFade(Data.animFall, Data.crossFade);
                    }
                    break;
                }

                case AirPhase.Warning:
                    if (_phaseTimer >= Data.warningDuration)
                    {
                        _phase      = AirPhase.Fall;
                        _phaseTimer = 0f;
                        _riseStartPos = ctx.Transform.position;
                    }
                    break;

                case AirPhase.Fall:
                {
                    float t = Mathf.Clamp01(_phaseTimer / Data.fallDuration);
                    Vector3 fallTarget = new Vector3(_targetPos.x,
                        _targetPos.y, _targetPos.z);
                    ctx.Transform.position = Vector3.Lerp(_riseStartPos, fallTarget, t);

                    if (_phaseTimer >= Data.fallDuration)
                    {
                        _phase      = AirPhase.Land;
                        _phaseTimer = 0f;
                        ApplyLanding(ctx);
                    }
                    break;
                }

                case AirPhase.Land:
                    if (_phaseTimer >= 0.4f)
                        ctx.Monster.ChangeState<PatrolState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            // Agent 재활성
            ctx.Agent.enabled = true;
            if (ctx.Agent.isOnNavMesh)
            {
                ctx.Agent.Warp(ctx.Transform.position);
                if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
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
