using System.Linq;
using UnityEngine;

/// <summary>
/// Q/E 스킬 통합 상태.
/// SkillSO.behavior가 있으면 SO에 실행을 위임하고,
/// 없으면 레거시 애니메이션 재생으로 폴백.
/// </summary>
public class ActSkillState : ActSkillStateBase
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

    protected override string StartSoundEvent => GetSkillSO()?.behavior?.StartSoundEvent ?? SoundEvent.PlayerSkill;

    protected override float GetCooldown()
    {
        float charCd = _controller.GetCharacterSkillCooldown(_slot);
        if (charCd > 0f) return charCd;
        var skillSO = GetSkillSO();
        return skillSO?.cooldown ?? 0f;
    }

    protected override void OnEnter()
    {
        _controller.CurrentAttackTypeForEffect = _actionType;

        _execution = new AbilityExecution();
        _controller.ActiveExecution = _execution;
        // 장비 스킬(E · R)은 조준 보조(A1) — 유물 Q는 각자 겨눈다
        if (_slot == SkillType.Q) _controller.RotateTowardsMousePosition();
        else                      _controller.RotateTowardsSkillAim();
        _controller.SetMoveScale(0f);
        _controller.SkillRecovering = false;
        _controller.SkillCancelLocked = false;
        _ended = false;

        // 스킬 진입 시 잔여 공격 입력 제거
        _controller.InputBuffer.TryConsume(Game.Inputs.Command.Light);

        _ctx = new SkillExecutionContext
        {
            Controller = _controller,
            Execution = _execution,
            WeaponData = _controller.WeaponManager?.CurrentWeaponData,
            Slot = _slot,
            ActionType = _actionType,
            RequestEnd = () => _ended = true,
        };
        _ctx.DamageIsAtkPercent = _slot != SkillType.Q && (GetSkillSO()?.damageIsAtkPercent ?? false);

        if (_slot != SkillType.Q) SkillTierCue.OnUse(_controller, _ctx.WeaponData, _slot, GetSkillSO());   // 단계가 오른 뒤 첫 사용 1회(10-12 S6)

        // 시전 스탠스(A4 · 결정 S3 경직 면역만): 시전 ~ 후딜 전까지 맞아도 끊기지 않는다. 피해 감소는 없다(유물 Q와 차이).
        // 유물 Q는 자기 스탠스(랜슬롯 피해 30% 감소)를 켜므로 여기서 먼저 잡지 않는다.
        if (_slot != SkillType.Q) _controller.BeginStance(_ctx, 0f);

        // 근접 장비 스킬 동안 칼날 트레일(A7) — 후딜 진입(EnterRecovery) · 종료에서 끈다
        if (_slot != SkillType.Q && !SkillTierResolver.IsRanged(_ctx.WeaponData)) _controller.TrailVfx?.BeginSkillTrail();

        // 캐릭터 고유 스킬 우선 확인
        var charRuntime = _controller.CreateCharacterSkillRuntime(_slot);
        if (charRuntime != null)
        {
            _runtime = charRuntime;
            _runtime.OnEnter(_ctx);
            return;
        }

        // 무기 기반 스킬 (기존 경로)
        var skillSO = GetSkillSO();
        var behavior = skillSO?.behavior;
        Debug.Log($"[ActSkillState] {_slot} OnEnter: skillSO={skillSO?.name ?? "NULL"}, behavior={behavior?.name ?? "NULL"}");

        if (behavior != null)
        {
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
        if (_ctx != null) _controller.EndStance(_ctx);
        _controller.SkillRecovering = false;
        _controller.SkillCancelLocked = false;
        if (_slot != SkillType.Q) _controller.TrailVfx?.EndSkillTrail();
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
            SkillType.E => wd?.skillE,
            SkillType.R => wd?.skillQ,
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

        _controller.Anim.CrossFadeInFixedTime(animName, 0.06f);
    }
}
