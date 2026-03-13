using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Game.Inputs;


public class ActHeavyAttackState : ILayerState<ActState>
{
    private PlayerController _controller;
    private ILayerStateChanger<ActState> _stateChanger;
    private PlayerAnimationEventReceiver _receiver;

    public void Init(PlayerController controller, ILayerStateChanger<ActState> stateChanger)
    {
        _controller = controller;
        _stateChanger = stateChanger;
    }

    public void Enter()
    {
        _controller.isAttacking = true;
        _controller.SetMoveScale(0f); // 공격 중 이동 제한

        _receiver = _controller.EventReceiver ?? _controller.GetComponentInChildren<PlayerAnimationEventReceiver>();
        SubscribeReceiver();

        PlayHeavyAnimation();
        _controller.BeginWeaponTrail();
    }

    public void Update() { }

    public void Exit()
    {
        UnsubscribeReceiver();
        _controller.EndWeaponTrail();
        _controller.isAttacking = false;
        _controller.SetMoveScale(1f);
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
        var action = _controller.CurrentAttackTypeForEffect; // GroundHeavy / AirHeavy
        bool isAir = !_controller.IsGrounded();
        string animName = $"{action}Attack"; // 콤보 없이 단발
        Debug.Log($"[ActHeavyAttackState] PlayHeavyAnimation - {animName}");
        _controller.Anim.CrossFade(animName, 0.08f);
    }
}
