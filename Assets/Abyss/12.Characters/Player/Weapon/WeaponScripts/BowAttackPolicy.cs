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
public class BowAttackPolicy : IAttackInputPolicy
{
    private readonly float _enterThreshold;  
    private readonly float _fullThreshold;   
    private readonly int _maxChargeStage;

    private float _startTime;
    private bool _holding;
    private bool _chargingStarted;
    private bool _promoted;

    private int _currentStage;
    public int CurrentStage => _currentStage;

    public BowAttackPolicy(float enterThreshold = 0.5f, float fullThreshold = 2f, int maxChargeStage = 2)
    {
        _enterThreshold = Mathf.Max(0f, enterThreshold);
        _fullThreshold = Mathf.Max(0f, fullThreshold);
        _maxChargeStage = Mathf.Max(1, maxChargeStage);
    }

    public void OnStarted(PlayerController c)
    {
        if (c.isAttacking)
            return; // 공격 중이면 무시

        _holding = true;
        _startTime = Time.unscaledTime;
        _chargingStarted = false;
        _promoted = false;
        _currentStage = 1;
    }

    public void OnCanceled(PlayerController c)
    {
        if (!_holding) return;
        _holding = false;

        float held = Time.unscaledTime - _startTime;

        if (held >= _fullThreshold)
        {
            _currentStage = _maxChargeStage;
            c.SetPendingAttack(Command.Heavy);
            c.InputBuffer.Push(Command.Heavy);
            Debug.Log($"[BowPolicy] Released -> Heavy attack (held={held:F2})");
        }
        else
        {
            _currentStage = 1;
            c.SetPendingAttack(Command.Light);
            c.InputBuffer.Push(Command.Light);
            Debug.Log($"[BowPolicy] Released -> Light attack (held={held:F2})");
        }
    }

    public void Tick(PlayerController c, float dt)
    {
        if (!_holding || c.isAttacking) return;

        float held = Time.unscaledTime - _startTime;

        // 모으기 표시 (enterThreshold)
        if (!_chargingStarted && held >= _enterThreshold)
        {
            _chargingStarted = true;
            c.InputBuffer.Push(Command.Charge);
            Debug.Log($"[BowPolicy] Charging started (held={held:F2})");
        }

        // 강공격 Pending 설정 (fullThreshold)
        if (!_promoted && held >= _fullThreshold)
        {
            _promoted = true;
            _currentStage = _maxChargeStage;
            c.SetPendingAttack(Command.Heavy);
            Debug.Log($"[BowPolicy] Full charge reached -> Heavy Pending (held={held:F2})");
        }
    }
}
