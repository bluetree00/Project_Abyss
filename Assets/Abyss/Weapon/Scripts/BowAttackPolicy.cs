using UnityEngine;
using Game.Inputs;

/// <summary>
/// BowAttackPolicy - enterThreshold(모으기 진입 지연) + fullThreshold(강공격 확정)
/// </summary>
public class BowAttackPolicy : IAttackInputPolicy
{
    private readonly float _enterThreshold;
    private readonly float _fullThreshold;
    private readonly int _maxChargeStage;

    private float _startTime;
    private bool _holding;
    private bool _wasAttacking;  // 공격 중 홀드 시 Tick 타이머 리셋용

    private bool _chargingStarted;
    private bool _promoted;

    private int _currentStage;
    public int CurrentStage => _currentStage;

    public BowAttackPolicy(float enterThreshold = 0.5f, float fullThreshold = 2f, int maxChargeStage = 2)
    {
        _enterThreshold = Mathf.Max(0f, enterThreshold);
        _fullThreshold  = Mathf.Max(0f, fullThreshold);
        _maxChargeStage = Mathf.Max(1, maxChargeStage);
    }

    public void OnStarted(PlayerController c)
    {
        // IsAttacking 중에도 _holding = true — OnCanceled에서 Light 버퍼링 가능하게 함
        _holding        = true;
        _startTime      = Time.unscaledTime;
        _chargingStarted = false;
        _promoted       = false;
        _currentStage   = 1;
        _wasAttacking   = c.Combo.IsAttacking;
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
        bool isAttacking = c.Combo.IsAttacking;

        // 공격이 막 끝난 경우 → 타이머 리셋
        if (_wasAttacking && !isAttacking && _holding)
        {
            _startTime       = Time.unscaledTime;
            _chargingStarted = false;
            _promoted        = false;
            Debug.Log("[BowPolicy] Attack ended mid-hold -> reset charge timer");
        }
        _wasAttacking = isAttacking;

        if (!_holding || isAttacking) return;

        float held = Time.unscaledTime - _startTime;

        if (!_chargingStarted && held >= _enterThreshold)
        {
            _chargingStarted = true;
            c.InputBuffer.Push(Command.Charge);
            Debug.Log($"[BowPolicy] Charging started (held={held:F2})");
        }

        if (!_promoted && held >= _fullThreshold)
        {
            _promoted = true;
            _currentStage = _maxChargeStage;
            c.SetPendingAttack(Command.Heavy);
            Debug.Log($"[BowPolicy] Full charge reached -> Heavy Pending (held={held:F2})");
        }
    }
}
