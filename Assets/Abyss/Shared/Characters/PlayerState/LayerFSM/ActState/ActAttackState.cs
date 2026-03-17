// ActAttackState.cs (콤보는 baseClipName 우선, addressableKey 무시 버전)
using System;
using System.Linq;
using UnityEngine;
using Game.Inputs;
using Game.Utility.Extensions;

public class ActAttackState : ILayerState<ActState>
{
    private PlayerController _controller;
    private ILayerStateChanger<ActState> _stateChanger;
    private PlayerAnimationEventReceiver _receiver;
    private AbilityExecution _execution;
    private int _maxCombo = 1;
    private float _comboExpiryTime = 0f;

    private float _comboWindowSec => Mathf.Max(0.05f, _controller?.Combo?.ResetTime ?? 0.18f);

    public void Init(PlayerController controller, ILayerStateChanger<ActState> stateChanger)
    {
        _controller = controller;
        _stateChanger = stateChanger;
    }

    public void Enter()
    {
        // 공중 콤보 스텝이 낙하 공격으로 지정된 경우 ActPlungeState로 위임
        if (!_controller.IsGrounded())
        {
            int plungeStep    = _controller.Combo.CurrentComboStep;
            var plungeAction  = _controller.CurrentAttackTypeForEffect;
            var plungeMapping = TryGetClipMapping(plungeStep, plungeAction, isAir: true);
            if (plungeMapping != null && plungeMapping.isPlunge)
            {
                _controller.CurrentAttackTypeForEffect = WeaponActionType.AirPlunge;
                _controller.PendingPlunge = new PlayerController.PlungeInfo
                {
                    fallClipName = plungeMapping.baseClipName,
                    fallSpeed    = plungeMapping.plungeFallSpeed
                };
                _stateChanger.Change(ActState.Plunge);
                return;
            }
        }

        _attackEndHandled = false;
        _execution = new AbilityExecution();
        _controller.ActiveExecution = _execution;
        _controller.RotateTowardsMousePosition();

        _receiver = _controller.EventReceiver ?? _controller.GetComponentInChildren<PlayerAnimationEventReceiver>();

        if (!_controller.Combo.IsAttacking)
        {
            _controller.Combo.SetAttacking(true);
            _controller.Combo.SetNextComboQueued(false);
            _controller.Combo.CloseWindow();
            _controller.SetMoveScale(0f); // 이동 제한
        }

        var action = _controller.CurrentAttackTypeForEffect;
        var wd = _controller.WeaponManager?.CurrentWeaponData;
        bool isAir = !_controller.IsGrounded();
        _maxCombo = wd != null ? (isAir ? Mathf.Max(1, wd.airEndCount) : Mathf.Max(1, wd.groundEndCount)) : 1;

        // 콤보 창을 미리 연다 (애니 이벤트에 의존하지 않음)
        if (!_controller.Combo.ComboWindowOpen)
        {
            _controller.Combo.OpenWindow();
            _comboExpiryTime = Time.unscaledTime + _comboWindowSec;
        }


        SubscribeReceiver();
        PlayCurrentComboAnimation();
        _controller.BeginWeaponTrail();
    }

    public void Update()
    {
        if (_controller.Combo.ComboWindowOpen && _controller.InputBuffer.TryConsume(Command.Light))
            _controller.Combo.SetNextComboQueued(true);

        if (_controller.Combo.ComboWindowOpen && Time.unscaledTime >= _comboExpiryTime)
        {
            _controller.Combo.CloseWindow();
            _stateChanger.Change(ActState.None);
        }
    }

    public void Exit()
    {
        UnsubscribeReceiver();
        _controller.EndWeaponTrail();

        _attackEndHandled = false;
        _controller.Combo.SetAttacking(false);
        _controller.Combo.SetNextComboQueued(false);
        _controller.Combo.CloseWindow();
        _controller.SetMoveScale(1f);
        _comboExpiryTime = 0f;

        _controller.ActiveExecution = null;
        _execution?.Cleanup(forceEffects: false);
        _execution = null;
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
        if (!_controller.Combo.IsAttacking) return;

        if (!_controller.Combo.ComboWindowOpen)
            _controller.Combo.OpenWindow();

        _comboExpiryTime = Time.unscaledTime + _comboWindowSec;
    }

    private void OnCloseCombo()
    {
        if (!_controller.Combo.IsAttacking) return;
        _controller.Combo.CloseWindow();
        _comboExpiryTime = 0f;
    }

    private bool _attackEndHandled = false;

    private void OnAttackEnd()
    {
        _controller.Combo.IncrementStep();

        if (_controller.Combo.CurrentComboStep >= _maxCombo)
        {
            _controller.Combo.ResetStep();
            _controller.Combo.CloseWindow();
            _stateChanger.Change(ActState.None);
        }
    }

    private void OnHitStep(int stepIndex)
    {
        if (!_controller.Combo.IsAttacking || stepIndex < 0) return;
        _controller.OnAttackHitStep(stepIndex);
    }

    private void OnGenericTag(string tag)
    {
        if (!_controller.Combo.IsAttacking) return;
        _controller.OnAnimationEventTag(tag);
    }

    // --------- 수정된 Play 함수 (오직 baseClipName 또는 fallback 사용) ----------
    private void PlayCurrentComboAnimation()
    {
        if (_controller == null || _controller.Anim == null)
            return;

        int step = _controller.Combo.CurrentComboStep; // 0-based
        var action = _controller.CurrentAttackTypeForEffect;
        bool isAir = !_controller.IsGrounded();

        // 1) 매핑에서 baseClipName을 찾아서 사용 (addressableKey 무시)
        string mappedBaseName = TryGetMappedBaseClipName(step, action, isAir);

        // 2) fallback 네이밍 (요구하신 형식)
        string fallbackStateName = $"{action}Attack_{(step + 1).ToString("00")}";

        string stateToPlay = !string.IsNullOrEmpty(mappedBaseName) ? mappedBaseName : fallbackStateName;

        Animator anim = _controller.Anim;
        int layerIndex = 0;
        int stateHash = Animator.StringToHash(stateToPlay);

        if (!isAir)
        {
            if (anim.HasState(layerIndex, stateHash))
            {
                anim.CrossFade(stateHash, 0.08f);
            }
            else
            {
                // fallback이 이미 fallbackStateName이면 더이상 시도할 게 없음
                if (stateToPlay != fallbackStateName)
                {
                    int fallbackHash = Animator.StringToHash(fallbackStateName);
                    if (anim.HasState(layerIndex, fallbackHash))
                    {
                        anim.CrossFade(fallbackHash, 0.08f);
                        Debug.Log($"[ActAttackState] Fallback to hardcoded ground state: {fallbackStateName}");
                        return;
                    }
                }
                Debug.LogWarning($"Animator state not found: {stateToPlay}");
            }
        }
        else
        {
            if (anim.HasState(layerIndex, stateHash))
            {
                anim.CrossFade(stateHash, 0.08f);
                // 공중 블렌드 파라미터는 프로젝트에 따라 Animator에 정의되어 있어야 함.
                // 파라미터가 없으면 Unity가 경고를 띄우지만 런타임 에러는 발생하지 않습니다.
                anim.SetFloat("AirLightAttackValue", 1f);
            }
            else
            {
                int fallbackHash = Animator.StringToHash(fallbackStateName);
                if (anim.HasState(layerIndex, fallbackHash))
                {
                    anim.CrossFade(fallbackHash, 0.08f);
                    Debug.Log($"[ActAttackState] Air state not found ({stateToPlay}), fallback to ground {fallbackStateName}");
                }
                else
                {
                    Debug.LogWarning($"Air blend state not found: {stateToPlay} and fallback {fallbackStateName} not found.");
                }
            }
        }
    }

    /// <summary>
    /// 해당 콤보 스텝의 ClipMapping 전체를 반환합니다.
    /// isPlunge 등 플래그 조회에 사용됩니다.
    /// </summary>
    private WeaponAnimationSetSO.ClipMapping TryGetClipMapping(int comboStep, WeaponActionType action, bool isAir)
    {
        var wd      = _controller.WeaponManager?.CurrentWeaponData;
        var animSet = wd?.animationSet as WeaponAnimationSetSO;
        if (animSet == null) return null;

        WeaponAnimGroup group = isAir ? WeaponAnimGroup.Air : WeaponAnimGroup.Ground;
        return animSet.GetMappings(group, action).FirstOrDefault(m => m.comboIndex == comboStep);
    }

    /// <summary>baseClipName만 반환하는 편의 래퍼</summary>
    private string TryGetMappedBaseClipName(int comboStep, WeaponActionType action, bool isAir)
    {
        var mapping = TryGetClipMapping(comboStep, action, isAir);
        return (mapping != null && !string.IsNullOrEmpty(mapping.baseClipName))
            ? mapping.baseClipName
            : null;
    }
}
