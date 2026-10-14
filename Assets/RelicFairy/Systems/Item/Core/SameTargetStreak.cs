using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 「같은 적 연속」 셈 — 주 대상(처음 맞은 적)만 센다(10-14 룬 재설계 R1).
/// 광역 휘두름에 함께 맞은 다른 적은 셈을 끊지 않는다 — 예전엔 맞은 적이 바뀔 때마다 0으로 돌아가 무리 속에서 영영 차지 않았다.
/// 주 대상을 <see cref="Gap"/>초 넘게 안 때렸거나 주 대상이 쓰러지면 다음에 맞은 적이 새 주 대상이 된다.
/// </summary>
public struct SameTargetStreak
{
    public const float Gap = 2f;

    private GameObject _target;
    private float _lastAt;

    public int Count { get; private set; }

    /// <summary>적중 한 번. 셈이 올랐으면(주 대상) true, 곁가지 적이면 false.</summary>
    public bool Hit(GameObject target, float now)
    {
        if (target == null) return false;
        if (target != _target)
        {
            if (TargetAlive() && now - _lastAt <= Gap) return false;   // 주 대상을 아직 치는 중 — 함께 맞은 적
            _target = target;
            Count = 0;
        }
        else if (now - _lastAt > Gap) Count = 0;
        _lastAt = now;
        Count++;
        return true;
    }

    /// <summary>발동 뒤 · 방 입장 — 처음부터.</summary>
    public void Reset()
    {
        _target = null;
        _lastAt = -999f;
        Count = 0;
    }

    /// <summary>주 대상을 마지막으로 친 뒤 흐른 시간.</summary>
    public float SinceLast(float now) => now - _lastAt;

    private bool TargetAlive()
    {
        if (_target == null || !_target.activeInHierarchy) return false;
        return !(_target.TryGetComponent<MonsterBase>(out var mb) && mb.IsDead);
    }
}
