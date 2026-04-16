using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 돌 던지기 패턴 (1페이즈 전용 원거리 패턴).
///
/// 자신의 앞에 돌을 소환하고 플레이어에게 던진다.
/// 사정거리 5~10m, 투사체 속도 15m/s, 투사체 반경 2.5m.
/// 던지기 전 들어올리는 모션이 길어 피하기 쉬움.
/// </summary>
[CreateAssetMenu(fileName = "FGLicheBallPatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/RockThrow")]
public class FGRockThrowPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animWindup = "MagicAttack1";
    [SerializeField] private string animThrow  = "MagicAttack2";
    [SerializeField] private float  crossFade  = 0.1f;

    [Header("투사체")]
    [SerializeField] private float windupDuration   = 1.2f;   // 들어올리는 모션 시간
    [SerializeField] private float projectileSpeed  = 15f;
    [SerializeField] private float projectileRadius = 2.5f;
    [SerializeField] private float warningDuration  = 0.3f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxProjectile;
    [SerializeField] private GameObject vfxImpact;

    [Header("쿨다운")]
    [SerializeField] private float patternCooldown = 8f;

    private float _cooldownEndTime = float.MinValue;
    private FGRockThrowState _state;

    public override void Initialize(BossPatternContext ctx)
    {
        _cooldownEndTime = float.MinValue;
        _state = new FGRockThrowState(this);
    }

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (Time.time < _cooldownEndTime) return false;
        // 1페이즈 전용
        var fg = (ctx.Ctx.Monster as ForestGuardianMonster)?.FGBlackboard;
        if (fg != null && fg.IsPhase2) return false;
        return ctx.Ctx.Runtime.DistToPlayer >= 5f;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
    internal void StartCooldown() => _cooldownEndTime = Time.time + patternCooldown;

    // ── 내부 상태 ─────────────────────────────────────────────────────

    private sealed class FGRockThrowState : FullLockState<FGRockThrowPatternSO>
    {
        private enum Sub { Windup, Flying }

        private Sub     _sub;
        private float   _timer;
        private Vector3 _projPos;
        private Vector3 _projDir;
        private bool       _launched;
        private bool       _hitApplied;
        private GameObject _activeVfx;
        private Material   _fallbackMat;   // vfxProjectile 미설정 시 구체 폴백 머티리얼
        private bool       _isFallback;

        public FGRockThrowState(FGRockThrowPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;

            FacePlayer(ctx);

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animWindup, Data.crossFade);

            _sub        = Sub.Windup;
            _timer      = 0f;
            _launched   = false;
            _hitApplied = false;
            _activeVfx  = null;
            _fallbackMat = null;
            _isFallback  = false;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            if (_sub == Sub.Windup)
            {
                FacePlayer(ctx);
                if (_timer < Data.windupDuration) return;

                // 투사체 발사
                LaunchProjectile(ctx);
                _sub   = Sub.Flying;
                _timer = 0f;
                return;
            }

            // ── 투사체 이동 ────────────────────────────────────────
            _projPos += _projDir * Data.projectileSpeed * Time.deltaTime;

            if (_activeVfx != null)
                _activeVfx.transform.position = _projPos;

            // 플레이어 충돌 체크
            if (!_hitApplied)
            {
                var hits = Physics.OverlapSphere(_projPos, Data.projectileRadius);
                foreach (var col in hits)
                {
                    var player = col.GetComponent<PlayerController>()
                              ?? col.GetComponentInParent<PlayerController>();
                    if (player == null) continue;

                    _hitApplied = true;
                    int damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
                    player.TakeDamage(damage);
                    player.ApplyKnockback(_projDir.normalized * ctx.Stat.knockbackForce * 0.5f);

                    if (Data.vfxImpact != null)
                        BossEffectPool.SpawnOneShot(Data.vfxImpact, _projPos, Quaternion.identity);

                    DestroyVfx();
                    ctx.Monster.ChangeState<PatrolState>();
                    return;
                }
            }

            // 최대 거리 초과 또는 타임아웃
            float elapsed = Time.time;
            float maxTime = 30f / Data.projectileSpeed + 0.5f;
            if (_timer >= maxTime)
            {
                DestroyVfx();
                ctx.Monster.ChangeState<PatrolState>();
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            DestroyVfx();
            Data.StartCooldown();
        }

        private void LaunchProjectile(MonsterContext ctx)
        {
            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animThrow, Data.crossFade);

            _projPos = ctx.Transform.position + ctx.Transform.forward * 1f + Vector3.up * 1f;

            Vector3 targetPos = ctx.Runtime.PlayerTarget != null
                ? ctx.Runtime.PlayerTarget.position
                : ctx.Transform.position + ctx.Transform.forward * 8f;

            Vector3 dir = targetPos - _projPos;
            dir.y = 0f;
            _projDir = dir.sqrMagnitude > 0.001f ? dir.normalized : ctx.Transform.forward;

            // 경고 장판
            MonsterGroundWarning.Spawn(targetPos, Data.projectileRadius,
                Data.warningDuration, new Color(1f, 0.4f, 0.1f, 0.8f));

            if (Data.vfxProjectile != null)
            {
                _isFallback = false;
                _activeVfx  = BossEffectPool.Spawn(Data.vfxProjectile, _projPos,
                    Quaternion.LookRotation(_projDir), null, false);
            }
            else
            {
                // VFX 미설정 시 회색 구체 프리미티브로 투사체 표현
                _isFallback = true;
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "[RockThrow_Rock]";
                Object.Destroy(sphere.GetComponent<Collider>());
                _fallbackMat = new Material(sphere.GetComponent<Renderer>().sharedMaterial);
                if (_fallbackMat.HasProperty("_BaseColor")) _fallbackMat.SetColor("_BaseColor", new Color(0.45f, 0.4f, 0.35f, 1f));
                if (_fallbackMat.HasProperty("_Color"))     _fallbackMat.SetColor("_Color",     new Color(0.45f, 0.4f, 0.35f, 1f));
                sphere.GetComponent<Renderer>().material = _fallbackMat;
                sphere.transform.localScale = Vector3.one * (Data.projectileRadius * 0.4f);
                sphere.transform.position   = _projPos;
                _activeVfx = sphere;
            }
        }

        private void DestroyVfx()
        {
            if (_activeVfx != null)
            {
                if (_isFallback)
                {
                    Object.Destroy(_fallbackMat);
                    Object.Destroy(_activeVfx);
                    _fallbackMat = null;
                }
                else
                {
                    BossEffectPool.Release(_activeVfx);
                }
                _activeVfx  = null;
                _isFallback = false;
            }
        }

        private static void FacePlayer(MonsterContext ctx)
        {
            if (ctx.Runtime.PlayerTarget == null) return;
            Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                ctx.Transform.rotation = Quaternion.RotateTowards(
                    ctx.Transform.rotation,
                    Quaternion.LookRotation(dir.normalized),
                    180f * Time.deltaTime);
        }
    }
}
}
