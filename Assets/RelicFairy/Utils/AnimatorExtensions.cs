// 파일: AnimatorExtensions.cs
// 위치: Assets/Scripts/Utility/Extensions/AnimatorExtensions.cs
using UnityEngine;

namespace Game.Utility.Extensions
{
    /// <summary>
    /// Animator 관련 확장 메서드
    /// </summary>
    public static class AnimatorExtensions
    {
        /// <summary>
        /// Animator에 특정 클립이 존재하는지 확인
        /// </summary>
        public static bool HasClip(this Animator animator, string clipName)
        {
            if (animator == null || string.IsNullOrEmpty(clipName))
                return false;

            var clips = animator.runtimeAnimatorController?.animationClips;
            if (clips == null)
                return false;

            foreach (var clip in clips)
            {
                if (clip != null && clip.name == clipName)
                    return true;
            }

            return false;
        }
    }
}
