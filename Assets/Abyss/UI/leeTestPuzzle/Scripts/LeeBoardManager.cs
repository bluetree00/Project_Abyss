using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Lee 퍼즐 시스템의 중심 매니저.
///
/// [SO 방식]  Inspector에 LeeGridAssetSO 할당 → EnterGrid(SO) 호출
/// [데이터 방식] LeeGridAssetData 코드 생성  → SetRuntimeData(data) or EnterGrid(data) 호출
///
/// 두 방식 모두 하위 호환. 활성 SO에 AddShape/SetPattern 등 뮤테이터를 호출하면 실시간 반영됨.
/// </summary>
public class LeeBoardManager : MonoBehaviour
{
    public static LeeBoardManager Instance { get; private set; }

    [Header("Config")]
    public LeeBoardConfigSO boardConfig;

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
    [Tooltip("leeGrid 컴포넌트가 루트에 있는 프리팹.")]
    public leeGrid gridPrefab;
    [Tooltip("leeShape 컴포넌트가 루트에 있는 프리팹.")]
    public leeShape shapePrefab;

    [Header("Initial Grid (SO 방식)")]
    [Tooltip("Inspector에 SO 직접 할당 시 Start에서 자동 진입.")]
    public LeeGridAssetSO initialGridAsset;

    [Header("Default Prefabs (런타임 SO 생성용)")]
    [Tooltip("데이터 방식 사용 시 gridSquarePrefab 폴백.")]
    public GameObject defaultSquarePrefab;
    [Tooltip("데이터 방식 사용 시 shapeBlockPrefab 폴백.")]
    public GameObject defaultShapeBlockPrefab;

    [Header("Runtime Data (데이터 방식 자동 초기화)")]
    [Tooltip("id가 비어 있지 않으면 Start에서 자동으로 EnterGrid 호출.")]
    public LeeGridAssetData initialRuntimeData;

    [Header("Shape 슬롯")]
    [Tooltip("shapeHost 기준 첫 번째 슬롯 위치.")]
    public Vector2 spawnOrigin = new Vector2(200, 400);
    [Tooltip("슬롯 간격 (Y축).")]
    public float spawnSlotStepY = 160f;
    [Tooltip("최대 슬롯 수.")]
    public int maxSpawnSlots = 5;

    // ── 내부 세션 ─────────────────────────────────────────────────────

    [System.Serializable]
    private class GridSession
    {
        public leeGrid gridInstance;
        public List<leeShape> shapeInstances = new();
        public bool filledEventFired = false;

        public Dictionary<leeShape, int> shapeToSlot = new();
        public HashSet<int> occupiedSlots = new();

        // 런타임 생성 SO 추적 (OnDestroy 시 Destroy 대상)
        public LeeGridPatternSO  runtimePatternSO;
        public LeeGridVisualSO   runtimeVisualSO;
        public LeeShapeAssetSO[] runtimeShapeSOs;
        public bool isRuntimeCreated = false;
    }

    // ── Inspector 이벤트 ──────────────────────────────────────────────

    [Serializable]
    public class GridEventEntry
    {
        public LeeGridAssetSO grid;
        public UnityEvent onFilled;
    }

    public List<GridEventEntry> gridEvents = new();

    [Serializable]
    public class GridFilledUnityEvent : UnityEvent<LeeGridAssetSO> { }
    public GridFilledUnityEvent onGridFilledUnity;

    // ── 내부 상태 ─────────────────────────────────────────────────────

    private readonly Dictionary<LeeGridAssetSO, GridSession> sessions = new();
    private LeeGridAssetSO activeAsset;

    /// <summary>현재 활성화된 그리드 SO. 뮤테이터(AddShape, SetPattern 등) 호출에 사용.</summary>
    public LeeGridAssetSO ActiveAsset => activeAsset;

    private readonly Dictionary<string, LeeGridAssetSO> _runtimeSOCache = new();
    private LeeBoardConfigSO    _runtimeBoardConfig;
    private LeePlacementRulesSO _runtimePlacementRules;

    private readonly Dictionary<LeeGridAssetSO, Action<LeeGridChangeType>> _dataChangeHandlers = new();

    private RectTransform cacheRoot;

    public event Action<LeeGridAssetSO> OnGridFilled;

    // ── 생명주기 ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

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

    void OnDestroy()
    {
        foreach (var kvp in _dataChangeHandlers)
        {
            var so = kvp.Key;
            if (so != null) so.OnDataChanged -= kvp.Value;
        }
        _dataChangeHandlers.Clear();

        foreach (var session in sessions.Values)
            if (session.isRuntimeCreated)
                LeeSORuntimeFactory.DestroyRuntimeSOs(
                    null,
                    session.runtimePatternSO,
                    session.runtimeVisualSO,
                    session.runtimeShapeSOs);

        foreach (var so in _runtimeSOCache.Values)
            if (so != null) Destroy(so);

        if (_runtimeBoardConfig    != null) Destroy(_runtimeBoardConfig);
        if (_runtimePlacementRules != null) Destroy(_runtimePlacementRules);
    }

    // ── 공개 API ──────────────────────────────────────────────────────

    /// <summary>
    /// 런타임 데이터로 그리드를 초기화한다. 기존 활성 그리드가 있으면 교체된다.
    /// 초기화 후 ActiveAsset에 뮤테이터를 호출해 셰이프/패턴을 추가로 변경할 수 있다.
    /// </summary>
    public void SetRuntimeData(LeeGridAssetData data)
    {
        if (data == null || string.IsNullOrEmpty(data.id)) return;
        initialRuntimeData = data;
        EnterGrid(data);
    }

    /// <summary>SO 방식 그리드 진입. LeeGridSelectButton 등에서 호출.</summary>
    public void EnterGrid(LeeGridAssetSO gridAsset)
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
    }

    /// <summary>데이터 방식 그리드 진입. 같은 id 재호출 시 캐시된 세션을 재사용한다.</summary>
    public void EnterGrid(LeeGridAssetData data)
    {
        if (data == null || string.IsNullOrEmpty(data.id)) return;
        if (gridPrefab == null || gridHost == null || shapeHost == null) return;

        SetModeSelection(false);

        if (activeAsset != null)
            DeactivateSession(activeAsset);

        if (!_runtimeSOCache.TryGetValue(data.id, out var gridSO))
        {
            gridSO = LeeSORuntimeFactory.CreateGridAssetSO(
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
    }

    /// <summary>동일 id의 퍼즐을 새 데이터로 완전 교체한다. 기존 SO/세션을 모두 재생성한다.</summary>
    public void UpdateActiveGrid(LeeGridAssetData newData)
    {
        if (newData == null || string.IsNullOrEmpty(newData.id)) return;

        if (_runtimeSOCache.TryGetValue(newData.id, out var oldSO))
        {
            if (sessions.TryGetValue(oldSO, out var oldSession))
            {
                if (activeAsset == oldSO) activeAsset = null;
                SetSessionActive(oldSession, false);
                if (oldSession.gridInstance != null) Destroy(oldSession.gridInstance.gameObject);
                foreach (var s in oldSession.shapeInstances)
                    if (s != null) Destroy(s.gameObject);
                sessions.Remove(oldSO);
                LeeSORuntimeFactory.DestroyRuntimeSOs(
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
    public void ApplyConfig(LeeBoardConfigData configData)
    {
        if (_runtimeBoardConfig != null) Destroy(_runtimeBoardConfig);
        _runtimeBoardConfig = LeeSORuntimeFactory.CreateBoardConfigSO(configData);
        boardConfig = _runtimeBoardConfig;
    }

    /// <summary>PlacementRules를 런타임에 교체한다.</summary>
    public void ApplyPlacementRules(LeePlacementRulesData rulesData)
    {
        if (_runtimePlacementRules != null) Destroy(_runtimePlacementRules);
        _runtimePlacementRules = LeeSORuntimeFactory.CreatePlacementRulesSO(rulesData);
        if (leeGridManager.Instance != null)
            leeGridManager.Instance.placementRules = _runtimePlacementRules;
    }

    /// <summary>선택 화면으로 돌아간다.</summary>
    public void BackToSelection()
    {
        if (activeAsset != null)
            DeactivateSession(activeAsset);

        activeAsset = null;

        if (leeGridManager.Instance != null)
            leeGridManager.Instance.SetActiveGrid(null);

        SetModeSelection(true);
    }

    /// <summary>
    /// 지정 그리드 세션을 전체 재빌드한다.
    /// LeeGridAssetSO.SetPattern / SetVisual 호출 시 자동으로 발동된다.
    /// </summary>
    public void RefreshGrid(LeeGridAssetSO asset)
    {
        if (asset == null) return;

        bool wasActive = activeAsset == asset;
        UnsubscribeSOChanges(asset);

        // 런타임 SO 레퍼런스 보존 — asset.pattern/visual/spawnableShapes가 아직 참조 중이므로 파괴하지 않는다.
        LeeGridPatternSO  savedPat = null;
        LeeGridVisualSO   savedVis = null;
        LeeShapeAssetSO[] savedShp = null;
        bool wasRuntime = false;

        if (sessions.TryGetValue(asset, out var old))
        {
            SetSessionActive(old, false);
            if (old.gridInstance != null) Destroy(old.gridInstance.gameObject);
            foreach (var s in old.shapeInstances)
                if (s != null) Destroy(s.gameObject);

            savedPat   = old.runtimePatternSO;
            savedVis   = old.runtimeVisualSO;
            savedShp   = old.runtimeShapeSOs;
            wasRuntime = old.isRuntimeCreated;
            sessions.Remove(asset);
        }

        if (wasActive) activeAsset = null;
        EnterGrid(asset);

        // 새 세션에 런타임 SO 추적 정보 이어받기 (OnDestroy 정리용)
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

    /// <summary>leeGridManager가 모든 배치 가능 칸이 채워졌음을 알릴 때 호출.</summary>
    public void NotifyGridFilled(LeeGridAssetSO asset)
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

    /// <summary>드래그 실패 시 leeShape.OnEndDrag에서 호출. 셰이프를 슬롯으로 되돌린다.</summary>
    public void ReSlotAndReturn(leeShape shape)
    {
        if (activeAsset == null) return;
        if (!sessions.TryGetValue(activeAsset, out var session)) return;
        PlaceUnplacedShapeToSlot(session, shape);
    }

    // ── 내부 구현 ─────────────────────────────────────────────────────

    private void SetModeSelection(bool selectionMode)
    {
        if (selectionRoot != null) selectionRoot.SetActive(selectionMode);
        if (gameplayRoot  != null) gameplayRoot.SetActive(!selectionMode);
    }

    private float GetGameplayScale() =>
        boardConfig != null ? Mathf.Max(0.0001f, boardConfig.gameplayUniformScale) : 1f;

    private GridSession CreateSession(LeeGridAssetSO asset)
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

        if (shapePrefab != null && asset.spawnableShapes != null)
        {
            foreach (var sAsset in asset.spawnableShapes)
            {
                if (sAsset == null) continue;
                var shape = Instantiate(shapePrefab, cacheRoot);
                shape.ApplyAsset(sAsset);
                shape.CacheStartTransform();
                var rt = shape.transform as RectTransform;
                if (rt != null)
                {
                    rt.localScale    = Vector3.one * GetGameplayScale();
                    rt.localRotation = Quaternion.identity;
                }
                session.shapeInstances.Add(shape);
            }
        }

        SetSessionActive(session, false);
        return session;
    }

    private void ActivateSession(LeeGridAssetSO asset)
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

        foreach (var s in session.shapeInstances)
        {
            if (s == null) continue;
            bool placed = s.GetOccupiedSquares()?.Count > 0;
            if (placed)
            {
                s.transform.SetParent(gridHost, true);
                s.transform.localScale = Vector3.one * GetGameplayScale();
            }
            else
            {
                PlaceUnplacedShapeToSlot(session, s);
            }
            s.gameObject.SetActive(true);
        }

        SetSessionActive(session, true);

        if (leeGridManager.Instance != null)
            leeGridManager.Instance.SetActiveGrid(session.gridInstance);

        SubscribeSOChanges(asset);
    }

    private void DeactivateSession(LeeGridAssetSO asset)
    {
        if (!sessions.TryGetValue(asset, out var session)) return;
        UnsubscribeSOChanges(asset);
        SetSessionActive(session, false);
    }

    private void SubscribeSOChanges(LeeGridAssetSO asset)
    {
        if (asset == null || _dataChangeHandlers.ContainsKey(asset)) return;
        Action<LeeGridChangeType> handler = changeType =>
        {
            if (changeType == LeeGridChangeType.Shapes)
                SyncShapes(asset);
            else
                RefreshGrid(asset);
        };
        _dataChangeHandlers[asset] = handler;
        asset.OnDataChanged += handler;
    }

    private void UnsubscribeSOChanges(LeeGridAssetSO asset)
    {
        if (asset == null) return;
        if (!_dataChangeHandlers.TryGetValue(asset, out var handler)) return;
        asset.OnDataChanged -= handler;
        _dataChangeHandlers.Remove(asset);
    }

    /// <summary>
    /// Shape 추가/삭제 시 기존 배치 상태를 유지하면서 증분 동기화한다.
    /// LeeGridAssetSO.AddShape / RemoveShape / SetShapes 호출 시 자동 발동.
    /// </summary>
    private void SyncShapes(LeeGridAssetSO asset)
    {
        if (!sessions.TryGetValue(asset, out var session)) return;

        var targetAssets = asset.spawnableShapes ?? System.Array.Empty<LeeShapeAssetSO>();

        // 기존 인스턴스를 SO별 Queue로 그룹화
        var pool = new Dictionary<LeeShapeAssetSO, Queue<leeShape>>();
        foreach (var s in session.shapeInstances)
        {
            if (s == null || s.shapeAsset == null) continue;
            if (!pool.TryGetValue(s.shapeAsset, out var q))
                pool[s.shapeAsset] = q = new Queue<leeShape>();
            q.Enqueue(s);
        }

        // 새 목록 구성: 재사용 or 신규 생성
        var newInstances = new List<leeShape>();
        foreach (var sAsset in targetAssets)
        {
            if (sAsset == null) continue;
            if (pool.TryGetValue(sAsset, out var q) && q.Count > 0)
            {
                newInstances.Add(q.Dequeue()); // 배치 상태 그대로 재사용
            }
            else
            {
                var shape = Instantiate(shapePrefab, cacheRoot);
                shape.ApplyAsset(sAsset);
                shape.CacheStartTransform();
                if (shape.transform is RectTransform rt)
                {
                    rt.localScale    = Vector3.one * GetGameplayScale();
                    rt.localRotation = Quaternion.identity;
                }
                newInstances.Add(shape);
            }
        }

        // 제거된 Shape 정리
        foreach (var q in pool.Values)
        {
            while (q.Count > 0)
            {
                var s = q.Dequeue();
                if (s == null) continue;
                if (s.GetOccupiedSquares()?.Count > 0 && leeGridManager.Instance != null)
                    leeGridManager.Instance.ReleaseShape(s);
                if (session.shapeToSlot.TryGetValue(s, out var slot))
                {
                    session.occupiedSlots.Remove(slot);
                    session.shapeToSlot.Remove(s);
                }
                Destroy(s.gameObject);
            }
        }

        session.shapeInstances = newInstances;

        // 활성 상태면 새 Shape를 슬롯에 배치
        if (activeAsset == asset)
        {
            foreach (var s in session.shapeInstances)
            {
                if (s == null) continue;
                bool placed = s.GetOccupiedSquares()?.Count > 0;
                if (!placed && !session.shapeToSlot.ContainsKey(s))
                    PlaceUnplacedShapeToSlot(session, s);
                s.gameObject.SetActive(true);
            }
        }
    }

    private static void SetSessionActive(GridSession session, bool active)
    {
        if (session.gridInstance != null)
            session.gridInstance.gameObject.SetActive(active);
        foreach (var s in session.shapeInstances)
            if (s != null) s.gameObject.SetActive(active);
    }

    private Vector2 GetSlotPos(int slotIndex) =>
        new Vector2(spawnOrigin.x, spawnOrigin.y - spawnSlotStepY * slotIndex);

    private int FindFirstFreeSlot(GridSession session)
    {
        for (int i = 0; i < maxSpawnSlots; i++)
            if (!session.occupiedSlots.Contains(i)) return i;
        return maxSpawnSlots - 1;
    }

    private void AssignSlotIfNeeded(GridSession session, leeShape shape)
    {
        if (session.shapeToSlot.ContainsKey(shape)) return;
        int slot = FindFirstFreeSlot(session);
        session.shapeToSlot[shape] = slot;
        session.occupiedSlots.Add(slot);
    }

    private void PlaceUnplacedShapeToSlot(GridSession session, leeShape shape)
    {
        AssignSlotIfNeeded(session, shape);
        int slot = session.shapeToSlot[shape];
        var rt = (RectTransform)shape.transform;
        shape.transform.SetParent(shapeHost, false);
        rt.anchoredPosition = GetSlotPos(slot);
        rt.localRotation    = Quaternion.identity;
        rt.localScale       = Vector3.one * GetGameplayScale();
        shape.SetHome(shapeHost, rt.anchoredPosition);
    }
    
}
