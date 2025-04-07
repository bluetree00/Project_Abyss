using UnityEngine;

namespace Game.CharacterStates.CharacterControllerStates
{
    /// <summary>
    /// 애니메이션 상태 관련 유틸리티 함수 모음
    /// 상태머신에서 애니메이션 진행 상태를 확인하거나, 조건 처리를 쉽게 해주는 헬퍼 클래스
    /// </summary>
    public static class AnimationHelper
    {
        /// <summary>
        /// 지정된 애니메이션이 일정 비율 이상 재생되었는지 확인
        /// </summary>
        /// <param name="anim">Animator 인스턴스</param>
        /// <param name="animationName">확인할 애니메이션 이름</param>
        /// <param name="endTime">종료로 간주할 normalizedTime (기본값: 0.95)</param>
        /// <param name="layer">애니메이션 레이어 (기본값: 0)</param>
        /// <returns>애니메이션이 끝났다면 true, 아니면 false</returns>
        public static bool IsAnimationFinished(Animator anim, string animationName, float endTime = 0.95f, int layer = 0)
        {
            if (anim == null) return false;
            AnimatorStateInfo animState = anim.GetCurrentAnimatorStateInfo(layer);
            return animState.IsName(animationName) && animState.normalizedTime >= endTime;
        }

        /// <summary>
        /// 현재 재생 중인 애니메이션의 normalizedTime (0.0f ~ 1.0f 이상)을 반환
        /// 애니메이션의 재생 진행률을 알 수 있음
        /// </summary>
        public static float GetAnimationNormalizedTime(Animator anim, int layer = 0)
        {
            return anim.GetCurrentAnimatorStateInfo(layer).normalizedTime;
        }

        /// <summary>
        /// 현재 애니메이터가 전이(Transition) 중인지 확인
        /// 전이 중에는 애니메이션 관련 조건 체크를 피하는 게 좋음
        /// </summary>
        public static bool IsInTransition(Animator anim, int layer = 0)
        {
            return anim.IsInTransition(layer);
        }

        /// <summary>
        /// 현재 재생 중인 애니메이션이 특정 이름인지 확인
        /// </summary>
        public static bool IsPlaying(Animator anim, string animName, int layer = 0)
        {
            return anim.GetCurrentAnimatorStateInfo(layer).IsName(animName);
        }
    }
}
