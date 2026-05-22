using UnityEngine;
using Game.Inputs;

public class ActHeavyAttackState : ILayerState<ActState>
{
    private PlayerController _controller;
    private ILayerStateChanger<ActState> _stateChanger;
    private PlayerAnimationEventReceiver _receiver;
    private AbilityExecution _execution;

    private bool  _exitFired;
    private float _elapsed;
    private const float TimeoutSec = 3f;

    public void Init(PlayerController controller, ILayerStateChanger<ActState> stateChanger)
    {
        _controller   = controller;
        _stateChanger = stateChanger;
    }

    public void Enter()
    {
        _execution = new AbilityExecution();
        _controller.ActiveExecution = _execution;
        _controller.RotateTowardsMousePosition();

        _controller.Combo.SetAttacking(true);
        _controller.SetMoveScale(0f);

        _receiver = _controller.EventReceiver ?? _controller.GetComponentInChildren<PlayerAnimationEventReceiver>();
        SubscribeReceiver();

        _exitFired = false;
        _elapsed   = 0f;

        var animSet = _controller.WeaponManager?.CurrentWeaponData?.animationSet as WeaponAnimationSetSO;
        _controller.Anim.speed = _controller.GlobalAttackAnimSpeedScale
                               * (animSet?.heavyAttackAnimSpeed ?? 1.7f);
        PlayHeavyAnimation();
    }

    public void Update()
    {
        _elapsed += Time.deltaTime;
        if (!_exitFired && _elapsed >= TimeoutSec)
        {
            _exitFired = true;
            _stateChanger.Change(ActState.None);
        }
    }

    public void Exit()
    {
        if (_controller.Anim != null)
            _controller.Anim.speed = 1f;

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
    }

    private void UnsubscribeReceiver()
    {
        if (_receiver == null) return;
        _receiver.OnAttackEnd -= OnAttackEnd;
    }

    private void OnAttackEnd()
    {
        if (_exitFired) return;
        _exitFired = true;
        _stateChanger.Change(ActState.None);
    }

    private void PlayHeavyAnimation()
    {
        var action = _controller.CurrentAttackTypeForEffect;
        string animName = $"{action}Attack";
        _controller.Anim.CrossFade(animName, 0.08f);
    }
}
