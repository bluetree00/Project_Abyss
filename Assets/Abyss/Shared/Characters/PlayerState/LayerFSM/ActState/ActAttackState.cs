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
    private int _maxCombo = 1;
    private float _comboExpiryTime = 0f;

    private float _comboWindowSec => Mathf.Max(0.05f, _controller?.LightComboResetTime ?? 0.18f);

    public void Init(PlayerController controller, ILayerStateChanger<ActState> stateChanger)
    {
        _controller = controller;
        _stateChanger = stateChanger;
    }

    public void Enter()
    {
         _attackEndHandled = false;
        _receiver = _controller.EventReceiver ?? _controller.GetComponentInChildren<PlayerAnimationEventReceiver>();

        if (!_controller.isAttacking)
        {
            _controller.isAttacking = true;
            _controller.nextComboQueued = false;
            _controller.comboWindowOpen = false;
            _controller.SetMoveScale(0f); // 이동 제한
        }

        var action = _controller.CurrentAttackTypeForEffect;
        var wd = _controller.WeaponManager?.CurrentWeaponData;
        bool isAir = !_controller.IsGrounded();
        _maxCombo = wd != null ? (isAir ? Mathf.Max(1, wd.airEndCount) : Mathf.Max(1, wd.groundEndCount)) : 1;

         // <-- 여기서 콤보 창을 미리 연다 (애니 이벤트에 의존하지 않음)
        // 이미 열려 있지 않다면 열고 만료시간 설정
        if (!_controller.comboWindowOpen)
        {
            _controller.OpenComboWindow();
            _comboExpiryTime = Time.unscaledTime + _comboWindowSec;
        }


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

        _attackEndHandled = false;
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

        // idempotent: 이미 열려 있으면 만료시간만 연장
        if (!_controller.comboWindowOpen)
        {
            _controller.OpenComboWindow();
        }

        // 애니에서 콤보 창을 열어주는 경우 만료시간 연장 또는 재설정
        _comboExpiryTime = Time.unscaledTime + _comboWindowSec;
    }
    private void OnCloseCombo()
    {
        if (!_controller.isAttacking) return;
        _controller.CloseComboWindow();
        _comboExpiryTime = 0f;
    }

        private bool _attackEndHandled = false;

    private void OnAttackEnd()
    {

        _controller.currentComboStep++;

        if (_controller.currentComboStep >= _maxCombo)
        {
            _controller.currentComboStep = 0;
            _controller.CloseComboWindow();
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

    // --------- 수정된 Play 함수 (오직 baseClipName 또는 fallback 사용) ----------
    private void PlayCurrentComboAnimation()
    {
        if (_controller == null || _controller.Anim == null)
            return;

        int step = _controller.currentComboStep; // 0-based
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
    /// 매핑에서 baseClipName만 반환합니다. addressableKey는 무시.
    /// comboStep은 0-based로 처리됩니다.
    /// </summary>
    private string TryGetMappedBaseClipName(int comboStep, WeaponActionType action, bool isAir)
    {
        var wd = _controller.WeaponManager?.CurrentWeaponData;
        if (wd == null) return null;

        // 실제 프로퍼티 이름을 프로젝트에 맞게 바꾸세요 (예: wd.animationSet 등)
        var animSet = wd.animationSet as WeaponAnimationSetSO;
        if (animSet == null) return null;

        WeaponAnimGroup group = isAir ? WeaponAnimGroup.Air : WeaponAnimGroup.Ground;
        var candidates = animSet.GetMappings(group, action);

        // comboIndex가 0-based로 저장되어 있다고 가정
        var mapping = candidates.FirstOrDefault(m => m.comboIndex == comboStep);
        if (mapping != null && !string.IsNullOrEmpty(mapping.baseClipName))
            return mapping.baseClipName;

        return null;
    }
}
