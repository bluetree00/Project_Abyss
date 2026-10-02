using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>잠복 해제 조건.</summary>
public enum DormantWakeUpMode
{
    /// <summary>피격당해야만 잠복 해제.</summary>
    OnAttackedOnly,
    /// <summary>플레이어가 인식 거리(detectionRange)에 들어오면 잠복 해제.</summary>
    OnDetectionRange,
    /// <summary>플레이어가 공격 사거리(attackRange)에 들어오면 잠복 해제.</summary>
    OnAttackRange,
}

[CreateAssetMenu(fileName = "DormantPatrolState",
                 menuName = "Lee/Monster/States/Patrol/Dormant")]
public class DormantPatrolStateSO : MonsterStateOverrideSO
{
    [Tooltip("Hide the world HP bar while the monster is dormant.")]
    public bool hideWorldHpBar = true;

    [Tooltip("Freeze the animator on the first frame of the idle animation.")]
    public bool freezeAnimator = false;

    [Tooltip("Optional idle state to force while dormant. Falls back to Config.animation.idleStateName.")]
    public string overrideIdleStateName;

    [Tooltip("잠복 해제 조건.\n" +
             "OnAttackedOnly: 피격 시에만 해제.\n" +
             "OnDetectionRange: 플레이어가 인식 거리에 접근하면 해제.\n" +
             "OnAttackRange: 플레이어가 공격 사거리에 접근하면 해제.\n" +
             "어떤 모드든 피격 시에는 항상 해제된다.")]
    public DormantWakeUpMode wakeUpMode = DormantWakeUpMode.OnAttackedOnly;

    [Tooltip("잠복을 푸는 거리(m). 0이면 모드 기본값(인식 거리 / 공격 사거리). OnAttackedOnly에서는 쓰지 않는다.\n" +
             "인식 거리는 감지 배율(×3)이 곱해진 값이라 위장 몬스터에게는 너무 멀고, 공격 사거리는 너무 가깝다 — 둘 사이를 여기서 정한다.")]
    public float wakeRadius = 0f;

    public override void RegisterOverrides(MonsterFSM fsm, MonsterBase monster)
    {
        fsm.RegisterAs<PatrolState>(new DormantPatrolState(this));
    }

    private sealed class DormantPatrolState : IMonsterState
    {
        private readonly DormantPatrolStateSO _data;

        /// <summary>true이면 스폰 위치로 귀환 중, false이면 잠복 대기 중.</summary>
        private bool _isReturning;

        /// <summary>Warp 후 1프레임 대기 중.</summary>
        private bool _waitingForSettle;

        /// <summary>스폰 위치 도착 판정 거리(m).</summary>
        private const float ReturnArrivalThreshold = 0.5f;

        /// <summary>이 상태에 들어올 때의 HP — 줄면(룬 · 지속 피해처럼 피격 플래그를 안 세우는 피해 포함) 깬다.</summary>
        private int _hpMark;

        public DormantPatrolState(DormantPatrolStateSO data)
        {
            _data = data;
        }

        /// <summary>거리 기반 잠복 해제 문턱.</summary>
        private float WakeThreshold(MonsterContext ctx)
        {
            if (_data.wakeRadius > 0f) return _data.wakeRadius;
            return _data.wakeUpMode == DormantWakeUpMode.OnDetectionRange
                ? ctx.Detection.detectionRange
                : ctx.Stat.attackRange;
        }

        public void Enter(MonsterContext ctx)
        {
            // 내부 상태 초기화 — 이전 사이클 잔여 플래그 제거
            _hpMark = ctx.Runtime.CurrentHp;
            _isReturning = false;
            _waitingForSettle = false;
            _waitingForDormantTransition = false;

            // 스폰 위치와 현재 위치가 먼 경우 귀환 모드로 진입
            float distToSpawn = Vector3.Distance(ctx.Transform.position, ctx.Runtime.SpawnPosition);
            if (distToSpawn > ReturnArrivalThreshold)
            {
                EnterReturn(ctx);
            }
            else
            {
                EnterDormant(ctx);
                // 제자리 잠복(막 태어남) — 이미 문턱 안에 선 플레이어에게도 깬다. 「한 번 벗어났다 다시 들어와야 깬다」는
                // 쫓다 포기하고 돌아온 자리에서 곧바로 다시 깨는 왕복을 막는 장치라 귀환 뒤 잠복에만 건다.
                // 이게 없으면 플레이어 곁에 태어난 선인장 · 성난 버섯이 맞기 전까지 영영 잠복했다(10-01 실측).
                ctx.Runtime.TargetCleared = true;
            }
        }

        public void Update(MonsterContext ctx)
        {
            // 피격 감지 — 귀환 중이든 잠복 중이든, TargetCleared 무관하게 항상 허용
            // 룬 · 지속 피해(TakeSynergyDamage)는 피격 플래그를 세우지 않는다 — 체력이 줄었으면 맞은 것으로 본다.
            if (ctx.Runtime.HasBeenAttacked || ctx.Runtime.CurrentHp < _hpMark)
            {
                ctx.Monster.ChangeState<ChaseState>();
                return;
            }

            // 귀환 모드
            if (_isReturning)
            {
                UpdateReturn(ctx);
                return;
            }

            // 잠복 진입 트랜지션 완료 대기 (freezeAnimator용)
            UpdateDormantTransition(ctx);

            // 타겟 이탈 확인 — 플레이어가 감지 범위를 벗어나면 재감지 허용
            if (!ctx.Runtime.TargetCleared
                && _data.wakeUpMode != DormantWakeUpMode.OnAttackedOnly)
            {
                float threshold = WakeThreshold(ctx);

                if (ctx.Runtime.PlayerTarget == null
                    || ctx.Monster.IsPlayerDead()
                    || ctx.Runtime.DistToPlayer > threshold)
                {
                    ctx.Runtime.TargetCleared = true;
                }
            }

            // 잠복 대기 — 거리 기반 잠복 해제 (TargetCleared 이후만)
            if (_data.wakeUpMode != DormantWakeUpMode.OnAttackedOnly
                && ctx.Runtime.TargetCleared
                && ctx.Runtime.PlayerTarget != null
                && !ctx.Monster.IsPlayerDead())
            {
                float threshold = WakeThreshold(ctx);

                if (ctx.Runtime.DistToPlayer <= threshold)
                    ctx.Monster.ChangeState<ChaseState>();
            }
        }

        public void Exit(MonsterContext ctx)
        {
            _isReturning = false;
            _waitingForSettle = false;
            _waitingForDormantTransition = false;
            ctx.Runtime.IsDormant = false;
            ctx.Runtime.IsReturning = false;
            ctx.Runtime.HasBeenAttacked = false;
            ctx.Runtime.TargetCleared = false;

            if (ctx.Animator != null)
            {
                ResetCombatTriggers(ctx);
                ctx.Animator.speed = 1f;

                if (!string.IsNullOrEmpty(ctx.Animation.speedParam))
                    ctx.Animator.SetFloat(ctx.Animation.speedParam, 0f);
            }

            if (_data.hideWorldHpBar)
                ctx.Monster.ShowWorldHPBar();

            if (ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();
        }

        // ── 귀환 모드 ────────────────────────────────────────

        private void EnterReturn(MonsterContext ctx)
        {
            _isReturning = true;
            _waitingForSettle = false;
            ctx.Runtime.IsDormant = false;
            ctx.Runtime.IsReturning = true;
            ctx.Runtime.HasBeenAttacked = false;
            ctx.Runtime.TargetCleared = false;

            // 귀환 중에는 HP 바를 이미 숨김 처리
            if (_data.hideWorldHpBar)
                ctx.Monster.HideWorldHPBar();

            // 패트롤 속도로 스폰 위치까지 이동
            float speed = ctx.Patrol.patrolSpeed > 0f
                ? ctx.Patrol.patrolSpeed
                : ctx.Stat.moveSpeed;
            ctx.Agent.speed = speed;

            if (ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
                ctx.Agent.SetDestination(ctx.Runtime.SpawnPosition);

            // 전투 중 남은 트리거를 모두 제거 후 이동 애니메이션 재생
            if (ctx.Animator != null)
            {
                ResetCombatTriggers(ctx);
                ctx.Animator.speed = 1f;
                string moveName = ctx.Animation.patrolStateName;
                if (!string.IsNullOrEmpty(moveName))
                    ctx.Animator.CrossFade(moveName, ctx.Animation.crossFadeDuration);
            }
        }

        private void UpdateReturn(MonsterContext ctx)
        {
            if (!ctx.Agent.isActiveAndEnabled || !ctx.Agent.isOnNavMesh)
            {
                SnapToSpawnAndDormant(ctx);
                return;
            }

            // Warp 후 1프레임 대기 — Agent/물리가 정착한 뒤 잠복 진입
            if (_waitingForSettle)
            {
                EnterDormant(ctx);
                return;
            }

            // 귀환 중에는 거리 기반 감지 완전 차단 — 피격만 반응 (Update 상단에서 처리)
            // 잠복 도달 후에만 TargetCleared 체크 + 거리 감지 시작

            KeepReturnAnimation(ctx);

            // 도착 판정 → 정확한 위치 Warp + 물리 정지 → 다음 프레임에서 잠복 진입
            if (!ctx.Agent.pathPending
                && ctx.Agent.remainingDistance <= ReturnArrivalThreshold)
            {
                ctx.Agent.ResetPath();
                ctx.Agent.Warp(ctx.Runtime.SpawnPosition);
                ctx.Agent.speed = 0f;
                StopPhysics(ctx);
                _waitingForSettle = true;
            }
        }

        /// <summary>NavMesh 비활성 시 직접 위치 스냅 후 잠복.</summary>
        private void SnapToSpawnAndDormant(MonsterContext ctx)
        {
            ctx.Transform.position = ctx.Runtime.SpawnPosition;
            StopPhysics(ctx);
            EnterDormant(ctx);
        }

        /// <summary>Rigidbody/Agent 물리 완전 정지.</summary>
        private static void StopPhysics(MonsterContext ctx)
        {
            // 몬스터 바디는 평소 kinematic(에이전트가 위치를 쥔다) — kinematic에 속도를 넣으면 경고만 나고 효과는 없다.
            // 잠복 몬스터(선인장·성난 버섯)가 잠복할 때마다 경고 한 쌍이 찍혀 Ch2 방 경고 집계를 오염시켰다(09-24).
            var rb = ctx.Monster.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            if (ctx.Agent.isActiveAndEnabled)
                ctx.Agent.velocity = Vector3.zero;
        }

        // ── 잠복 모드 ────────────────────────────────────────

        /// <summary>잠복 트랜지션 대기 중인지.</summary>
        private bool _waitingForDormantTransition;

        private void EnterDormant(MonsterContext ctx)
        {
            _isReturning = false;
            _waitingForSettle = false;
            _waitingForDormantTransition = false;
            ctx.Runtime.IsDormant = true;
            ctx.Runtime.IsReturning = false;
            ctx.Runtime.HasBeenAttacked = false;
            ctx.Runtime.TargetCleared = false;

            if (ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();

            ctx.Agent.speed = 0f;
            StopPhysics(ctx);

            if (_data.hideWorldHpBar)
                ctx.Monster.HideWorldHPBar();

            var idleStateName = string.IsNullOrEmpty(_data.overrideIdleStateName)
                ? ctx.Animation.idleStateName
                : _data.overrideIdleStateName;

            if (ctx.Animator == null || string.IsNullOrEmpty(idleStateName))
                return;

            ResetCombatTriggers(ctx);
            ctx.Animator.speed = 1f;

            // 속도 파라미터 리셋 — 블렌드 트리가 이동 포즈를 블렌딩하지 않도록
            if (!string.IsNullOrEmpty(ctx.Animation.speedParam))
                ctx.Animator.SetFloat(ctx.Animation.speedParam, 0f);

            if (_data.freezeAnimator)
            {
                // 정지형(선인장 등): CrossFade로 부드럽게 전환 후 freeze 대기
                ctx.Animator.CrossFade(idleStateName, 0.25f);
                _waitingForDormantTransition = true;
            }
            else
            {
                ctx.Animator.CrossFade(idleStateName, 0.2f);
            }
        }

        /// <summary>잠복 트랜지션이 완료되면 Animator를 정지시킨다.</summary>
        private void UpdateDormantTransition(MonsterContext ctx)
        {
            if (!_waitingForDormantTransition) return;
            if (ctx.Animator == null) return;

            // 트랜지션 완료 대기
            if (ctx.Animator.IsInTransition(0)) return;

            var idleStateName = string.IsNullOrEmpty(_data.overrideIdleStateName)
                ? ctx.Animation.idleStateName
                : _data.overrideIdleStateName;

            int idleHash = Animator.StringToHash(idleStateName);
            var current = ctx.Animator.GetCurrentAnimatorStateInfo(0);

            if (current.shortNameHash == idleHash)
            {
                ctx.Animator.speed = 0f;
                _waitingForDormantTransition = false;
            }
        }

        // ── 애니메이션 헬퍼 ──────────────────────────────────

        /// <summary>
        /// 귀환 중 매 프레임 호출. 전투 트리거 등으로 Animator가
        /// 이동 애니메이션에서 이탈했을 때 강제로 복귀시킨다.
        /// </summary>
        private static void KeepReturnAnimation(MonsterContext ctx)
        {
            if (ctx.Animator == null) return;
            if (ctx.Animator.IsInTransition(0)) return;

            string moveName = ctx.Animation.patrolStateName;
            if (string.IsNullOrEmpty(moveName)) return;

            int moveHash = Animator.StringToHash(moveName);
            if (!ctx.Animator.HasState(0, moveHash)) return;

            var current = ctx.Animator.GetCurrentAnimatorStateInfo(0);
            if (current.shortNameHash == moveHash) return;

            // 이탈 감지 — 이동 애니메이션으로 강제 복귀
            ResetCombatTriggers(ctx);
            ctx.Animator.speed = 1f;
            ctx.Animator.CrossFade(moveName,
                Mathf.Min(0.08f, ctx.Animation.crossFadeDuration));
        }

        /// <summary>
        /// 전투 중 설정된 Trigger 파라미터를 모두 리셋한다.
        /// 귀환/잠복 진입 시 잔여 트리거에 의한 의도치 않은
        /// 애니메이션 재생을 방지한다.
        /// </summary>
        private static void ResetCombatTriggers(MonsterContext ctx)
        {
            if (ctx.Animator == null) return;

            // 모든 트리거 파라미터를 일괄 리셋 — 상태명과 무관하게 안전
            foreach (var p in ctx.Animator.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Trigger)
                    ctx.Animator.ResetTrigger(p.nameHash);
            }
        }
    }
}
}
