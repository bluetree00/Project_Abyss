using UnityEngine;

/// <summary>
/// 포이즈(아머치) 게이지 — 넉백/날아감의 게이트.
/// 피격마다 임팩트만큼 깎이고, 0 이하가 되면 브레이크(=날아감 발동) 후 최대치로 초기화된다.
/// 회복이 붙어 있어 "연타로 맞으면 브레이크 / 띄엄띄엄 맞으면 회복돼 안 날아감"이 자동 성립한다
/// (별도의 'N초 안에 M대' 카운터가 필요 없다).
/// PlayerController가 소유하고 매 프레임 Tick한다 — ComboController와 동일 패턴.
/// </summary>
public class PoiseController
{
    private float _current;
    private float _regenDelayRemaining;
    private float _immunityRemaining;
    private bool  _initialized;

    /// <summary>현재 포이즈.</summary>
    public float Current => _current;

    /// <summary>날아감 직후 넉백 면역 중 — 포이즈가 깎이지 않고 재발동하지 않는다(저글링 방지).</summary>
    public bool IsImmune => _immunityRemaining > 0f;

    /// <summary>최대치로 채운다. 런 시작/부활/브레이크 직후.</summary>
    public void ResetFull(float maxPoise)
    {
        _current = Mathf.Max(1f, maxPoise);
        _regenDelayRemaining = 0f;
        _initialized = true;
    }

    /// <summary>매 프레임 호출 — 면역/회복지연 타이머 진행 후 포이즈 회복.</summary>
    public void Tick(float dt, float maxPoise, float regenDelay, float regenPerSec)
    {
        if (!_initialized) ResetFull(maxPoise);

        if (_immunityRemaining > 0f)
            _immunityRemaining = Mathf.Max(0f, _immunityRemaining - dt);

        // 마지막 피격 후 지연이 끝나야 회복 시작
        if (_regenDelayRemaining > 0f)
        {
            _regenDelayRemaining = Mathf.Max(0f, _regenDelayRemaining - dt);
            return;
        }

        if (_current < maxPoise)
            _current = Mathf.Min(maxPoise, _current + Mathf.Max(0f, regenPerSec) * dt);
    }

    /// <summary>
    /// 임팩트 적용. 포이즈가 0 이하로 떨어지면 브레이크 처리(초기화 + 넉백 면역)하고 true를 반환한다.
    /// 넉백 면역 중이면 포이즈가 깎이지 않고 false.
    /// </summary>
    public bool TakeImpact(float impact, float maxPoise, float regenDelay, float immunityDuration)
    {
        if (!_initialized) ResetFull(maxPoise);
        if (IsImmune || impact <= 0f) return false;

        _current -= impact;
        _regenDelayRemaining = Mathf.Max(0f, regenDelay);

        if (_current > 0f) return false;

        Break(maxPoise, immunityDuration);
        return true;
    }

    /// <summary>포이즈를 무시하고 즉시 브레이크(보스 대기술 등 확정 날아감). 면역 중이면 무시된다.</summary>
    public bool ForceBreak(float maxPoise, float immunityDuration)
    {
        if (!_initialized) ResetFull(maxPoise);
        if (IsImmune) return false;

        Break(maxPoise, immunityDuration);
        return true;
    }

    // 브레이크 — 포이즈를 최대치로 초기화하고 넉백 면역을 건다(즉시 재브레이크/무한 저글링 방지).
    private void Break(float maxPoise, float immunityDuration)
    {
        _current = Mathf.Max(1f, maxPoise);
        _regenDelayRemaining = 0f;
        _immunityRemaining = Mathf.Max(0f, immunityDuration);
        _initialized = true;
    }
}
