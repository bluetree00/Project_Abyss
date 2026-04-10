using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 퍼즐 시스템의 중심 매니저.
///
/// [SO 방식]  Inspector에 GridAssetSO 할당 → EnterGrid(SO) 호출
/// [데이터 방식] GridAssetData 코드 생성  → SetRuntimeData(data) or EnterGrid(data) 호출
///
/// Shape는 Grid와 무관하게 공용 풀로 관리된다.
/// 런타임에 S키를 누르면 어드레서블 Shape 그룹에서 랜덤으로 하나를 생성한다.
/// </summary>
public class BoardManager : MonoBehaviour
{
    public static BoardManager Instance { get; private set; }

    [Header("Config")]
    public BoardConfigSO boardConfig;

    [Header("UI Roots")]
    [Tooltip("선택 화면 루트 (버튼/이미지가 여기에 있음).")]
    public GameObject selectionRoot;
    [Tooltip("게임플레이 화면 루트 (그리드 호스트 + 셰이프 호스트가 여기에 있음).")]
    public GameObject gameplayRoot;

    [Header("Gameplay Hosts")]
    [Tooltip("선택된 그리드가 생성될 RectTransform.")]
    public RectTransform gridHost;
    [Tooltip("셰이프가 생성될 RectTransform.")]
    public RectTransform shapeHost;

    [Header("Prefabs")]
    [Tooltip("Grid 컴포넌트가 루트에 있는 프리팹.")]
    public Grid gridPrefab;
    [Tooltip("Shape 컴포넌트가 루트에 있는 프리팹.")]
    public Shape shapePrefab;

    [Header("Initial Grid (SO 방식)")]
    [Tooltip("Inspector에 SO 직접 할당 시 Start에서 자동 진입.")]
    public GridAssetSO initialGridAsset;

    [Header("Default Prefabs (런타임 SO 생성용)")]
    [Tooltip("데이터 방식 사용 시 gridSquarePrefab 폴백.")]
    public GameObject defaultSquarePrefab;
    [Tooltip("데이터 방식 사용 시 shapeBlockPrefab 폴백.")]
    public GameObject defaultShapeBlockPrefab;

    [Header("Runtime Data (데이터 방식 자동 초기화)")]
    [Tooltip("id가 비어 있지 않으면 Start에서 자동으로 EnterGrid 호출.")]
    public GridAssetData initialRuntimeData;

    [Header("Shape 슬롯")]
    [Tooltip("첫 번째 슬롯의 X 위치 / Y는 shapeHost 상단으로부터의 패딩(px).")]
    public Vector2 spawnOrigin = new Vector2(0f, 80f);
    [Tooltip("슬롯 간격 (Y축).")]
    public float spawnSlotStepY = 220f;

    [Header("Grid Name UI")]
    [Tooltip("현재 그리드 이름을 표시하는 텍스트 (옵션).")]
    public TMPro.TMP_Text gridNameText;

    [Header("Back Button")]
    [Tooltip("선택 화면으로 돌아가는 버튼 (옵션, 없으면 자동 탐색).")]
    [SerializeField] private UnityEngine.UI.Button backButton;

    [Header("Addressables")]
    [Tooltip("어드레서블 Shape SO 그룹 키 (레이블 또는 그룹명).")]
    public string shapeGroupKey = "SO Shape";

    // ── 내부 세션 ─────────────────────────────────────────────────────

    [System.Serializable]
    private class GridSession
    {
        public Grid gridInstance;
        public bool filledEventFired = false;

        // 런타임 생성 SO 추적 (OnDestroy 시 Destroy 대상)
        public GridPatternSO  runtimePatternSO;
        public GridVisualSO   runtimeVisualSO;
        public ShapeAssetSO[] runtimeShapeSOs;
        public bool isRuntimeCreated = false;
    }

    // Shape의 전역 배치 상태 (어느 그리드에 배치됐는지 공유)
    private class GlobalPlacement
    {
        public GridAssetSO    grid;
        public List<GridSquare> squares;
        public Vector2           anchoredPosition;
    }

    // ── Inspector 이벤트 ──────────────────────────────────────────────

    [Serializable]
    public class GridEventEntry
    {
        public GridAssetSO grid;
        public UnityEvent onFilled;
    }

    public List<GridEventEntry> gridEvents = new();

    [Serializable]
    public class GridFilledUnityEvent : UnityEvent<GridAssetSO> { }
    public GridFilledUnityEvent onGridFilledUnity;

    // ── 내부 상태 ─────────────────────────────────────────────────────

    private readonly Dictionary<GridAssetSO, GridSession> sessions = new();
    private GridAssetSO activeAsset;

    /// <summary>현재 활성화된 그리드 SO. 뮤테이터(AddShape, SetPattern 등) 호출에 사용.</summary>
    public GridAssetSO ActiveAsset => activeAsset;

    private readonly Dictionary<string, GridAssetSO> _runtimeSOCache = new();
    private BoardConfigSO    _runtimeBoardConfig;
    private PlacementRulesSO _runtimePlacementRules;

    private readonly Dictionary<GridAssetSO, Action<GridChangeType>> _dataChangeHandlers = new();

    private RectTransform cacheRoot;

    // ── 공용 Shape 풀 ─────────────────────────────────────────────────

    private readonly List<Shape> _sharedShapes = new();
    // 슬롯 Y 위치 (shapeHost 로컬, 음수 = 아래). 실제 높이 기반으로 누적 계산.
    private readonly Dictionary<Shape, float> _sharedShapeSlotY = new();
    private float _slotCursorY = 0f;

    // 전역 배치 상태: 어느 그리드에 배치됐는지 (null이면 슬롯에 있음)
    private readonly Dictionary<Shape, GlobalPlacement> _globalPlacements = new();

    private List<ShapeAssetSO> _cachedShapeSOs;
    private AsyncOperationHandle<IList<ShapeAssetSO>> _shapeLoadHandle;
    private bool _shapeHandleValid;
    private bool _shapeLoadInProgress;

    public event Action<GridAssetSO> OnGridFilled;

    // ── 생명주기 ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // gridNameText 자동 탐색
        if (gridNameText == null)
        {
            var found = GetComponentInParent<Canvas>(true)?.GetComponentsInChildren<TMPro.TMP_Text>(true);
            if (found != null)
                foreach (var t in found)
                    if (t.gameObject.name == "GridNameText") { gridNameText = t; break; }
        }

        // BackButton 자동 탐색 및 연결
        if (backButton == null)
        {
            var canvas = GetComponentInParent<Canvas>(true);
            if (canvas != null)
            {
                var buttons = canvas.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                foreach (var btn in buttons)
                    if (btn.gameObject.name == "BackButton") { backButton = btn; break; }
            }
        }
        if (backButton != null)
            backButton.onClick.AddListener(BackToSelection);

        var go = new GameObject("CacheRoot", typeof(RectTransform));
        cacheRoot = (RectTransform)go.transform;
        cacheRoot.SetParent(transform, false);
        cacheRoot.gameObject.SetActive(true);
    }

    void Start()
    {
        bool startSelection = boardConfig == null || boardConfig.startInSelectionMode;
        SetModeSelection(startSelection);

        if (!startSelection && initialGridAsset != null)
        {
            EnterGrid(initialGridAsset);
            return;
        }

        if (initialRuntimeData != null && !string.IsNullOrEmpty(initialRuntimeData.id))
            EnterGrid(initialRuntimeData);
    }

    // void Update()
    // {
    //     // 레거시 S키 Shape 스폰 (서버 데이터 방식에서는 BlockSynergyBridge가 담당)
    //     if (Input.GetKeyDown(KeyCode.S))
    //         StartCoroutine(SpawnRandomShapeCoroutine());
    // }

    void OnDestroy()
    {
        if (backButton != null)
            backButton.onClick.RemoveListener(BackToSelection);

        foreach (var kvp in _dataChangeHandlers)
        {
            var so = kvp.Key;
            if (so != null) so.OnDataChanged -= kvp.Value;
        }
        _dataChangeHandlers.Clear();

        foreach (var session in sessions.Values)
            if (session.isRuntimeCreated)
                SORuntimeFactory.DestroyRuntimeSOs(
                    null,
                    session.runtimePatternSO,
                    session.runtimeVisualSO,
                    session.runtimeShapeSOs);

        foreach (var so in _runtimeSOCache.Values)
            if (so != null) Destroy(so);

        if (_runtimeBoardConfig    != null) Destroy(_runtimeBoardConfig);
        if (_runtimePlacementRules != null) Destroy(_runtimePlacementRules);

        if (_shapeHandleValid && _shapeLoadHandle.IsValid())
            Addressables.Release(_shapeLoadHandle);
    }

    // ── 공개 API ──────────────────────────────────────────────────────

    /// <summary>
    /// 런타임 데이터로 그리드를 초기화한다. 기존 활성 그리드가 있으면 교체된다.
    /// </summary>
    public void SetRuntimeData(GridAssetData data)
    {
        if (data == null || string.IsNullOrEmpty(data.id)) return;
        initialRuntimeData = data;
        EnterGrid(data);
    }

    /// <summary>SO 방식 그리드 진입. GridSelectButton 등에서 호출.</summary>
    public void EnterGrid(GridAssetSO gridAsset)
    {
        if (gridAsset == null) return;
        if (gridPrefab == null || gridHost == null || shapeHost == null) return;

        SetModeSelection(false);

        if (activeAsset != null)
            DeactivateSession(activeAsset);

        activeAsset = gridAsset;

        if (!sessions.TryGetValue(gridAsset, out var s) || s.gridInstance == null)
            sessions[gridAsset] = CreateSession(gridAsset);

        ActivateSession(gridAsset);
        UpdateGridNameUI(gridAsset.name);
    }

    /// <summary>데이터 방식 그리드 진입. 같은 id 재호출 시 캐시된 세션을 재사용한다.</summary>
    public void EnterGrid(GridAssetData data)
    {
        if (data == null || string.IsNullOrEmpty(data.id)) return;
        if (gridPrefab == null || gridHost == null || shapeHost == null) return;

        SetModeSelection(false);

        if (activeAsset != null)
            DeactivateSession(activeAsset);

        if (!_runtimeSOCache.TryGetValue(data.id, out var gridSO))
        {
            gridSO = SORuntimeFactory.CreateGridAssetSO(
                data, defaultSquarePrefab, defaultShapeBlockPrefab,
                out var patSO, out var visSO, out var shpSOs);
            _runtimeSOCache[data.id] = gridSO;

            var session = CreateSession(gridSO);
            session.runtimePatternSO = patSO;
            session.runtimeVisualSO  = visSO;
            session.runtimeShapeSOs  = shpSOs;
            session.isRuntimeCreated = true;
            sessions[gridSO] = session;
        }
        else if (!sessions.ContainsKey(gridSO) || sessions[gridSO].gridInstance == null)
        {
            var session = CreateSession(gridSO);
            session.isRuntimeCreated = true;
            sessions[gridSO] = session;
        }

        activeAsset = gridSO;
        ActivateSession(gridSO);
        UpdateGridNameUI(data.displayName);
    }

    /// <summary>동일 id의 퍼즐을 새 데이터로 완전 교체한다. 기존 SO/세션을 모두 재생성한다.</summary>
    public void UpdateActiveGrid(GridAssetData newData)
    {
        if (newData == null || string.IsNullOrEmpty(newData.id)) return;

        if (_runtimeSOCache.TryGetValue(newData.id, out var oldSO))
        {
            if (sessions.TryGetValue(oldSO, out var oldSession))
            {
                if (activeAsset == oldSO) activeAsset = null;
                SetGridActive(oldSession, false);
                if (oldSession.gridInstance != null) Destroy(oldSession.gridInstance.gameObject);
                sessions.Remove(oldSO);
                SORuntimeFactory.DestroyRuntimeSOs(
                    oldSO,
                    oldSession.runtimePatternSO,
                    oldSession.runtimeVisualSO,
                    oldSession.runtimeShapeSOs);
            }
            _runtimeSOCache.Remove(newData.id);
        }

        EnterGrid(newData);
    }

    /// <summary>BoardConfig를 런타임에 교체한다.</summary>
    public void ApplyConfig(BoardConfigData configData)
    {
        if (_runtimeBoardConfig != null) Destroy(_runtimeBoardConfig);
        _runtimeBoardConfig = SORuntimeFactory.CreateBoardConfigSO(configData);
        boardConfig = _runtimeBoardConfig;
    }

    /// <summary>PlacementRules를 런타임에 교체한다.</summary>
    public void ApplyPlacementRules(PlacementRulesData rulesData)
    {
        if (_runtimePlacementRules != null) Destroy(_runtimePlacementRules);
        _runtimePlacementRules = SORuntimeFactory.CreatePlacementRulesSO(rulesData);
        if (GridManager.Instance != null)
            GridManager.Instance.placementRules = _runtimePlacementRules;
    }

    /// <summary>선택 화면으로 돌아간다.</summary>
    public void BackToSelection()
    {
        if (activeAsset != null)
            DeactivateSession(activeAsset);

        activeAsset = null;

        if (GridManager.Instance != null)
            GridManager.Instance.SetActiveGrid(null);

        SetModeSelection(true);
    }

    /// <summary>
    /// 지정 그리드 세션을 전체 재빌드한다.
    /// GridAssetSO.SetPattern / SetVisual 호출 시 자동으로 발동된다.
    /// </summary>
    public void RefreshGrid(GridAssetSO asset)
    {
        if (asset == null) return;

        bool wasActive = activeAsset == asset;
        UnsubscribeSOChanges(asset);

        GridPatternSO  savedPat = null;
        GridVisualSO   savedVis = null;
        ShapeAssetSO[] savedShp = null;
        bool wasRuntime = false;

        if (sessions.TryGetValue(asset, out var old))
        {
            SetGridActive(old, false);
            if (old.gridInstance != null) Destroy(old.gridInstance.gameObject);

            // 이 그리드에 배치된 Shape의 전역 배치 정보 초기화 (squares가 무효화됨)
            foreach (var s in _sharedShapes)
            {
                if (_globalPlacements.TryGetValue(s, out var gp) && gp.grid == asset)
                {
                    _globalPlacements.Remove(s);
                    s.SetOccupiedSquares(new List<GridSquare>());
                }
            }

            savedPat   = old.runtimePatternSO;
            savedVis   = old.runtimeVisualSO;
            savedShp   = old.runtimeShapeSOs;
            wasRuntime = old.isRuntimeCreated;
            sessions.Remove(asset);
        }

        if (wasActive) activeAsset = null;
        EnterGrid(asset);

        if (wasRuntime && sessions.TryGetValue(asset, out var newSession))
        {
            newSession.runtimePatternSO = savedPat;
            newSession.runtimeVisualSO  = savedVis;
            newSession.runtimeShapeSOs  = savedShp;
            newSession.isRuntimeCreated = true;
        }
    }

    /// <summary>현재 활성 그리드를 재빌드한다.</summary>
    public void RefreshActiveGrid()
    {
        if (activeAsset != null) RefreshGrid(activeAsset);
    }

    /// <summary>GridManager가 모든 배치 가능 칸이 채워졌음을 알릴 때 호출.</summary>
    public void NotifyGridFilled(GridAssetSO asset)
    {
        if (asset == null) return;
        if (!sessions.TryGetValue(asset, out var session)) return;
        if (session.filledEventFired) return;
        session.filledEventFired = true;

        asset.onAllPlaceableFilled?.Invoke();
        OnGridFilled?.Invoke(asset);
        onGridFilledUnity?.Invoke(asset);

        foreach (var entry in gridEvents)
            if (entry.grid == asset) { entry.onFilled?.Invoke(); break; }
    }

    /// <summary>
    /// Shape가 그리드에 성공적으로 배치된 직후 호출.
    /// 전역 배치 상태를 기록하고 슬롯을 해제한다.
    /// </summary>
    public void OnShapePlaced(Shape shape)
    {
        if (activeAsset == null) return;

        _globalPlacements[shape] = new GlobalPlacement
        {
            grid             = activeAsset,
            squares          = new List<GridSquare>(shape.GetOccupiedSquares()),
            anchoredPosition = ((RectTransform)shape.transform).anchoredPosition,
        };

        if (_sharedShapeSlotY.ContainsKey(shape))
        {
            _sharedShapeSlotY.Remove(shape);
            ReflowSlots();
        }
    }

    /// <summary>
    /// Shape 드래그 시작 시 호출. 전역 배치 상태에서 제거해 다시 슬롯으로 돌아올 수 있게 한다.
    /// </summary>
    public void OnShapePickedUp(Shape shape)
    {
        _globalPlacements.Remove(shape);
    }

    /// <summary>드래그 실패 시 Shape.OnEndDrag에서 호출. 셰이프를 슬롯으로 되돌린다.</summary>
    public void ReSlotAndReturn(Shape shape)
    {
        if (activeAsset == null) return;
        if (!_sharedShapes.Contains(shape)) return;
        PlaceSharedShapeToSlot(shape);
    }

    // ── Shape 스폰 (S키) ──────────────────────────────────────────────

    /// <summary>
    /// 어드레서블 Shape 그룹에서 랜덤으로 하나를 로드해 공용 풀에 추가한다.
    /// S키 입력 시 호출된다.
    /// </summary>
    private IEnumerator SpawnRandomShapeCoroutine()
    {
        if (_shapeLoadInProgress) yield break;

        if (_cachedShapeSOs == null)
        {
            _shapeLoadInProgress = true;
            _shapeLoadHandle = Addressables.LoadAssetsAsync<ShapeAssetSO>(shapeGroupKey, null);
            _shapeHandleValid = true;
            yield return _shapeLoadHandle;
            _shapeLoadInProgress = false;

            if (_shapeLoadHandle.Status == AsyncOperationStatus.Succeeded)
                _cachedShapeSOs = new List<ShapeAssetSO>(_shapeLoadHandle.Result);
            else
            {
                Debug.LogWarning($"[BoardManager] Shape SO 로드 실패: 키={shapeGroupKey}");
                yield break;
            }
        }

        if (_cachedShapeSOs == null || _cachedShapeSOs.Count == 0) yield break;

        var asset = _cachedShapeSOs[UnityEngine.Random.Range(0, _cachedShapeSOs.Count)];
        SpawnSharedShape(asset);
    }

    /// <summary>Shape SO 하나로 공용 shape 인스턴스를 생성하고 풀에 추가한다.</summary>
    /// <summary>Shape SO로 공용 풀에 Shape 인스턴스를 생성한다. 활성 그리드 없어도 동작.</summary>
    public void SpawnSharedShape(ShapeAssetSO asset)
    {
        if (shapePrefab == null || asset == null) return;

        var shape = Instantiate(shapePrefab, cacheRoot);
        shape.ApplyAsset(asset);
        if (shape.transform is RectTransform rt)
        {
            rt.localScale    = Vector3.one * GetGameplayScale();
            rt.localRotation = Quaternion.identity;
        }
        shape.CacheStartTransform();
        _sharedShapes.Add(shape);

        if (activeAsset != null)
            PlaceSharedShapeToSlot(shape);
        else
            shape.gameObject.SetActive(false);
    }

    // ── 내부 구현 ─────────────────────────────────────────────────────

    private void UpdateGridNameUI(string name)
    {
        if (gridNameText != null)
            gridNameText.text = name ?? "";
    }

    private void SetModeSelection(bool selectionMode)
    {
        if (selectionRoot != null) selectionRoot.SetActive(selectionMode);
        if (gameplayRoot  != null) gameplayRoot.SetActive(!selectionMode);
    }

    private float GetGameplayScale() =>
        boardConfig != null ? Mathf.Max(0.0001f, boardConfig.gameplayUniformScale) : 1f;

    private GridSession CreateSession(GridAssetSO asset)
    {
        var session = new GridSession();

        session.gridInstance = Instantiate(gridPrefab, cacheRoot);
        var gridRT = session.gridInstance.transform as RectTransform;
        if (gridRT != null)
        {
            gridRT.anchoredPosition = Vector2.zero;
            gridRT.localScale       = Vector3.one * GetGameplayScale();
            gridRT.localRotation    = Quaternion.identity;
        }
        session.gridInstance.Initialize(asset);

        SetGridActive(session, false);
        return session;
    }

    private void ActivateSession(GridAssetSO asset)
    {
        if (!sessions.TryGetValue(asset, out var session)) return;

        if (gameplayRoot != null) gameplayRoot.SetActive(true);

        session.gridInstance.transform.SetParent(gridHost, false);
        var gridRT = session.gridInstance.transform as RectTransform;
        if (gridRT != null)
        {
            gridRT.anchoredPosition = Vector2.zero;
            gridRT.localScale       = Vector3.one * GetGameplayScale();
        }
        SetGridActive(session, true);

        // 콘텐츠 크기·스크롤 위치 초기화 → PlaceSharedShapeToSlot이 다시 계산
        if (shapeHost != null)
        {
            shapeHost.sizeDelta        = new Vector2(shapeHost.sizeDelta.x, 0f);
            shapeHost.anchoredPosition = Vector2.zero;
        }

        // 슬롯 초기화 후 전역 배치 상태 기준으로 Shape 처리
        _sharedShapeSlotY.Clear();
        _slotCursorY = -spawnOrigin.y;
        foreach (var s in _sharedShapes)
        {
            if (s == null) continue;

            if (_globalPlacements.TryGetValue(s, out var gp))
            {
                if (gp.grid == asset)
                {
                    // 이 그리드에 배치된 Shape: 위치 복원
                    s.transform.SetParent(gridHost, false);
                    var rt = (RectTransform)s.transform;
                    rt.anchoredPosition = gp.anchoredPosition;
                    rt.localRotation    = Quaternion.identity;
                    rt.localScale       = Vector3.one * GetGameplayScale();
                    foreach (var sq in gp.squares)
                        sq?.SetOccupied(true);
                    s.SetOccupiedSquares(gp.squares);
                    s.gameObject.SetActive(true);
                }
                // 다른 그리드에 배치된 Shape: 슬롯에 추가하지 않고 숨김 유지
            }
            else
            {
                // 배치되지 않은 Shape: 슬롯에 표시
                s.gameObject.SetActive(true);
                PlaceSharedShapeToSlot(s);
            }
        }

        if (GridManager.Instance != null)
            GridManager.Instance.SetActiveGrid(session.gridInstance);

        SubscribeSOChanges(asset);
    }

    private void DeactivateSession(GridAssetSO asset)
    {
        if (!sessions.TryGetValue(asset, out var session)) return;
        UnsubscribeSOChanges(asset);

        // 이 그리드에 배치된 Shape의 최신 위치를 전역 배치 정보에 저장
        foreach (var s in _sharedShapes)
        {
            if (s == null) continue;
            if (_globalPlacements.TryGetValue(s, out var gp) && gp.grid == asset)
                gp.anchoredPosition = ((RectTransform)s.transform).anchoredPosition;
        }

        // 모든 Shape를 cacheRoot로 이동 후 숨김 (슬롯 상태 초기화)
        foreach (var s in _sharedShapes)
        {
            if (s == null) continue;
            s.transform.SetParent(cacheRoot, false);
            s.gameObject.SetActive(false);
        }

        _sharedShapeSlotY.Clear();
        _slotCursorY = 0f;

        SetGridActive(session, false);
    }

    private void SubscribeSOChanges(GridAssetSO asset)
    {
        if (asset == null || _dataChangeHandlers.ContainsKey(asset)) return;
        Action<GridChangeType> handler = changeType =>
        {
            // Layout 변경(패턴/비주얼)만 처리 — Shape는 공용 풀로 관리하므로 SO 변경 불필요
            if (changeType == GridChangeType.Layout)
                RefreshGrid(asset);
        };
        _dataChangeHandlers[asset] = handler;
        asset.OnDataChanged += handler;
    }

    private void UnsubscribeSOChanges(GridAssetSO asset)
    {
        if (asset == null) return;
        if (!_dataChangeHandlers.TryGetValue(asset, out var handler)) return;
        asset.OnDataChanged -= handler;
        _dataChangeHandlers.Remove(asset);
    }

    private static void SetGridActive(GridSession session, bool active)
    {
        if (session.gridInstance != null)
            session.gridInstance.gameObject.SetActive(active);
    }

    private void PlaceSharedShapeToSlot(Shape shape)
    {
        float scale = GetGameplayScale();

        if (!_sharedShapeSlotY.ContainsKey(shape))
        {
            float height    = GetShapeSlotHeight(shape) * scale;
            float maxLocalY = GetShapeMaxLocalY(shape)  * scale;
            // spawnSlotStepY = 원하는 슬롯 총 높이. shape보다 작으면 최소 20px 여백 확보.
            float padding   = Mathf.Max(20f, spawnSlotStepY - height);

            _sharedShapeSlotY[shape] = _slotCursorY - maxLocalY;
            _slotCursorY -= height + padding;
        }

        var rt = (RectTransform)shape.transform;
        shape.transform.SetParent(shapeHost, false);
        // 앵커를 상단 고정(0.5, 1)으로 설정 → shapeHost sizeDelta 변경 시 기존 Shape 위치 밀림 방지
        rt.anchorMin        = new Vector2(0.5f, 1f);
        rt.anchorMax        = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(spawnOrigin.x, _sharedShapeSlotY[shape]);
        rt.localRotation    = Quaternion.identity;
        rt.localScale       = Vector3.one * scale;
        shape.SetHome(shapeHost, rt.anchoredPosition);

        RefreshShapeHostSize();
    }

    /// <summary>Shape의 실제 Y 범위 높이 (cellOffsets 최대 – 최소 + 1) × cellSize.</summary>
    private float GetShapeSlotHeight(Shape shape)
    {
        if (shape.cellOffsets == null || shape.cellOffsets.Count == 0)
            return shape.cellSize;
        int minOff = int.MaxValue, maxOff = int.MinValue;
        foreach (var o in shape.cellOffsets)
        {
            if (o.y < minOff) minOff = o.y;
            if (o.y > maxOff) maxOff = o.y;
        }
        return (maxOff - minOff + 1) * shape.cellSize;
    }

    /// <summary>Shape 피벗에서 최상단 블록 상단까지의 로컬 Y 거리.</summary>
    private float GetShapeMaxLocalY(Shape shape)
    {
        if (shape.cellOffsets == null || shape.cellOffsets.Count == 0)
            return shape.cellSize * 0.5f;
        int maxOff = int.MinValue;
        foreach (var o in shape.cellOffsets) if (o.y > maxOff) maxOff = o.y;
        return maxOff * shape.cellSize + shape.cellSize * 0.5f;
    }

    /// <summary>
    /// 슬롯에 남아있는 Shape들을 위에서부터 빈틈 없이 재정렬한다.
    /// Shape 배치로 중간/상단 슬롯이 비었을 때 호출된다.
    /// </summary>
    private void ReflowSlots()
    {
        if (_sharedShapeSlotY.Count == 0)
        {
            _slotCursorY = -spawnOrigin.y;
            RefreshShapeHostSize();
            return;
        }

        // 현재 Y 내림차순 정렬 → 화면 위쪽(덜 음수) Shape 먼저
        var ordered = new List<Shape>(_sharedShapeSlotY.Keys);
        ordered.Sort((a, b) => _sharedShapeSlotY[b].CompareTo(_sharedShapeSlotY[a]));

        float scale = GetGameplayScale();
        _slotCursorY = -spawnOrigin.y;
        _sharedShapeSlotY.Clear();

        foreach (var s in ordered)
        {
            float height    = GetShapeSlotHeight(s) * scale;
            float maxLocalY = GetShapeMaxLocalY(s)  * scale;
            float padding   = Mathf.Max(20f, spawnSlotStepY - height);

            float slotY = _slotCursorY - maxLocalY;
            _sharedShapeSlotY[s] = slotY;
            _slotCursorY -= height + padding;

            var rt = (RectTransform)s.transform;
            rt.anchoredPosition = new Vector2(spawnOrigin.x, slotY);
            s.SetHome(shapeHost, rt.anchoredPosition);
        }

        RefreshShapeHostSize();
    }

    /// <summary>
    /// shapeHost sizeDelta.y를 현재 슬롯 커서 기준으로 정확히 설정한다.
    /// ScrollRect Content 높이를 슬롯 증감에 따라 동적으로 유지한다.
    /// </summary>
    private void RefreshShapeHostSize()
    {
        if (shapeHost == null) return;
        float needed = _sharedShapeSlotY.Count > 0 ? -_slotCursorY : 0f;
        shapeHost.sizeDelta = new Vector2(shapeHost.sizeDelta.x, needed);
    }
}
