using UnityEngine;

/// <summary>
/// 스태미너 게이지 — 대시(회피)의 자원 게이트.
/// 지연 없이 <b>항상 초당 일정량 회복</b>한다(멈추지 않음).
/// 연속 대시를 막는 건 회복 지연이 아니라 '대시 후 짧은 텀'(CharacterData.dodgeCooldown)이 담당한다.
/// PlayerController가 소유하고 매 프레임 Tick한다 — ComboController/PoiseController와 동일 패턴.
/// </summary>
public class StaminaController
{
    private float _current;
    private float _regenDelayRemaining;   // 대시 직후 잠깐은 회복이 멈춘다
    private bool  _initialized;

    /// <summary>현재 스태미너.</summary>
    public float Current => _current;

    // 초기화 전(_initialized=false)에는 '가득 찬 것'으로 취급한다.
    // 그렇지 않으면 Tick이 처음 돌기 전 프레임에 View가 _current=0을 읽어 '텅 빈 바'가 잠깐 뜬다.
    public float Normalized(float max) => (!_initialized || max <= 0f) ? 1f : Mathf.Clamp01(_current / max);

    /// <summary>가득 찼는지 — 가득 차면 UI를 숨긴다(젤다/원신/명조 공통 문법).</summary>
    public bool IsFull(float max) => !_initialized || _current >= max - 0.01f;

    public void ResetFull(float max)
    {
        _current             = Mathf.Max(0f, max);
        _regenDelayRemaining = 0f;
        _initialized         = true;
    }

    /// <summary>소모 가능한지(행동 게이트). 실제 소모는 하지 않는다.</summary>
    public bool CanConsume(float amount, float max)
    {
        if (!_initialized) ResetFull(max);
        return _current >= amount;
    }

    /// <summary>소모 시도. 부족하면 false(=대시 불가). 성공하면 짧은 회복 지연을 건다.</summary>
    public bool TryConsume(float amount, float max, float regenDelay)
    {
        if (!_initialized) ResetFull(max);
        if (_current < amount) return false;

        _current             = Mathf.Max(0f, _current - amount);
        _regenDelayRemaining = Mathf.Max(0f, regenDelay);   // 대시 직후엔 잠깐 안 찬다
        return true;
    }

    /// <summary>매 프레임 호출 — 대시 직후 짧은 지연이 끝나면 계속 회복.</summary>
    public void Tick(float dt, float max, float regenPerSec)
    {
        if (!_initialized)
        {
            ResetFull(max);
            return;
        }

        if (_regenDelayRemaining > 0f)
        {
            _regenDelayRemaining = Mathf.Max(0f, _regenDelayRemaining - dt);
            return;
        }

        if (_current < max)
            _current = Mathf.Min(max, _current + Mathf.Max(0f, regenPerSec) * dt);
    }
}
