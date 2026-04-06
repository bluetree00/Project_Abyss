using UnityEngine;

namespace Abyss.Monster
{
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
        private bool _isReturning;
        private bool _waitingForSettle;
        private bool _waitingForDormantTransition;

        private const float ReturnArrivalThreshold = 0.5f;

        public override void Enter(MonsterContext ctx)
        {
            _isReturning = false;
            _waitingForSettle = false;
            _waitingForDormantTransition = false;

            float distToSpawn = Vector3.Distance(ctx.Transform.position, ctx.Runtime.SpawnPosition);
            if (distToSpawn > ReturnArrivalThreshold)
                EnterReturn(ctx);
            else
                EnterDormant(ctx);
        }

        public override void Update(MonsterContext ctx)
        {
            if (ctx.Runtime.HasBeenAttacked)
            {
                ctx.Monster.ChangeState<ChaseState>();
                return;
            }

            if (_isReturning)
            {
                UpdateReturn(ctx);
                return;
            }

            UpdateDormantTransition(ctx);
        }

        public override void Exit(MonsterContext ctx)
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

            ctx.Monster.ShowWorldHPBar();

            if (ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();
        }

        // ── 귀환 ────────────────────────────────────────────

        private void EnterReturn(MonsterContext ctx)
        {
            _isReturning = true;
            _waitingForSettle = false;
            ctx.Runtime.IsDormant = false;
            ctx.Runtime.IsReturning = true;
            ctx.Runtime.HasBeenAttacked = false;
            ctx.Runtime.TargetCleared = false;

            ctx.Monster.HideWorldHPBar();

            float speed = ctx.Patrol.patrolSpeed > 0f
                ? ctx.Patrol.patrolSpeed
                : ctx.Stat.moveSpeed;
            ctx.Agent.speed = speed;

            if (ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
                ctx.Agent.SetDestination(ctx.Runtime.SpawnPosition);

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

            // Warp 후 1프레임 대기 — Agent/물리가 정착한 뒤 잠복
            if (_waitingForSettle)
            {
                EnterDormant(ctx);
                return;
            }

            KeepReturnAnimation(ctx);

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

        // ── 잠복 ────────────────────────────────────────────

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
            ctx.Monster.HideWorldHPBar();

            if (ctx.Animator != null && !string.IsNullOrEmpty(ctx.Animation.idleStateName))
            {
                ResetCombatTriggers(ctx);
                ctx.Animator.speed = 1f;
                if (!string.IsNullOrEmpty(ctx.Animation.speedParam))
                    ctx.Animator.SetFloat(ctx.Animation.speedParam, 0f);
                ctx.Animator.CrossFade(ctx.Animation.idleStateName, 0.25f);
                _waitingForDormantTransition = true;
            }
        }

        private void UpdateDormantTransition(MonsterContext ctx)
        {
            if (!_waitingForDormantTransition) return;
            if (ctx.Animator == null) return;
            if (ctx.Animator.IsInTransition(0)) return;

            int idleHash = Animator.StringToHash(ctx.Animation.idleStateName);
            var current = ctx.Animator.GetCurrentAnimatorStateInfo(0);
            if (current.shortNameHash == idleHash)
            {
                ctx.Animator.speed = 0f;
                _waitingForDormantTransition = false;
            }
        }

        // ── 헬퍼 ────────────────────────────────────────────

        private void SnapToSpawnAndDormant(MonsterContext ctx)
        {
            ctx.Transform.position = ctx.Runtime.SpawnPosition;
            StopPhysics(ctx);
            EnterDormant(ctx);
        }

        private static void StopPhysics(MonsterContext ctx)
        {
            var rb = ctx.Monster.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            if (ctx.Agent.isActiveAndEnabled)
                ctx.Agent.velocity = Vector3.zero;
        }

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

            ResetCombatTriggers(ctx);
            ctx.Animator.speed = 1f;
            ctx.Animator.CrossFade(moveName,
                Mathf.Min(0.08f, ctx.Animation.crossFadeDuration));
        }

        private static void ResetCombatTriggers(MonsterContext ctx)
        {
            if (ctx.Animator == null) return;

            foreach (var p in ctx.Animator.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Trigger)
                    ctx.Animator.ResetTrigger(p.nameHash);
            }
        }
    }
}
}
