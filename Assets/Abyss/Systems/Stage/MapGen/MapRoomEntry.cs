using System.Collections.Generic;

/// <summary>
/// 뒤끝 차트 "MapData" 시트의 1행 = 방 1개.
/// grid_csv가 비어있으면 자동 생성(rule) 모드.
/// </summary>
[System.Serializable]
public class MapRoomEntry
{
    public string room_id;
    public string category;        // Battle, Elite, Boss, Event, Shop, Start
    public string theme;           // Forest, Cave, Castle, Town ...
    public string palette;         // BlockPalette SO 이름 (Addressables 키)
    public int    width;           // grid_csv가 있으면 무시 (csv에서 자동 계산)
    public int    height;
    public string grid_csv;        // "W,W,W;W,F,W;W,W,W" — 비어있으면 rule 모드
    public string layout_rule;     // rule 모드일 때 LayoutRule ID
    public string entrance;        // Scatter, Fade, Instant
    public float  scatter_range;   // 흩뿌리기 범위
    public float  return_duration; // 복귀 애니메이션 시간
    public int    stat_version;    // 버전 관리
}

[System.Serializable]
public class MapRoomEntryCollection
{
    public List<MapRoomEntry> rooms;
}
