using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 서버 데이터 ↔ BoardManager ↔ 시너지 효과를 연결하는 브릿지.
/// GameScene에 배치하여 사용.
///
/// 역할:
/// 1. Panel_Grid에 Puzzle.prefab을 동적 로드하여 BoardManager 확보
/// 2. BlockDataManager에서 Grid 데이터 → GridAssetData → BoardManager에 등록
/// 3. 아이템 획득 시 shape_id → ShapeData → BoardManager에 Shape 등록
/// 4. BoardManager.OnGridFilled 구독 → 시너지 효과 PlayerRuntimeStats에 적용
/// </summary>
public class BlockSynergyBridge : MonoBehaviour
{
    public static BlockSynergyBridge Instance { get; private set; }

    [Header("Puzzle 프리팹 (직접 참조)")]
    [SerializeField] private GameObject puzzlePrefab;

    [Header("수동 참조 (없으면 Panel_Grid에서 동적 생성)")]
    [SerializeField] private BoardManager boardManager;

    private readonly Dictionary<string, string> _gridIdBySOName = new();
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
            boardManager.OnGridFilled -= HandleGridFilled;

        if (_puzzleInstance != null)
            Destroy(_puzzleInstance);

        if (Instance == this) Instance = null;
    }

    // ── 초기화: Panel_Grid에 Puzzle UI 생성 + 서버 Grid 등록 ──

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

        // BoardManager가 없으면 Panel_Grid에 Puzzle.prefab 동적 로드
        if (boardManager == null)
        {
            SpawnPuzzleAndInitAsync().Forget();
            return;
        }

        RegisterAllGrids(blockData);
    }

    private async UniTaskVoid SpawnPuzzleAndInitAsync()
    {
        // Panel_Grid 찾기 (HudView 하위, 비활성 포함)
        var panelGrid = FindPanelGrid();
        if (panelGrid == null)
        {
            Debug.LogWarning("[BlockSynergyBridge] Panel_Grid를 찾을 수 없음");
            return;
        }

        if (puzzlePrefab == null)
        {
            Debug.LogWarning("[BlockSynergyBridge] puzzlePrefab이 할당되지 않음");
            return;
        }

        // Panel_Grid를 일시 활성화 (Instantiate + Start() 실행 보장)
        bool wasActive = panelGrid.gameObject.activeSelf;
        if (!wasActive) panelGrid.gameObject.SetActive(true);

        _puzzleInstance = Instantiate(puzzlePrefab, panelGrid);

        // RectTransform 풀 스트레치
        var rt = _puzzleInstance.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        // BoardManager 참조 확보
        boardManager = _puzzleInstance.GetComponentInChildren<BoardManager>(true);
        if (boardManager == null)
        {
            Debug.LogError("[BlockSynergyBridge] Puzzle 프리팹에 BoardManager 없음");
            if (!wasActive) panelGrid.gameObject.SetActive(false);
            return;
        }

        // 1프레임 대기 (BoardManager.Awake/Start 실행 보장)
        await UniTask.Yield();

        boardManager.OnGridFilled += HandleGridFilled;

        var blockData = Managers.BlockData;
        if (blockData != null && blockData.IsInitialized)
            RegisterAllGrids(blockData);

        // 원래 비활성이었으면 다시 비활성 (Tab키로 열 때 활성화)
        if (!wasActive) panelGrid.gameObject.SetActive(false);

        Debug.Log("[BlockSynergyBridge] Puzzle UI 생성 + 서버 Grid 등록 완료");
    }

    private void RegisterAllGrids(BlockDataManager blockData)
    {
        _initialized = true;

        // 이벤트 구독 (중복 방지)
        boardManager.OnGridFilled -= HandleGridFilled;
        boardManager.OnGridFilled += HandleGridFilled;

        // order 순서로 Grid 등록
        var sortedIds = blockData.GetGridIdsSortedByOrder();
        bool isFirst = true;

        foreach (var gridId in sortedIds)
        {
            var meta = blockData.GetGridMeta(gridId);
            if (meta == null || string.IsNullOrEmpty(meta.effect_type)) continue;

            var gridAssetData = ConvertToGridAssetData(gridId, meta);
            boardManager.EnterGrid(gridAssetData);

            if (isFirst)
                isFirst = false;

            Debug.Log($"[BlockSynergyBridge] Grid 등록 (order={meta.order}): {gridId} ({meta.grid_name}) {meta.rows}x{meta.cols}");
        }

        // 첫 번째 그리드로 복귀
        if (sortedIds.Count > 0)
        {
            var firstMeta = blockData.GetGridMeta(sortedIds[0]);
            if (firstMeta != null)
                boardManager.EnterGrid(ConvertToGridAssetData(sortedIds[0], firstMeta));
        }
    }

    private Transform FindPanelGrid()
    {
        // 자신(@HUD)의 하위에서 Panel_Grid 검색
        return FindChildRecursive(transform, "Panel_Grid");
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

        // BoardManager의 활성 GridAssetSO에 ShapeAssetSO를 동적 추가
        var activeAsset = boardManager.ActiveAsset;
        if (activeAsset == null)
        {
            Debug.LogWarning("[BlockSynergyBridge] 활성 Grid 없음, Shape 등록 실패");
            return;
        }

        // ShapeAssetSO를 런타임 생성
        var shapeSO = ScriptableObject.CreateInstance<ShapeAssetSO>();
        shapeSO.shapeName = shapeEntry.shape_name;
        shapeSO.cellOffsets = offsets;
        shapeSO.cellSize = shapeEntry.cell_size > 0 ? shapeEntry.cell_size : 90f;

        activeAsset.AddShape(shapeSO);
        Debug.Log($"[BlockSynergyBridge] Shape 추가: {shapeEntry.shape_name} (id={shapeId})");
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
        var blockData = Managers.BlockData;
        if (blockData == null) return;

        var entries = blockData.GetGrid(gridId);
        if (entries == null) return;

        var run = GameRunBootstrapper.Instance != null ? GameRunBootstrapper.Instance.Run : null;
        var player = run?.Player;
        if (player == null) return;

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.effect_type)) continue;

            // Always 트리거: 즉시 스탯 적용
            if (entry.trigger == "Always")
                player.RuntimeStats.ApplySynergyEffect(entry.effect_type, entry.value);

            Debug.Log($"[BlockSynergyBridge] 시너지 발동: {gridId} → {entry.effect_type} +{entry.value}");
        }
    }

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
            visual = new GridVisualData(),
            spawnableShapes = System.Array.Empty<ShapeData>(),
        };
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
