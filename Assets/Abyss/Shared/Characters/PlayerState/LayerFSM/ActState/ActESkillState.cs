using System.Linq;

public class ActESkillState : ActSkillStateBase<ActState>
{
    protected override SkillType Slot => SkillType.E;

    private AbilityExecution _execution;

    protected override float GetCooldown()
        => _controller.WeaponManager?.MainWeaponData?.skillECooldown ?? 0f;

    protected override void OnEnter()
    {
        _controller.CurrentAttackTypeForEffect = WeaponActionType.ESkill;

        _execution = new AbilityExecution();
        _controller.ActiveExecution = _execution;
        _controller.RotateTowardsMousePosition();
        _controller.SetMoveScale(0f);

        PlaySkillAnimation();
    }

    protected override void OnExit()
    {
        _controller.SetMoveScale(1f);
        _controller.ActiveExecution = null;
        _execution?.Cleanup(forceEffects: false);
        _execution = null;
    }

    private void PlaySkillAnimation()
    {
        string animName = "ESkill_01";

        var wd = _controller.WeaponManager?.MainWeaponData;
        if (wd?.animationSet is WeaponAnimationSetSO animSet)
        {
            var mapping = animSet.GetMappings(WeaponAnimGroup.Ground, WeaponActionType.ESkill)
                                 .FirstOrDefault(m => !string.IsNullOrEmpty(m.baseClipName));
            if (mapping != null) animName = mapping.baseClipName;
        }

        _controller.Anim.CrossFade(animName, 0.08f);
    }
}
