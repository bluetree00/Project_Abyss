using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 조명 프리팹 위에 덮어씌우는 챕터 무드 값.
/// 프리팹은 전 챕터가 공유하고(WallTorch/CenterLight), 색·세기·범위만 팔레트가 소유한다.
/// 프리팹을 챕터 수만큼 복제하지 않는 이유: 두 프리팹 모두 메시 없는 Point Light 단독이라
/// 복제해봐야 달라지는 건 이 세 값뿐이다.
/// </summary>
[System.Serializable]
public class LightTint
{
    [Tooltip("조명 색. 알파 0이면 프리팹 원본 색을 유지한다(미설정 취급).")]
    public Color color = new Color(0f, 0f, 0f, 0f);

    [Min(0f), Tooltip("프리팹 강도에 곱할 배율. 1이면 원본 유지.")]
    public float intensityMul = 1f;

    [Min(0f), Tooltip("프리팹 범위(range)에 곱할 배율. 1이면 원본 유지.")]
    public float rangeMul = 1f;

    /// <summary>대상 계층의 Light에 적용. 미설정 값은 건드리지 않는다.</summary>
    public void ApplyTo(GameObject go)
    {
        if (go == null) return;
        var lt = go.GetComponentInChildren<Light>();   // 자기 자신 포함 — WallTorch는 Light가 자식에 있다
        if (lt == null) return;
        if (color.a > 0f) lt.color = color;
        if (!Mathf.Approximately(intensityMul, 1f)) lt.intensity *= intensityMul;
        if (!Mathf.Approximately(rangeMul, 1f))     lt.range     *= rangeMul;
    }
}

/// <summary>
/// 팔레트별 방 조명 설정. BlockPalette에 직렬화되어 테마별 조명을 바인딩한다.
/// </summary>
[System.Serializable]
public class RoomLightingConfig
{
    [Tooltip("벽 안쪽 면에 배치할 조명 프리팹 (Point Light 포함 횃불/벽등). 비워두면 벽 조명 없음.")]
    public GameObject wallLightPrefab;

    [Tooltip("방 중앙 천장에 배치할 조명 프리팹. 비워두면 중앙 조명 없음.")]
    public GameObject centerLightPrefab;

    [Min(1), Tooltip("벽 조명 배치 간격 (값이 클수록 듬성). 기본 5.")]
    public int wallLightSpacing = 5;

    [Range(0f, 1f), Tooltip("벽 높이 중 조명 위치 비율. 0=하단, 1=상단. 0.4 권장.")]
    public float wallLightHeightRatio = 0.4f;

    [Min(0), Tooltip("배치할 벽 조명 최대 개수. 0이면 무제한(현행). 큰 방의 과도한 실시간 조명을 캡한다.")]
    public int maxWallLights = 0;

    [Header("Chapter Mood")]
    [Tooltip("벽 조명 무드. 챕터 지배색을 여기서 준다.")]
    public LightTint wallLightTint = new();

    [Tooltip("중앙 조명 무드. 벽 조명과 다른 색을 주면 방에 2색 대비가 생긴다.")]
    public LightTint centerLightTint = new();
}

/// <summary>벽을 세우는 방식. 팔레트(테마)별로 다르다.</summary>
public enum WallBuildMode
{
    /// <summary>1셀 벽 프리팹을 wallHeight만큼 수직 반복(현행 큐브 방식). 실내 던전용.</summary>
    Stacked,
    /// <summary>셀당 벽 프리팹 1개만 배치. 높이는 프리팹 메시가 소유(절벽 등). 자연 지형용.</summary>
    Single,
}

/// <summary>
/// 테마별 블록 세트. TileType → BlockDef 매핑.
/// 같은 TileType에 여러 BlockDef를 등록하면 가중치 랜덤 선택.
///
/// themeMatch 필드로 MapRoomEntry.theme와 매칭되는 팔레트를 선택할 수 있다.
/// "*" 또는 빈값이면 범용(fallback) 팔레트로 간주.
/// </summary>
[CreateAssetMenu(menuName = "Map/BlockPalette")]
public class BlockPalette : ScriptableObject
{
    [Tooltip("방 테마 문자열. 비어있거나 \"*\"면 범용(모든 테마 fallback).")]
    [SerializeField] private string themeMatch = "*";

    [Header("Vertical Profile")]
    [Tooltip("벽 세우는 방식. Stacked=1셀 프리팹 수직 반복(실내), Single=셀당 1개(절벽 등 자연지형).")]
    [SerializeField] private WallBuildMode wallMode = WallBuildMode.Stacked;

    [Min(1), Tooltip("벽 높이(셀 수). Stacked면 반복 층수, Single이면 문 개구부/복도/천장 높이 계산용 논리 높이. 0/미설정 시 부트스트래퍼 전역값 폴백.")]
    [SerializeField] private int wallHeight = 12;

    [Tooltip("천장을 덮을지. false면 열린 하늘(숲·심연). true면 천장 배치(실내).")]
    [SerializeField] private bool hasCeiling = true;

    [Header("Lighting")]
    [SerializeField] private RoomLightingConfig lighting = new();

    [Header("Blocks")]
    [SerializeField] private List<BlockDef> blocks = new();

    private Dictionary<TileType, List<BlockDef>> _cache;

    public string ThemeMatch => themeMatch;
    public WallBuildMode WallMode => wallMode;
    public int WallHeight => wallHeight;
    public bool HasCeiling => hasCeiling;
    public RoomLightingConfig Lighting => lighting;

    /// <summary>주어진 테마와 이 팔레트가 일치하는지. "*" 또는 빈값은 항상 매칭.</summary>
    public bool MatchesTheme(string theme)
    {
        if (string.IsNullOrEmpty(themeMatch) || themeMatch == "*") return true;
        if (string.IsNullOrEmpty(theme)) return false;
        return themeMatch.Trim().Equals(theme.Trim(), System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>TileType에 맞는 BlockDef를 가중치 랜덤으로 선택.</summary>
    /// <param name="rng">시드 RNG. 넘기면 선택이 결정적이 되어 같은 시드로 같은 방을 재현할 수 있다
    /// (이어하기). null이면 Unity 전역 Random — 재현이 필요 없는 경로(레거시 존 빌드·에디터 툴)용.</param>
    public BlockDef Pick(TileType type, System.Random rng = null)
    {
        BuildCacheIfNeeded();

        if (!_cache.TryGetValue(type, out var list) || list.Count == 0)
            return null;

        if (list.Count == 1)
            return list[0];

        // 가중치 랜덤
        int total = 0;
        foreach (var b in list) total += b.weight;

        int roll = rng != null ? rng.Next(0, total) : Random.Range(0, total);
        int acc = 0;
        foreach (var b in list)
        {
            acc += b.weight;
            if (roll < acc) return b;
        }

        return list[0];
    }

    /// <summary>특정 TileType에 등록된 BlockDef가 있는지.</summary>
    public bool Has(TileType type)
    {
        BuildCacheIfNeeded();
        return _cache.ContainsKey(type) && _cache[type].Count > 0;
    }

    /// <summary>특정 TileType에 등록된 모든 BlockDef를 반환. 없으면 빈 배열.</summary>
    public IReadOnlyList<BlockDef> GetAll(TileType type)
    {
        BuildCacheIfNeeded();
        return _cache.TryGetValue(type, out var list) ? list : System.Array.Empty<BlockDef>();
    }

    private void BuildCacheIfNeeded()
    {
        if (_cache != null) return;

        _cache = new Dictionary<TileType, List<BlockDef>>();
        foreach (var b in blocks)
        {
            if (b == null) continue;
            if (!_cache.TryGetValue(b.tileType, out var list))
            {
                list = new List<BlockDef>();
                _cache[b.tileType] = list;
            }
            list.Add(b);
        }
    }

    private void OnValidate()
    {
        _cache = null; // 에디터 변경 시 캐시 리셋
    }
}
