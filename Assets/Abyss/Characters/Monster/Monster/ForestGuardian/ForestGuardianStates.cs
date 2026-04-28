using UnityEngine;
using UnityEngine.AI;

namespace Abyss.Monster
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
    private const float FaceSpeed = 8f;

    public void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null)
        {
            ctx.Agent.enabled         = true;
            ctx.Agent.updateRotation  = false;
            if (ctx.Agent.isOnNavMesh) ctx.Agent.isStopped = false;
            ctx.Agent.speed           = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier;
        }
        ApplyAnimSpeed(ctx);
        PlayAnim(ctx, ctx.Animation.chaseStateName);
    }

    public void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        if (ctx.Runtime.DistToPlayer <= ctx.Monster.GetCombatHitDistance(ctx))
        {
            ctx.Monster.ChangeState<AttackReadyState>();
            return;
        }

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.SetDestination(ctx.Runtime.PlayerTarget.position);

        FacePlayer(ctx);
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
        ctx.Transform.rotation = Quaternion.Slerp(
            ctx.Transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * FaceSpeed);
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
        ctx.Animator.CrossFade(stateName, ctx.Animation.crossFadeDuration);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// AttackReady
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGAttackReadyState : IMonsterState
{
    private const float FaceSpeed = 3f;

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

        if (ctx.Runtime.DistToPlayer > ctx.Monster.GetCombatHitDistance(ctx) * 1.3f)
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
        ctx.Transform.rotation = Quaternion.Slerp(
            ctx.Transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * FaceSpeed);
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.speed = 1f;
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
        ctx.Agent.enabled = false;

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
        // 상태가 외부 전환(사망 등)으로 종료될 경우에도 플래그 정리
        if (_isPoiseBreak)
            (ctx.Monster as ForestGuardianMonster)?.FGBlackboard.ClearPoiseBroken();
        base.Exit(ctx);
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
            fg.UnbindBossHudIfBoundPublic();

        base.Enter(ctx);
    }
}
}
