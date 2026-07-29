using System;
using UnityEngine;

/// <summary>
/// 디자인된 런 구조의 확정 마일스톤 1개. "이 방 진입 순번(visitIndex)에선 이 종류를 강제".
/// 시드 가변 롤을 덮어써 이벤트/상점/정예 등을 확정 배치하는 데 쓴다.
/// visitIndex: 플레이어가 진입하는 방의 visitCount 값(첫 방=0). 그 방의 출구 롤 시점에 적용.
/// </summary>
[Serializable]
public struct RunMilestone
{
    [Tooltip("강제가 적용될 방 진입 순번(visitCount). 첫 방=0.")]
    public int          visitIndex;
    [Tooltip("해당 순번에서 강제할 방 종류. 템플릿이 풀에 없으면 Normal로 폴백.")]
    public RoomPlanKind kind;
}

/// <summary>
/// 절차적 런의 구조 골격. RunSequencer가 매 클리어마다 출구 카테고리를 롤할 때 사용한다.
/// 고정 슬롯맵(chapter_map)을 대체하는 규칙 기반 설정.
/// 정적 데이터만 — 런타임 상태(visitCount 등)는 RunSequencer가 보유.
///
/// 두 층위로 구조를 표현한다:
///  ① 확률형 — ShopChance/EventChance/EliteChance 등으로 시드 가변 롤.
///  ② 확정 마일스톤(_milestones) — 특정 visitIndex에서 종류를 강제(디자인된 스파인). 확률형을 덮어쓴다.
/// 보스는 _bossThreshold(도달 시 보스 어프로치)로 확정되는 별도 마일스톤.
/// </summary>
[CreateAssetMenu(fileName = "NewRunStructureConfig", menuName = "Stage/Run Structure Config")]
public class RunStructureConfig : ScriptableObject, IRunStructure
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

    [Header("확정 마일스톤 (디자인된 스파인 — 확률형을 덮어씀)")]
    [Tooltip("특정 visitIndex에서 방 종류를 강제. 비우면 전부 시드 가변. Boss/PreBoss는 _bossThreshold가 담당.")]
    [SerializeField] private RunMilestone[] _milestones;

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

    /// <summary>해당 진입 순번에 확정 마일스톤이 있으면 그 종류를 반환, 없으면 null(시드 가변 롤).
    /// Boss/PreBoss는 _bossThreshold 게이팅이 담당하므로 마일스톤 종류로 지정해도 무시된다(RunSequencer).</summary>
    public RoomPlanKind? GetMilestoneKind(int visitIndex)
    {
        if (_milestones == null) return null;
        for (int i = 0; i < _milestones.Length; i++)
            if (_milestones[i].visitIndex == visitIndex) return _milestones[i].kind;
        return null;
    }
}
