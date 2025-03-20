using UnityEngine;

namespace Game.CharacterStates
{
    // CharacterController를 상속하는 모든 캐릭터가 공통으로 사용하는 상태 베이스
    public abstract class BaseCharacterState : State<CharacterController>
    {
        public override void Enter(CharacterController owner)
        {
            // 공통 초기화 작업 (필요하면)
        }

        public override void Execute(CharacterController owner)
        {
            // 공통 실행 로직 (필요하면)
        }

        public override void Exit(CharacterController owner)
        {
            // 공통 종료 작업 (필요하면)
        }
    }
}
