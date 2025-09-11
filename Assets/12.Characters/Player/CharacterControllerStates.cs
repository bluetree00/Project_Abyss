using UnityEngine;

namespace Game.CharacterStates.PlayerCharacterStates
{
    /// <summary>
    /// 애니메이션 상태를 공통으로 처리하는 추상 클래스.
    /// 특정 애니메이션 이름과 종료 시간(normalizedTime)을 기준으로 상태 전환을 제어한다.
    /// </summary>
    public abstract class AnimationState<T> : State<T> where T : PlayerCharacter
    {
        private int _animHash;         // 애니메이션 해시 값
        private float _endTime;        // 애니메이션 종료 시점 (normalizedTime)
        private int _layer;            // 사용할 애니메이션 레이어
        private bool _blocksInput;     // 입력 차단 여부

        public override bool BlocksInput => _blocksInput;

        /// <summary>
        /// 애니메이션을 초기화하는 메서드. 상태 진입 전에 호출해야 한다.
        /// </summary>
        protected void InitAnimation(string animName, float endTime = 0.95f, int layer = 0, bool blocksInput = true)
        {
            _animHash = Animator.StringToHash(animName);
            _endTime = endTime;
            _layer = layer;
            _blocksInput = blocksInput;
        }

        public override void Enter(T owner)
        {
            owner.Anim.CrossFade(_animHash, 0.1f, _layer);  // 애니메이션 전환
        }

        public override void Execute(T owner)
        {
            if (owner.Anim.IsInTransition(_layer)) return;

            var stateInfo = owner.Anim.GetCurrentAnimatorStateInfo(_layer);
            if (stateInfo.shortNameHash == _animHash && stateInfo.normalizedTime >= _endTime)
            {
                _blocksInput = false;  // 애니메이션 종료 시 입력 차단 해제
                OnAnimationEnd(owner);
            }
        }

        public override void Exit(T owner)
        {
            _blocksInput = false;
        }

        /// <summary>
        /// 애니메이션 종료 시 호출될 메서드 (상속 클래스에서 구현)
        /// </summary>
        protected abstract void OnAnimationEnd(T owner);
    }

    // ---------------------------
    // 기본 행동 상태들 정의
    // ---------------------------

    /// <summary>
    /// 캐릭터의 Idle 상태. 기본 대기 상태.
    /// </summary>
    public class IdleState<T> : State<T> where T : PlayerCharacter
    {
        public override void Enter(T owner)
        {
           // owner.Anim.CrossFade("Idle", 0.2f);
        }

        public override void Execute(T owner)
        {
            // 필요 시 Idle 상태 중 처리 로직 작성
        }

        public override void Exit(T owner)
        {
           
        }
    }

    /// <summary>
    /// 캐릭터가 이동 중일 때의 상태. (걷기)
    /// </summary>
    public class MoveState<T> : State<T> where T : PlayerCharacter
    {
        public override void Enter(T owner)
        {
            owner.Anim?.CrossFade("Moving", 0.1f);
        }

        public override void Execute(T owner)
        {
            // 실제 이동 로직이 필요하면 여기에 추가
            // 예: owner.Move(owner.moveDirection, owner.CharacterData.baseMoveSpeed);
        }

        public override void Exit(T owner) { }
    }

    /// <summary>
    /// 캐릭터가 달릴 때의 상태.
    /// </summary>
    public class RuningState<T> : State<T> where T : PlayerCharacter
    {
        public override void Enter(T owner)
        {
            // 상태 진입 시 필요한 초기화가 있다면 여기에
        }

        public override void Execute(T owner)
        {
            owner.Anim?.CrossFade("Runing", 0.1f);
        }

        public override void Exit(T owner) { }
    }

    /// <summary>
    /// 캐릭터가 구르기(dodge)할 때의 상태.
    /// </summary>
    public class DodgeState<T> : State<T> where T : PlayerCharacter
    {
        public override void Enter(T owner)
        {
            owner.Anim?.CrossFade("Dodge", 0.1f);
        }

        public override void Execute(T owner)
        {
            // 구르기 도중 이동이나 회피 무적 처리 등을 추가할 수 있음
        }

        public override void Exit(T owner) { }
    }

    /// <summary>
    /// 캐릭터가 사망할 때의 상태.
    /// </summary>
    public class DieState<T> : State<T> where T : PlayerCharacter
    {
        public override void Enter(T owner)
        {
            owner.Anim?.CrossFade("Die", 0.1f);
        }

        public override void Execute(T owner)
        {
            // 죽은 후 대기 로직이 필요할 수도 있음
        }

        public override void Exit(T owner) { }
    }
}
