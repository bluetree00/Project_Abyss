using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

/// <summary>
/// 서버 데이터 ↔ BoardManager ↔ 시너지 효과를 연결하는 브릿지.
/// GameScene에 배치하여 사용.
///
/// ⚠️ 파일명(BlockSynergyBridge.cs)과 클래스명(MerlinRuneBridge)이 다르다 — 검색 시 주의.
///
/// 역할:
/// 1. UI_GridPanel.BoardContainer(DDOL 계층 직속 자식)에 Puzzle.prefab을 동적 스폰하여 BoardManager 확보
/// 2. RuneDataManager에서 존 시너지 데이터 → GridAssetData → BoardManager에 등록
/// 3. 룬판 점유 변화(OnZoneCellsUpdated) → 속성별 <b>점유 셀 개수</b>로 단계 판정 → RuneEffects 적용/해제
///    (연결성/클러스터는 판정에 쓰지 않는다)
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
    /// <summary>시너지 단계 신규 달성 시 zoneId 전달 — 판(뷰)이 해당 속성 존을 터뜨리는 연출용.</summary>
    public event System.Action<string> OnZoneSynergyBurst;
    public event System.Action         OnCenterBonusActivated;
    /// <summary>블록 추가/제거 시마다 발생. CharacterInfoPanelView가 활성효과를 갱신하는 데 사용.</summary>
    public event System.Action         OnSynergiesUpdated;

    // ── SerializeField ──
    [Header("Puzzle 프리팹 (직접 참조)")]
    [SerializeField] private GameObject puzzlePrefab;

    [Header("수동 참조 (없으면 UI_GridPanel.BoardContainer에 동적 스폰)")]
    [SerializeField] private BoardManager boardManager;

    // ── Private ──
    private readonly Dictionary<string, GridAssetData> _registeredGrids = new();
    private GameObject _puzzleInstance;
    private bool _initialized;

    // 임계값·CENTER 체크
    private readonly Dictionary<string, HashSet<int>> _appliedThresholds = new();
    private readonly Dictionary<string, int>           _zoneOccupiedCounts  = new();
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

            // 셰이프 패널 배경(ShapeAreaBG)은 더 이상 만들지 않는다.
            // 드래그 스테이징이 배치 화면의 '아이템 목록'으로 대체되면서, 이 장식 패널은
            // 같은 자리(우측 0.75~1.00)에 겹쳐 뜨는 레거시 껍데기만 남았다.
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

    // ── 배치 가능 조회 (선택 팝업용) ──

    /// <summary>
    /// 해당 모양을 지금 판에 놓을 자리가 있는지. <b>읽기 전용</b>이며 배치 화면이 닫혀 있어도 동작한다.
    /// 룬 선택 팝업이 후보마다 "배치 가능/자리 없음"을 표시하는 데 쓴다.
    /// </summary>
    public bool CanPlaceShape(IReadOnlyList<Vector2Int> cellOffsets, string elementId = null, bool isLegendary = false)
    {
        if (cellOffsets == null || cellOffsets.Count == 0) return false;

        var view = Object.FindFirstObjectByType<MerlinRuneHexGridView>(FindObjectsInactive.Include);
        if (view == null)
        {
            // 보드가 아직 구성 전이면 판정 불가 — 막지 말고 통과시킨다(선택 자체는 허용).
            Debug.LogWarning("[MerlinRuneBridge] CanPlaceShape: HexGridView 없음 — 배치 가능으로 간주");
            return true;
        }
        return view.CanPlaceAnywhere(cellOffsets, elementId, isLegendary);
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
        if (placements == null || placements.Count == 0) return;
        if (boardManager == null)
        {
            Debug.LogWarning("[MerlinRuneBridge] RestoreRunePlacements: BoardManager 없음 — Shape 재구성 생략");
            return;
        }

        var view = Object.FindFirstObjectByType<MerlinRuneHexGridView>(FindObjectsInactive.Include);

        // 이어하기는 배치 화면을 한 번도 열지 않은 상태에서 불린다 — 그때는 GridSquare가 아직
        // 없어(BuildGrid는 패널 오픈 시점 호출) 여기서 그냥 돌아갔고, 저장된 룬이 판에 하나도
        // 그려지지 않았다. 판을 먼저 세운다(이미 세워져 있으면 무동작).
        if (view != null && view.HexGrid == null) view.BuildGrid();

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

    /// <summary>
    /// 런 하드리셋 — 판에 놓인 룬까지 <b>전부</b> 버린다.
    ///
    /// 이 브릿지는 DDOL(@UIRoot)에 살아 씬 전환·런 종료로 죽지 않는다. <see cref="ResetSynergyState"/>는
    /// 효과 적용 이력만 지우므로, 그것만 호출하면 <b>판 위의 룬이 다음 런까지 남아</b>
    /// 새 런의 빈 인벤토리와 어긋난다(보유 목록엔 없는 룬이 판에 놓여 있음).
    ///
    /// 부분 철거(배치 하나씩 제거)는 GridSquare 점유·공용 Shape 풀·슬롯 리플로우가 얽혀 누락되기 쉬우므로,
    /// <b>퍼즐 인스턴스를 통째로 파괴하고 다음 InitializeGridsFromServer에서 새로 짓는다.</b>
    /// 판은 런 상태의 뷰일 뿐이라 밑바닥부터 다시 짓는 것이 곧 하드리셋의 정의다.
    /// </summary>
    public void ClearBoard()
    {
        ResetSynergyState();

        // 판(헥사 뷰)은 UI_GridPanel(DDOL) 소속이라 퍼즐 인스턴스와 함께 죽지 않는다 —
        // 칸 점유를 여기서 직접 비우지 않으면 지난 런의 배치가 다음 런 판에 그대로 남는다.
        Object.FindFirstObjectByType<MerlinRuneHexGridView>(FindObjectsInactive.Include)?
            .ResetBoardOccupancy();

        if (boardManager != null)
            boardManager.OnGridSessionActivated -= HandleGridSessionActivated;

        if (_puzzleInstance != null)
            Destroy(_puzzleInstance);

        _puzzleInstance = null;
        boardManager    = null;
        _initialized    = false;
        _registeredGrids.Clear();
    }

    /// <summary>런 종료·이어하기 시 시너지 적용 상태(단계 가드·점유 수·중앙보너스) 초기화. 외부에서 호출.</summary>
    public void ResetSynergyState()
    {
        _appliedThresholds.Clear();
        _zoneOccupiedCounts.Clear();
        _centerBonusActive = false;
        _activeReactions.Clear();
        // 반응 스탯도 0으로 되돌린다(플레이어가 살아 있으면).
        AppBootstrapper.Instance?.CurrentRun?.Player?.RuntimeStats?
            .SetReactionBonuses(0f, 0f, 0f, 0f, 0f, 0f);
    }

    /// <summary>
    /// 현재 발동 중인 시너지 단계 수(세이브 슬롯 카드 표시용).
    /// 과거엔 GameRunSession._appliedSynergies.Count를 썼는데, 그건 <b>하강 시 줄지 않아</b>
    /// 누적 이력이었다(= 틀린 값). 여기 _appliedThresholds는 상승·하강 양방향으로 관리되는 실제 상태다.
    /// </summary>
    public int ActiveSynergyCount
    {
        get
        {
            int n = 0;
            foreach (var kv in _appliedThresholds) n += kv.Value.Count;
            return n;
        }
    }

    // ── 임계값 기반 시너지 체크 ──────────────────────────────────

    /// <summary>
    /// MerlinRuneHexGridView.RefreshPlacedCells 이후 호출.
    /// 존별 점유 수를 받아 임계값 달성 여부를 확인하고 시너지를 적용한다.
    /// </summary>
    public void OnZoneCellsUpdated(Dictionary<string, int> zoneCounts)
    {
        // 단계 판정 = 속성별 점유 셀 "개수"(연결성 미고려). threshold는 데이터 구동(점유 셀 수).
        zoneCounts ??= new Dictionary<string, int>();

        RFLog.D($"[GridChk] 시너지 갱신 수신 (frame {Time.frameCount}) zones={zoneCounts.Count}");

        // 전체 갱신: 제거된 존이 이전 값을 유지하지 않도록 먼저 초기화 (표시·판정 모두 점유 수 기준)
        _zoneOccupiedCounts.Clear();
        foreach (var kvp in zoneCounts)
            _zoneOccupiedCounts[kvp.Key] = kvp.Value;

        CheckAndApplyThresholds(zoneCounts);
        CheckCenterBonus(zoneCounts);
        RefreshReactions();
        OnSynergiesUpdated?.Invoke();
    }

    /// <summary>현재 존별 점유 셀 수(단계 판정·표시 공용). MerlinRuneSynergyStatusView에서 읽는다.</summary>
    public IReadOnlyDictionary<string, int> GetZoneOccupiedCounts() => _zoneOccupiedCounts;

    // ── 속성 반응 (인접 두 존 동시 활성) ──────────────────────────────

    /// <summary>존 zoneId의 현재 달성 단계 수(0~4). _appliedThresholds가 상승·하락 반영.</summary>
    public int GetZoneTier(string zoneId)
        => _appliedThresholds.TryGetValue(zoneId, out var set) ? set.Count : 0;

    private readonly List<RuneReactionDef.Def> _activeReactions = new();
    /// <summary>현재 발동 중인 반응 목록(UI 표시용). min 단계 강도는 GetZoneTier로 재계산 가능.</summary>
    public IReadOnlyList<RuneReactionDef.Def> ActiveReactions => _activeReactions;

    /// <summary>
    /// 활성 반응을 재평가해 플레이어 스탯에 합산한다.
    /// 발동 조건: 두 인접 존이 각각 1단계 이상. 강도 = min(두 단계) × ValuePerTier.
    /// 6쌍이 각기 다른 스탯을 주므로, 스탯별로 합산해 한 번에 SetReactionBonuses로 밀어넣는다.
    /// </summary>
    private void RefreshReactions()
    {
        _activeReactions.Clear();
        float crit = 0f, critDmg = 0f, atkSpd = 0f, dmgPct = 0f, skillCdr = 0f, dr = 0f;

        foreach (var def in RuneReactionDef.All)
        {
            int tierA = GetZoneTier(def.ZoneA);
            int tierB = GetZoneTier(def.ZoneB);
            if (tierA < 1 || tierB < 1) continue;   // 둘 다 1단계 이상이어야 발동

            _activeReactions.Add(def);
            float amount = Mathf.Min(tierA, tierB) * def.ValuePerTier;

            switch (def.Stat)
            {
                case RuneReactionDef.StatKind.CritChance:      crit     += amount; break;
                case RuneReactionDef.StatKind.CritDamage:      critDmg  += amount; break;
                case RuneReactionDef.StatKind.AttackSpeed:     atkSpd   += amount; break;
                case RuneReactionDef.StatKind.DamagePercent:   dmgPct   += amount; break;
                case RuneReactionDef.StatKind.SkillCdr:        skillCdr += amount; break;
                case RuneReactionDef.StatKind.DamageReduction: dr       += amount; break;
            }
        }

        var stats = AppBootstrapper.Instance?.CurrentRun?.Player?.RuntimeStats;
        stats?.SetReactionBonuses(crit, critDmg, atkSpd, dmgPct, skillCdr, dr);
    }

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

                // 구 SynergyRecord 원장에 기록하던 코드 제거 —
                // 그 원장은 복원 시 구 '플랫 스탯' 스위치로 흘러가 신 속성 효과가 전부 no-op이 됐다.
                // 시너지의 진실원본은 룬 보드 점유 셀(runeCellsJson)이고, 복원도 그쪽이 담당한다.

                OnSynergyActivated?.Invoke($"{zoneId}: {entry.effect_type}");
                OnZoneSynergyBurst?.Invoke(zoneId);   // 판 연출: 해당 속성 존 터짐
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

    /// <summary>
    /// 중앙(CENTER) 공명 — 자체 효과가 없고, <b>활성 속성 시너지 전부의 효과값을 증폭</b>한다.
    /// 중앙은 6속성 존과 모두 맞닿은 허브이고, 채우는 만큼 속성 존을 못 채우는 순수 기회비용이라
    /// 보상이 "질(효과 강도)"이어야 몰빵 빌드와 대비되는 두 번째 축이 된다.
    ///
    /// 배수는 CENTER 시너지 행(threshold 4/8/12/16 → value 0.10/0.20/0.35/0.50)에서 읽는다 = 데이터 구동.
    /// RuneEffect로 만들지 않는 이유: 증폭 대상에 자기 자신이 포함돼 재귀가 된다.
    /// </summary>
    private void CheckCenterBonus(Dictionary<string, int> zoneCounts)
    {
        zoneCounts.TryGetValue(ElementDef.CenterId, out int centerCount);

        // 달성한 단계 중 가장 높은 것의 value가 보너스율.
        float bonus = 0f;
        var rows = Managers.RuneData?.GetZoneSynergies(ElementDef.CenterId);
        if (rows != null)
            foreach (var e in rows)
                if (e != null && centerCount >= e.threshold && e.value > bonus)
                    bonus = e.value;

        bool active = bonus > 0f;
        var player = AppBootstrapper.Instance?.CurrentRun?.Player;
        player?.RuneEffects?.SetAmplifier(1f + bonus);   // 하강 시에도 그대로 되돌아온다(1 + 0)

        if (active == _centerBonusActive) return;        // 상태 무변동 — 통지 스킵
        _centerBonusActive = active;

        OnCenterBonusActivated?.Invoke();
        Debug.Log($"[MerlinRuneBridge] 중앙 공명 {(active ? $"활성 — 속성 시너지 +{bonus * 100f:F0}% (점유 {centerCount})" : "해제")}");
    }

    /// <summary>단계 도달 → 속성 효과 활성화. (구 ApplySynergyMechanicEffect 호출은 제거 — 신 24종은 그 스위치에 없어 항상 no-op이었다)</summary>
    private void ApplyMechanicEffect(string zoneId, RuneSynergyEntry entry)
    {
        var player = AppBootstrapper.Instance?.CurrentRun?.Player;
        if (player == null) return;

        player.RuneEffects.Activate(entry);
    }

    /// <summary>
    /// [정제소 존핵] 룬판이 계산한 존별 증폭 배수를 디스패처에 전달한다.
    /// 매칭 존에 놓인 존핵만 그 존 시너지 효과를 강화한다(중앙 공명과 곱연산).
    /// </summary>
    public void OnZoneAmplifiersUpdated(IReadOnlyDictionary<string, float> zoneAmps)
    {
        var player = AppBootstrapper.Instance?.CurrentRun?.Player;
        player?.RuneEffects?.SetZoneAmplifiers(zoneAmps);
    }

    /// <summary>등록된 GridAssetData 전체를 반환.</summary>
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
