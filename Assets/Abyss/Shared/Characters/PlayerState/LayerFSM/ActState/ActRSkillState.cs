using System.Linq;
using UnityEngine;

/// <summary>
/// 메인 무기 R 스킬 상태.
/// WeaponAnimationSetSO에서 RSkill 클립 조회 → CrossFade 재생.
/// </summary>
public class ActRSkillState : ILayerState<ActState>
{
    private PlayerController _controller;
    private ILayerStateChanger<ActState> _stateChanger;
    private AbilityExecution _execution;

    public void Init(PlayerController controller, ILayerStateChanger<ActState> stateChanger)
    {
        _controller   = controller;
        _stateChanger = stateChanger;
    }

    public void Enter()
    {
        _controller.CurrentAttackTypeForEffect = WeaponActionType.RSkill;

        _execution = new AbilityExecution();
        _controller.ActiveExecution = _execution;
        _controller.RotateTowardsMousePosition();

        _controller.SetMoveScale(0f);
        PlaySkillAnimation();
    }

    public void Update() { }

    public void Exit()
    {
        _controller.SetMoveScale(1f);

        _controller.ActiveExecution = null;
        _execution?.Cleanup(forceEffects: false);
        _execution = null;
    }

    private void PlaySkillAnimation()
    {
        string animName = "RSkill_01"; // fallback

        // R스킬은 메인 무기 기준
        var wd = _controller.WeaponManager?.MainWeaponData;
        if (wd?.animationSet is WeaponAnimationSetSO animSet)
        {
            var mapping = animSet.GetMappings(WeaponAnimGroup.Ground, WeaponActionType.RSkill)
                                 .FirstOrDefault(m => !string.IsNullOrEmpty(m.baseClipName));
            if (mapping != null)
                animName = mapping.baseClipName;
        }

        _controller.Anim.CrossFade(animName, 0.08f);
    }
}
