using UnityEngine;
using System.Collections;

namespace Game.CharacterStates.CharacterControllerStates
{
    public abstract class AnimationState<T> : State<T> where T : CharacterController
    {
        private string _animName;
        private float _endTime = 0.95f;
        private int _layer = 0;
        private bool _blocksInput = true;

        public override bool BlocksInput => _blocksInput;

        protected void SetupAnimation(string animName, float endTime = 0.95f, int layer = 0)
        {
            _animName = animName;
            _endTime = endTime;
            _layer = layer;
        }

        public override void Enter(T owner)
        {
            owner.Anim.CrossFade(_animName, 0.1f);
        }

        public override void Execute(T owner)
        {
            if (owner.Anim.IsInTransition(_layer)) return;

            if (AnimationHelper.IsAnimationFinished(owner.Anim, _animName, _endTime, _layer))
            {
                _blocksInput = false;
                OnAnimationEnd(owner);
            }
        }

        public override void Exit(T owner)
        {
            _blocksInput = false;
        }

        protected abstract void OnAnimationEnd(T owner);
    }

    // 기본 상속할 상태 클래스들

    public class IdleState<T> : State<T> where T : CharacterController
    {
        public override void Enter(T owner)
        {
            string idleAnim = owner.GetIdleAnimationName();
            owner.Anim.CrossFade(idleAnim, 0.1f);
        }

        public override void Execute(T owner)
        {
            // Idle 상태에서 필요한 로직이 있다면 여기에
        }

        public override void Exit(T owner)
        {
            Debug.Log($"{typeof(T).Name} - Idle 상태 종료");
        }
    }

    public class MoveState<T> : State<T> where T : CharacterController
    {
        public override void Enter(T owner)
        {
            owner.Anim?.CrossFade("Moving", 0.1f);
        }

        public override void Execute(T owner)
        {
            owner.Move(owner.MoveDirection, owner.CharacterData.baseMoveSpeed);
        }

        public override void Exit(T owner) { }
    }

    public class RunningState<T> : State<T> where T : CharacterController
    {
        public override void Enter(T owner)
        {
            owner.Anim?.CrossFade("Running", 0.1f);
        }

        public override void Execute(T owner)
        {
            owner.Move(owner.MoveDirection, owner.CharacterData.baseRunSpeed);
        }

        public override void Exit(T owner) { }
    }

    public class DodgeState<T> : AnimationState<T> where T : CharacterController
    {
        private readonly Vector3 direction;
        private readonly float speed;
        private readonly float duration;

        public DodgeState(Vector3 direction, float speed, float duration)
        {
            this.direction = direction;
            this.speed = speed;
            this.duration = duration;
            SetupAnimation("Dodge", 0.9f);
        }

        public override void Enter(T owner)
        {
            base.Enter(owner);
            owner.StartCoroutine(DodgeRoutine(owner));
        }

        private IEnumerator DodgeRoutine(T owner)
        {
            float startTime = Time.time;
            while (Time.time < startTime + duration)
            {
                if (owner == null) yield break;
                if (owner.Rigid == null) yield break;

                owner.Rigid.velocity = direction * speed;
                Quaternion targetRot = Quaternion.LookRotation(direction);
                owner.transform.rotation = Quaternion.Slerp(owner.transform.rotation, targetRot, Time.deltaTime * 10f);
                yield return null;
            }

            if (owner?.Rigid != null)
                owner.Rigid.velocity = Vector3.zero;

            if (owner?.StateMachine != null)
                owner.StateMachine.ChangeState(new IdleState<T>());
        }

        protected override void OnAnimationEnd(T owner)
        {
            // 상태 전이는 코루틴에서 처리됨
        }
    }

    public class DieState<T> : AnimationState<T> where T : CharacterController
    {
        public override void Enter(T owner)
        {
            SetupAnimation("Die", 0.95f);
            base.Enter(owner);
        }

        protected override void OnAnimationEnd(T owner)
        {
            Debug.Log($"{typeof(T).Name} has died.");
        }
    }
}