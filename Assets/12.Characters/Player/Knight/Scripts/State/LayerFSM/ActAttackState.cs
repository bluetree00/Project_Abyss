using System;
using UnityEngine;
using Game.Inputs;

public class ActAttackState : ILayerState<ActState>
{
    private PlayerController _controller;
    private ILayerStateChanger<ActState> _stateChanger;

    private int _maxCombo = 1;
    private float _comboExpiryTime = 0f;
    private PlayerAnimationEventReceiver _receiver;

    private float _comboWindowSec => Mathf.Max(0.05f, _controller?.LightComboResetTime ?? 0.18f);

    public void Init(PlayerController controller, ILayerStateChanger<ActState> stateChanger)
    {
        _controller = controller;
        _stateChanger = stateChanger;
    }

    public void Enter()
    {
          
        _receiver = _controller.EventReceiver ?? _controller.GetComponentInChildren<PlayerAnimationEventReceiver>();

        if (!_controller.isAttacking)
        {
            _controller.isAttacking = true;
            _controller.nextComboQueued = false;
            _controller.comboWindowOpen = false;
            _controller.SetMoveScale(0f); // 속도만 줄임
        }

        // 무기 정보 기반 최대 콤보 계산
        var wd = _controller.WeaponManager?.CurrentWeaponData;
        bool isAir = !_controller.IsGrounded();
        _maxCombo = wd != null ? (isAir ? Mathf.Max(1, wd.airEndCount) : Mathf.Max(1, wd.groundEndCount)) : 1;

        // 현재 콤보 보정
        _controller.currentComboStep = Mathf.Clamp(_controller.currentComboStep, 0, _maxCombo - 1);

        SubscribeReceiver();
        PlayCurrentComboAnimation();
    }

    public void Update()
    {
        if (_controller.comboWindowOpen && _controller.InputBuffer.TryConsume(Command.Light))
            _controller.nextComboQueued = true;

        if (_controller.comboWindowOpen && Time.unscaledTime >= _comboExpiryTime)
        {
            _controller.comboWindowOpen = false;
            _stateChanger.Change(ActState.None);
        }
    }

    public void Exit()
    {
        UnsubscribeReceiver();
        _controller.isAttacking = false;
        _controller.nextComboQueued = false;
        _controller.comboWindowOpen = false;
        _controller.SetMoveScale(1f);
        _comboExpiryTime = 0f;
    }

    private void SubscribeReceiver()
    {
        if (_receiver == null) return;
        _receiver.OnOpenCombo += OnOpenCombo;
        _receiver.OnCloseCombo += OnCloseCombo;
        _receiver.OnAttackEnd += OnAttackEnd;
        _receiver.OnHitStep += OnHitStep;
        _receiver.OnGenericTag += OnGenericTag;
    }

    private void UnsubscribeReceiver()
    {
        if (_receiver == null) return;
        _receiver.OnOpenCombo -= OnOpenCombo;
        _receiver.OnCloseCombo -= OnCloseCombo;
        _receiver.OnAttackEnd -= OnAttackEnd;
        _receiver.OnHitStep -= OnHitStep;
        _receiver.OnGenericTag -= OnGenericTag;
    }

    private void OnOpenCombo()
    {
        if (!_controller.isAttacking) return;
        _controller.OpenComboWindow();
        _comboExpiryTime = Time.unscaledTime + _comboWindowSec;
    }

    private void OnCloseCombo()
    {
        if (!_controller.isAttacking) return;
        _controller.CloseComboWindow();
        _comboExpiryTime = 0f;
    }

    private void OnAttackEnd()
    {
        _controller.currentComboStep++;

        // 최대 콤보 체크
        if (_controller.currentComboStep >= _maxCombo)
        {
            _controller.currentComboStep = 0;
            _controller.CloseComboWindow();   // 콤보 창 닫고 타이머 갱신
            _stateChanger.Change(ActState.None);
        }
    }

    private void OnHitStep(int stepIndex)
    {
        if (!_controller.isAttacking || stepIndex < 0) return;
        _controller.OnAttackHitStep(stepIndex);
    }

    private void OnGenericTag(string tag)
    {
        if (!_controller.isAttacking) return;
        _controller.OnAnimationEventTag(tag);
    }

    private void PlayCurrentComboAnimation()
    {
        int step = _controller.currentComboStep;
        string stateName = $"NormalAttack_{step + 1}";

        if (HasAnimatorClip(_controller.Anim, stateName))
            _controller.Anim.CrossFade(stateName, 0.08f);
        else
            Debug.LogWarning($"[ActAttackState] '{stateName}' 클립 없음.");
    }

    private bool HasAnimatorClip(Animator animator, string clipName)
    {
        if (animator == null || string.IsNullOrEmpty(clipName)) return false;
        var clips = animator.runtimeAnimatorController?.animationClips;
        if (clips == null) return false;
        foreach (var c in clips)
            if (c != null && c.name == clipName) return true;
        return false;
    }
}
