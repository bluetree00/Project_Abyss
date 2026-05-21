using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 런 중 보유 중인 서약 목록을 HUD에 표시하는 패널.
/// CovenantHandler.OnCovenantListChanged 이벤트를 받아 Refresh한다.
/// </summary>
public sealed class CovenantPanelView : MonoBehaviour
{
    [SerializeField] private UI_CovenantSlot[] _slots;

    private void Awake()
    {
        if (_slots == null || _slots.Length == 0)
            _slots = GetComponentsInChildren<UI_CovenantSlot>(true);
    }

    public void Refresh(IReadOnlyList<CovenantBase> covenants)
    {
        if (_slots == null) return;

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] == null) continue;

            if (i < covenants.Count)
                _slots[i].Bind(covenants[i]);
            else
                _slots[i].SetEmpty();
        }
    }

    public void Clear()
    {
        if (_slots == null) return;
        foreach (var slot in _slots)
            slot?.SetEmpty();
    }
}
