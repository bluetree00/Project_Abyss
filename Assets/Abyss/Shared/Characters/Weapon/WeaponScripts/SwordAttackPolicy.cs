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
    private readonly float _enterThreshold;  // 모으기 시작을 인식할 최소 시간
    private readonly float _fullThreshold;   // 강공격 확정 시간
    private readonly int _maxChargeStage;

    private float _startTime;
    private bool _holding;
    private bool _wasAttacking;  // 공격 중에 눌린 경우 Tick에서 타이머 리셋용

    private bool _chargingStarted;
    private bool _promoted;

    private int _currentStage;
    public int CurrentStage => _currentStage;

    public SwordAttackPolicy(float enterThreshold = 2f, float fullThreshold = 3f, int maxChargeStage = 2)
    {
        _enterThreshold = Mathf.Max(0.0f, enterThreshold);
        _fullThreshold  = Mathf.Max(_enterThreshold, fullThreshold);
        _maxChargeStage = Mathf.Max(1, maxChargeStage);
    }

    public void OnStarted(PlayerController c)
    {
        // IsAttacking 중에도 _holding = true — OnCanceled에서 Light 버퍼링 가능하게 함
        // Charge 시작은 Tick에서 IsAttacking = false일 때만 진행
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

        if (_promoted)
        {
            Debug.Log($"[SwordPolicy] Released after promoted -> nothing to do (stage={_currentStage})");
            return;
        }

        float held = Time.unscaledTime - _startTime;

        if (held >= _fullThreshold)
        {
            _currentStage = _maxChargeStage;
            c.SetPendingAttack(Command.Heavy);
            c.InputBuffer.Push(Command.Heavy);
            Debug.Log($"[SwordPolicy] Released -> set pending Heavy (held={held:F2})");
        }
        else
        {
            _currentStage = 1;
            c.SetPendingAttack(Command.Light);
            c.InputBuffer.Push(Command.Light);
            Debug.Log($"[SwordPolicy] Released -> set pending Light (held={held:F2})");
        }
    }

    public void Tick(PlayerController c, float dt)
    {
        bool isAttacking = c.Combo.IsAttacking;

        // 공격이 막 끝난 경우 → 타이머 리셋 (공격 중 홀드 시간이 차지로 오인되지 않도록)
        if (_wasAttacking && !isAttacking && _holding)
        {
            _startTime       = Time.unscaledTime;
            _chargingStarted = false;
            _promoted        = false;
            Debug.Log("[SwordPolicy] Attack ended mid-hold -> reset charge timer");
        }
        _wasAttacking = isAttacking;

        if (!_holding || isAttacking) return;

        float held = Time.unscaledTime - _startTime;

        if (!_chargingStarted && held >= _enterThreshold)
        {
            _chargingStarted = true;
            c.InputBuffer.Push(Command.Charge);
            Debug.Log($"Charging started (held={held:F2})");
        }

        if (!_promoted && held >= _fullThreshold)
        {
            _promoted = true;
            c.SetPendingAttack(Command.Heavy);
            c.InputBuffer.Push(Command.Heavy);
            Debug.Log($"Full charge reached -> Heavy Pending (held={held:F2})");
        }
    }
}
