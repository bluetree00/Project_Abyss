using UnityEngine;

/// <summary>
/// 챕터별 데이터.
/// SO를 기본값으로 사용하되, 서버에서 내려온 데이터로 오버라이드 가능.
/// 런타임 데이터는 ChapterRuntimeData로 변환하여 사용한다.
/// </summary>
[CreateAssetMenu(fileName = "NewChapterData", menuName = "Stage/Chapter Data")]
public class ChapterDataSO : ScriptableObject
{
    [Header("기본 정보")]
    public ChapterId chapterId;
    public string chapterName;
    [TextArea] public string description;

    [Header("맵 배경")]
    public Sprite mapBackground;
    public Color mapBackgroundTint = Color.white;
    public string mapBackgroundKey;

    [Header("맵 장식")]
    [Tooltip("배경 위·노드 아래 레이어에 스캐터할 장식 스프라이트 목록. 비우면 Bootstrapper의 defaultDecorationSprites 폴백.")]
    public Sprite[] decorationSprites;

    [Header("필드 구조물")]
    [Tooltip("인게임 방 배경에 스폰할 테마 환경 프리팹 Addressables 키. 비우면 스폰 안 함.")]
    public string fieldPrefabKey;

    [Header("맵 테마")]
    [Tooltip("인게임 방 렌더링 시 BlockPalette/DecorationCatalog 매칭 키 (예: Forest, Cave, Abyss). 비우면 방별 theme 또는 Default 팔레트 폴백.")]
    public string theme;

    [Header("사운드")]
    public string bgmKey;

    [Header("난이도")]
    [Tooltip("몬스터 스탯 배수 (1.0 = 기본)")]
    public float difficultyScale = 1f;
    [Tooltip("몬스터 수 배수")]
    public float monsterCountScale = 1f;

    [Header("몬스터 필터")]
    [Tooltip("이 챕터에서 사용할 몬스터 풀 태그 (비어있으면 전체)")]
    public string monsterPoolTag;

    [Header("보상")]
    [Tooltip("골드 드롭 배수")]
    public float goldMultiplier = 1f;
    [Tooltip("아이템 드롭 확률 배수")]
    public float itemDropMultiplier = 1f;

    [Header("맵 노드 구성")]
    [Tooltip("중간 층 수 (Start/Boss 제외). 예: 5면 총 7층")]
    public int middleLayers = 5;
    [Tooltip("피크 층 (1부터 시작). 이 층까지 노드가 늘어나고 이후 줄어듦. 예: 3이면 3층까지 확장")]
    public int peakLayer = 3;

    /// <summary>SO 기본값으로 런타임 데이터 생성.</summary>
    public ChapterRuntimeData ToRuntime()
    {
        return new ChapterRuntimeData
        {
            chapterId        = chapterId,
            chapterName      = chapterName,
            description      = description,
            mapBackground    = mapBackground,
            mapBackgroundTint = mapBackgroundTint,
            mapBackgroundKey = mapBackgroundKey,
            theme            = theme,
            fieldPrefabKey   = fieldPrefabKey,
            bgmKey           = bgmKey,
            difficultyScale  = difficultyScale,
            monsterCountScale = monsterCountScale,
            monsterPoolTag   = monsterPoolTag,
            goldMultiplier   = goldMultiplier,
            itemDropMultiplier = itemDropMultiplier,
            middleLayers     = middleLayers,
            peakLayer        = peakLayer,
        };
    }
}

/// <summary>
/// 챕터 런타임 데이터.
/// SO 기본값 + 서버 오버라이드를 병합한 결과.
/// 서버 확장 시 이 클래스에 서버 전용 필드 추가.
/// </summary>
[System.Serializable]
public class ChapterRuntimeData
{
    public ChapterId chapterId;
    public string chapterName;
    public string description;

    // 맵
    public Sprite mapBackground;
    public Color mapBackgroundTint = Color.white;
    public string mapBackgroundKey;

    // 테마
    public string theme;
    public string fieldPrefabKey;

    // 사운드
    public string bgmKey;

    // 난이도
    public float difficultyScale = 1f;
    public float monsterCountScale = 1f;
    public string monsterPoolTag;

    // 보상
    public float goldMultiplier = 1f;
    public float itemDropMultiplier = 1f;

    // 맵 노드 구성
    public int middleLayers = 5;
    public int peakLayer = 3;

    /// <summary>서버 데이터로 오버라이드 (null/0이 아닌 값만 덮어씀).</summary>
    public void MergeFromServer(ChapterServerEntry server)
    {
        if (server == null) return;
        if (!string.IsNullOrEmpty(server.chapter_name)) chapterName = server.chapter_name;
        if (!string.IsNullOrEmpty(server.description)) description = server.description;
        if (!string.IsNullOrEmpty(server.map_bg_key)) mapBackgroundKey = server.map_bg_key;
        if (!string.IsNullOrEmpty(server.theme)) theme = server.theme;
        if (!string.IsNullOrEmpty(server.bgm_key)) bgmKey = server.bgm_key;
        if (!string.IsNullOrEmpty(server.monster_pool_tag)) monsterPoolTag = server.monster_pool_tag;
        if (server.difficulty_scale > 0) difficultyScale = server.difficulty_scale;
        if (server.monster_count_scale > 0) monsterCountScale = server.monster_count_scale;
        if (server.gold_multiplier > 0) goldMultiplier = server.gold_multiplier;
        if (server.item_drop_multiplier > 0) itemDropMultiplier = server.item_drop_multiplier;
        if (server.total_layers > 0) middleLayers = server.total_layers - 2;
        if (server.peak_layer > 0) peakLayer = server.peak_layer;
    }
}

/// <summary>서버에서 내려받는 챕터 데이터 엔트리. CSV/JSON 파싱 대상.</summary>
[System.Serializable]
public class ChapterServerEntry
{
    public string chapter_id;
    public string chapter_name;
    public string description;
    public string map_bg_key;
    public string theme;
    public string bgm_key;
    public float difficulty_scale;
    public float monster_count_scale;
    public string monster_pool_tag;
    public float gold_multiplier;
    public float item_drop_multiplier;

    // 맵 노드 구성
    public int total_layers;
    public int peak_layer;

    // 버전
    public int stat_version;
}
