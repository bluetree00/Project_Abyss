// ActAttackState.cs
using System;
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
        _receiver = _controller.EventReceiver ?? _controller.GetComponentInChildren<PlayerAnimationEventReceiver>();

        if (!_controller.isAttacking)
        {
            _controller.isAttacking = true;
            _controller.nextComboQueued = false;
            _controller.comboWindowOpen = false;
            _controller.SetMoveScale(0f); // 이동 제한
        }

        // --- 공격 타입 이미 AttackReady에서 결정됨 ---
        var action = _controller.CurrentAttackTypeForEffect;

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
        // 콤보 입력 처리
        if (_controller.comboWindowOpen && _controller.InputBuffer.TryConsume(Command.Light))
            _controller.nextComboQueued = true;

        // 콤보 시간 만료
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



    private void PlayCurrentComboAnimation()
    {
        if (_controller == null || _controller.Anim == null)
            return;

        int step = _controller.currentComboStep;
        var action = _controller.CurrentAttackTypeForEffect;
        bool isAir = !_controller.IsGrounded();

        string stepStr = (step + 1).ToString("00"); // 01, 02, ...
        string groundStateName = $"{action}Attack_{stepStr}";
        string airStateName = $"{action}Attack_{stepStr}";

        Animator anim = _controller.Anim;
        int layerIndex = 0; // Base Layer 기준, 필요 시 레이어 맞춤
        int stateHash;
        
        if (!isAir)
        {
            // 지상 공격: 기존 clip 재생
            stateHash = Animator.StringToHash(groundStateName);

        

            if (anim.HasState(layerIndex, stateHash))
            {
                anim.CrossFade(stateHash, 0.08f);
                // anim.Update(0f); // 즉시 적용
                // anim.Play(stateHash, layerIndex, 0f); // 상태 초기화
                // Debug.Log($"[ActAttackState] Forced play state: {groundStateName}");
            }
            else
            {
                Debug.LogWarning($"Animator state not found: {groundStateName}");
            }
        }
        else
        {
            // 공중 공격: BlendTree 상태 진입
            stateHash = Animator.StringToHash(airStateName);

             Debug.Log($"[ActAttackState] Playing ground attack animation: {groundStateName}");

            if (anim.HasState(layerIndex, stateHash))
            {
                anim.CrossFade(stateHash, 0.08f);

                anim.SetFloat("AirLightAttackValue", 1); // BlendTree 파라미터 설정
            }
            else
            {
                Debug.LogWarning($"Air blend state not found: {airStateName}, fallback to ground state.");

                // fallback: 지상 clip이 존재하면 재생
                int fallbackHash = Animator.StringToHash(groundStateName);
                if (anim.HasState(layerIndex, fallbackHash))
                    anim.CrossFade(fallbackHash, 0.08f);
            }
        }
    }


}
