using System;

/// <summary>
/// 게임 시스템이 Quest 의존 없이 이벤트를 발행하는 정적 버스.
/// QuestManager가 구독해 ReceiveReport로 자동 라우팅한다.
/// </summary>
public static class QuestEvents
{
    public static event Action<string> OnMonsterKilled;
    public static event Action<string> OnRoomCleared;
    public static event Action<string> OnItemCollected;
    public static event Action<int>    OnGoldGained;

    public static void ReportKill(string codeName)        => OnMonsterKilled?.Invoke(codeName);
    public static void ReportRoomClear(string category)   => OnRoomCleared?.Invoke(category);
    public static void ReportItemCollect(string itemId)   => OnItemCollected?.Invoke(itemId);
    public static void ReportGold(int amount)             => OnGoldGained?.Invoke(amount);
}
