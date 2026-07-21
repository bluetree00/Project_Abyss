using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace RelicFairy.Monster
{
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Idle
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKIdleState : IMonsterState
{
    public void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
        PlayAnim(ctx, ctx.Animation.idleStateName);
    }

    public void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead()) return;
        // 추적 없이 즉시 공격 대기 상태로 전환
        ctx.Monster.ChangeState<AttackReadyState>();
    }

    public void Exit(MonsterContext ctx) { }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(stateName, ctx.Animation.crossFadeDuration);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Chase — Run/Walk1 거리 기반 속도 + StrafeLeft/Right 견제
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKChaseState : IMonsterState
{
    private const float FaceSpeed       = 8f;
    private const float EngageDistance  = 2.5f;
    private const float ChaseStopDist   = 1.0f;

    // Run → Walk1 전환 거리
    private const float RunThreshold    = 8f;
    // 스트레이프 진입 허용 최대 거리
    private const float StrafeMaxRange  = 8f;
    // 모드별 NavMesh 속도 배율
    private const float WalkSpeedMult   = 0.6f;
    private const float StrafeSpeedMult = 0.5f;
    // 모드 유지 시간 범위
    private const float ApproachMinTime = 2.0f;
    private const float ApproachMaxTime = 4.5f;
    private const float StrafeMinTime   = 1.2f;
    private const float StrafeMaxTime   = 2.8f;

    private const string WalkAnim        = "Walk1";
    private const string StrafeLeftAnim  = "StrafeLeft";
    private const string StrafeRightAnim = "StrafeRight";

    private enum ChaseMode { Approaching, Strafing }

    private ChaseMode _mode;
    private float     _modeTimer;
    private float     _modeExpiry;
    private int       _strafeDir;   // +1=오른쪽, -1=왼쪽
    private string    _currentAnim;

    public void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null)
        {
            ctx.Agent.enabled = true;
            if (!ctx.Agent.isOnNavMesh) ctx.Monster.TrySnapAgentToNavMesh();
            ctx.Agent.updatePosition  = true;
            ctx.Agent.updateRotation  = false;
            if (ctx.Agent.isOnNavMesh)
            {
                ctx.Agent.isStopped        = false;
                ctx.Agent.stoppingDistance = ChaseStopDist;
                ctx.Agent.speed            = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier;
            }
        }

        _mode        = ChaseMode.Approaching;
        _modeTimer   = 0f;
        _modeExpiry  = Random.Range(ApproachMinTime, ApproachMaxTime);
        _strafeDir   = 1;
        _currentAnim = ctx.Animation.chaseStateName;

        ApplyAnimSpeed(ctx);
        // 즉시 시작 — 블렌딩 중 멈춤 버그 방지
        PlayAnimImmediate(ctx, ctx.Animation.chaseStateName);
    }

    public void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        if (ctx.Runtime.DistToPlayer <= EngageDistance)
        {
            ctx.Monster.ChangeState<AttackReadyState>();
            return;
        }

        _modeTimer += Time.deltaTime;
        FacePlayer(ctx);

        float dist = ctx.Runtime.DistToPlayer;

        if (_mode == ChaseMode.Approaching)
            UpdateApproaching(ctx, dist);
        else
            UpdateStrafing(ctx, dist);
    }

    public void Exit(MonsterContext ctx)
    {
        if (ctx.Agent != null)
        {
            ctx.Agent.updateRotation = true;
            if (ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
        }
    }

    // ── Approaching ──────────────────────────────────────

    private void UpdateApproaching(MonsterContext ctx, float dist)
    {
        bool isRunRange  = dist > RunThreshold;
        string nextAnim  = isRunRange ? ctx.Animation.chaseStateName : WalkAnim;
        float  speedMult = isRunRange ? 1f : WalkSpeedMult;

        if (_currentAnim != nextAnim)
        {
            _currentAnim = nextAnim;
            PlayAnimBlend(ctx, nextAnim);
        }

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.speed = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier * speedMult;
            ctx.Agent.SetDestination(ctx.Runtime.PlayerTarget.position);
        }

        // 걷기 범위 안에서 일정 시간 후 스트레이프 전환
        if (_modeTimer >= _modeExpiry && !isRunRange)
            EnterStrafeMode(ctx);
    }

    // ── Strafing ─────────────────────────────────────────

    private void UpdateStrafing(MonsterContext ctx, float dist)
    {
        if (_modeTimer >= _modeExpiry || dist > StrafeMaxRange)
        {
            _mode       = ChaseMode.Approaching;
            _modeTimer  = 0f;
            _modeExpiry = Random.Range(ApproachMinTime, ApproachMaxTime);

            bool   isRun     = dist > RunThreshold;
            string nextAnim  = isRun ? ctx.Animation.chaseStateName : WalkAnim;
            _currentAnim = nextAnim;
            PlayAnimBlend(ctx, nextAnim);

            if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
                ctx.Agent.speed = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier *
                                  (isRun ? 1f : WalkSpeedMult);
            return;
        }

        // 플레이어 방향 수직으로 이동 — 원형 견제
        if (ctx.Runtime.PlayerTarget != null && ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.001f)
            {
                Vector3 perp = Vector3.Cross(Vector3.up, toPlayer.normalized) * _strafeDir;
                ctx.Agent.SetDestination(ctx.Transform.position + perp * 2.5f);
            }
        }
    }

    private void EnterStrafeMode(MonsterContext ctx)
    {
        _mode       = ChaseMode.Strafing;
        _modeTimer  = 0f;
        _modeExpiry = Random.Range(StrafeMinTime, StrafeMaxTime);
        _strafeDir  = Random.value > 0.5f ? 1 : -1;

        string strafeAnim = _strafeDir > 0 ? StrafeRightAnim : StrafeLeftAnim;
        _currentAnim = strafeAnim;
        PlayAnimBlend(ctx, strafeAnim);

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.speed = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier * StrafeSpeedMult;
    }

    // ── 헬퍼 ──────────────────────────────────────────────

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        ctx.Transform.rotation = Quaternion.Slerp(
            ctx.Transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * FaceSpeed);
    }

    private static void ApplyAnimSpeed(MonsterContext ctx)
    {
        if (ctx.Animator == null) return;
        ctx.Animator.speed = (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.AnimSpeedMult ?? 1f;
    }

    private static void PlayAnimImmediate(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.speed = (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.AnimSpeedMult ?? 1f;
        ctx.Animator.Play(stateName, 0, 0f);
    }

    private static void PlayAnimBlend(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.speed = (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.AnimSpeedMult ?? 1f;
        ctx.Animator.CrossFade(stateName, 0.15f);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// AttackReady
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKAttackReadyState : IMonsterState
{
    private const float FaceSpeed   = 5f;
    private const float SettleDelay = 0.35f;

    public void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.velocity  = Vector3.zero;
            ctx.Agent.ResetPath();
        }

        (ctx.Monster as DeathKnightBossMonster)?.EnsurePatternDelay(SettleDelay);
        // 패턴 대기 중에는 Idle 애니메이션 유지 — 플레이어 방향 추적 없음
        PlayAnim(ctx, ctx.Animation.idleStateName);
    }

    public void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }
        // 패턴 사용 중에만 플레이어를 바라봄 — 대기 중에는 회전 없음
    }

    public void Exit(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;
    }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        ctx.Transform.rotation = Quaternion.Slerp(
            ctx.Transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * FaceSpeed);
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        var dk = ctx.Monster as DeathKnightBossMonster;
        ctx.Animator.speed = dk?.DKBlackboard.AnimSpeedMult ?? 1f;
        ctx.Animator.CrossFade(stateName, ctx.Animation.crossFadeDuration);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Attack (안전망 — 패턴이 처리하므로 AttackReady 로 위임)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKAttackState : IMonsterState
{
    public void Enter(MonsterContext ctx)  => ctx.Monster.ChangeState<AttackReadyState>();
    public void Update(MonsterContext ctx) { }
    public void Exit(MonsterContext ctx)   { }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// GetHit — gethit1/2/3 랜덤 재생
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKGetHitState : GetHitState
{
    private static readonly string[] HitAnims = { "GetHit1", "GetHit2", "GetHit3" };
    private const float StaggerDuration = 0.5f;

    public override void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.velocity  = Vector3.zero;
            ctx.Agent.ResetPath();
        }

        ctx.Runtime.StateTimer = StaggerDuration;
        PlayRandomHitAnim(ctx);

        // 경직 중 콤보 러너 차단
        (ctx.Monster as DeathKnightBossMonster)?.SetStagger(true);
    }

    public override void Update(MonsterContext ctx)
    {
        // 매 프레임 speed=1f 강제 — 외부에서 speed를 변경해도 즉시 정상화
        if (ctx.Animator != null) ctx.Animator.speed = 1f;

        ctx.Runtime.StateTimer -= Time.deltaTime;
        if (ctx.Runtime.StateTimer > 0f) return;

        RestoreAgent(ctx);

        if (ctx.Runtime.PlayerTarget != null && !ctx.Monster.IsPlayerDead())
            ctx.Monster.ChangeState<AttackReadyState>();
        else
            ctx.Monster.ChangeState<PatrolState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;

        (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.ClearArmorBroken();

        // 경직 해제
        (ctx.Monster as DeathKnightBossMonster)?.SetStagger(false);
    }

    private static void PlayRandomHitAnim(MonsterContext ctx)
    {
        if (ctx.Animator == null) return;
        string anim = HitAnims[Random.Range(0, HitAnims.Length)];
        if (!ctx.Animator.HasState(0, Animator.StringToHash(anim))) return;
        // CrossFade 대신 Play — 블렌딩 없이 즉시 전환 (블렌딩이 슬로우처럼 보이는 현상 제거)
        ctx.Animator.speed = 1f;
        ctx.Animator.Play(anim, 0, 0f);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Die
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKDieState : DieState
{
    public override void Enter(MonsterContext ctx)
    {
        if (ctx.Monster is DeathKnightBossMonster dk)
            dk.UnbindBossHudIfBoundPublic();

        // 사망 시 DK 전용 카메라 오빗 복원
        GameCameraController.Instance?.DeactivateDKPlayerOrbit(1.5f);

        base.Enter(ctx);
    }
}

}
