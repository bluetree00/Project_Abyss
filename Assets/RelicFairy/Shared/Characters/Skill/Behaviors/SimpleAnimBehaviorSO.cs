using System.Linq;
using UnityEngine;

/// <summary>
/// 단순 애니메이션 재생 스킬 행동.
/// 애니메이션을 재생하고 일정 시간 후 종료.
/// WeaponAbilitySetSO의 이펙트는 애니메이션 이벤트로 트리거.
/// </summary>
[CreateAssetMenu(menuName = "Game/Skill/SimpleAnimBehavior")]
public class SimpleAnimBehaviorSO : SkillBehaviorSO
{
    [Header("애니메이션")]
    [Tooltip("비어있으면 WeaponAnimationSetSO에서 해당 ActionType 매핑 사용")]
    public string animationStateName;
    public float crossFadeDuration = 0.08f;

    [Header("지속 시간")]
    [Tooltip("0이면 애니메이션 이벤트에 의존")]
    public float duration = 1f;

    public override ISkillRuntime CreateRuntime() => new Runtime(this);

    private class Runtime : ISkillRuntime
    {
        private readonly SimpleAnimBehaviorSO _data;
        private float _timer;

        public Runtime(SimpleAnimBehaviorSO data) => _data = data;

        public void OnEnter(SkillExecutionContext ctx)
        {
            string animName = _data.animationStateName;

            if (string.IsNullOrEmpty(animName))
            {
                animName = $"{ctx.Slot}Skill_01";
                var wd = ctx.WeaponData;
                if (wd?.animationSet is WeaponAnimationSetSO animSet)
                {
                    var mapping = animSet.GetMappings(WeaponAnimGroup.Ground, ctx.ActionType)
                                         .FirstOrDefault(m => !string.IsNullOrEmpty(m.baseClipName));
                    if (mapping != null) animName = mapping.baseClipName;
                }
            }

            ctx.Animator.CrossFade(animName, _data.crossFadeDuration);
            _timer = 0f;
        }

        public void OnUpdate(SkillExecutionContext ctx)
        {
            if (_data.duration > 0f)
            {
                _timer += Time.deltaTime;
                if (_timer >= _data.duration)
                    ctx.RequestEnd();
            }
        }

        public void OnExit(SkillExecutionContext ctx) { }
    }
}
