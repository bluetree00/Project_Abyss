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

    /// <summary>
    /// 이 방에서 활성화될 수 있는 스포너의 총 최대 개수 (확정 + 후보 합산).
    /// 우선순위 — 확정(M계열)이 먼저 소비되고, 남은 슬롯이 후보(m계열)에서 랜덤 채워짐.
    ///   · 0 이하      : 모든 M/m 타일 활성 (제한 없음)
    ///   · 확정 ≥ max  : 확정 전부 활성, 후보는 모두 Floor 치환
    ///   · 확정 &lt; max : 확정 유지 + 후보 중 (max - 확정수)개만 랜덤 선택
    ///   · 확정 0 + 후보 0 + max > 0 : 스포너 사용 안 함 (예외 안전)
    /// </summary>
    public int    max_active_spawners;
}

[System.Serializable]
public class MapRoomEntryCollection
{
    public List<MapRoomEntry> rooms;
}
