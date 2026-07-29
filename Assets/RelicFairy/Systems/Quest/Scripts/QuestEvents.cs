using System;

/// <summary>
/// 게임 시스템이 Quest 의존 없이 진행도를 보고하는 정적 버스.
/// 단일 범용 채널(Report)만 QuestManager가 구독한다 — 새 카테고리는 코드 추가 없이
/// Quest 에셋 저작 + Report(category, ...) 한 줄 호출만으로 확장된다.
/// </summary>
public static class QuestEvents
{
    /// <summary>범용 보고 채널. (category, target, amount) — QuestManager가 단일 구독.</summary>
    public static event Action<string, object, int> OnReported;

    /// <summary>
    /// 몬스터 처치 전용 타입 이벤트. 퀘스트 외부 소비자 전용:
    /// RuneEffectDispatcher(룬 OnKill 효과), AbyssEssenceTracker(각성 정수). 제거 금지.
    /// </summary>
    public static event Action<string> OnMonsterKilled;

    /// <summary>어디서든 호출하는 범용 보고. category와 target은 Quest 에셋의 Category/Target과 매칭된다.</summary>
    public static void Report(string category, object target, int amount = 1)
        => OnReported?.Invoke(category, target, amount);

    // ── 자주 쓰는 보고는 얇은 헬퍼로 가독성 유지 (내부적으로 Report 위임) ──
    public static void ReportKill(string codeName)
    {
        OnMonsterKilled?.Invoke(codeName);   // 룬/각성 등 퀘스트 외 소비자
        Report("Kill", codeName, 1);
    }

    public static void ReportRoomClear(string category)  => Report("Room", category, 1);
    public static void ReportItemCollect(string itemId)  => Report("Item", itemId, 1);
    public static void ReportGold(int amount)            => Report("Gold", "*", amount);
}
