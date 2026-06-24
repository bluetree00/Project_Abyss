/// <summary>
/// 런 구조(페이싱·난이도·마일스톤)의 읽기 계약. RunSequencer는 이 인터페이스에만 의존한다.
/// 구현: RunStructureConfig(SO — 오프라인 폴백) / RunStructureEntry(CSV 정본 — 서버 CDN RUN_STRUCTURE).
/// </summary>
public interface IRunStructure
{
    int    BossThreshold      { get; }
    string PreBossRoomKey     { get; }
    string BossRoomKey        { get; }
    int    ShopMaxPerChapter  { get; }
    float  ShopChance         { get; }
    int    EventMaxPerChapter { get; }
    float  EventChance        { get; }
    float  EliteChance        { get; }

    /// <summary>visitCount 시점의 목표 난이도(difficulty_scale)를 평가.</summary>
    float DifficultyAt(int visitCount);

    /// <summary>해당 진입 순번의 확정 마일스톤 종류(없으면 null=시드 가변 롤).
    /// Boss/PreBoss는 BossThreshold 게이팅이 담당하므로 마일스톤으로 지정해도 RunSequencer가 무시한다.</summary>
    RoomPlanKind? GetMilestoneKind(int visitIndex);
}
