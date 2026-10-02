using System;
using UnityEngine;

/// <summary>
/// 랜슬롯 「원한」 — 배신의 낙인이 상한을 넘쳐 새겨진 몫이 플레이어 쪽에 저축된다(유물 성장 v2 §3-1).
/// 다음 심판 막타가 원한 1개당 참격파 1줄로 쓴다(명조 「전자 효과 → 자기폭발 → 여우의 별자리가 거둔다」 결).
/// 런 상태라 플레이어와 함께 산다 — 플레이어 컴포넌트(랜슬롯 첫 조각과 함께 붙는다).
/// </summary>
[DisallowMultipleComponent]
public sealed class GrudgeStore : MonoBehaviour
{
    public const int DefaultCap = 5;

    private int _value;
    private int _cap = DefaultCap;
    private int _capBonus;   // 타락(어둠 반응) 동안 +3

    public event Action Changed;

    public int Value => _value;
    public int Cap   => _cap + _capBonus;
    public bool IsFull => _value >= Cap;

    /// <summary>플레이어의 저장소(없으면 붙인다).</summary>
    public static GrudgeStore Of(Component player, bool create)
    {
        if (player == null) return null;
        if (player.TryGetComponent<GrudgeStore>(out var g)) return g;
        return create ? player.gameObject.AddComponent<GrudgeStore>() : null;
    }

    /// <summary>원한을 더한다. 실제로 더한 수(상한에서 잘린 몫은 버린다).</summary>
    public int Add(int n)
    {
        if (n <= 0) return 0;
        int before = _value;
        _value = Mathf.Min(Cap, _value + n);
        if (_value != before) Changed?.Invoke();
        return _value - before;
    }

    /// <summary>원한을 모두 쓴다. 쓴 수를 돌려준다.</summary>
    public int TakeAll()
    {
        int v = _value;
        if (v <= 0) return 0;
        _value = 0;
        Changed?.Invoke();
        return v;
    }

    /// <summary>원한을 <paramref name="n"/>개 쓴다(광기의 왕관 ③ 등). 모자라면 false — 아무것도 쓰지 않는다.</summary>
    public bool TrySpend(int n)
    {
        if (n <= 0) return true;
        if (_value < n) return false;
        _value -= n;
        Changed?.Invoke();
        return true;
    }

    /// <summary>기본 상한(원한의 칼날 8).</summary>
    public void SetBaseCap(int cap)
    {
        _cap = Mathf.Max(1, cap);
        _value = Mathf.Min(_value, Cap);
        Changed?.Invoke();
    }

    /// <summary>덧붙는 상한(타락 +3) — 끝나면 0.</summary>
    public void SetCapBonus(int bonus)
    {
        _capBonus = Mathf.Max(0, bonus);
        _value = Mathf.Min(_value, Cap);
        Changed?.Invoke();
    }
}
