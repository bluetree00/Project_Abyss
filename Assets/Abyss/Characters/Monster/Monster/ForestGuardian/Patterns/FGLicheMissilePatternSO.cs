using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 땅 가르기 패턴 (2페이즈 전용).
///
/// 정면으로 3회 연속 땅을 찍어 공격.
/// 타격할수록 범위가 넓어지는 장판 (폭 4m→5m→6m, 거리 5m/타격).
/// 타격 간격 0.4초, 장판 잔류 1.5초.
/// </summary>
[CreateAssetMenu(fileName = "FGLicheMissilePatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/GroundSlash")]
public class FGGroundSlashPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animAttack = "VerticalAttack";
    [SerializeField] private float  crossFade  = 0.1f;

    [Header("땅 가르기 설정")]
    [SerializeField] private int   strikeCount    = 3;
    [SerializeField] private float strikeInterval = 0.4f;   // 타격 간격
    [SerializeField] private float strikeDist     = 5f;     // 타격당 거리
    [SerializeField] private float baseWidth      = 4f;     // 1타 폭
    [SerializeField] private float widthIncrease  = 1f;     // 타당 폭 증가
    [SerializeField] private float hazardDuration = 1.5f;   // 장판 잔류
    [SerializeField] private float windupDuration = 0.5f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxStrike;

    [Header("쿨다운")]
    [SerializeField] private float patternCooldown = 10f;

    private float _cooldownEndTime = float.MinValue;
    private FGGroundSlashState _state;

    public override void Initialize(BossPatternContext ctx)
    {
        _cooldownEndTime = float.MinValue;
        _state = new FGGroundSlashState(this);
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

    private sealed class FGGroundSlashState : FullLockState<FGGroundSlashPatternSO>
    {
        private enum Sub { Windup, Striking }

        private Sub   _sub;
        private float _timer;
        private int   _strikesDone;

        public FGGroundSlashState(FGGroundSlashPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;

            FacePlayer(ctx);

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animAttack, Data.crossFade);

            _sub         = Sub.Windup;
            _timer       = 0f;
            _strikesDone = 0;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            if (_sub == Sub.Windup)
            {
                if (_timer < Data.windupDuration) return;
                _sub   = Sub.Striking;
                _timer = 0f;
                DoStrike(ctx);
                return;
            }

            // ── 연속 타격 ────────────────────────────────────────
            if (_timer < Data.strikeInterval) return;
            _timer = 0f;

            if (_strikesDone < Data.strikeCount)
            {
                DoStrike(ctx);
            }
            else
            {
                ctx.Monster.ChangeState<PatrolState>();
            }
        }

        public override void Exit(MonsterContext ctx) => Data.StartCooldown();

        private void DoStrike(MonsterContext ctx)
        {
            _strikesDone++;
            float width = Data.baseWidth + Data.widthIncrease * (_strikesDone - 1);

            // 장판: 이전 타격 끝점에서 strikeDist 거리 직사각형
            Vector3 fwd    = GetFlatForward(ctx);
            float   offset = Data.strikeDist * _strikesDone - Data.strikeDist * 0.5f;
            Vector3 center = ctx.Transform.position + fwd * offset;

            MonsterGroundWarning.SpawnRect(
                center, fwd, width, Data.strikeDist,
                Data.hazardDuration,
                new Color(0.8f, 0.2f, 0.1f, 0.85f));

            // 데미지 판정
            int damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
            var hits = Physics.OverlapBox(
                center + Vector3.up * 0.5f,
                new Vector3(width * 0.5f, 0.5f, Data.strikeDist * 0.5f),
                Quaternion.LookRotation(fwd));

            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                Vector3 dir = (col.transform.position - ctx.Transform.position).normalized;
                dir.y = 0.2f;
                player.TakeDamage(damage);
                player.ApplyKnockback(dir * ctx.Stat.knockbackForce * 0.7f);
                break;
            }

            if (Data.vfxStrike != null)
                BossEffectPool.SpawnOneShot(Data.vfxStrike, center, Quaternion.LookRotation(fwd));
        }

        private static void FacePlayer(MonsterContext ctx)
        {
            if (ctx.Runtime.PlayerTarget == null) return;
            Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                ctx.Transform.rotation = Quaternion.LookRotation(dir.normalized);
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
