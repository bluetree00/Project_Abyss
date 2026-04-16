using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 회전 킥 패턴.
///
/// 플레이어 방향으로 이동하며 제자리 회전. 360° 전방위 히트박스.
///
/// 1페이즈 : 이동 속도 3m/s, 지속 3초, 반경 4m. 준비 모션이 김.
/// 2페이즈 : 이동 속도 6m/s, 지속 2초, 반경 6m. 훨씬 빠르게 접근.
///
/// 히트박스 보정: 이동 속도에 비례하여 데미지 최대 2배까지 상승.
///
/// 경고 시각: 보라색 원형 장판(반경 = hitRadius), 스핀 중에는 보스를 따라 이동.
/// </summary>
[CreateAssetMenu(fileName = "FGSpiderDashPatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/SpinKick")]
public class FGSpinKickPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animWindup   = "360SpinKick";
    [SerializeField] private string animSpinning = "720SpinKick";
    [SerializeField] private float  crossFade    = 0.1f;

    [Header("Phase 1 설정")]
    [SerializeField] private float p1_moveSpeed     = 3f;
    [SerializeField] private float p1_duration      = 3f;
    [SerializeField] private float p1_hitRadius     = 4f;
    [SerializeField] private float p1_windupDuration = 0.8f;

    [Header("Phase 2 설정")]
    [SerializeField] private float p2_moveSpeed     = 6f;
    [SerializeField] private float p2_duration      = 2f;
    [SerializeField] private float p2_hitRadius     = 6f;
    [SerializeField] private float p2_windupDuration = 0.3f;

    [Header("공격")]
    [SerializeField] private float tickInterval = 0.2f;   // 히트 판정 간격

    [Header("쿨다운")]
    [SerializeField] private float patternCooldown = 7f;

    private float _cooldownEndTime = float.MinValue;
    private FGSpinKickState _state;

    public override void Initialize(BossPatternContext ctx)
    {
        _cooldownEndTime = float.MinValue;
        _state = new FGSpinKickState(this);
    }

    public override bool CanExecute(BossPatternContext ctx) => Time.time >= _cooldownEndTime;

    public override SpecialStateBase GetRuntimeState() => _state;
    internal void StartCooldown() => _cooldownEndTime = Time.time + patternCooldown;

    // ── 내부 상태 ─────────────────────────────────────────────────────

    private sealed class FGSpinKickState : FullLockState<FGSpinKickPatternSO>
    {
        private enum Sub { Windup, Spinning }

        private Sub   _sub;
        private float _timer;
        private float _tickTimer;
        private float _spinDuration;
        private float _moveSpeed;
        private float _hitRadius;
        private bool  _isPhase2;

        // 스핀 범위 시각화 디스크
        private GameObject _spinDiscObj;
        private Material   _spinDiscMat;

        public FGSpinKickState(FGSpinKickPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            var fg = (ctx.Monster as ForestGuardianMonster)?.FGBlackboard;
            _isPhase2    = fg?.IsPhase2 ?? false;
            _moveSpeed   = _isPhase2 ? Data.p2_moveSpeed : Data.p1_moveSpeed;
            _hitRadius   = _isPhase2 ? Data.p2_hitRadius : Data.p1_hitRadius;
            _spinDuration = _isPhase2 ? Data.p2_duration : Data.p1_duration;

            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animWindup, Data.crossFade);

            // 경고 장판: 스핀 반경 보라색 원 (윈드업 + 스핀 전체 지속)
            float windupDur = _isPhase2 ? Data.p2_windupDuration : Data.p1_windupDuration;
            MonsterGroundWarning.Spawn(
                ctx.Transform.position, _hitRadius,
                windupDur + _spinDuration + 0.15f,
                new Color(0.85f, 0.15f, 0.9f, 0.8f));

            // 스핀 디스크: 납작한 실린더로 범위 시각화
            _spinDiscObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _spinDiscObj.name = "[SpinKick_Disc]";
            Object.Destroy(_spinDiscObj.GetComponent<Collider>());
            _spinDiscMat = new Material(_spinDiscObj.GetComponent<Renderer>().sharedMaterial);
            if (_spinDiscMat.HasProperty("_BaseColor")) _spinDiscMat.SetColor("_BaseColor", new Color(0.85f, 0.15f, 0.9f, 1f));
            if (_spinDiscMat.HasProperty("_Color"))     _spinDiscMat.SetColor("_Color",     new Color(0.85f, 0.15f, 0.9f, 1f));
            _spinDiscObj.GetComponent<Renderer>().material = _spinDiscMat;
            _spinDiscObj.transform.localScale = new Vector3(_hitRadius * 2f, 0.04f, _hitRadius * 2f);
            _spinDiscObj.transform.position   = ctx.Transform.position;

            _sub      = Sub.Windup;
            _timer    = 0f;
            _tickTimer = 0f;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            if (_sub == Sub.Windup)
            {
                float windupDur = _isPhase2 ? Data.p2_windupDuration : Data.p1_windupDuration;
                if (_timer < windupDur) return;

                if (ctx.Animator != null)
                    ctx.Animator.CrossFade(Data.animSpinning, Data.crossFade);

                _sub   = Sub.Spinning;
                _timer = 0f;
                return;
            }

            // ── 회전 이동 ──────────────────────────────────────────
            MoveTowardPlayer(ctx);

            // 디스크 위치: 보스를 따라 이동
            if (_spinDiscObj != null)
                _spinDiscObj.transform.position = ctx.Transform.position;

            // ── 히트 판정 + 이동 위치 경고 갱신 ──────────────────
            _tickTimer += Time.deltaTime;
            if (_tickTimer >= Data.tickInterval)
            {
                _tickTimer = 0f;
                ApplyHit(ctx);
                // 현재 위치에 짧은 경고 원 갱신 (보스 이동에 따라 위치 추적)
                MonsterGroundWarning.Spawn(
                    ctx.Transform.position, _hitRadius,
                    Data.tickInterval + 0.05f,
                    new Color(0.85f, 0.15f, 0.9f, 0.6f));
            }

            if (_timer >= _spinDuration)
                ctx.Monster.ChangeState<PatrolState>();
        }

        public override void Exit(MonsterContext ctx)
        {
            // 디스크 오브젝트 정리
            if (_spinDiscObj != null)
            {
                Object.Destroy(_spinDiscMat);
                Object.Destroy(_spinDiscObj);
                _spinDiscObj = null;
                _spinDiscMat = null;
            }

            if (ctx.Agent.isOnNavMesh)
            {
                ctx.Agent.speed            = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier;
                ctx.Agent.stoppingDistance = ctx.Monster.GetCombatStopDistance(ctx);
                ctx.Agent.ResetPath();
            }
            Data.StartCooldown();
        }

        private void MoveTowardPlayer(MonsterContext ctx)
        {
            if (ctx.Runtime.PlayerTarget == null) return;
            if (!ctx.Agent.isOnNavMesh) return;

            ctx.Agent.speed            = _moveSpeed;
            ctx.Agent.stoppingDistance = 0f;
            ctx.Agent.SetDestination(ctx.Runtime.PlayerTarget.position);
        }

        private void ApplyHit(MonsterContext ctx)
        {
            int damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier
                               * GetSpeedDamageMult(ctx));
            float kbForce = ctx.Stat.knockbackForce * 0.5f;

            var hits = Physics.OverlapSphere(ctx.Transform.position, _hitRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                Vector3 dir = (col.transform.position - ctx.Transform.position);
                dir.y = 0.1f;
                player.TakeDamage(damage);
                player.ApplyKnockback(dir.normalized * kbForce);
                break;
            }
        }

        /// <summary>이동 속도 비례 데미지 배율 (1.0~2.0).</summary>
        private float GetSpeedDamageMult(MonsterContext ctx)
        {
            float baseSpeed = ctx.Stat.moveSpeed;
            if (baseSpeed <= 0f) return 1f;
            return Mathf.Clamp(_moveSpeed / baseSpeed, 1f, 2f);
        }
    }
}
}
