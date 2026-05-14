using System;

public enum ChapterId { Chapter1, Chapter2, Chapter3, Chapter4, Chapter5 }
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
