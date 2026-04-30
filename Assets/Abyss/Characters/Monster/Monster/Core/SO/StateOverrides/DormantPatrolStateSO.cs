using UnityEngine;

namespace Abyss.Monster
{
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

    public override void RegisterOverrides(MonsterFSM fsm, MonsterBase monster)
    {
        fsm.RegisterAs<PatrolState>(new DormantPatrolState(this));
    }

    private sealed class DormantPatrolState : IMonsterState
    {
        private readonly DormantPatrolStateSO _data;

        public DormantPatrolState(DormantPatrolStateSO data)
        {
            _data = data;
        }

        public void Enter(MonsterContext ctx)
        {
            EnterDormant(ctx);
        }

        public void Update(MonsterContext ctx)
        {
            if (ctx.Runtime.HasBeenAttacked)
                ctx.Monster.ChangeState<ChaseState>();
        }

        public void Exit(MonsterContext ctx)
        {
            ctx.Runtime.IsDormant = false;

            if (ctx.Animator != null)
                ctx.Animator.speed = 1f;

            if (_data.hideWorldHpBar)
                ctx.Monster.ShowWorldHPBar();

            if (ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();
        }

        private void EnterDormant(MonsterContext ctx)
        {
            ctx.Runtime.IsDormant = true;
            ctx.Runtime.HasBeenAttacked = false;

            if (ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();

            ctx.Agent.speed = 0f;

            if (_data.hideWorldHpBar)
                ctx.Monster.HideWorldHPBar();

            var idleStateName = string.IsNullOrEmpty(_data.overrideIdleStateName)
                ? ctx.Animation.idleStateName
                : _data.overrideIdleStateName;

            if (ctx.Animator == null || string.IsNullOrEmpty(idleStateName))
                return;

            ctx.Animator.speed = 1f;
            ctx.Animator.Play(idleStateName, 0, 0f);
            ctx.Animator.Update(0f);

            if (_data.freezeAnimator)
                ctx.Animator.speed = 0f;
        }
    }
}
}
