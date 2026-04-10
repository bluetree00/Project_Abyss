using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using TMPro;

/// <summary>
/// 서버 데이터 ↔ BoardManager ↔ 시너지 효과를 연결하는 브릿지.
/// GameScene에 배치하여 사용.
///
/// 역할:
/// 1. Panel_Grid에 Puzzle.prefab을 동적 로드하여 BoardManager 확보
/// 2. BlockDataManager에서 Grid 데이터 → GridAssetData → BoardManager에 등록
/// 3. SelectionRoot에 6개 썸네일 생성 → 클릭 시 확대 뷰 전환
/// 4. 아이템 획득 시 shape_id → ShapeData → BoardManager에 Shape 등록
/// 5. BoardManager.OnGridFilled 구독 → 시너지 효과 PlayerRuntimeStats에 적용
/// </summary>
public class BlockSynergyBridge : MonoBehaviour
{
    // ── Constants ──
    private const float THUMBNAIL_WIDTH = 160f;
    private const float THUMBNAIL_HEIGHT = 200f;
    private const float THUMBNAIL_SPACING = 12f;
    private const int THUMBNAIL_COLUMNS = 3;

    // ── Static ──
    public static BlockSynergyBridge Instance { get; private set; }

    // ── SerializeField ──
    [Header("Puzzle 프리팹 (직접 참조)")]
    [SerializeField] private GameObject puzzlePrefab;

    [Header("수동 참조 (없으면 Panel_Grid에서 동적 생성)")]
    [SerializeField] private BoardManager boardManager;

    [Header("썸네일 폰트")]
    [SerializeField] private TMP_FontAsset thumbnailFont;

    // ── Private ──
    private readonly Dictionary<string, string> _gridIdBySOName = new();
    private readonly Dictionary<string, GridAssetData> _registeredGrids = new();
    private readonly List<GridThumbnail> _thumbnails = new();
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

        _thumbnails.Clear();
        _registeredGrids.Clear();

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

        // BoardManager 자동 탐색 (이미 내장된 Puzzle 프리팹에서)
        if (boardManager == null)
        {
            boardManager = GetComponentInChildren<BoardManager>(true);
        }

        // 그래도 없으면 Panel_Grid에 Puzzle.prefab 동적 로드
        if (boardManager == null)
        {
            SpawnPuzzleAndInitAsync().Forget();
            return;
        }

        // Puzzle 루트 RectTransform 보정 (Canvas 제거 후 scale 0 방지)
        EnsurePuzzleRectTransform();

        // BoardManager.Awake 강제 실행 (Panel_Grid 비활성 시 Awake 미실행 방지)
        EnsureBoardManagerAwake();

        boardManager.OnGridFilled -= HandleGridFilled;
        boardManager.OnGridFilled += HandleGridFilled;

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

        // 썸네일 UI 생성 → 선택 모드로 시작
        BuildThumbnails(sortedIds, blockData);
        boardManager.BackToSelection();
    }

    // ── 썸네일 UI 생성 ──

    private void BuildThumbnails(List<string> sortedIds, BlockDataManager blockData)
    {
        // 기존 썸네일 제거
        foreach (var thumb in _thumbnails)
            if (thumb != null) Destroy(thumb.gameObject);
        _thumbnails.Clear();

        // SelectionRoot 찾기
        var selectionRoot = boardManager.selectionRoot;
        if (selectionRoot == null)
        {
            Debug.LogWarning("[BlockSynergyBridge] SelectionRoot를 찾을 수 없음");
            return;
        }

        var selectionRT = selectionRoot.GetComponent<RectTransform>();

        // SelectionRoot를 부모에 스트레치 (클릭 영역 보장)
        selectionRT.anchorMin = Vector2.zero;
        selectionRT.anchorMax = Vector2.one;
        selectionRT.offsetMin = Vector2.zero;
        selectionRT.offsetMax = Vector2.zero;

        // 기존 레거시 자식 제거 (GridLoader 등)
        for (int i = selectionRT.childCount - 1; i >= 0; i--)
            Destroy(selectionRT.GetChild(i).gameObject);

        // GridLayoutGroup 설정
        SetupGridLayout(selectionRoot);

        // 썸네일 생성
        foreach (var gridId in sortedIds)
        {
            if (!_registeredGrids.TryGetValue(gridId, out var gridData)) continue;

            var meta = blockData.GetGridMeta(gridId);
            string displayName = meta?.grid_name ?? gridId;

            var thumbnail = CreateThumbnail(selectionRT, gridId, gridData, displayName);
            _thumbnails.Add(thumbnail);
        }

        Debug.Log($"[BlockSynergyBridge] 썸네일 {_thumbnails.Count}개 생성 완료");
    }

    private void SetupGridLayout(GameObject root)
    {
        // 기존 LayoutGroup 제거
        var oldVertical = root.GetComponent<VerticalLayoutGroup>();
        if (oldVertical != null) Destroy(oldVertical);
        var oldHorizontal = root.GetComponent<HorizontalLayoutGroup>();
        if (oldHorizontal != null) Destroy(oldHorizontal);
        var oldGrid = root.GetComponent<GridLayoutGroup>();
        if (oldGrid != null) Destroy(oldGrid);

        // GridLayoutGroup 추가 (3열)
        var gridLayout = root.AddComponent<GridLayoutGroup>();
        gridLayout.cellSize = new Vector2(THUMBNAIL_WIDTH, THUMBNAIL_HEIGHT);
        gridLayout.spacing = Vector2.one * THUMBNAIL_SPACING;
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = THUMBNAIL_COLUMNS;
        gridLayout.childAlignment = TextAnchor.UpperCenter;
        gridLayout.padding = new RectOffset(20, 20, 20, 20);

        // ContentSizeFitter 확보
        if (!root.TryGetComponent<ContentSizeFitter>(out var fitter))
            fitter = root.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    }

    private GridThumbnail CreateThumbnail(RectTransform parent, string gridId,
                                           GridAssetData data, string displayName)
    {
        // 루트 GO
        var go = new GameObject($"Thumb_{gridId}", typeof(RectTransform), typeof(Image), typeof(GridThumbnail));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(THUMBNAIL_WIDTH, THUMBNAIL_HEIGHT);

        // 배경
        var bg = go.GetComponent<Image>();
        bg.color = new Color(0.12f, 0.12f, 0.18f, 0.9f);

        // 이름 텍스트 (상단)
        var nameGO = new GameObject("NameText", typeof(RectTransform), typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(go.transform, false);
        var nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0f, 1f);
        nameRT.anchorMax = new Vector2(1f, 1f);
        nameRT.pivot = new Vector2(0.5f, 1f);
        nameRT.sizeDelta = new Vector2(0f, 30f);
        nameRT.anchoredPosition = new Vector2(0f, -4f);

        var nameText = nameGO.GetComponent<TMP_Text>();
        nameText.text = displayName;
        nameText.fontSize = 14f;
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.color = Color.white;
        nameText.enableAutoSizing = false;
        nameText.overflowMode = TextOverflowModes.Ellipsis;
        nameText.raycastTarget = false;
        if (thumbnailFont != null)
            nameText.font = thumbnailFont;

        // 그리드 프리뷰 컨테이너 (하단)
        var containerGO = new GameObject("GridPreview", typeof(RectTransform));
        containerGO.transform.SetParent(go.transform, false);
        var containerRT = containerGO.GetComponent<RectTransform>();
        containerRT.anchorMin = new Vector2(0.1f, 0.05f);
        containerRT.anchorMax = new Vector2(0.9f, 0.8f);
        containerRT.offsetMin = Vector2.zero;
        containerRT.offsetMax = Vector2.zero;

        // 보더 이미지 (선택 피드백용)
        var borderGO = new GameObject("Border", typeof(RectTransform), typeof(Image));
        borderGO.transform.SetParent(go.transform, false);
        borderGO.transform.SetAsFirstSibling();
        var borderRT = borderGO.GetComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = Vector2.zero;
        borderRT.offsetMax = Vector2.zero;
        var borderImg = borderGO.GetComponent<Image>();
        borderImg.color = new Color(0.4f, 0.4f, 0.5f, 0.6f);
        borderImg.raycastTarget = false;
        // 배경보다 뒤로 (outline 효과)
        bg.color = new Color(0.12f, 0.12f, 0.18f, 0.9f);

        // GridThumbnail 컴포넌트 설정
        var thumbnail = go.GetComponent<GridThumbnail>();

        // SerializeField에 접근 불가하므로 리플렉션 대신 public Setup 사용
        // nameText, gridContainer, borderImage는 Setup 내부에서 참조가 필요
        // → GridThumbnail에 Init 메서드 추가 필요

        thumbnail.Init(nameText, containerRT, borderImg);
        thumbnail.Setup(gridId, data, displayName, boardManager);

        return thumbnail;
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

        // ShapeAssetSO를 런타임 생성
        var shapeSO = ScriptableObject.CreateInstance<ShapeAssetSO>();
        shapeSO.shapeName = shapeEntry.shape_name;
        shapeSO.shapeBlockPrefab = boardManager.defaultShapeBlockPrefab;
        shapeSO.cellOffsets = offsets;
        shapeSO.cellSize = shapeEntry.cell_size > 0 ? shapeEntry.cell_size : 90f;

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

    private void EnsureBoardManagerAwake()
    {
        if (boardManager == null) return;

        // Panel_Grid이 비활성이면 BoardManager.Awake가 안 돌았을 수 있음
        // 임시 활성화 → Awake 트리거 → 복원
        if (!boardManager.gameObject.activeInHierarchy)
        {
            var panelGrid = FindPanelGrid();
            if (panelGrid != null)
            {
                bool wasActive = panelGrid.gameObject.activeSelf;
                panelGrid.gameObject.SetActive(true);
                // Awake/Start가 즉시 실행됨
                if (!wasActive)
                    panelGrid.gameObject.SetActive(false);
                Debug.Log("[BlockSynergyBridge] BoardManager Awake 강제 실행 완료");
            }
        }
    }

    private void EnsurePuzzleRectTransform()
    {
        var gameplayRoot = boardManager.transform.parent;
        var puzzleRoot = gameplayRoot?.parent;
        var panelGrid = puzzleRoot?.parent;

        // Panel_Grid, Puzzle, SelectionRoot만 stretch-fill (컨테이너 역할)
        StretchFill(panelGrid);
        StretchFill(puzzleRoot);

        // SelectionRoot만 stretch (GameplayRoot와 자식은 원래 레이아웃 유지)
        if (puzzleRoot != null)
        {
            var selectionRoot = boardManager.selectionRoot?.GetComponent<RectTransform>();
            StretchFill(selectionRoot);
        }
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
