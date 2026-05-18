using System;

/// <summary>
/// 챕터 간 구분을 위한 ID. Chapter1~4는 게임 챕터에 대응.
/// </summary>
public enum ChapterId { Chapter1, Chapter2, Chapter3, Chapter4 }

/// <summary>
/// 챕터 세계를 덮는 배리어 종류.
/// 방 활성화 시 플랫폼이 배리어 아래서 수면 위로 솟아오른다.
/// </summary>
public enum BarrierType
{
    Water,  // Chapter1 — 잊혀진 숲 강줄기
    Lava,   // Chapter2 — 불길의 협곡
    Fog,    // Chapter3 — 안개의 심연
    Void,   // Chapter4 — 종말의 허공
}
public enum StageCategory { Start, Normal, Boss }

public enum NormalRoomCategory
{
    Random,
    Battle,
    Elite,
    Event,
    Shop
}

public enum RoomCategory
{
    Battle,   // ✅ Combat 대신 Battle
    Elite,
    Event,
    Shop,
    Start,
    Boss,
    Rest,     // 챕터 2+ 시작 노드 — 쉬어가는 방
    Unknown
}

public enum StagePointState
{
    Locked,
    Available,
    Visited,
    Cleared
}

public static class RoomCategoryUtil
{
    public static RoomCategory Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return RoomCategory.Unknown;

        return raw.Trim().ToLowerInvariant() switch
        {
            // ✅ JSON이 "Combat"이어도 Battle로 흡수
            "combat" => RoomCategory.Battle,
            "battle" => RoomCategory.Battle,

            "elite"  => RoomCategory.Elite,
            "event"  => RoomCategory.Event,
            "shop"   => RoomCategory.Shop,
            "start"  => RoomCategory.Start,
            "boss"   => RoomCategory.Boss,
            "rest"   => RoomCategory.Rest,
            _ => RoomCategory.Unknown
        };
    }
}
