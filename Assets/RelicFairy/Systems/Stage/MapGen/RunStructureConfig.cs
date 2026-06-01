using UnityEngine;

/// <summary>
/// 절차적 런의 구조 골격. RunSequencer가 매 클리어마다 출구 카테고리를 롤할 때 사용한다.
/// 고정 슬롯맵(chapter_map)을 대체하는 규칙 기반 설정.
/// 정적 데이터만 — 런타임 상태(visitCount 등)는 RunSequencer가 보유.
/// </summary>
[CreateAssetMenu(fileName = "NewRunStructureConfig", menuName = "Stage/Run Structure Config")]
public class RunStructureConfig : ScriptableObject
{
    [Header("보스 진입")]
    [Tooltip("이 방문 수에 도달하면 보스 어프로치(보스 전방→보스) 진입")]
    [SerializeField] private int _bossThreshold = 12;
    [Tooltip("단일 고정 보스 전방 방 pool_key")]
    [SerializeField] private string _preBossRoomKey;
    [Tooltip("보스방 pool_key")]
    [SerializeField] private string _bossRoomKey;

    [Header("상점")]
    [SerializeField] private int _shopMaxPerChapter = 2;
    [SerializeField, Range(0f, 1f)] private float _shopChance = 0.25f;

    [Header("이벤트")]
    [SerializeField] private int _eventMaxPerChapter = 2;
    [SerializeField, Range(0f, 1f)] private float _eventChance = 0.2f;

    [Header("정예")]
    [SerializeField, Range(0f, 1f)] private float _eliteChance = 0.15f;

    [Header("난이도 곡선 (visitCount → 목표 difficulty_scale)")]
    [SerializeField] private AnimationCurve _difficultyByVisit = AnimationCurve.Linear(0f, 0.4f, 12f, 1.8f);

    public int    BossThreshold      => _bossThreshold;
    public string PreBossRoomKey     => _preBossRoomKey;
    public string BossRoomKey        => _bossRoomKey;
    public int    ShopMaxPerChapter  => _shopMaxPerChapter;
    public float  ShopChance         => _shopChance;
    public int    EventMaxPerChapter => _eventMaxPerChapter;
    public float  EventChance        => _eventChance;
    public float  EliteChance        => _eliteChance;

    /// <summary>visitCount 시점의 목표 난이도(difficulty_scale)를 곡선에서 평가.</summary>
    public float DifficultyAt(int visitCount) => _difficultyByVisit.Evaluate(visitCount);
}
