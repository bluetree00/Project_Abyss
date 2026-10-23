using UnityEngine;
using UnityEngine.AI;

namespace RelicFairy.Monster
{
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Idle
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGIdleState : IMonsterState
{
    public void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
        PlayAnim(ctx, ctx.Animation.idleStateName);
    }

    public void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead()) return;
        if (!ctx.Monster.ShouldStartChase(ctx)) return;
        ctx.Monster.ChangeState<ChaseState>();
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

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Chase
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGChaseState : IMonsterState
{
    private const float FaceSpeed      = 8f;
    private const float EngageDistance = 2.5f;
    private const float ChaseStopDist  = 1.0f;
    // 10-18 실측 — 진입 크로스페이드가 앞 클립 길이 비율이라 긴 공격 뒤 0.45초 넘게 공격 자세로 미끄러졌다(11회) · 멈춰도 걷기(8회)
    private const float WalkBlend  = 0.2f;    // 초(고정)
    private const float TurnCap    = 200f;    // 추격 회전 초당 상한 — 큰 몸(10-18)
    private const float StillSpeed = 0.3f;
    private const float MoveSpeed  = 0.5f;
    private const float StillHold  = 0.12f;   // 0.25 + 섞기 0.2는 멈춘 뒤 0.45초 제자리 걷기로 보였다(10-19 재실측 7회)
    private float _stillFor;

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
            }
            ctx.Agent.speed = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier;
        }
        ApplyAnimSpeed(ctx);
        _stillFor = 0f;
        // 아직 서 있으면(공격 · 피격 뒤) 전투 대기로 들어가 움직일 때 걷기 — 진입부터 걷기를 틀면 제자리 걷기였다(10-19 재실측)
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh && FlatSpeed(ctx) < MoveSpeed && !string.IsNullOrEmpty(ctx.Animation.attackReadyStateName))
        {
            _stillFor = StillHold;
            PlayAnim(ctx, ctx.Animation.attackReadyStateName);
        }
        else PlayAnim(ctx, ctx.Animation.chaseStateName);
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

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.SetDestination(ctx.Runtime.PlayerTarget.position);

        FacePlayer(ctx);
        KeepWalkOrIdle(ctx);
    }

    /// <summary>걷기는 실제로 움직일 때만 — 거의 안 움직인 지 StillHold 지나면 전투 대기, MoveSpeed 넘으면 다시 걷기(10-18).</summary>
    private void KeepWalkOrIdle(MonsterContext ctx)
    {
        if (ctx.Animator == null || ctx.Agent == null) return;
        float speed = FlatSpeed(ctx);
        _stillFor = speed < StillSpeed ? _stillFor + Time.deltaTime : 0f;   // 전환 중에도 센다(전엔 걷기 섞는 0.2초를 빼먹었다)
        string walk = ctx.Animation.chaseStateName, idle = ctx.Animation.attackReadyStateName;
        if (string.IsNullOrEmpty(walk) || string.IsNullOrEmpty(idle)) return;
        var an = ctx.Animator;
        int to = an.IsInTransition(0) ? an.GetNextAnimatorStateInfo(0).shortNameHash : an.GetCurrentAnimatorStateInfo(0).shortNameHash;
        int walkH = Animator.StringToHash(walk), idleH = Animator.StringToHash(idle);
        if (speed >= MoveSpeed) { if (to != walkH) PlayAnim(ctx, walk); return; }   // 대기로 섞는 중이어도 바로(기다리면 0.4초 대기 자세로 미끄러졌다)
        if (to == walkH && _stillFor >= StillHold) PlayAnim(ctx, idle);   // 걷기(로 가는 중 포함)인데 서 있다 → 대기
    }

    private static float FlatSpeed(MonsterContext ctx)
    {
        Vector3 v = ctx.Agent.velocity; v.y = 0f;
        return v.magnitude;
    }

    public void Exit(MonsterContext ctx)
    {
        if (ctx.Agent != null)
        {
            ctx.Agent.updateRotation = true;
            if (ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
        }
    }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        ctx.Transform.rotation = Quaternion.RotateTowards(ctx.Transform.rotation,   // 초당 상한(10-18 실측 회전 튐 55~74°)
            Quaternion.Slerp(ctx.Transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * FaceSpeed), TurnCap * Time.deltaTime);
    }

    private static void ApplyAnimSpeed(MonsterContext ctx)
    {
        if (ctx.Animator == null) return;
        var fg = ctx.Monster as ForestGuardianMonster;
        ctx.Animator.speed = (fg != null && fg.FGBlackboard.IsPhase2) ? 1.2f : 1f;
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.CrossFadeInFixedTime(stateName, WalkBlend);   // 고정 0.2초 — 앞 클립 길이 비율이면 긴 공격 뒤 늘어졌다(10-18)
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// AttackReady
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGAttackReadyState : IMonsterState
{
    private const float FaceSpeed      = 3f;
    private const float ReadyTurnCap   = 150f;   // 공격 대기 회전 초당 상한(10-18)
    // FGChaseState.EngageDistance * 1.5f — 동일 상수 유지
    private const float ChaseBackDist  = 2.5f * 1.5f;

    public void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
        PlayAnim(ctx, ctx.Animation.attackReadyStateName);
    }

    public void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        if (ctx.Runtime.DistToPlayer > ChaseBackDist)
        {
            ctx.Monster.ChangeState<ChaseState>();
            return;
        }

        FacePlayer(ctx);
    }

    public void Exit(MonsterContext ctx) { }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        ctx.Transform.rotation = Quaternion.RotateTowards(ctx.Transform.rotation,   // 서 있는 자세로 크게 돌지 않게 초당 상한(10-18 실측 391°/s)
            Quaternion.Slerp(ctx.Transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * FaceSpeed), ReadyTurnCap * Time.deltaTime);
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        var fg = ctx.Monster as ForestGuardianMonster;
        ctx.Animator.speed = fg?.FGBlackboard.AnimSpeedMult ?? 1f;
        ctx.Animator.CrossFade(stateName, ctx.Animation.crossFadeDuration);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Attack (안전망)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGAttackState : IMonsterState
{
    public void Enter(MonsterContext ctx)  => ctx.Monster.ChangeState<AttackReadyState>();
    public void Update(MonsterContext ctx) { }
    public void Exit(MonsterContext ctx)   { }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// GetHit (방어도 파괴 경직 · 그로기 포함)
//
// ─ IsGroggy = true  : GroggyAnim + GroggyDuration (기존 그로기 시스템)
// ─ IsPoiseBroken = true : 방향 GetHit 애님 + PoiseStaggerTime (방어도 파괴 경직)
//   방어도 파괴 경직이 끝나면 ClearPoiseBroken() 호출 후 추적 복귀.
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGGetHitState : GetHitState
{
    private const string GroggyAnim   = "Groggy";
    private const string HitFrontAnim = "GetHit_Front";
    private const string HitBackAnim  = "GetHit_Back";
    private const string HitLeftAnim  = "GetHit_Left";
    private const string HitRightAnim = "GetHit_Right";
    private const string HitHeavyAnim = "GetHit_Heavy";

    private bool _isGroggy;
    private bool _isPoiseBreak;

    public override void Enter(MonsterContext ctx)
    {
        // enabled=false 대신 isStopped 사용 — 재활성화 시 NavMesh Warp 스냅 텔레포트 방지
        if (ctx.Agent != null && ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.velocity  = Vector3.zero;
            ctx.Agent.ResetPath();
        }

        var fg = ctx.Monster as ForestGuardianMonster;
        _isGroggy     = fg != null && fg.FGBlackboard.IsGroggy;
        _isPoiseBreak = fg != null && fg.FGBlackboard.IsPoiseBroken;

        if (_isGroggy)
        {
            ctx.Runtime.StateTimer = ForestGuardianBlackboard.GroggyDuration;
            PlayAnim(ctx, GroggyAnim, 0.1f);
        }
        else if (_isPoiseBreak)
        {
            ctx.Runtime.StateTimer = ForestGuardianBlackboard.PoiseStaggerTime;
            PlayAnim(ctx, GetDirectionalAnim(fg), 0.05f);
        }
        else
        {
            // 방어도·그로기 모두 아닌 경우 (발생하지 않아야 하나 안전망)
            ctx.Runtime.StateTimer = 0f;
        }
    }

    public override void Update(MonsterContext ctx)
    {
        if (_isGroggy)
        {
            var fg = ctx.Monster as ForestGuardianMonster;
            if (fg != null && fg.FGBlackboard.IsGroggy) return;
        }
        else
        {
            ctx.Runtime.StateTimer -= Time.deltaTime;
            if (ctx.Runtime.StateTimer > 0f) return;
        }

        // 방어도 파괴 플래그 해제
        if (_isPoiseBreak)
            (ctx.Monster as ForestGuardianMonster)?.FGBlackboard.ClearPoiseBroken();

        RestoreAgent(ctx);

        if (ctx.Runtime.PlayerTarget != null && !ctx.Monster.IsPlayerDead())
            ctx.Monster.ChangeState<ChaseState>();
        else
            ctx.Monster.ChangeState<PatrolState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        if (_isPoiseBreak)
            (ctx.Monster as ForestGuardianMonster)?.FGBlackboard.ClearPoiseBroken();

        // Agent를 disabled하지 않았으므로 isStopped만 해제 (base.Exit의 Warp 생략)
        if (ctx.Agent != null && ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;
    }

    private static string GetDirectionalAnim(ForestGuardianMonster fg)
    {
        if (fg == null) return HitFrontAnim;
        return fg.FGBlackboard.LastHitDirection switch
        {
            ForestGuardianBlackboard.HitDirection.Back  => HitBackAnim,
            ForestGuardianBlackboard.HitDirection.Left  => HitLeftAnim,
            ForestGuardianBlackboard.HitDirection.Right => HitRightAnim,
            ForestGuardianBlackboard.HitDirection.Heavy => HitHeavyAnim,
            _                                           => HitFrontAnim,
        };
    }

    private static void PlayAnim(MonsterContext ctx, string stateName, float crossFade)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(stateName, crossFade, 0, 0f);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Die
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGDieState : DieState
{
    public override void Enter(MonsterContext ctx)
    {
        // 보스 HUD 해제
        if (ctx.Monster is ForestGuardianMonster fg)
        {
            fg.UnbindBossHudIfBoundPublic();
            // 맵 밖에서 쓰러졌으면 안쪽으로(09-30) — 시체 · 끝 장면이 벽 너머에 남지 않게
            BossArenaGuard.ReturnInside(ctx.Transform, ctx.Agent, ctx.Runtime.PlayerTarget, fg.ArenaCenter);
        }

        base.Enter(ctx);
        BossStoryScenes.PlayEnd(ctx, StoryProgress.ForestGuardian);   // 봉인기 = 봉인 · 해방기 = 처치
    }
}
}
