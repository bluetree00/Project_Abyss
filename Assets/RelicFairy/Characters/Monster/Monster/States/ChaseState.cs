using UnityEngine;


namespace RelicFairy.Monster
{
/// <summary>
/// 추격 상태.
/// 플레이어를 향해 NavMesh로 이동.
/// - 공격 사정거리 이내 → AttackReady
/// - 추격 포기 거리 초과 또는 플레이어 사망 → Patrol
/// </summary>
public class ChaseState : IMonsterState
{
    // SetDestination은 플레이어가 이 거리 이상 이동했을 때만 재호출 (매 프레임 경로 재계산 방지)
    private const float DestinationUpdateThresholdSq = 0.09f; // 0.3m²
    // 멈춰 선 채 달리기 막기(10-18 실측 황소 제자리 달리기 9회) — 거의 안 움직인 지 StillHold 지나면 전투 대기, MoveSpeed 넘으면 다시 추격 동작
    private const float StillSpeed = 0.3f;
    private const float ChaseTurnCap = 360f;  // 추격 회전 초당 상한(10-18)
    private const float MoveSpeed  = 0.5f;    // 대기 ↔ 달리기 오가며 깜빡이지 않게(Unity 이동 · 애니 결합 예제 0.5 m/s)
    private const float StillHold  = 0.12f;   // 0.25 + 섞기 0.15는 멈춘 뒤 0.4초 제자리 달리기로 보였다(10-19 재실측)
    private const float IdleBlend  = 0.15f;
    private Vector3 _lastDestination = Vector3.positiveInfinity;
    private float   _stillFor;

    public virtual void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.speed = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier;
            ctx.Agent.stoppingDistance = ctx.Monster.GetCombatStopDistance(ctx);
        }
        _lastDestination = Vector3.positiveInfinity; // 진입 시 즉시 경로 계산 보장
        _stillFor = 0f;
        // 아직 서 있으면(공격 · 피격 뒤) 전투 대기로 들어가 움직이기 시작할 때 추격 동작 — 진입부터 틀면 제자리 달리기였다(10-19 재실측)
        if (!EnterIdleIfStill(ctx)) PlayAnim(ctx, ctx.Animation.chaseStateName);
    }

    private bool EnterIdleIfStill(MonsterContext ctx)
    {
        if (ctx.Animator == null || ctx.Agent == null || !ctx.Agent.isOnNavMesh || string.IsNullOrEmpty(ctx.Animation.attackReadyStateName)) return false;
        int idle = Animator.StringToHash(ctx.Animation.attackReadyStateName);
        if (!ctx.Animator.HasState(0, idle)) return false;
        Vector3 v = ctx.Agent.velocity; v.y = 0f;
        if (v.magnitude >= MoveSpeed) return false;
        _stillFor = StillHold;   // 이미 서 있다
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFadeInFixedTime(idle, IdleBlend, 0);
        return true;
    }

    public virtual void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        bool onNavMesh = ctx.Agent != null && ctx.Agent.isOnNavMesh;

        // NavMesh 비사용 몬스터(Lich 등): 이동·회전을 자체 컨트롤러에 위임
        // GiveUpChase·AttackReady 체크는 NavMesh 없을 때 의미없는 상태 진입을 막기 위해 건너뜀
        if (!onNavMesh)
        {
            if (ctx.Monster.ShouldGiveUpChase(ctx))
            {
                ctx.Monster.ChangeState<PatrolState>();
                return;
            }
            KeepChaseAnimation(ctx);
            return;
        }

        // 공격 사정거리 이내 → 공격 준비
        if (ctx.Monster.ShouldEnterAttackReady(ctx))
        {
            ctx.Monster.ChangeState<AttackReadyState>();
            return;
        }

        // 추격 포기 → 배회 복귀
        if (ctx.Monster.ShouldGiveUpChase(ctx))
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        // 추격 이동 + 회전 — 플레이어가 충분히 이동했을 때만 경로 재계산
        Vector3 targetPos = ctx.Runtime.PlayerTarget.position;
        if ((targetPos - _lastDestination).sqrMagnitude > DestinationUpdateThresholdSq)
        {
            ctx.Agent.SetDestination(targetPos);
            _lastDestination = targetPos;
        }
        FaceTarget(ctx);
        KeepChaseOrIdle(ctx);

        // 이동 속도 파라미터 갱신
        if (!string.IsNullOrEmpty(ctx.Animation.speedParam) && ctx.Animator != null)
            ctx.Animator.SetFloat(ctx.Animation.speedParam, ctx.Agent.velocity.magnitude,
                ctx.Animation.speedDampTime, Time.deltaTime);
    }

    public virtual void Exit(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
    }

    // ── 헬퍼 ──────────────────────────────────────────────

    protected static void FaceTarget(MonsterContext ctx)
    {
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        Quaternion target = Quaternion.LookRotation(dir);
        // Slerp(dt×10)은 180° 차에서 초당 ~1800° — 초당 상한을 씌운다(10-18 실측 황소 추격 1081°/s)
        ctx.Transform.rotation = Quaternion.RotateTowards(ctx.Transform.rotation,
            Quaternion.Slerp(ctx.Transform.rotation, target, Time.deltaTime * 10f), ChaseTurnCap * Time.deltaTime);
    }

    protected static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;

        string fallback = ctx.Animation.patrolStateName;
        string finalState = !string.IsNullOrEmpty(stateName) && ctx.Animator.HasState(0, Animator.StringToHash(stateName))
            ? stateName
            : (!string.IsNullOrEmpty(fallback) && ctx.Animator.HasState(0, Animator.StringToHash(fallback))
                ? fallback
                : null);

        if (string.IsNullOrEmpty(finalState)) return;
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(finalState, ctx.Animation.crossFadeDuration);
    }

    protected static void KeepChaseAnimation(MonsterContext ctx, bool evenInTransition = false)
    {
        if (ctx.Animator == null) return;
        if (!evenInTransition && ctx.Animator.IsInTransition(0)) return;
        if (string.IsNullOrEmpty(ctx.Animation.chaseStateName)) return;

        int chaseHash = Animator.StringToHash(ctx.Animation.chaseStateName);
        if (!ctx.Animator.HasState(0, chaseHash)) return;

        var current = ctx.Animator.GetCurrentAnimatorStateInfo(0);
        if (current.shortNameHash == chaseHash) return;

        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(ctx.Animation.chaseStateName, Mathf.Min(0.08f, ctx.Animation.crossFadeDuration));
    }

    /// <summary>추격 동작은 실제로 움직일 때만 — 거의 안 움직인 지 StillHold가 지나면 전투 대기, MoveSpeed 넘게 다시 움직이면 추격 동작(10-18).</summary>
    protected void KeepChaseOrIdle(MonsterContext ctx)
    {
        if (ctx.Animator == null || ctx.Agent == null || string.IsNullOrEmpty(ctx.Animation.attackReadyStateName)) { KeepChaseAnimation(ctx); return; }
        int idle = Animator.StringToHash(ctx.Animation.attackReadyStateName);
        if (!ctx.Animator.HasState(0, idle)) { KeepChaseAnimation(ctx); return; }
        Vector3 v = ctx.Agent.velocity; v.y = 0f;
        float speed = v.magnitude;
        _stillFor = speed < StillSpeed ? _stillFor + Time.deltaTime : 0f;
        var an = ctx.Animator;
        int to = an.IsInTransition(0) ? an.GetNextAnimatorStateInfo(0).shortNameHash : an.GetCurrentAnimatorStateInfo(0).shortNameHash;
        if (to == idle)
        {
            if (speed >= MoveSpeed) KeepChaseAnimation(ctx, true);   // 다시 움직인다 — 대기로 섞는 중이어도 바로(기다리면 대기 자세로 미끄러졌다 · 10-19)
            return;
        }
        if (_stillFor < StillHold) { KeepChaseAnimation(ctx); return; }
        if (ctx.Animator.IsInTransition(0)) return;
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFadeInFixedTime(idle, IdleBlend, 0);
    }

    /// <summary>
    /// 둘레 돌기 · 물러나기 — 플레이어를 보며 옆 · 뒤로 움직일 땐 그쪽 걷기(팩에 있으면), 멈춰 있으면 전투 대기.
    /// 예전엔 늘 앞으로 달리는 동작이라 옆 · 뒤로 미끄러졌다(10-14 정예 애니 점검). 걷기가 없으면 추적 동작.
    /// </summary>
    protected static void KeepMoveAnimation(MonsterContext ctx)
    {
        if (ctx.Animator == null || ctx.Agent == null) return;
        if (ctx.Animator.IsInTransition(0)) return;
        Vector3 v = ctx.Agent.velocity; v.y = 0f;
        float speed = v.magnitude;
        int want;
        if (speed < 0.3f)
            want = string.IsNullOrEmpty(ctx.Animation.attackReadyStateName) ? 0 : Animator.StringToHash(ctx.Animation.attackReadyStateName);
        else
        {
            var set = MonsterAnimStates.Of(ctx);
            Vector3 local = ctx.Transform.InverseTransformDirection(v);
            want = local.z < -0.5f * speed ? set.Back
                 : Mathf.Abs(local.x) > 0.6f * speed ? (local.x < 0f ? set.Left : set.Right)
                 : 0;
        }
        if (want == 0 || !ctx.Animator.HasState(0, want)) { KeepChaseAnimation(ctx); return; }
        if (ctx.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash == want) return;
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(want, 0.15f, 0);
    }
}
}
