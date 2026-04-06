using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 리체 폼 패턴 — 리체 브레스.
/// 정면 방향으로 브레스 발사(폭 1m, 거리 8m). 2초 지속, 0.3초마다 판정.
/// </summary>
[CreateAssetMenu(fileName = "FGLicheBreathPatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/Liche/LicheBreath")]
public class FGLicheBreathPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animStateName = "Magic02";
    [SerializeField] private float  crossFade     = 0.15f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxPrefab;

    [Header("브레스 설정")]
    [SerializeField] private float breathDuration  = 2f;
    [SerializeField] private float breathWidth     = 1f;
    [SerializeField] private float breathLength    = 8f;
    [SerializeField] private float tickInterval    = 0.3f;

    private FGLicheBreathState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new FGLicheBreathState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ────────────────────────────────────────────────────

    private sealed class FGLicheBreathState : FullLockState<FGLicheBreathPatternSO>
    {
        private float _elapsed;
        private float _tickTimer;
        private GameObject _vfxInstance;

        public FGLicheBreathState(FGLicheBreathPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;
            _elapsed   = 0f;
            _tickTimer = 0f;

            // 그리드 경고 (Front2: 정면 2칸)
            MonsterGroundWarning.SpawnGrid(
                ctx.Transform.position,
                ctx.Transform.forward,
                MonsterGroundWarning.GridShape.Front2,
                Data.breathDuration,
                new Color(0.3f, 0.8f, 1f, 0.9f));

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animStateName))
                ctx.Animator.CrossFade(Data.animStateName, Data.crossFade);

            if (Data.vfxPrefab != null)
                _vfxInstance = BossEffectPool.Spawn(Data.vfxPrefab,
                    ctx.Transform.position + ctx.Transform.forward * (Data.breathLength * 0.5f),
                    ctx.Transform.rotation);
        }

        public override void Update(MonsterContext ctx)
        {
            _elapsed   += Time.deltaTime;
            _tickTimer += Time.deltaTime;

            // 기획서: 플레이어 움직임을 따라간다
            if (ctx.Runtime.PlayerTarget != null)
            {
                Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(toPlayer);
                    ctx.Transform.rotation = Quaternion.RotateTowards(
                        ctx.Transform.rotation, targetRot, 90f * Time.deltaTime);
                }
            }

            // VFX 위치 갱신 (현재 forward 기준)
            if (_vfxInstance != null)
            {
                _vfxInstance.transform.position = ctx.Transform.position
                    + ctx.Transform.forward * (Data.breathLength * 0.5f);
                _vfxInstance.transform.rotation = ctx.Transform.rotation;
            }

            if (_tickTimer >= Data.tickInterval)
            {
                _tickTimer = 0f;
                ApplyHit(ctx);
            }

            if (_elapsed >= Data.breathDuration)
            {
                if (_vfxInstance != null)
                    BossEffectPool.Release(_vfxInstance);
                ctx.Monster.ChangeState<PatrolState>();
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            if (_vfxInstance != null)
                BossEffectPool.Release(_vfxInstance);
        }

        private void ApplyHit(MonsterContext ctx)
        {
            // 정면 BoxCast로 판정
            Vector3 center = ctx.Transform.position + ctx.Transform.forward * (Data.breathLength * 0.5f);
            Vector3 halfExtents = new Vector3(Data.breathWidth * 0.5f, 1f, Data.breathLength * 0.5f);
            var hits = Physics.OverlapBox(center, halfExtents, ctx.Transform.rotation);

            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 0.4f); // 지속 피해는 줄임
            float kbForce = ctx.Stat.knockbackForce * 0.3f;

            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                player.TakeDamage(damage);
                Vector3 dir = ctx.Transform.forward;
                dir.y = 0.2f;
                player.ApplyKnockback(dir.normalized * kbForce);
            }
        }
    }
}
}
