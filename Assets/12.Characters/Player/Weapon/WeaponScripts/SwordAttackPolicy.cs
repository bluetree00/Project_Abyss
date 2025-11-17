using System;
using UnityEngine;
using Game.Inputs;

/// <summary>
/// SwordAttackPolicy - enterThreshold(모으기 진입 지연) + fullThreshold(강공격 확정)
/// - enterThreshold 미만: 짧은 탭(바로 라이트)
/// - enterThreshold 이상: 모으기 시작 표시 (한 번만 Command.Charge 푸시)
/// - fullThreshold 도달: 강공격 확정 -> Pending(Heavy) 설정 (한 번만)
/// - OnCanceled: 이미 강공격이 확정되었으면 중복 방지, 아니면 라이트 푸시(또는 Pending)
/// </summary>
public class SwordAttackPolicy : IAttackInputPolicy
{
    private readonly float _enterThreshold;  // 모으기 시작을 인식할 최소 시간 (예: 0.08s)
    private readonly float _fullThreshold;   // 강공격 확정 시간 (예: 0.25s)
    private readonly int _maxChargeStage;    // 확장용(현재는 2)

    private float _startTime;
    private bool _holding;

    private bool _chargingStarted;   // enterThreshold를 넘겨 모으기 표시를 이미 보냈는가
    private bool _promoted;          // fullThreshold를 넘겨 강공격 Pending을 세팅했는가

    // 외부에서 필요하면 stage 정보 제공 가능
    private int _currentStage;
    public int CurrentStage => _currentStage;

    //장비의 값으로 차지하게 추후 수정
    public SwordAttackPolicy(float enterThreshold = 2f, float fullThreshold = 3f, int maxChargeStage = 2)
    {

        _enterThreshold = Mathf.Max(0.0f, enterThreshold);
        _fullThreshold = Mathf.Max(_enterThreshold, fullThreshold);
        _maxChargeStage = Mathf.Max(1, maxChargeStage);
    }

    public void OnStarted(PlayerController c)
    {
        if (c.isAttacking)
        {
            Debug.Log("[SwordPolicy] Already attacking -> ignore input");
            return; // 공격 중이면 무시
        }


        _holding = true;
        _startTime = Time.unscaledTime;
        _chargingStarted = false;
        _promoted = false;
        _currentStage = 1;

        // (옵션) 시작 VFX는 ChargeState에서 처리하도록 두거나, 여기서 시그널만 보냄
        // Debug.Log("[SwordPolicy] OnStarted");
    }

    public void OnCanceled(PlayerController c)
    {
        if (!_holding) return;
        _holding = false;

        // 이미 fullThreshold에서 promoted 되어 Pending이 올라갔으면 중복 처리 금지
        if (_promoted)
        {
            Debug.Log($"[SwordPolicy] Released after promoted -> nothing to do (stage={_currentStage})");
            return;
        }

        if (c.isAttacking)
        {
            // 공격 중에는 모으기 진행 불가
            return;
        }

        float held = Time.unscaledTime - _startTime;

        // 아직 promoted 되지 않았다면 release 시점으로 판단
        if (held >= _fullThreshold)
        {
            // full 도달 (이 브랜치는 보통 Tick에서 처리되므로 드물게 실행)
            _currentStage = _maxChargeStage;
            // 상태 흐름을 위해 Pending 설정
            c.SetPendingAttack(Command.Heavy);

            // (선택) InputBuffer 기록
            c.InputBuffer.Push(Game.Inputs.Command.Heavy);
            Debug.Log($"[SwordPolicy] Released -> set pending Heavy (held={held:F2})");
        }
        else
        {
            // full 미달 -> 라이트
            _currentStage = 1;
            c.SetPendingAttack(Command.Light);
            c.InputBuffer.Push(Game.Inputs.Command.Light);
            Debug.Log($"[SwordPolicy] Released -> set pending Light (held={held:F2})");
        }
    }

    public void Tick(PlayerController c, float dt)
    {
        if (!_holding || c.isAttacking) return;

        float held = Time.unscaledTime - _startTime;

        // 모으기 표시
        if (!_chargingStarted && held >= _enterThreshold)
        {
            _chargingStarted = true;
            c.InputBuffer.Push(Command.Charge);
            Debug.Log($"Charging started (held={held:F2})");
        }

        // 강공격 Pending 설정 (fullThreshold)
        if (!_promoted && held >= _fullThreshold)
        {
            _promoted = true;
            c.SetPendingAttack(Command.Heavy);
            c.InputBuffer.Push(Command.Heavy);
            Debug.Log($"Full charge reached -> Heavy Pending (held={held:F2})");
        }
    }

}
