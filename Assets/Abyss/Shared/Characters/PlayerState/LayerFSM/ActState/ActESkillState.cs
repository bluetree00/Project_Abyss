using System.Linq;
using UnityEngine;

public class ActESkillState : ILayerState<ActState>
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
        if (!_controller.CooldownTracker.IsReady(SkillType.E))
        {
            _stateChanger.Change(ActState.None);
            return;
        }

        _controller.CurrentAttackTypeForEffect = WeaponActionType.ESkill;

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

        float cd = _controller.WeaponManager?.MainWeaponData?.skillECooldown ?? 0f;
        if (cd > 0f)
            _controller.CooldownTracker.StartCooldown(SkillType.E, cd);
    }

    private void PlaySkillAnimation()
    {
        string animName = "ESkill_01"; // fallback

        // E스킬은 메인 무기 기준
        var wd = _controller.WeaponManager?.MainWeaponData;
        if (wd?.animationSet is WeaponAnimationSetSO animSet)
        {
            var mapping = animSet.GetMappings(WeaponAnimGroup.Ground, WeaponActionType.ESkill)
                                 .FirstOrDefault(m => !string.IsNullOrEmpty(m.baseClipName));
            if (mapping != null)
                animName = mapping.baseClipName;
        }

        _controller.Anim.CrossFade(animName, 0.08f);
    }
}
