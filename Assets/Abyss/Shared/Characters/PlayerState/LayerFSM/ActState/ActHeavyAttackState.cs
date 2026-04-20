using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Game.Inputs;


public class ActHeavyAttackState : ILayerState<ActState>
{
    private PlayerController _controller;
    private ILayerStateChanger<ActState> _stateChanger;
    private PlayerAnimationEventReceiver _receiver;
    private AbilityExecution _execution;

    public void Init(PlayerController controller, ILayerStateChanger<ActState> stateChanger)
    {
        _controller = controller;
        _stateChanger = stateChanger;
    }

    public void Enter()
    {
        // 공중이고 ClipMapping에 isPlunge가 설정된 경우 낙하 공격으로 위임
        if (!_controller.IsGrounded())
        {
            var mapping = GetAirHeavyMapping();
            if (mapping != null && mapping.isPlunge)
            {
                _controller.CurrentAttackTypeForEffect = WeaponActionType.AirPlunge;
                _controller.PendingPlunge = new PlayerController.PlungeInfo
                {
                    fallClipName = mapping.baseClipName,
                    fallSpeed    = mapping.plungeFallSpeed,
                    descendAt    = mapping.plungeDescendAt
                };
                _stateChanger.Change(ActState.Plunge);
                return;
            }
        }

        _execution = new AbilityExecution();
        _controller.ActiveExecution = _execution;
        _controller.RotateTowardsMousePosition();

        _controller.Combo.SetAttacking(true);
        _controller.SetMoveScale(0f);

        _receiver = _controller.EventReceiver ?? _controller.GetComponentInChildren<PlayerAnimationEventReceiver>();
        SubscribeReceiver();

        PlayHeavyAnimation();
    }

    public void Update() { }

    public void Exit()
    {
        UnsubscribeReceiver();
        _controller.Combo.SetAttacking(false);
        _controller.SetMoveScale(1f);

        _controller.ActiveExecution = null;
        _execution?.Cleanup(forceEffects: false);
        _execution = null;
    }

    private void SubscribeReceiver()
    {
        if (_receiver == null) return;
        _receiver.OnAttackEnd += OnAttackEnd;
        _receiver.OnHitStep += OnHitStep;
    }

    private void UnsubscribeReceiver()
    {
        if (_receiver == null) return;
        _receiver.OnAttackEnd -= OnAttackEnd;
        _receiver.OnHitStep -= OnHitStep;
    }

    private void OnAttackEnd()
    {
        _stateChanger.Change(ActState.None);
    }

    private void OnHitStep(int stepIndex)
    {
        _controller.OnAttackHitStep(stepIndex);
    }

    private void PlayHeavyAnimation()
    {
        var action = _controller.CurrentAttackTypeForEffect;
        string animName = $"{action}Attack";
        Debug.Log($"[ActHeavyAttackState] PlayHeavyAnimation - {animName}");
        _controller.Anim.CrossFade(animName, 0.08f);
    }

    private WeaponAnimationSetSO.ClipMapping GetAirHeavyMapping()
    {
        var wd      = _controller.WeaponManager?.CurrentWeaponData;
        var animSet = wd?.animationSet as WeaponAnimationSetSO;
        if (animSet == null) return null;

        foreach (var m in animSet.GetMappings(WeaponAnimGroup.Air, WeaponActionType.AirHeavy))
            return m;
        return null;
    }
}
