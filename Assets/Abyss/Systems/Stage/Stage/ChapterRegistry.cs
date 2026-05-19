using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전체 챕터 데이터 목록.
/// Inspector에서 챕터별 SO를 등록하고, 런타임에 ChapterId로 조회한다.
/// </summary>
[CreateAssetMenu(fileName = "ChapterRegistry", menuName = "Stage/Chapter Registry")]
public class ChapterRegistry : ScriptableObject
{
    [Header("챕터 기본 데이터 (테마·난이도·사운드)")]
    [SerializeField] private List<ChapterDataSO>   chapters = new();

    [Header("챕터 지형 레이아웃 (12방 배치·배리어)")]
    [SerializeField] private List<ChapterLayoutSO> layouts  = new();

    private Dictionary<ChapterId, ChapterDataSO>   _dataLookup;
    private Dictionary<ChapterId, ChapterLayoutSO> _layoutLookup;

    // ── ChapterDataSO 조회 ─────────────────────
    public ChapterDataSO GetData(ChapterId id)
    {
        if (_dataLookup == null) BuildLookups();
        return _dataLookup.TryGetValue(id, out var v) ? v : null;
    }

    // ── ChapterLayoutSO 조회 ───────────────────
    public ChapterLayoutSO GetLayout(ChapterId id)
    {
        if (_layoutLookup == null) BuildLookups();
        return _layoutLookup.TryGetValue(id, out var v) ? v : null;
    }

    public IReadOnlyList<ChapterDataSO>   AllData    => chapters;
    public IReadOnlyList<ChapterLayoutSO> AllLayouts => layouts;

    private void BuildLookups()
    {
        _dataLookup   = new Dictionary<ChapterId, ChapterDataSO>();
        _layoutLookup = new Dictionary<ChapterId, ChapterLayoutSO>();

        foreach (var ch in chapters)
            if (ch != null && !_dataLookup.ContainsKey(ch.chapterId))
                _dataLookup.Add(ch.chapterId, ch);

        foreach (var lay in layouts)
            if (lay != null && !_layoutLookup.ContainsKey(lay.ChapterId))
                _layoutLookup.Add(lay.ChapterId, lay);
    }

    private void OnEnable() { _dataLookup = null; _layoutLookup = null; }
}
