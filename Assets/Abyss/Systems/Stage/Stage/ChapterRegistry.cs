using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전체 챕터 데이터 목록.
/// Inspector에서 챕터별 SO를 등록하고, 런타임에 ChapterId로 조회한다.
/// </summary>
[CreateAssetMenu(fileName = "ChapterRegistry", menuName = "Stage/Chapter Registry")]
public class ChapterRegistry : ScriptableObject
{
    [SerializeField] private List<ChapterDataSO> chapters = new();

    private Dictionary<ChapterId, ChapterDataSO> _lookup;

    public ChapterDataSO Get(ChapterId id)
    {
        if (_lookup == null) BuildLookup();
        return _lookup.TryGetValue(id, out var data) ? data : null;
    }

    public IReadOnlyList<ChapterDataSO> All => chapters;

    private void BuildLookup()
    {
        _lookup = new Dictionary<ChapterId, ChapterDataSO>();
        foreach (var ch in chapters)
        {
            if (ch != null && !_lookup.ContainsKey(ch.chapterId))
                _lookup.Add(ch.chapterId, ch);
        }
    }

    private void OnEnable() => _lookup = null;
}
