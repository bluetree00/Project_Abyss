using System.Linq;
using UnityEngine;

public class ActQSkillState : ILayerState<ActState>
{
    private PlayerController _controller;
    private ILayerStateChanger<ActState> _stateChanger;
    private AbilityExecution _execution;

    public void Init(PlayerController controller, ILayerStateChanger<ActState> stateChanger)
    {
        _controller = controller;
        _stateChanger = stateChanger;
    }

    public void Enter()
    {
        _controller.CurrentAttackTypeForEffect = WeaponActionType.QSkill;

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
        string animName = "QSkill_01"; // fallback

        var wd = _controller.WeaponManager?.CurrentWeaponData;
        if (wd?.animationSet is WeaponAnimationSetSO animSet)
        {
            var mapping = animSet.GetMappings(WeaponAnimGroup.Ground, WeaponActionType.QSkill)
                                 .FirstOrDefault(m => !string.IsNullOrEmpty(m.baseClipName));
            if (mapping != null)
                animName = mapping.baseClipName;
        }

        _controller.Anim.CrossFade(animName, 0.08f);
    }
}
