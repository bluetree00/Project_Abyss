using System.Linq;
using UnityEngine;

/// <summary>
/// Q/E 스킬 통합 상태.
/// SkillSO.behavior가 있으면 SO에 실행을 위임하고,
/// 없으면 레거시 애니메이션 재생으로 폴백.
/// </summary>
public class ActSkillState : ActSkillStateBase<ActState>
{
    private readonly SkillType _slot;
    private readonly WeaponActionType _actionType;

    private AbilityExecution _execution;
    private ISkillRuntime _runtime;
    private SkillExecutionContext _ctx;
    private bool _ended;

    public ActSkillState(SkillType slot, WeaponActionType actionType)
    {
        _slot = slot;
        _actionType = actionType;
    }

    protected override SkillType Slot => _slot;

    protected override float GetCooldown()
    {
        var skillSO = GetSkillSO();
        return skillSO?.cooldown ?? 0f;
    }

    protected override void OnEnter()
    {
        _controller.CurrentAttackTypeForEffect = _actionType;

        _execution = new AbilityExecution();
        _controller.ActiveExecution = _execution;
        _controller.RotateTowardsMousePosition();
        _controller.SetMoveScale(0f);
        _ended = false;

        var skillSO = GetSkillSO();
        var behavior = skillSO?.behavior;
        Debug.Log($"[ActSkillState] {_slot} OnEnter: skillSO={skillSO?.name ?? "NULL"}, behavior={behavior?.name ?? "NULL"}");

        if (behavior != null)
        {
            _ctx = new SkillExecutionContext
            {
                Controller = _controller,
                Execution = _execution,
                WeaponData = _controller.WeaponManager?.CurrentWeaponData,
                Slot = _slot,
                ActionType = _actionType,
                RequestEnd = () => _ended = true,
            };

            _runtime = behavior.CreateRuntime();
            _runtime.OnEnter(_ctx);
        }
        else
        {
            // 레거시 폴백: 애니메이션만 재생
            PlaySkillAnimation();
        }
    }

    protected override void OnUpdate()
    {
        if (_runtime != null)
        {
            _runtime.OnUpdate(_ctx);
            if (_ended)
            {
                Debug.Log($"[ActSkillState] {_slot} ended → ActState.None");
                _stateChanger.Change(ActState.None);
            }
        }
        else
        {
            // behavior가 null이면 (레거시) 일정 시간 후 자동 종료
            _ended = true;
            _stateChanger.Change(ActState.None);
        }
    }

    protected override void OnExit()
    {
        _runtime?.OnExit(_ctx);
        _runtime = null;
        _ctx = null;
        _ended = false;

        _controller.SetMoveScale(1f);
        _controller.ActiveExecution = null;
        _execution?.Cleanup(forceEffects: false);
        _execution = null;
    }

    private SkillSO GetSkillSO()
    {
        var wd = _controller.WeaponManager?.CurrentWeaponData;
        return _slot switch
        {
            SkillType.Q => wd?.skillQ,
            SkillType.E => wd?.skillE,
            _ => null
        };
    }

    private void PlaySkillAnimation()
    {
        string animName = $"{_slot}Skill_01";

        var wd = _controller.WeaponManager?.CurrentWeaponData;
        if (wd?.animationSet is WeaponAnimationSetSO animSet)
        {
            var mapping = animSet.GetMappings(WeaponAnimGroup.Ground, _actionType)
                                 .FirstOrDefault(m => !string.IsNullOrEmpty(m.baseClipName));
            if (mapping != null) animName = mapping.baseClipName;
        }

        _controller.Anim.CrossFade(animName, 0.08f);
    }
}
