using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 리체 폼 패턴 — 리체 미사일.
/// 부채꼴(45°) 방향으로 미사일 5발 연속 발사(0.2초 간격, 거리 5m).
/// </summary>
[CreateAssetMenu(fileName = "FGLicheMissilePatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/Liche/LicheMissile")]
public class FGLicheMissilePatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animStateName = "Magic04";
    [SerializeField] private float  crossFade     = 0.15f;

    [Header("미사일 설정")]
    [SerializeField] private MonsterProjectile missilePrefab;
    [SerializeField] private int   missileCount    = 5;
    [SerializeField] private float missileInterval = 0.2f;
    [SerializeField] private float missileSpeed    = 10f;
    [SerializeField] private float missileRange    = 5f;
    [SerializeField] private float spreadAngle     = 45f;   // 부채꼴 전체 각도
    [SerializeField] private float missileHeight   = 1.2f;  // 발사 위치 높이 오프셋

    private FGLicheMissileState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new FGLicheMissileState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ────────────────────────────────────────────────────

    private sealed class FGLicheMissileState : FullLockState<FGLicheMissilePatternSO>
    {
        private float _elapsed;
        private int   _firedCount;

        public FGLicheMissileState(FGLicheMissilePatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;
            _elapsed    = 0f;
            _firedCount = 0;

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animStateName))
                ctx.Animator.CrossFade(Data.animStateName, Data.crossFade);
        }

        public override void Update(MonsterContext ctx)
        {
            _elapsed += Time.deltaTime;

            // 발사 타이밍: firedCount * interval
            while (_firedCount < Data.missileCount
                && _elapsed >= _firedCount * Data.missileInterval)
            {
                FireMissile(ctx, _firedCount);
                _firedCount++;
            }

            float totalDuration = 2f; // 기획서: 공격 지속 시간 2초
            if (_elapsed >= totalDuration)
                ctx.Monster.ChangeState<PatrolState>();
        }

        public override void Exit(MonsterContext ctx) { }

        private void FireMissile(MonsterContext ctx, int index)
        {
            if (Data.missilePrefab == null) return;

            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
            float kbForce = ctx.Stat.knockbackForce;

            // 부채꼴 각도 계산: 5발 → -22.5, -11.25, 0, 11.25, 22.5도
            float halfSpread = Data.spreadAngle * 0.5f;
            float step = Data.missileCount > 1
                ? Data.spreadAngle / (Data.missileCount - 1)
                : 0f;
            float angle = -halfSpread + step * index;

            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * ctx.Transform.forward;
            Vector3 spawnPos = ctx.Transform.position
                             + ctx.Transform.forward * 0.5f
                             + Vector3.up * Data.missileHeight;

            var projObj = BossEffectPool.Spawn(
                Data.missilePrefab.gameObject, spawnPos, Quaternion.LookRotation(dir));
            var proj = projObj != null ? projObj.GetComponent<MonsterProjectile>() : null;
            if (proj == null) return;
            proj.Init(dir, Data.missileSpeed, Data.missileRange, damage, kbForce);
        }
    }
}
}
