using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

/// <summary>
/// 서버 데이터 ↔ BoardManager ↔ 시너지 효과를 연결하는 브릿지.
/// GameScene에 배치하여 사용.
///
/// 역할:
/// 1. UI_GridPanel.BoardContainer(DDOL 계층 직속 자식)에 Puzzle.prefab을 동적 스폰하여 BoardManager 확보
/// 2. BlockDataManager에서 Grid 데이터 → GridAssetData → BoardManager에 등록
/// 3. 아이템 획득 시 shape_id → ShapeData → BoardManager에 Shape 등록
/// 4. BoardManager.OnGridFilled 구독 → 시너지 효과 PlayerRuntimeStats에 적용
/// </summary>
public class BlockSynergyBridge : MonoBehaviour
{
    // ── Constants ──
    // 그리드 squareGap 과 Shape cellSize 를 동일 값으로 유지해 크기를 일치시킴
    private const float GRID_CELL_SIZE = 120f;

    // ── Static ──
    public static BlockSynergyBridge Instance { get; private set; }

    // 그리드 완성 시 UI_GridPanel에 시각 피드백 전달
    public event System.Action<string> OnSynergyActivated;

    // ── SerializeField ──
    [Header("Puzzle 프리팹 (직접 참조)")]
    [SerializeField] private GameObject puzzlePrefab;

    [Header("수동 참조 (없으면 UI_GridPanel.BoardContainer에 동적 스폰)")]
    [SerializeField] private BoardManager boardManager;

    // ── Private ──
    private readonly Dictionary<string, string> _gridIdBySOName = new();
    private readonly Dictionary<string, GridAssetData> _registeredGrids = new();
    private readonly HashSet<string> _appliedGridIds = new();
    private GameObject _puzzleInstance;
    private bool _initialized;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (boardManager != null)
        {
            boardManager.OnGridFilled -= HandleGridFilled;
            boardManager.OnGridSessionActivated -= HandleGridSessionActivated;
        }

        if (_puzzleInstance != null)
            Destroy(_puzzleInstance);

        _registeredGrids.Clear();

        if (Instance == this) Instance = null;
    }

    // ── 초기화: UI_GridPanel.BoardContainer에 Puzzle UI 생성 + 서버 Grid 등록 ──

    /// <summary>
    /// BlockDataManager에서 모든 Grid 데이터를 읽어 BoardManager에 등록한다.
    /// GameRunBootstrapper 초기화 이후에 호출.
    /// </summary>
    public void InitializeGridsFromServer()
    {
        if (_initialized) return;

        var blockData = Managers.BlockData;
        if (blockData == null || !blockData.IsInitialized)
        {
            Debug.LogWarning("[BlockSynergyBridge] BlockData 미초기화");
            return;
        }

        // boardManager가 BoardContainer 자식이 아닌 경우(구 Panel_Grid 참조 등)는 무시
        var boardContainer = UI_GridPanel.Instance?.BoardContainer;
        bool isValidRef = boardManager != null &&
                          boardContainer != null &&
                          boardManager.transform.IsChildOf(boardContainer);

        if (isValidRef)
        {
            boardManager.OnGridFilled -= HandleGridFilled;
            boardManager.OnGridFilled += HandleGridFilled;
            boardManager.OnGridSessionActivated -= HandleGridSessionActivated;
            boardManager.OnGridSessionActivated += HandleGridSessionActivated;

            RegisterAllGrids(blockData);
            return;
        }

        // 구 Panel_Grid 참조 초기화 후 BoardContainer에 동적 스폰
        boardManager = null;
        SpawnPuzzleAndInitAsync().Forget();
    }

    private async UniTaskVoid SpawnPuzzleAndInitAsync()
    {
        if (puzzlePrefab == null)
        {
            Debug.LogWarning("[BlockSynergyBridge] puzzlePrefab이 할당되지 않음");
            return;
        }

        // UI_GridPanel.boardContainer(DDOL 계층, UI_GridPanel 직속 자식)에 직접 스폰
        var container = UI_GridPanel.Instance?.BoardContainer;
        if (container == null)
        {
            Debug.LogWarning("[BlockSynergyBridge] UI_GridPanel.BoardContainer를 찾을 수 없음");
            return;
        }

        // UI_GridPanel이 비활성이면 일시 활성화 (Awake/Start 보장)
        var uiPanel = UI_GridPanel.Instance.gameObject;
        bool wasActive = uiPanel.activeSelf;
        if (!wasActive) uiPanel.SetActive(true);

        _puzzleInstance = Instantiate(puzzlePrefab, container);

        var rt = _puzzleInstance.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        boardManager = _puzzleInstance.GetComponentInChildren<BoardManager>(true);
        if (boardManager == null)
        {
            Debug.LogError("[BlockSynergyBridge] Puzzle 프리팹에 BoardManager 없음");
            if (!wasActive) uiPanel.SetActive(false);
            return;
        }

        // 1프레임 대기 (BoardManager.Awake/Start 실행 보장)
        await UniTask.Yield();

        boardManager.OnGridFilled += HandleGridFilled;
        boardManager.OnGridSessionActivated -= HandleGridSessionActivated;
        boardManager.OnGridSessionActivated += HandleGridSessionActivated;

        var blockData = Managers.BlockData;
        if (blockData != null && blockData.IsInitialized)
            RegisterAllGrids(blockData);

        if (!wasActive) uiPanel.SetActive(false);

        Debug.Log("[BlockSynergyBridge] Puzzle UI 생성 완료 (UI_GridPanel.BoardContainer)");
    }

    private void RegisterAllGrids(BlockDataManager blockData)
    {
        _initialized = true;

        // 이벤트 구독 (중복 방지)
        boardManager.OnGridFilled -= HandleGridFilled;
        boardManager.OnGridFilled += HandleGridFilled;

        // order 순서로 Grid 데이터 수집
        var sortedIds = blockData.GetGridIdsSortedByOrder();
        _registeredGrids.Clear();

        foreach (var gridId in sortedIds)
        {
            var meta = blockData.GetGridMeta(gridId);
            if (meta == null) continue;

            var gridAssetData = ConvertToGridAssetData(gridId, meta);
            _registeredGrids[gridId] = gridAssetData;

            Debug.Log($"[BlockSynergyBridge] Grid 등록 (order={meta.order}): {gridId} ({meta.grid_name}) {meta.rows}x{meta.cols}");
        }

        // GameplayRoot(편집 화면) 레이아웃 구성
        if (boardManager.gameplayRoot != null)
        {
            // GameplayRoot: 부모 stretch fill (Puzzle.prefab 기본값 100×100 → 전체 화면)
            var gameplayRT = boardManager.gameplayRoot.GetComponent<RectTransform>();
            if (gameplayRT != null)
            {
                gameplayRT.anchorMin = Vector2.zero;
                gameplayRT.anchorMax = Vector2.one;
                gameplayRT.offsetMin = new Vector2(40f, 40f);
                gameplayRT.offsetMax = new Vector2(-40f, -40f);
                gameplayRT.pivot = new Vector2(0.5f, 0.5f);
            }

            // GridHost: 좌측 62% × 상하 90% — 그리드 편집 영역
            if (boardManager.gridHost != null)
            {
                boardManager.gridHost.anchorMin = new Vector2(0.02f, 0.05f);
                boardManager.gridHost.anchorMax = new Vector2(0.62f, 0.95f);
                boardManager.gridHost.offsetMin = Vector2.zero;
                boardManager.gridHost.offsetMax = Vector2.zero;
                boardManager.gridHost.pivot = new Vector2(0.5f, 0.5f);
            }

            // ShapeScrollView: 우측 패널(63%~94%) — 스크롤 뷰포트 영역
            var shapeSSV = boardManager.gameplayRoot.GetComponentInChildren<ShapeScrollView>(true);
            if (shapeSSV != null)
            {
                var ssvRT = shapeSSV.transform as RectTransform;
                if (ssvRT != null)
                {
                    ssvRT.anchorMin = new Vector2(0.63f, 0.05f);
                    ssvRT.anchorMax = new Vector2(0.94f, 0.95f);
                    ssvRT.offsetMin = Vector2.zero;
                    ssvRT.offsetMax = Vector2.zero;
                    ssvRT.pivot = new Vector2(0.5f, 0.5f);
                }

                // ShapeHost를 ShapeScrollView 내부로 리패런트 (RectMask2D 클리핑 적용)
                if (boardManager.shapeHost != null &&
                    boardManager.shapeHost.parent != shapeSSV.transform)
                {
                    boardManager.shapeHost.SetParent(shapeSSV.transform, false);
                    // ScrollRect Content 표준 앵커: 너비=부모 전체, 상단 고정
                    boardManager.shapeHost.anchorMin = new Vector2(0f, 1f);
                    boardManager.shapeHost.anchorMax = new Vector2(1f, 1f);
                    boardManager.shapeHost.pivot     = new Vector2(0.5f, 1f);
                    boardManager.shapeHost.offsetMin = Vector2.zero;
                    boardManager.shapeHost.offsetMax = Vector2.zero;
                }
            }
            else if (boardManager.shapeHost != null)
            {
                // ShapeScrollView 없을 때 폴백
                boardManager.shapeHost.anchorMin = new Vector2(0.63f, 0.05f);
                boardManager.shapeHost.anchorMax = new Vector2(0.94f, 0.95f);
                boardManager.shapeHost.offsetMin = Vector2.zero;
                boardManager.shapeHost.offsetMax = Vector2.zero;
                boardManager.shapeHost.pivot = new Vector2(0.5f, 1f);
            }

            // spawnOrigin 수정: X=0(중앙), Y=80(상단 80px 아래서 시작)
            boardManager.spawnOrigin = new Vector2(0f, 80f);

            // 그리드 영역 배경
            EnsureAreaBackground(boardManager.gameplayRoot, "GridAreaBG",
                new Vector2(0.02f, 0.05f), new Vector2(0.62f, 0.95f),
                new Color(0f, 0f, 0f, 0.18f));

            // 셰이프 패널 배경
            EnsureAreaBackground(boardManager.gameplayRoot, "ShapeAreaBG",
                new Vector2(0.63f, 0.05f), new Vector2(0.94f, 0.95f),
                new Color(0f, 0f, 0f, 0.18f));
        }

    }

    private void HandleGridSessionActivated(Grid gridInstance) { }

    /// <summary>
    /// GameplayRoot 아래에 반투명 배경 Image를 생성한다.
    /// 이미 같은 이름이 있으면 스킵.
    /// </summary>
    private static void EnsureAreaBackground(GameObject root, string name,
        Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        if (root == null) return;
        if (root.transform.Find(name) != null) return;

        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(root.transform, false);
        go.transform.SetAsFirstSibling();

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot     = new Vector2(0.5f, 0.5f);

        var img = go.GetComponent<Image>();
        img.color         = color;
        img.raycastTarget = false;

        var le = go.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
    }

    // ── 치트 ──

    /// <summary>디버그용: 특정 그리드의 시너지를 강제 발동한다.</summary>
    public void CheatTriggerSynergy(string gridId)
    {
        ApplySynergyEffects(gridId);
    }

    // ── 아이템 획득 시 블록(Shape) 등록 ──

    /// <summary>
    /// 아이템의 shape_id로 블록을 생성하여 BoardManager에 Shape 등록.
    /// </summary>
    public void RegisterShapeFromItem(int shapeId)
    {
        var blockData = Managers.BlockData;
        if (blockData == null || boardManager == null) return;

        var shapeEntry = blockData.GetShape(shapeId);
        if (shapeEntry == null)
        {
            Debug.LogWarning($"[BlockSynergyBridge] Shape 없음: {shapeId}");
            return;
        }

        var offsets = BlockDataManager.ParseCellOffsets(shapeEntry);

        // ShapeAssetSO를 런타임 생성
        var shapeSO = ScriptableObject.CreateInstance<ShapeAssetSO>();
        shapeSO.shapeName = shapeEntry.shape_name;
        shapeSO.shapeBlockPrefab = boardManager.defaultShapeBlockPrefab;
        shapeSO.cellOffsets = offsets;
        shapeSO.cellSize = shapeEntry.cell_size > 0 ? shapeEntry.cell_size : GRID_CELL_SIZE;

        // 공용 풀에 직접 추가 (활성 그리드 없어도 누적됨)
        boardManager.SpawnSharedShape(shapeSO);
        Debug.Log($"[BlockSynergyBridge] Shape 추가(공용풀): {shapeEntry.shape_name} (id={shapeId})");
    }

    // ── Grid 완성 시 시너지 효과 적용 ──

    private void HandleGridFilled(GridAssetSO filledAsset)
    {
        if (filledAsset == null) return;

        string gridId = null;
        foreach (var kvp in _gridIdBySOName)
        {
            if (kvp.Key == filledAsset.name)
            {
                gridId = kvp.Value;
                break;
            }
        }

        if (string.IsNullOrEmpty(gridId))
        {
            Debug.LogWarning($"[BlockSynergyBridge] grid_id 매핑 실패: {filledAsset.name}");
            return;
        }

        ApplySynergyEffects(gridId);
    }

    private void ApplySynergyEffects(string gridId)
    {
        // 이미 적용된 그리드는 중복 적용하지 않음
        if (_appliedGridIds.Contains(gridId)) return;

        var blockData = Managers.BlockData;
        if (blockData == null) return;

        var entries = blockData.GetGrid(gridId);
        if (entries == null) return;

        var run = AppBootstrapper.Instance?.CurrentRun;
        var player = run?.Player;
        if (player == null) return;

        var stats = player.RuntimeStats;

        _appliedGridIds.Add(gridId);

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.effect_type)) continue;

            switch (entry.trigger)
            {
                case "Always":
                    stats.ApplySynergyEffect(entry.effect_type, entry.value);
                    break;

                case "OnHit":
                    stats.RegisterConditionalSynergy(new ConditionalSynergy
                    {
                        gridId     = gridId,
                        effectType = entry.effect_type,
                        trigger    = "OnHit",
                        value      = entry.value,
                        maxStack   = entry.max_stack > 0 ? entry.max_stack : 1,
                        duration   = entry.duration,
                    });
                    break;

                case "OnLowHp":
                    stats.RegisterConditionalSynergy(new ConditionalSynergy
                    {
                        gridId     = gridId,
                        effectType = entry.effect_type,
                        trigger    = "OnLowHp",
                        value      = entry.value,
                        threshold  = entry.value2 > 0f ? entry.value2 : 0.3f,
                    });
                    break;
            }

            // GameRunSession에 이력 기록 (씬 전환 시 복원용)
            run?.RecordSynergy(new SynergyRecord
            {
                gridId     = gridId,
                effectType = entry.effect_type,
                trigger    = entry.trigger,
                value      = entry.value,
                value2     = entry.value2,
                maxStack   = entry.max_stack,
                duration   = entry.duration,
            });

            Debug.Log($"[BlockSynergyBridge] 시너지 발동: {gridId} → {entry.effect_type} ({entry.trigger}) +{entry.value}");
        }

        var desc = BuildSynergyDescription(entries);
        if (!string.IsNullOrEmpty(desc))
            OnSynergyActivated?.Invoke(desc);
    }

    private static string BuildSynergyDescription(System.Collections.Generic.IEnumerable<BlockGridEntry> entries)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.effect_type)) continue;
            float pct = entry.value * 100f;
            if (sb.Length > 0) sb.Append("  ");
            sb.Append($"{entry.effect_type} {(pct >= 0f ? "+" : "")}{pct:F0}%");
        }
        return sb.ToString();
    }

    /// <summary>런 종료 시 적용 이력 초기화. 외부에서 호출.</summary>
    public void ClearAppliedGrids()
    {
        _appliedGridIds.Clear();
    }

    /// <summary>등록된 GridAssetData 전체를 반환. GridGalleryView/GridEditView에서 참조.</summary>
    public IReadOnlyDictionary<string, GridAssetData> GetRegisteredGrids()
        => _registeredGrids;


    // ── 변환 유틸 ──

    private GridAssetData ConvertToGridAssetData(string gridId, BlockGridEntry meta)
    {
        var rows01 = BlockDataManager.ParseGridRows(meta);
        int rowCount = meta.rows > 0 ? meta.rows : rows01.Length;
        int colCount = meta.cols > 0 ? meta.cols : (rows01.Length > 0 ? rows01[0].Length : 0);

        // SO 이름 → grid_id 매핑 저장
        _gridIdBySOName[gridId] = gridId;

        return new GridAssetData
        {
            id = gridId,
            displayName = meta.grid_name,
            pattern = new GridPatternData
            {
                rows = rowCount,
                columns = colCount,
                rows01 = rows01,
            },
            visual = new GridVisualData { squareGap = GRID_CELL_SIZE, squareScale = 0.9f },
            spawnableShapes = System.Array.Empty<ShapeData>(),
        };
    }

    private static void StretchFill(Transform t)
    {
        if (t == null) return;
        StretchFill(t.GetComponent<RectTransform>());
    }

    private static void StretchFill(RectTransform rt)
    {
        if (rt == null) return;
        if (rt.localScale.sqrMagnitude < 0.01f)
            rt.localScale = Vector3.one;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindChildRecursive(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
