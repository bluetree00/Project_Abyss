using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전체 서약 데이터 SO 목록을 관리하는 테이블.
/// CovenantContext를 통해 각 서약에 주입되며,
/// covenantId로 해당 SO를 빠르게 조회한다.
/// </summary>
[CreateAssetMenu(fileName = "CovenantDataTable", menuName = "RelicFairy/Covenant/Covenant Data Table")]
public sealed class CovenantDataTableSO : ScriptableObject
{
    [SerializeField] private CovenantDataSO[] _entries = Array.Empty<CovenantDataSO>();

    private Dictionary<string, CovenantDataSO> _map;

    public CovenantDataSO Get(string covenantId)
    {
        if (_map == null) BuildMap();
        _map.TryGetValue(covenantId, out var data);
        return data;
    }

    private void BuildMap()
    {
        _map = new Dictionary<string, CovenantDataSO>(_entries.Length);
        foreach (var entry in _entries)
        {
            if (entry != null && !string.IsNullOrEmpty(entry.covenantId))
                _map[entry.covenantId] = entry;
        }
    }

    private void OnValidate() => _map = null;
}
