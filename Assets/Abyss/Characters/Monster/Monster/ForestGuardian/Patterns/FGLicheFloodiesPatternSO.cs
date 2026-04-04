using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 리체 폼 패턴 — 리체 플러디즈.
/// 플레이어 주변 랜덤 오프셋 2개 위치에 트랩 소환(반경 4m, 3초 유지 후 피해).
/// </summary>
[CreateAssetMenu(fileName = "FGLicheFloodiesPatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/Liche/LicheFloodies")]
public class FGLicheFloodiesPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animStateName = "Magic03";
    [SerializeField] private float  crossFade     = 0.15f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxTrapPrefab;

    [Header("트랩 설정")]
    [SerializeField] private float trapRadius       = 4f;
    [SerializeField] private float trapDuration     = 3f;
    [SerializeField] private float offsetRangeMin   = 1.5f;
    [SerializeField] private float offsetRangeMax   = 4f;

    private FGLicheFloodiesState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new FGLicheFloodiesState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ────────────────────────────────────────────────────

    private sealed class FGLicheFloodiesState : FullLockState<FGLicheFloodiesPatternSO>
    {
        private float   _timer;
        private bool    _attacked;
        private Vector3 _pos1;
        private Vector3 _pos2;
        private GameObject _vfx1;
        private GameObject _vfx2;

        public FGLicheFloodiesState(FGLicheFloodiesPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;
            _timer   = 0f;
            _attacked = false;

            // 플레이어 위치 기준 랜덤 오프셋 2개
            Vector3 playerPos = ctx.Runtime.PlayerTarget != null
                ? ctx.Runtime.PlayerTarget.position
                : ctx.Transform.position + ctx.Transform.forward * 3f;

            _pos1 = playerPos + RandomOffset(Data.offsetRangeMin, Data.offsetRangeMax);
            _pos2 = playerPos + RandomOffset(Data.offsetRangeMin, Data.offsetRangeMax);

            // 경고 원 2개
            MonsterGroundWarning.Spawn(_pos1, Data.trapRadius, Data.trapDuration,
                new Color(0.8f, 0.2f, 1f, 0.9f));
            MonsterGroundWarning.Spawn(_pos2, Data.trapRadius, Data.trapDuration,
                new Color(0.8f, 0.2f, 1f, 0.9f));

            // VFX 소환
            if (Data.vfxTrapPrefab != null)
            {
                _vfx1 = BossEffectPool.Spawn(Data.vfxTrapPrefab, _pos1, Quaternion.identity);
                _vfx2 = BossEffectPool.Spawn(Data.vfxTrapPrefab, _pos2, Quaternion.identity);
            }

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animStateName))
                ctx.Animator.CrossFade(Data.animStateName, Data.crossFade);
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            if (!_attacked && _timer >= Data.trapDuration)
            {
                _attacked = true;
                ApplyHit(ctx, _pos1);
                ApplyHit(ctx, _pos2);

                if (_vfx1 != null) BossEffectPool.Release(_vfx1);
                if (_vfx2 != null) BossEffectPool.Release(_vfx2);
            }

            if (_timer >= Data.trapDuration + 0.4f)
                ctx.Monster.ChangeState<PatrolState>();
        }

        public override void Exit(MonsterContext ctx)
        {
            if (_vfx1 != null) BossEffectPool.Release(_vfx1);
            if (_vfx2 != null) BossEffectPool.Release(_vfx2);
        }

        private void ApplyHit(MonsterContext ctx, Vector3 pos)
        {
            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
            float kbForce = ctx.Stat.knockbackForce;
            var hits = Physics.OverlapSphere(pos, Data.trapRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                player.TakeDamage(damage);
                Vector3 dir = (col.transform.position - pos).normalized;
                dir.y = 0.3f;
                player.ApplyKnockback(dir.normalized * kbForce);
            }
        }

        private static Vector3 RandomOffset(float minR, float maxR)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist  = Random.Range(minR, maxR);
            return new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
        }
    }
}
}
