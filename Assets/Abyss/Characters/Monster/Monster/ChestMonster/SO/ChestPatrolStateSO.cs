using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// Mimic patrol override SO.
/// Added to MonsterConfigSO.stateOverrides to replace only PatrolState with
/// the chest-style idle disguise behavior.
/// </summary>
[CreateAssetMenu(fileName = "ChestPatrolState",
                 menuName  = "Lee/Monster/States/Patrol/ChestPatrol")]
public class ChestPatrolStateSO : MonsterStateOverrideSO
{
    public override void RegisterOverrides(MonsterFSM fsm, MonsterBase monster)
    {
        fsm.RegisterAs<PatrolState>(new MimicPatrolState());
    }

    private class MimicPatrolState : PatrolState
    {
        public override void Enter(MonsterContext ctx)
        {
            EnterDormant(ctx);
        }

        public override void Update(MonsterContext ctx)
        {
            if (ctx.Runtime.HasBeenAttacked)
                ctx.Monster.ChangeState<ChaseState>();
        }

        public override void Exit(MonsterContext ctx)
        {
            ctx.Runtime.IsDormant = false;

            if (ctx.Animator != null)
                ctx.Animator.speed = 1f;

            ctx.Monster.ShowWorldHPBar();

            if (ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();
        }

        private static void EnterDormant(MonsterContext ctx)
        {
            ctx.Runtime.IsDormant = true;
            ctx.Runtime.HasBeenAttacked = false;

            if (ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();

            ctx.Agent.speed = 0f;
            ctx.Monster.HideWorldHPBar();

            if (ctx.Animator != null && !string.IsNullOrEmpty(ctx.Animation.idleStateName))
            {
                ctx.Animator.speed = 1f;
                ctx.Animator.Play(ctx.Animation.idleStateName, 0, 0f);
                ctx.Animator.Update(0f);
                ctx.Animator.speed = 0f;
            }
        }
    }
}
}
