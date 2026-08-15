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
    // 이번 '누름' 한 번에 차지는 <b>딱 한 번</b>만 — 강공격이 나간 뒤 버튼을 계속 쥐고 있어도
    // 다시 모으지 않는다. (아래 블로킹 해제 리셋이 매 공격 종료마다 타이머를 되살려
    // 차지→강공격→차지→강공격 무한 반복이 되던 문제)
    private bool  _chargeConsumed;

    public SwordAttackPolicy(float enterThreshold = 2f, float fullThreshold = 3f, int maxChargeStage = 2)
    {
        _enterThreshold = Mathf.Max(0.0f, enterThreshold);
    }

    public void OnStarted(PlayerController c)
    {
        _holding         = true;
        _startTime       = Time.unscaledTime;
        _chargingStarted = false;
        _chargeConsumed  = false;   // 새로 누름 — 차지 1회 재허용
        _wasBlocked      = c.IsChargeBlocked;
    }

    public void OnCanceled(PlayerController c)
    {
        if (!_holding) return;
        _holding = false;

        // 이번 누름은 이미 차지로 소비됨 → 손을 뗄 때 약공격이 덤으로 나가지 않게 한다.
        if (_chargeConsumed) return;

        // ChargeState 내부 타이머가 이미 Heavy로 전환했으면 아무것도 안 함
        if (c.IsInHeavyAttackState) return;

        // 릴리즈 → 차지 취소 or Light 공격
        c.SetPendingAttack(Command.Light);
        c.InputBuffer.Push(Command.Light);
    }

    public void Tick(PlayerController c, float dt)
    {
        bool isBlocked = c.IsChargeBlocked;

        // 블로킹 상태(공격/공중/회피)가 끝난 경우 → 타이머 리셋
        // holdThreshold를 ChargeState 진입 시점 기준으로 맞추기 위함.
        // 단, 이미 이번 누름으로 차지를 한 번 썼다면 되살리지 않는다(무한 반복 방지).
        if (_wasBlocked && !isBlocked && _holding && !_chargeConsumed)
        {
            _startTime       = Time.unscaledTime;
            _chargingStarted = false;
        }
        _wasBlocked = isBlocked;

        if (!_holding || isBlocked || _chargeConsumed) return;

        float held = Time.unscaledTime - _startTime;

        // enterThreshold 도달 → ChargeState 진입 커맨드 (one-shot)
        if (!_chargingStarted && held >= _enterThreshold)
        {
            _chargingStarted = true;
            _chargeConsumed  = true;   // 이번 누름의 차지 소진 — 손을 뗄 때까지 재진입 금지
            c.InputBuffer.Push(Command.Charge);
        }
    }
}
