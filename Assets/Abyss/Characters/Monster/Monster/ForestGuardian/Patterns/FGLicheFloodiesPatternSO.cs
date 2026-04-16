using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 브레스 패턴 (2페이즈 전용).
///
/// 보스 정면으로 브레스를 직선 발사하며 플레이어 방향으로 회전 추적.
/// 사정거리 10m, 가로 1m, 회전 속도 60°/s, 지속 3.5초.
/// 플레이어는 달려야 피할 수 있음.
/// </summary>
[CreateAssetMenu(fileName = "FGLicheFloodiesPatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/Breath")]
public class FGBreathPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animName  = "MagicAttack3";
    [SerializeField] private float  crossFade = 0.1f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxBreath;
    [SerializeField] private Vector3    breathLocalOffset = new Vector3(0f, 1.2f, 1.5f);

    [Header("브레스 설정")]
    [SerializeField] private float warningDuration  = 1.0f;
    [SerializeField] private float breathDuration   = 3.5f;
    [SerializeField] private float castRadius       = 0.5f;    // SphereCast 반경 (가로 1m)
    [SerializeField] private float castMaxDist      = 10f;
    [SerializeField] private float tickInterval     = 0.2f;

    [Header("회전")]
    [SerializeField] private float aimTurnSpeed    = 120f;
    [SerializeField] private float breathTurnSpeed = 60f;      // 추적 회전 속도

    [Header("쿨다운")]
    [SerializeField] private float patternCooldown = 15f;

    private float _cooldownEndTime = float.MinValue;
    private FGBreathState _state;

    public override void Initialize(BossPatternContext ctx)
    {
        _cooldownEndTime = float.MinValue;
        _state = new FGBreathState(this);
    }

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (Time.time < _cooldownEndTime) return false;
        // 2페이즈 전용
        var fg = (ctx.Ctx.Monster as ForestGuardianMonster)?.FGBlackboard;
        return fg != null && fg.IsPhase2;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
    internal void StartCooldown() => _cooldownEndTime = Time.time + patternCooldown;

    // ── 내부 상태 ─────────────────────────────────────────────────────

    private sealed class FGBreathState : FullLockState<FGBreathPatternSO>
    {
        private int        _phase;        // 0=조준, 1=브레스
        private float      _timer;
        private float      _tickTimer;
        private GameObject _activeVfx;

        public FGBreathState(FGBreathPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animName, Data.crossFade);

            _phase     = 0;
            _timer     = 0f;
            _tickTimer = 0f;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            // ── phase 0: 조준 ─────────────────────────────────────
            if (_phase == 0)
            {
                FacePlayer(ctx, Data.aimTurnSpeed);
                SpawnBreathWarning(ctx, 0.25f);

                if (_timer < Data.warningDuration) return;

                // 브레스 VFX 활성화
                if (Data.vfxBreath != null)
                {
                    _activeVfx = BossEffectPool.Spawn(
                        Data.vfxBreath,
                        ctx.Transform.position,
                        ctx.Transform.rotation,
                        ctx.Transform,
                        false);
                    if (_activeVfx != null)
                    {
                        _activeVfx.transform.localPosition = Data.breathLocalOffset;
                        _activeVfx.transform.localRotation = Quaternion.identity;
                    }
                }

                _phase     = 1;
                _timer     = 0f;
                _tickTimer = 0f;
                return;
            }

            // ── phase 1: 브레스 발사 ──────────────────────────────
            FacePlayer(ctx, Data.breathTurnSpeed);

            _tickTimer += Time.deltaTime;
            if (_tickTimer >= Data.tickInterval)
            {
                _tickTimer = 0f;
                SpawnBreathWarning(ctx, Data.tickInterval + 0.05f);
                DoBreathHit(ctx);
            }

            if (_timer >= Data.breathDuration)
                ctx.Monster.ChangeState<PatrolState>();
        }

        public override void Exit(MonsterContext ctx)
        {
            if (_activeVfx != null)
            {
                BossEffectPool.Release(_activeVfx);
                _activeVfx = null;
            }
            Data.StartCooldown();
        }

        private void SpawnBreathWarning(MonsterContext ctx, float duration)
        {
            Vector3 fwd = GetFlatForward(ctx);
            Vector3 origin = ctx.Transform.position;
            MonsterGroundWarning.SpawnRect(
                origin, fwd,
                Data.castRadius * 2f, Data.castMaxDist,
                duration,
                new Color(0.2f, 0.8f, 0.2f, 0.7f));
        }

        private void DoBreathHit(MonsterContext ctx)
        {
            Vector3 origin = ctx.Transform.position + Vector3.up * 1.2f + ctx.Transform.forward * 1f;
            Vector3 fwd    = GetFlatForward(ctx);
            int damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 0.4f);

            var hits = Physics.SphereCastAll(origin, Data.castRadius, fwd, Data.castMaxDist);
            foreach (var hit in hits)
            {
                var player = hit.collider.GetComponent<PlayerController>()
                          ?? hit.collider.GetComponentInParent<PlayerController>();
                if (player == null) continue;
                player.TakeDamage(damage);
                break;
            }
        }

        private static void FacePlayer(MonsterContext ctx, float speed)
        {
            if (ctx.Runtime.PlayerTarget == null) return;
            Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;
            ctx.Transform.rotation = Quaternion.RotateTowards(
                ctx.Transform.rotation,
                Quaternion.LookRotation(dir.normalized),
                speed * Time.deltaTime);
        }

        private static Vector3 GetFlatForward(MonsterContext ctx)
        {
            Vector3 fwd = ctx.Transform.forward;
            fwd.y = 0f;
            return fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
        }
    }
}
}
