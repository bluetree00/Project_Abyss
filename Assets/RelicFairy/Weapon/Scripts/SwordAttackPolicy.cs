using UnityEngine;
using Game.Inputs;

/// <summary>
/// SwordAttackPolicy - enterThreshold(모으기 진입 지연)
/// - 블로킹 상태(공격·공중·회피)가 끝나면 타이머를 리셋해 holdThreshold 카운트를 ChargeState 진입 기준으로 맞춤
/// - enterThreshold 미만 릴리즈: Light 푸시
/// - enterThreshold 이상: Command.Charge 한 번 푸시 → ActAttackChargeState 진입
/// - holdThreshold(강공격 확정)는 ActAttackChargeState 내부 타이머가 담당
/// </summary>
public class SwordAttackPolicy : IAttackInputPolicy
{
    private readonly float _enterThreshold;

    private float _startTime;
    private bool  _holding;
    private bool  _wasBlocked;    // 이전 프레임 IsChargeBlocked (공격·공중·회피 통합)
    private bool  _chargingStarted;

    public SwordAttackPolicy(float enterThreshold = 2f, float fullThreshold = 3f, int maxChargeStage = 2)
    {
        _enterThreshold = Mathf.Max(0.0f, enterThreshold);
    }

    public void OnStarted(PlayerController c)
    {
        _holding         = true;
        _startTime       = Time.unscaledTime;
        _chargingStarted = false;
        _wasBlocked      = c.IsChargeBlocked;
    }

    public void OnCanceled(PlayerController c)
    {
        if (!_holding) return;
        _holding = false;

        // ChargeState 내부 타이머가 이미 Heavy로 전환했으면 아무것도 안 함
        if (c.IsInHeavyAttackState) return;

        // 릴리즈 → 차지 취소 or Light 공격
        c.SetPendingAttack(Command.Light);
        c.InputBuffer.Push(Command.Light);
        Debug.Log($"[SwordPolicy] Released -> Light");
    }

    public void Tick(PlayerController c, float dt)
    {
        bool isBlocked = c.IsChargeBlocked;

        // 블로킹 상태(공격/공중/회피)가 끝난 경우 → 타이머 리셋
        // holdThreshold를 ChargeState 진입 시점 기준으로 맞추기 위함
        if (_wasBlocked && !isBlocked && _holding)
        {
            _startTime       = Time.unscaledTime;
            _chargingStarted = false;
            Debug.Log("[SwordPolicy] Blocking ended mid-hold -> reset charge timer");
        }
        _wasBlocked = isBlocked;

        if (!_holding || isBlocked) return;

        float held = Time.unscaledTime - _startTime;

        // enterThreshold 도달 → ChargeState 진입 커맨드 (one-shot)
        if (!_chargingStarted && held >= _enterThreshold)
        {
            _chargingStarted = true;
            c.InputBuffer.Push(Command.Charge);
            Debug.Log($"[SwordPolicy] Charging started (held={held:F2})");
        }
    }
}
