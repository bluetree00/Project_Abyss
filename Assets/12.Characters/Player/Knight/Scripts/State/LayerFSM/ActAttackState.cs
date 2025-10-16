// ActAttackState.cs
using System;
using UnityEngine;
using Game.Inputs;

/// <summary>
/// Act 레이어의 공격 상태 (Light 콤보 중심).
/// - 장비의 groundEndCount / airEndCount를 최대 콤보로 사용.
/// - 애니 이름 규칙: "NormalAttack_" + (step+1)
/// - 스킬/얼티밋은 고정 이름 사용 (별도 처리 필요 시 확장).
/// - 콤보 입력은 InputBuffer를 통해 받고, AE 이벤트로 타이밍을 제어.
/// </summary>
public class ActAttackState : ILayerState<ActState>
{
    private PlayerController _controller;
    private ILayerStateChanger<ActState> _stateChanger;

    // 콤보 관련
    private int _maxCombo = 1;                 // 무기에서 읽어옴 (최대 콤보 수)
    private float _comboExpiryTime = 0f;       // 콤보 윈도우 만료 시점 (unscaled time)
    private float _comboWindowSec => Mathf.Max(0.05f, _controller?.LightComboResetTime ?? 0.18f);

    public void Init(PlayerController controller, ILayerStateChanger<ActState> stateChanger)
    {
        _controller = controller;
        _stateChanger = stateChanger;
    }

    public void Enter()
    {
        // 기본 플래그
        _controller.isAttacking = true;
        _controller.nextComboQueued = false;
        _controller.comboWindowOpen = false;

        // 현재 장비로부터 최대 콤보 결정 (지상/공중 구분)
        var wd = _controller.WeaponManager.CurrentWeaponData;
        bool isAir = !_controller.IsGrounded();

        if (wd != null)
            _maxCombo = isAir ? Mathf.Max(1, wd.airEndCount) : Mathf.Max(1, wd.groundEndCount);
        else
        {
            _maxCombo = 1;
            Debug.LogWarning("[ActAttackState] 장비 데이터 없음 - maxCombo=1 사용");
        }

        // 안전하게 currentComboStep 보정
        if (_controller.currentComboStep < 0) _controller.currentComboStep = 0;
        if (_controller.currentComboStep >= _maxCombo) _controller.currentComboStep = 0;

        // 이동 제어(예: 공격중 이동 감소)
        _controller.AcquireMoveLock();
        _controller.SetMoveScale(0.5f);

        // 즉시 현재 콤보 스텝 애니 재생
        PlayCurrentComboAnimation();
    }

    public void Update()
    {
        // 콤보 윈도우 열려 있을 때 입력 소비
        if (_controller.comboWindowOpen && _controller.InputBuffer.TryConsume(Command.Light))
        {
            _controller.nextComboQueued = true;
        }

        // 콤보 윈도우 타임아웃 검사
        if (_controller.comboWindowOpen && Time.unscaledTime >= _comboExpiryTime)
        {
            // 윈도우 만료 -> 콤보 리셋하고 상태 종료
            _controller.comboWindowOpen = false;
            _controller.currentComboStep = 0;
            _stateChanger.Change(ActState.None);
        }
    }

    public void Exit()
    {
        _controller.isAttacking = false;
        _controller.comboWindowOpen = false;
        _controller.nextComboQueued = false;
        _controller.ReleaseMoveLock();
        _controller.SetMoveScale(1f);
        _comboExpiryTime = 0f;
    }

    // ----------------- Animation Events (Animator에서 호출) -----------------
    // AE_OpenCombo : 애니에서 콤보 입력을 허용하는 프레임(또는 구간 시작)에 호출
    public void AE_OpenCombo()
    {
        _controller.OpenComboWindow();
        _comboExpiryTime = Time.unscaledTime + _comboWindowSec;
    }

    // AE_CloseCombo : 콤보 입력 구간을 닫으려면 호출 (선택)
    public void AE_CloseCombo()
    {
        _controller.CloseComboWindow();
        _comboExpiryTime = 0f;
    }

    // AE_AttackEnd : 공격 애니가 끝났을 때 호출
    public void AE_AttackEnd()
    {
        // 다음 콤보 예약이 있으면 다음 콤보로 전이
        if (_controller.nextComboQueued)
        {
            _controller.nextComboQueued = false;

            if (_controller.currentComboStep + 1 < _maxCombo)
            {
                // 다음 콤보 스텝으로 증가하고 AttackReady(혹은 바로 Attack)로 전환
                _controller.currentComboStep++;
                _stateChanger.Change(ActState.AttackReady);
            }
            else
            {
                // 이미 마지막 콤보였음 -> 리셋 후 종료
                _controller.currentComboStep = 0;
                _stateChanger.Change(ActState.None);
            }
        }
        else
        {
            // 예약이 없으면 콤보 리셋 및 종료
            _controller.currentComboStep = 0;
            _stateChanger.Change(ActState.None);
        }
    }

    // ----------------- 재생 유틸 -----------------
    private void PlayCurrentComboAnimation()
    {
        int step = _controller.currentComboStep; // 0-based
        string stateName = $"NormalAttack_{step + 1}"; // "NormalAttack_1" 등

        // Animator에 해당 클립(또는 오버라이드 키)이 존재하는지 확인하고 재생
        if (HasAnimatorClip(_controller.Anim, stateName))
        {
            _controller.Anim.CrossFade(stateName, 0.08f);
        }
        else
        {
            Debug.LogWarning($"[ActAttackState] Animator에 '{stateName}' 클립이 없습니다. (재생 생략)");
            // 재생할 수 없다면 즉시 AE_AttackEnd처럼 동작하게 할지 여부 결정 (지금은 생략)
        }
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
