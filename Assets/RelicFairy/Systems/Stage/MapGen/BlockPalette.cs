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
/// <summary>봉인 문 등장 방식. 문 자산의 성격(건축물 vs 자연물)에 맞춰 팔레트가 고른다.</summary>
public enum SealDoorMotion
{
    /// <summary>개구부 위에서 내리닫이처럼 떨어진다 — 석문·철문.</summary>
    Drop,
    /// <summary>바닥에 붙은 채 아래에서 자라오른다 — 뿌리·덩굴 등 유기물.</summary>
    Grow,
}

/// <summary>통로 끝 '다음 방'을 가리는 방식. 챕터 컨셉에 맞춰 팔레트가 고른다.</summary>
public enum CorridorVeilMode
{
    /// <summary>어둠·안개로 덮는다(숲·폐허·요새).</summary>
    Fog,
    /// <summary>빛이 너무 밝아 보이지 않는다(성역) — 어둠으로 가리는 다른 챕터와 정반대 대비.</summary>
    Radiance,
}

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

    [Header("Corridor / Door — 테마 컨셉 일치용")]
    [Tooltip("통로 끝 '다음 방'을 가리는 방식.\n" +
             "Fog = 어둠·안개로 덮는다(숲·폐허·요새).\n" +
             "Radiance = 빛이 너무 밝아 안 보인다(성역) — 어둠으로 가리는 다른 챕터와 정반대 대비를 만든다.")]
    [SerializeField] private CorridorVeilMode veilMode = CorridorVeilMode.Fog;

    [Tooltip("복도 끝(다음 방 챔버 입구)에 깔 안개 프리팹. 챔버를 가려 '너머에 무언가 있다'로 읽히게 한다.\n" +
             "색은 아래 veilTint(비우면 벽조명 색)로 틴트되므로 프리팹은 챕터 공용 1개면 된다. 비우면 안개 없음.")]
    [SerializeField] private GameObject corridorFogPrefab;

    [Tooltip("베일 겹 수. 숲=낮고 넓게 여러 겹 / 폐허=흩날리듯 적게 / 요새=밀폐감 있게 두껍게.")]
    [SerializeField, Range(1, 6)] private int veilLayers = 3;

    [Tooltip("베일 높이(m). 0에 가까울수록 바닥을 기는 안개, 크면 공간을 채우는 연무.")]
    [SerializeField, Range(0f, 8f)] private float veilHeight = 1f;

    [Tooltip("베일 크기 배율. 챕터마다 통로 폭·벽 높이가 달라 보정이 필요하다.")]
    [SerializeField, Range(0.2f, 3f)] private float veilScale = 1f;

    [Tooltip("베일 색. alpha 0이면 벽조명 색(챕터 지배색)을 그대로 쓴다.")]
    [SerializeField] private Color veilTint = new(0f, 0f, 0f, 0f);

    [Tooltip("Radiance 모드 빛 세기 — 성역처럼 '빛으로 가릴' 때만 쓴다.")]
    [SerializeField, Range(0f, 20f)] private float veilRadianceIntensity = 6f;

    [Tooltip("이 테마의 봉인 석문 프리팹. 비우면 GameRunBootstrapper의 공용 문으로 폴백한다.\n" +
             "고딕 석문 하나를 전 테마에 쓰면 숲·심연 방에서 컨셉이 어긋난다.")]
    [SerializeField] private GameObject sealDoorPrefab;

    [Tooltip("봉인 문 등장 방식.\n" +
             "Drop = 개구부 위에서 내리닫이처럼 떨어진다(석문·철문).\n" +
             "Grow = 바닥에 붙은 채 아래에서 자라오른다(뿌리·덩굴 등 유기물).\n" +
             "뿌리가 하늘에서 떨어지면 컨셉이 깨지므로 자연물은 반드시 Grow.")]
    [SerializeField] private SealDoorMotion sealDoorMotion = SealDoorMotion.Drop;

    [Header("Blocks")]
    [SerializeField] private List<BlockDef> blocks = new();

    private Dictionary<TileType, List<BlockDef>> _cache;

    public string ThemeMatch => themeMatch;
    public WallBuildMode WallMode => wallMode;
    public int WallHeight => wallHeight;
    public bool HasCeiling => hasCeiling;
    public RoomLightingConfig Lighting => lighting;
    public GameObject       CorridorFogPrefab => corridorFogPrefab;
    public GameObject       SealDoorPrefab    => sealDoorPrefab;
    public SealDoorMotion   SealDoorMotion    => sealDoorMotion;
    public CorridorVeilMode VeilMode          => veilMode;
    public int              VeilLayers        => Mathf.Max(1, veilLayers);
    public float            VeilHeight        => veilHeight;
    public float            VeilScale         => Mathf.Max(0.05f, veilScale);
    public float            VeilRadianceIntensity => veilRadianceIntensity;

    /// <summary>베일 색. 지정 안 했으면(alpha 0) 챕터 지배색인 벽조명 색을 그대로 쓴다.</summary>
    public Color VeilColor => veilTint.a > 0.001f
        ? veilTint
        : (lighting?.wallLightTint != null && lighting.wallLightTint.color.a > 0.001f
            ? lighting.wallLightTint.color
            : Color.white);

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
