using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 드래곤 보스 — 공중 연속 물기.
/// 공중에서 플레이어를 따라다니며 3회 반복:
///   ① 바닥 경고 장판 → ② 빠르게 내려꽂음(Attack01) → ③ 복귀
/// </summary>
[CreateAssetMenu(fileName = "DBBitePatternSO",
                 menuName = "Abyss/Boss/DragonBoss/Bite")]
public class DBBitePatternSO : BossPatternSO
{
    [Header("Animation")]
    [SerializeField] private string animAttack = "Attack01";
    [SerializeField] private float  crossFade  = 0.1f;

    [Header("Fly")]
    [SerializeField] private float flyHeight    = 6f;    // 공중 대기 높이
    [SerializeField] private float riseSpeed    = 8f;    // 부상 속도 (m/s)
    [SerializeField] private float trackSpeed   = 5f;    // 공중에서 플레이어 추적 속도 (m/s)

    [Header("Attack")]
    [SerializeField] private float warningDuration = 0.6f;  // 경고 장판 표시 시간
    [SerializeField] private float diveSpeed       = 20f;   // 내려꽂는 속도 (m/s)
    [SerializeField] private float riseBackSpeed   = 12f;   // 복귀 속도 (m/s)
    [SerializeField] private float hitRadius       = 2.5f;
    [SerializeField] private int   hitCount        = 3;

    [Header("Cooldown")]
    [SerializeField] private float patternCooldown = 12f;

    private float _cooldownEndTime = -999f;
    private BiteState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new BiteState(this);
    public override bool CanExecute(BossPatternContext ctx) => Time.time >= _cooldownEndTime;
    public override SpecialStateBase GetRuntimeState() => _state;

    internal void StartCooldown() => _cooldownEndTime = Time.time + patternCooldown;

    // ── 서브 페이즈 ────────────────────────────────────────────────
    private enum Sub { Rising, Tracking, Warning, Diving, Rising2 }

    private sealed class BiteState : FullLockState<DBBitePatternSO>
    {
        private Sub     _sub;
        private float   _timer;
        private int     _hitsDone;
        private Vector3 _groundTarget;   // 현재 공격 목표 지면 위치
        private Vector3 _airPos;         // 공중 대기 위치 (갱신됨)
        private float   _targetAirY;     // Enter에서 한 번만 계산한 목표 공중 y
        private bool    _originalUpdatePosition;
        private bool    _originalUpdateRotation;

        public BiteState(DBBitePatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            if (ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();
            ctx.Agent.velocity         = Vector3.zero;
            _originalUpdatePosition    = ctx.Agent.updatePosition;
            _originalUpdateRotation    = ctx.Agent.updateRotation;
            ctx.Agent.updatePosition   = false;
            ctx.Agent.updateRotation   = false;

            _hitsDone   = 0;
            _sub        = Sub.Rising;
            _timer      = 0f;
            // 목표 공중 y를 Enter에서 한 번만 계산 (매 프레임 재계산 금지)
            float groundY  = GetStaticGroundY(ctx.Transform.position);
            _targetAirY    = groundY + Data.flyHeight;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            switch (_sub)
            {
                // ── ① 부상: Enter에서 계산한 고정 목표 y까지 올라감 ────
                case Sub.Rising:
                {
                    Vector3 cur    = ctx.Transform.position;
                    Vector3 target = new Vector3(cur.x, _targetAirY, cur.z);
                    ctx.Transform.position = Vector3.MoveTowards(cur, target, Data.riseSpeed * Time.deltaTime);

                    FacePlayer(ctx);

                    if (Mathf.Abs(ctx.Transform.position.y - _targetAirY) < 0.15f)
                    {
                        ctx.Transform.position = new Vector3(cur.x, _targetAirY, cur.z);
                        _airPos = ctx.Transform.position;
                        _sub    = Sub.Tracking;
                        _timer  = 0f;
                    }
                    break;
                }

                // ── ② 공중 추적: 플레이어 XZ 따라이동 + 경고 ────────────
                case Sub.Tracking:
                {
                    TrackPlayerXZ(ctx);
                    FacePlayer(ctx);

                    // 0.3초 후 경고 장판 시작
                    if (_timer >= 0.3f)
                    {
                        _groundTarget = GetGroundTarget(ctx);
                        SpawnWarning(_groundTarget, Data.hitRadius, Data.warningDuration,
                            new Color(0.9f, 0.3f, 0.1f));
                        _sub  = Sub.Warning;
                        _timer = 0f;
                    }
                    break;
                }

                // ── ③ 경고 대기 ──────────────────────────────────────────
                case Sub.Warning:
                {
                    FacePlayer(ctx);
                    if (_timer < Data.warningDuration) return;

                    if (ctx.Animator != null)
                        ctx.Animator.CrossFade(Data.animAttack, Data.crossFade);

                    _sub  = Sub.Diving;
                    _timer = 0f;
                    break;
                }

                // ── ④ 내려꽂기 ───────────────────────────────────────────
                case Sub.Diving:
                {
                    ctx.Transform.position = Vector3.MoveTowards(
                        ctx.Transform.position,
                        _groundTarget + Vector3.up * 0.3f,
                        Data.diveSpeed * Time.deltaTime);

                    FacePlayer(ctx);

                    float dist = Vector3.Distance(
                        new Vector3(ctx.Transform.position.x, 0f, ctx.Transform.position.z),
                        new Vector3(_groundTarget.x, 0f, _groundTarget.z));

                    if (dist < 0.4f || ctx.Transform.position.y <= _groundTarget.y + 0.5f)
                    {
                        ApplyHit(ctx, _groundTarget);
                        _hitsDone++;

                        if (_hitsDone >= Data.hitCount)
                        {
                            // 패턴 종료 — 지면으로 착지
                            ctx.Transform.position = _groundTarget;
                            ctx.Agent.Warp(_groundTarget);
                            RestoreAgent(ctx);
                            ctx.Monster.ChangeState<PatrolState>();
                            return;
                        }

                        // 다음 타격을 위해 복귀 (목표 y는 고정값 사용)
                        _airPos = new Vector3(_groundTarget.x, _targetAirY, _groundTarget.z);
                        _sub    = Sub.Rising2;
                        _timer  = 0f;
                    }
                    break;
                }

                // ── ⑤ 복귀: 다음 공격을 위해 다시 올라감 ────────────────
                case Sub.Rising2:
                {
                    ctx.Transform.position = Vector3.MoveTowards(
                        ctx.Transform.position, _airPos, Data.riseBackSpeed * Time.deltaTime);

                    FacePlayer(ctx);

                    if (Vector3.Distance(ctx.Transform.position, _airPos) < 0.2f)
                    {
                        _sub  = Sub.Tracking;
                        _timer = 0f;
                    }
                    break;
                }
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            RestoreAgent(ctx);
            Data.StartCooldown();
        }

        // ── 헬퍼 ─────────────────────────────────────────────────────

        private void TrackPlayerXZ(MonsterContext ctx)
        {
            if (ctx.Runtime.PlayerTarget == null) return;
            Vector3 targetXZ = ctx.Runtime.PlayerTarget.position;
            targetXZ.y = ctx.Transform.position.y; // y 고정
            ctx.Transform.position = Vector3.MoveTowards(
                ctx.Transform.position, targetXZ, Data.trackSpeed * Time.deltaTime);
        }

        private Vector3 GetGroundTarget(MonsterContext ctx)
        {
            Vector3 pos = ctx.Runtime.PlayerTarget != null
                ? ctx.Runtime.PlayerTarget.position
                : ctx.Transform.position;
            pos.y = GetStaticGroundY(pos);
            return pos;
        }

        private static void SpawnWarning(Vector3 pos, float radius, float duration, Color color)
            => MonsterGroundWarning.Spawn(pos, radius, duration, color);

        private void ApplyHit(MonsterContext ctx, Vector3 pos)
        {
            int damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);

            var hits = Physics.OverlapSphere(pos, Data.hitRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                Vector3 dir = (col.transform.position - pos).normalized;
                dir.y = 0.2f;
                player.TakeDamage(damage);

                var dragon  = ctx.Monster as DragonBossMonster;
                var element = dragon?.DBBlackboard?.CurrentElement
                              ?? DragonBossBlackboard.DragonElement.Ice;
                switch (element)
                {
                    case DragonBossBlackboard.DragonElement.Ice:
                        player.ApplyKnockback(Vector3.zero, 2f);
                        break;
                    case DragonBossBlackboard.DragonElement.Thunder:
                        player.ApplyKnockback(Vector3.zero, 0.5f);
                        break;
                    default:
                        player.ApplySlow(0.4f, 2f);
                        break;
                }
                break;
            }
        }

        /// 보스 자신의 콜라이더를 제외하고 지면 y를 구한다.
        /// 보스 중심에서 위로 10m → 아래 방향 레이캐스트, 보스 콜라이더 무시.
        private static float GetStaticGroundY(Vector3 pos)
        {
            Vector3 origin = pos + Vector3.up * 10f;
            // 반경 0.05짜리 SphereCast로 자기 콜라이더를 건너뛰는 대신
            // 충분히 높은 위에서 아래로 Raycast — 보스보다 위에서 시작하므로
            // 보스 콜라이더에 맞을 가능성이 낮음. 맞더라도 0.0 이하가 아닌 값 반환.
            if (Physics.Raycast(origin, Vector3.down, out var hit, 30f, -1, QueryTriggerInteraction.Ignore))
            {
                // y가 보스 시작 위치보다 낮은 지면만 유효 (보스 자신 제외)
                if (hit.point.y < pos.y - 0.5f)
                    return hit.point.y;
            }
            // 레이캐스트 실패 또는 보스 자신에 맞은 경우: y=0 가정
            return 0f;
        }

        private void FacePlayer(MonsterContext ctx)
        {
            if (ctx.Runtime.PlayerTarget == null) return;
            Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;
            ctx.Transform.rotation = Quaternion.RotateTowards(
                ctx.Transform.rotation,
                Quaternion.LookRotation(dir.normalized),
                120f * Time.deltaTime);
        }

        private void RestoreAgent(MonsterContext ctx)
        {
            ctx.Agent.updatePosition = _originalUpdatePosition;
            ctx.Agent.updateRotation = _originalUpdateRotation;
        }
    }
}
}
