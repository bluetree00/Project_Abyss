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

    /// <summary>업적 진척이 보는 카테고리. 값은 <b>누적이 아니라 절대값</b>이므로 Task는 SimpleSet을 쓴다.</summary>
    public const string RecordCategory = "Record";

    /// <summary>
    /// 영구 기록 하나를 업적에 흘린다. <b>런 종료 시 1회</b>만 호출한다.
    /// <para><b>왜 이 경로인가</b> — 업적은 「기록의 표시 형태」다(정본). 처치·방·상점을
    /// QuestEvents로 따로 세면 같은 사실을 두 곳이 기억하게 되고, 이어하기나 저장 시점에 따라 어긋난다.
    /// <see cref="UserGameData.records"/> 하나만 정본으로 두고 그 값을 그대로 실어 보낸다.</para>
    /// </summary>
    public static void ReportRecord(string key, int value) => Report(RecordCategory, key, value);
}
