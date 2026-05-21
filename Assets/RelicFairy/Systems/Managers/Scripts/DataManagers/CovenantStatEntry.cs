using UnityEngine;

/// <summary>
/// COVENANT_STAT_DATA 차트 1행 = 서약 1개 × 슬롯 1개의 스테이지별 수치.
/// slot 번호는 각 CovenantBase 구현체의 private const int V_XXX 인덱스와 1:1 대응한다.
/// </summary>
[System.Serializable]
public sealed class CovenantStatEntry
{
    public int    index;
    public string covenant_id;
    public int    slot;
    public string description;
    public float  basic;
    public float  enhanced;
    public float  evolved;
    public int    stat_version;

    public float Get(CovenantStage stage) => stage switch
    {
        CovenantStage.Enhanced => enhanced,
        CovenantStage.Evolved  => evolved,
        _                      => basic,
    };

    public int GetInt(CovenantStage stage) => Mathf.RoundToInt(Get(stage));
}

[System.Serializable]
internal sealed class CovenantStatEntryCollection
{
    public System.Collections.Generic.List<CovenantStatEntry> entries;
}
