using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

/// <summary>
/// 서버 데이터 ↔ BoardManager ↔ 시너지 효과를 연결하는 브릿지.
/// GameScene에 배치하여 사용.
///
/// 역할:
/// 1. UI_GridPanel.BoardContainer(DDOL 계층 직속 자식)에 Puzzle.prefab을 동적 스폰하여 BoardManager 확보
/// 2. RuneDataManager에서 존 시너지 데이터 → GridAssetData → BoardManager에 등록
/// 3. 아이템 획득 시 shape_id → ShapeData → BoardManager에 Shape 등록
/// 4. BoardManager.OnGridFilled 구독 → 시너지 효과 PlayerRuntimeStats에 적용
/// </summary>
public class MerlinRuneBridge : MonoBehaviour
{
    // ── Constants ──
    // 그리드 squareGap 과 Shape cellSize 를 동일 값으로 유지해 크기를 일치시킴
    private const float GRID_CELL_SIZE = 54f;   // CELL_SIZE(50) + CELL_GAP(4) = MerlinRuneHexGridView.CELL_STEP

    // ── Static ──
    public static MerlinRuneBridge Instance { get; private set; }

    // 그리드 완성 시 UI_GridPanel에 시각 피드백 전달
    public event System.Action<string> OnSynergyActivated;
    public event System.Action         OnCenterBonusActivated;
    /// <summary>블록 추가/제거 시마다 발생. CharacterInfoPanelView가 활성효과를 갱신하는 데 사용.</summary>
    public event System.Action         OnSynergiesUpdated;

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

    // 임계값·CENTER 체크
    private readonly Dictionary<string, HashSet<int>> _appliedThresholds = new();
    private readonly Dictionary<string, int>           _lastClusterSizes  = new();
    private readonly List<int>                         _downgradeScratch  = new();   // 하강 해제 임계값(루프 중 수정 방지)
    private bool _centerBonusActive;

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
    /// RuneDataManager에서 모든 Grid 데이터를 읽어 BoardManager에 등록한다.
    /// GameRunBootstrapper 초기화 이후에 호출.
    /// </summary>
    public void InitializeGridsFromServer()
    {
        if (_initialized) return;

        var blockData = Managers.RuneData;
        if (blockData == null || !blockData.IsInitialized)
        {
            Debug.LogWarning("[MerlinRuneBridge] BlockData 미초기화");
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
            Debug.LogWarning("[MerlinRuneBridge] puzzlePrefab이 할당되지 않음");
            return;
        }

        // UI_GridPanel.boardContainer(DDOL 계층, UI_GridPanel 직속 자식)에 직접 스폰
        var container = UI_GridPanel.Instance?.BoardContainer;
        if (container == null)
        {
            Debug.LogWarning("[MerlinRuneBridge] UI_GridPanel.BoardContainer를 찾을 수 없음");
            return;
        }

        // UI_GridPanel이 비활성이면 일시 활성화 (Awake/Start 보장)
        var uiPanel = UI_GridPanel.Instance.gameObject;
        bool wasActive = uiPanel.activeSelf;
        if (!wasActive) uiPanel.SetActive(true);

        _puzzleInstance = Instantiate(puzzlePrefab, container);

        boardManager = _puzzleInstance.GetComponentInChildren<BoardManager>(true);
        if (boardManager == null)
        {
            Debug.LogError("[MerlinRuneBridge] Puzzle 프리팹에 BoardManager 없음");
            if (!wasActive) uiPanel.SetActive(false);
            return;
        }

        // BoardContainer → UI_GridPanel 루트로 이동: 전체화면 기준 앵커로 Shape 패널 배치 가능
        _puzzleInstance.transform.SetParent(UI_GridPanel.Instance.transform, false);
        _puzzleInstance.transform.SetAsLastSibling();

        var rt = _puzzleInstance.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        // 1프레임 대기 (BoardManager.Awake/Start 실행 보장)
        await UniTask.Yield();

        boardManager.OnGridFilled += HandleGridFilled;
        boardManager.OnGridSessionActivated -= HandleGridSessionActivated;
        boardManager.OnGridSessionActivated += HandleGridSessionActivated;

        var blockData = Managers.RuneData;
        if (blockData != null && blockData.IsInitialized)
            RegisterAllGrids(blockData);

        if (!wasActive) uiPanel.SetActive(false);

        Debug.Log("[MerlinRuneBridge] Puzzle UI 생성 완료 (UI_GridPanel.BoardContainer)");
    }

    private void RegisterAllGrids(RuneDataManager blockData)
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

            Debug.Log($"[MerlinRuneBridge] Zone 등록: {gridId} ({meta?.grid_name})");
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
                gameplayRT.offsetMin = Vector2.zero;
                gameplayRT.offsetMax = Vector2.zero;
                gameplayRT.pivot = new Vector2(0.5f, 0.5f);
            }

            // ShapeScrollView: 헥사 셀 오른쪽 빈 공간(62%)부터 시작, ItemInfo(하단 30%) 미침범
            // x: 62%~87% — 헥사 그리드 오른쪽 끝(~64%)과 우측 패널 경계(75%) 사이 포함
            // y: 31%~96% — ItemInfo 상단(30%)에서 멈춤
            var shapeSSV = boardManager.gameplayRoot.GetComponentInChildren<ShapeScrollView>(true);
            if (shapeSSV != null)
            {
                var ssvRT = shapeSSV.transform as RectTransform;
                if (ssvRT != null)
                {
                    ssvRT.anchorMin = new Vector2(0.75f, 0.60f);
                    ssvRT.anchorMax = new Vector2(1.00f, 0.944f); // 헤더(60px) 침범 방지
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
                boardManager.shapeHost.anchorMin = new Vector2(0.75f, 0.60f);
                boardManager.shapeHost.anchorMax = new Vector2(1.00f, 0.944f);
                boardManager.shapeHost.offsetMin = Vector2.zero;
                boardManager.shapeHost.offsetMax = Vector2.zero;
                boardManager.shapeHost.pivot = new Vector2(0.5f, 1f);
            }

            // spawnOrigin: X=0(중앙), Y=120(탭스트립 + 여백 확보)
            boardManager.spawnOrigin = new Vector2(0f, 120f);

            // 셰이프 패널 배경 (스타일 패널)
            EnsureStyledShapePanel(boardManager.gameplayRoot);
        }

    }

    private void HandleGridSessionActivated(Grid gridInstance) { }

    /// <summary>
    /// ShapeHost 우측 영역(63%~94%)에 디자인된 모양 스테이징 패널을 생성한다.
    /// 어두운 배경 + 상단 파란 액센트 선 + 안내 레이블로 구성.
    /// </summary>
    private static void EnsureStyledShapePanel(GameObject root)
    {
        if (root == null) return;
        const string NAME = "ShapeAreaBG";
        if (root.transform.Find(NAME) != null) return;

        // 메인 배경
        var go = new GameObject(NAME, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(root.transform, false);
        go.transform.SetAsFirstSibling();

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.75f, 0.60f);
        rt.anchorMax = new Vector2(1.00f, 0.944f); // 헤더 침범 방지
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot     = new Vector2(0.5f, 0.5f);

        var bg = go.GetComponent<Image>();
        bg.color         = new Color(0.10f, 0.12f, 0.18f, 0.88f);
        bg.raycastTarget = false;

        var le = go.AddComponent<LayoutElement>();
        le.ignoreLayout = true;

        // 상단 파란 액센트 선
        var accentGO = new GameObject("Accent", typeof(RectTransform), typeof(Image));
        accentGO.transform.SetParent(go.transform, false);
        var accentRT = accentGO.GetComponent<RectTransform>();
        accentRT.anchorMin = new Vector2(0f, 1f);
        accentRT.anchorMax = Vector2.one;
        accentRT.offsetMin = Vector2.zero;
        accentRT.offsetMax = new Vector2(0f, -3f);
        accentRT.pivot     = new Vector2(0.5f, 1f);
        var accentImg = accentGO.GetComponent<Image>();
        accentImg.color         = new Color(0.3f, 0.6f, 1.0f, 0.8f);
        accentImg.raycastTarget = false;

        // 안내 레이블
        var labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGO.transform.SetParent(go.transform, false);
        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0f, 0.88f);
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = new Vector2(6f, 0f);
        labelRT.offsetMax = Vector2.zero;
        labelRT.pivot     = new Vector2(0.5f, 1f);
        var label = labelGO.GetComponent<TextMeshProUGUI>();
        label.text      = "드래그하여 배치";
        label.fontSize  = 10f;
        label.color     = new Color(0.55f, 0.65f, 0.85f, 0.9f);
        label.alignment = TextAlignmentOptions.TopLeft;
        label.raycastTarget = false;
    }

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
        var shapeSO = BuildShapeSO(shapeId);
        if (shapeSO == null) return;

        // 공용 풀에 직접 추가 (활성 그리드 없어도 누적됨)
        boardManager.SpawnSharedShape(shapeSO);
        Debug.Log($"[MerlinRuneBridge] Shape 추가(공용풀): {shapeSO.shapeName} (id={shapeId})");
    }

    /// <summary>shape_id로 런타임 ShapeAssetSO를 생성한다(없으면 null).</summary>
    private ShapeAssetSO BuildShapeSO(int shapeId)
    {
        var blockData = Managers.RuneData;
        if (blockData == null || boardManager == null) return null;

        var shapeEntry = blockData.GetShape(shapeId);
        if (shapeEntry == null)
        {
            Debug.LogWarning($"[MerlinRuneBridge] Shape 없음: {shapeId}");
            return null;
        }

        var shapeSO = ScriptableObject.CreateInstance<ShapeAssetSO>();
        shapeSO.shapeName        = shapeEntry.shape_name;
        shapeSO.shapeBlockPrefab = boardManager.defaultShapeBlockPrefab;
        shapeSO.cellOffsets      = RuneDataManager.ParseCellOffsets(shapeEntry);
        shapeSO.cellSize         = shapeEntry.cell_size > 0 ? shapeEntry.cell_size : GRID_CELL_SIZE;
        return shapeSO;
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
            Debug.LogWarning($"[MerlinRuneBridge] grid_id 매핑 실패: {filledAsset.name}");
            return;
        }

        ApplySynergyEffects(gridId);
    }

    private void ApplySynergyEffects(string gridId)
    {
        // 이미 적용된 그리드는 중복 적용하지 않음
        if (_appliedGridIds.Contains(gridId)) return;

        var blockData = Managers.RuneData;
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

            RFLog.D($"[MerlinRuneBridge] 시너지 발동: {gridId} → {entry.effect_type} ({entry.trigger}) +{entry.value}");
        }

        var desc = BuildSynergyDescription(entries);
        if (!string.IsNullOrEmpty(desc))
            OnSynergyActivated?.Invoke(desc);
    }

    private static string BuildSynergyDescription(System.Collections.Generic.IEnumerable<RuneSynergyEntry> entries)
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

    // ── 세이브/이어하기 (룬 보드 점유 셀) ──

    /// <summary>현재 룬 보드 점유 셀 스냅샷. 세이브 시 호출.</summary>
    public IReadOnlyList<Vector2Int> CaptureRuneCells()
    {
        var view = Object.FindFirstObjectByType<MerlinRuneHexGridView>(FindObjectsInactive.Include);
        return view != null ? view.GetOccupiedCells() : null;
    }

    /// <summary>이어하기: 저장된 점유 셀을 룬 보드에 재주입해 빌드(시너지)를 복원한다.</summary>
    public void RestoreRuneCells(IReadOnlyList<Vector2Int> cells)
    {
        if (cells == null || cells.Count == 0) return;
        var view = Object.FindFirstObjectByType<MerlinRuneHexGridView>(FindObjectsInactive.Include);
        if (view == null)
        {
            Debug.LogWarning("[MerlinRuneBridge] RestoreRuneCells: HexGridView 없음 — 룬 보드 복원 생략");
            return;
        }
        view.RestoreOccupiedCells(cells);
    }

    /// <summary>현재 배치된 Shape 스냅샷(재구성용). 점유 셀(CaptureRuneCells)과 별개.</summary>
    public IReadOnlyList<RunePlacementEntry> CaptureRunePlacements()
        => boardManager != null ? boardManager.CapturePlacements() : null;

    /// <summary>
    /// 이어하기: 저장된 Shape 배치를 재구성해 재집기/재편집 가능 상태로 복원한다.
    /// 시너지는 별도로 점유 재계산(RestoreRuneCells)이 권위 — 이 호출은 시각/상호작용 레이어.
    /// </summary>
    public void RestoreRunePlacements(IReadOnlyList<RunePlacementEntry> placements)
    {
        if (placements == null || placements.Count == 0 || boardManager == null) return;

        var view = Object.FindFirstObjectByType<MerlinRuneHexGridView>(FindObjectsInactive.Include);
        var grid = view != null ? view.HexGrid : null;
        var squares = grid != null ? grid.GetGridSquares() : null;
        if (squares == null)
        {
            Debug.LogWarning("[MerlinRuneBridge] RestoreRunePlacements: HexGrid 없음 — Shape 재구성 생략");
            return;
        }

        var inv = AppBootstrapper.Instance?.CurrentRun?.ItemInventory;

        foreach (var p in placements)
        {
            if (p?.cells == null || p.cells.Count == 0) continue;

            var targets = new List<GridSquare>();
            foreach (var c in p.cells)
            {
                var sq = squares.Find(s => s != null && s.col == c.x && s.row == c.y);
                if (sq != null) targets.Add(sq);
            }
            if (targets.Count == 0) continue;

            // 인벤토리 placed 아이템에 재바인딩 (재집기 시 인벤토리 동기화)
            RuntimeItemData item = null;
            if (inv != null && !string.IsNullOrEmpty(p.instanceId))
                foreach (var it in inv.PlacedItems)
                    if (it != null && it.instanceId == p.instanceId) { item = it; break; }

            var shapeSO = BuildShapeSO(p.shapeId);
            if (shapeSO == null) continue;

            boardManager.RestorePlacedShape(shapeSO, item, targets);
        }
    }

    /// <summary>런 종료 시 적용 이력 초기화. 외부에서 호출.</summary>
    public void ClearAppliedGrids()
    {
        _appliedGridIds.Clear();
        _appliedThresholds.Clear();
        _lastClusterSizes.Clear();
        _centerBonusActive = false;
    }

    // ── 임계값 기반 시너지 체크 ──────────────────────────────────

    /// <summary>
    /// MerlinRuneHexGridView.RefreshPlacedCells 이후 호출.
    /// 존별 점유 수를 받아 임계값 달성 여부를 확인하고 시너지를 적용한다.
    /// </summary>
    public void OnZoneCellsUpdated(Dictionary<string, int> zoneCounts,
                                    Dictionary<string, int> clusterSizes)
    {
        // 단계 판정 = 속성별 점유 셀 "개수"(연결성 미고려). threshold는 데이터 구동(점유 셀 수).
        // clusterSizes(연결 클러스터)는 더 이상 판정에 쓰지 않으나, 그리드 뷰 시그니처 호환을 위해 인자만 유지.
        zoneCounts ??= new Dictionary<string, int>();

        RFLog.D($"[GridChk] 시너지 갱신 수신 (frame {Time.frameCount}) zones={zoneCounts.Count}");

        // 전체 갱신: 제거된 존이 이전 값을 유지하지 않도록 먼저 초기화 (표시·판정 모두 점유 수 기준)
        _lastClusterSizes.Clear();
        foreach (var kvp in zoneCounts)
            _lastClusterSizes[kvp.Key] = kvp.Value;

        CheckAndApplyThresholds(zoneCounts);
        CheckCenterBonus(zoneCounts);
        OnSynergiesUpdated?.Invoke();
    }

    /// <summary>현재 존별 점유 셀 수(단계 판정·표시 공용). MerlinRuneSynergyStatusView에서 읽는다.</summary>
    public IReadOnlyDictionary<string, int> GetLastClusterSizes() => _lastClusterSizes;

    private void CheckAndApplyThresholds(Dictionary<string, int> zoneCounts)
    {
        var blockData = Managers.RuneData;
        if (blockData == null) return;

        DowngradeBelowThreshold(blockData, zoneCounts);

        foreach (var kvp in zoneCounts)
        {
            string zoneId       = kvp.Key;
            int    occupiedCount = kvp.Value;

            if (zoneId == "CENTER") continue;

            var entries = blockData.GetZoneSynergies(zoneId);
            if (entries == null) continue;

            if (!_appliedThresholds.TryGetValue(zoneId, out var applied))
            {
                applied = new HashSet<int>();
                _appliedThresholds[zoneId] = applied;
            }

            foreach (var entry in entries)
            {
                if (occupiedCount < entry.threshold) continue;
                if (applied.Contains(entry.threshold)) continue;

                applied.Add(entry.threshold);
                ApplyMechanicEffect(zoneId, entry);

                RFLog.D($"[MerlinRuneBridge] 점유 임계값 달성: {zoneId} 점유={occupiedCount} >= {entry.threshold} → {entry.effect_type}");

                var run = AppBootstrapper.Instance?.CurrentRun;
                run?.RecordSynergy(new SynergyRecord
                {
                    gridId     = zoneId,
                    effectType = entry.effect_type,
                    trigger    = entry.trigger,
                    value      = entry.value,
                    value2     = entry.value2,
                    maxStack   = entry.max_stack,
                    duration   = entry.duration,
                });

                OnSynergyActivated?.Invoke($"{zoneId}: {entry.effect_type}");
            }
        }
    }

    /// <summary>
    /// 런 중 룬 재배치로 점유가 줄어든 존의 단계를 해제한다(상승만 적용하던 기존 로직의 역방향).
    /// 점유 0이 된 존은 zoneCounts에서 사라질 수 있으므로 이전 적용분(_appliedThresholds) 기준으로 순회한다.
    /// 속성 효과는 디스패처에서 해제하고, _appliedThresholds에서 제거해 재상승 시 다시 적용되도록 한다.
    /// </summary>
    private void DowngradeBelowThreshold(RuneDataManager blockData, Dictionary<string, int> zoneCounts)
    {
        var player = AppBootstrapper.Instance?.CurrentRun?.Player;

        foreach (var kv in _appliedThresholds)
        {
            string zoneId  = kv.Key;
            var    applied = kv.Value;
            if (applied.Count == 0) continue;

            zoneCounts.TryGetValue(zoneId, out int occupiedCount);   // 없으면 0

            _downgradeScratch.Clear();
            foreach (int thr in applied)
                if (occupiedCount < thr) _downgradeScratch.Add(thr);
            if (_downgradeScratch.Count == 0) continue;

            var entries = blockData.GetZoneSynergies(zoneId);
            for (int i = 0; i < _downgradeScratch.Count; i++)
            {
                int thr = _downgradeScratch[i];
                applied.Remove(thr);

                var entry = FindEntryByThreshold(entries, thr);
                if (entry == null) continue;

                player?.RuneEffects?.Deactivate(entry.effect_type);
                RFLog.D($"[MerlinRuneBridge] 점유 하락 단계 해제: {zoneId} 점유={occupiedCount} < {thr} → {entry.effect_type}");
            }
        }
    }

    private static RuneSynergyEntry FindEntryByThreshold(IReadOnlyList<RuneSynergyEntry> entries, int threshold)
    {
        if (entries == null) return null;
        for (int i = 0; i < entries.Count; i++)
            if (entries[i] != null && entries[i].threshold == threshold) return entries[i];
        return null;
    }

    private void CheckCenterBonus(Dictionary<string, int> zoneCounts)
    {
        zoneCounts.TryGetValue("CENTER", out int centerCount);
        bool shouldActivate = centerCount >= 2;
        if (shouldActivate == _centerBonusActive) return;   // 상태 무변동 — 스킵

        _centerBonusActive = shouldActivate;

        var player = AppBootstrapper.Instance?.CurrentRun?.Player;
        if (player != null)
            player.RuntimeStats.SynergyMechanics.CenterBonusEnabled = shouldActivate;   // 하강 시에도 +25% 해제

        // 구독자(UI 푸터)는 IsCenterBonusActive 상태를 다시 읽어 갱신하므로 양방향 전이 모두 통지한다.
        OnCenterBonusActivated?.Invoke();
        Debug.Log($"[MerlinRuneBridge] CENTER 보너스 {(shouldActivate ? "활성화: 활성 듀오 시너지 +25%" : "해제")}");
    }

    private void ApplyMechanicEffect(string zoneId, RuneSynergyEntry entry)
    {
        var player = AppBootstrapper.Instance?.CurrentRun?.Player;
        if (player == null) return;

        player.RuntimeStats.ApplySynergyMechanicEffect(entry);

        // 속성 단계 효과 스켈레톤 연결: 단계 도달 시 효과 핸들러 활성화 (본문은 단계적 구현)
        player.RuneEffects.Activate(entry);
    }

    /// <summary>등록된 GridAssetData 전체를 반환. GridGalleryView에서 참조.</summary>
    public IReadOnlyDictionary<string, GridAssetData> GetRegisteredGrids()
        => _registeredGrids;

    /// <summary>CENTER 보너스 활성 여부. UI_GridPanel Footer에서 표시.</summary>
    public bool IsCenterBonusActive => _centerBonusActive;

    /// <summary>Puzzle 인스턴스를 UI_GridPanel 루트의 최상단 자식으로 올린다. GridPanel 열릴 때 호출.</summary>
    public void EnsureOnTop()
    {
        if (_puzzleInstance != null)
            _puzzleInstance.transform.SetAsLastSibling();
    }


    // ── 변환 유틸 ──

    private GridAssetData ConvertToGridAssetData(string gridId, RuneSynergyEntry meta)
    {
        // 존맵에서 해당 zone의 셀 위치를 추출하여 GridPatternData 생성
        var positions = Managers.RuneData?.GetZoneCellPositions(gridId)
                        ?? new System.Collections.Generic.List<UnityEngine.Vector2Int>();
        var (rows01, rowCount, colCount) = RuneDataManager.BuildZonePattern(positions);

        _gridIdBySOName[gridId] = gridId;

        return new GridAssetData
        {
            id = gridId,
            displayName = meta?.grid_name ?? gridId,
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
