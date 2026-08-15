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
    /// <summary>이번 '누름'에서 강공격을 이미 냈는지. 손을 뗄 때까지 다시 나지 않는다
    /// (근접 <see cref="SwordAttackPolicy"/>의 _chargeConsumed와 같은 규칙).</summary>
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

        // 만충으로 Tick이 이미 강공격을 냈으면 릴리즈로 한 발 더 내지 않는다(이중 발사).
        if (_promoted) return;

        float held = Time.unscaledTime - _startTime;

        if (held >= _fullThreshold)
        {
            _currentStage = _maxChargeStage;
            c.SetPendingAttack(Command.Heavy);
            c.InputBuffer.Push(Command.Heavy);
        }
        else
        {
            _currentStage = 1;
            c.SetPendingAttack(Command.Light);
            c.InputBuffer.Push(Command.Light);
        }
    }

    public void Tick(PlayerController c, float dt)
    {
        bool isAttacking = c.Combo.IsAttacking;

        // 공격이 막 끝난 경우 → 타이머 리셋(계속 쥐고 있으면 약공격은 이어 쏜다).
        //
        // _promoted는 <b>되살리지 않는다</b>. 예전엔 여기서 함께 false로 돌려놓아
        // 강공격 → 공격 종료 → 타이머 부활 → 만충 → 또 강공격이 무한 반복됐다.
        // 근접은 SwordAttackPolicy._chargeConsumed로 이미 막았는데, 활은 별도 클래스라 그 수정이 오지 않았다.
        if (_wasAttacking && !isAttacking && _holding && !_promoted)
        {
            _startTime       = Time.unscaledTime;
            _chargingStarted = false;
        }
        _wasAttacking = isAttacking;

        if (!_holding || isAttacking) return;

        float held = Time.unscaledTime - _startTime;

        if (!_chargingStarted && held >= _enterThreshold)
        {
            _chargingStarted = true;
            c.InputBuffer.Push(Command.Charge);
        }

        if (!_promoted && held >= _fullThreshold)
        {
            _promoted = true;
            _currentStage = _maxChargeStage;
            c.SetPendingAttack(Command.Heavy);
        }
    }
}
