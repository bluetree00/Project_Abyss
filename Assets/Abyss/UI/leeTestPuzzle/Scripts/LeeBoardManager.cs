using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.Events;

/// <summary>
/// New flow (fixes scale/position issues by separating Selection UI and Gameplay UI):
/// 1) User selects a grid via a separate UI button/image (see LeeGridSelectButton).
/// 2) Only the selected grid is instantiated under GridHost (left side).
/// 3) All shapes assigned to that grid are instantiated under ShapeHost (right side), non-overlapping.
/// 4) BackToSelection() returns to the selection UI.
/// 5) Grid + Shapes share the same uniform scale (boardConfig.gameplayUniformScale).
/// </summary>
public class LeeBoardManager : MonoBehaviour
{
    public static LeeBoardManager Instance { get; private set; }

    [Header("Config")]
    public LeeBoardConfigSO boardConfig;

    [Header("UI Roots")]
    [Tooltip("Selection screen root (buttons/images live here).")]
    public GameObject selectionRoot;

    [Tooltip("Gameplay screen root (grid host + shape host live here).")]
    public GameObject gameplayRoot;

    [Header("Gameplay Hosts")]
    [Tooltip("Selected grid is instantiated under this (left side).")]
    public RectTransform gridHost;

    [Tooltip("Shapes are instantiated under this (right side).")]
    public RectTransform shapeHost;

    [Header("Prefabs")]
    [Tooltip("Prefab that has leeGrid on the ROOT.")]
    public leeGrid gridPrefab;

    [Tooltip("Prefab that has leeShape on the ROOT.")]
    public leeShape shapePrefab;

    [Header("Optional: initial grid (legacy)")]
    public LeeGridAssetSO initialGridAsset;

    [Header("Spawn Slots")]
    public Vector2 spawnOrigin = new Vector2(200, 400); // 첫 슬롯 위치(ShapeHost 기준)
    public float spawnSlotStepY = 160f;                   // 슬롯 간격 (원하는 값으로!)
    public int maxSpawnSlots = 5;

    [System.Serializable]
    private class GridSession
    {
        public leeGrid gridInstance;
        public List<leeShape> shapeInstances = new();
        public bool filledEventFired = false;

        public Dictionary<leeShape, int> shapeToSlot = new();
        public HashSet<int> occupiedSlots = new();
    }

    [Serializable]
    public class GridEventEntry
    {
        public LeeGridAssetSO grid;
        public UnityEvent onFilled;
    }

    public List<GridEventEntry> gridEvents = new();
    private readonly Dictionary<LeeGridAssetSO, GridSession> sessions = new();
    private LeeGridAssetSO activeAsset = null;

    private RectTransform cacheRoot;

    private leeGrid activeGridInstance;
    private readonly List<leeShape> activeShapes = new();
    public event Action<LeeGridAssetSO> OnGridFilled;

    [Serializable]
    public class GridFilledUnityEvent : UnityEvent<LeeGridAssetSO> { }

    public GridFilledUnityEvent onGridFilledUnity;

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

        // Optional: auto-open a specific grid for quick testing.
        if (!startSelection && initialGridAsset != null)
            EnterGrid(initialGridAsset);
    }

    /// <summary>
    /// Called by LeeGridSelectButton (selection UI).
    /// </summary>
    public void EnterGrid(LeeGridAssetSO gridAsset)
    {
        if (gridAsset == null) return;
        if (gridPrefab == null || gridHost == null || shapeHost == null) return;

        SetModeSelection(false);

        //기존 활성 세션 숨김
        if (activeAsset != null)
            DeactivateSession(activeAsset);

        activeAsset = gridAsset;

        //세션 없으면 생성
        if (!sessions.TryGetValue(gridAsset, out var session) || session.gridInstance == null)
        {
            session = CreateSession(gridAsset);
            sessions[gridAsset] = session;
        }

        //세션 활성(호스트로 붙이기)
        ActivateSession(gridAsset);
    }

    public void BackToSelection()
    {
        if (activeAsset != null)
        DeactivateSession(activeAsset);

        activeAsset = null;

        if (leeGridManager.Instance != null)
            leeGridManager.Instance.SetActiveGrid(null);

        SetModeSelection(true);
    }

    private void SetModeSelection(bool selectionMode)
    {
        if (selectionRoot != null) selectionRoot.SetActive(selectionMode);
        if (gameplayRoot != null) gameplayRoot.SetActive(!selectionMode);
    }

    private void ClearGameplay()
    {
        if (activeGridInstance != null)
        {
            Destroy(activeGridInstance.gameObject);
            activeGridInstance = null;
        }

        for (int i = 0; i < activeShapes.Count; i++)
        {
            if (activeShapes[i] != null)
                Destroy(activeShapes[i].gameObject);
        }
        activeShapes.Clear();

        // Also clear any leftover children (safety)
        if (gridHost != null)
        {
            for (int i = gridHost.childCount - 1; i >= 0; i--)
                Destroy(gridHost.GetChild(i).gameObject);
        }
        if (shapeHost != null)
        {
            for (int i = shapeHost.childCount - 1; i >= 0; i--)
                Destroy(shapeHost.GetChild(i).gameObject);
        }
    }

    private float GetGameplayScale()
    {
        if (boardConfig == null) return 1f;
        return Mathf.Max(0.0001f, boardConfig.gameplayUniformScale);
    }

    private void SpawnSingleShape(LeeGridAssetSO gridAsset)
    {
        if (gridAsset == null || gridAsset.spawnableShapes == null || gridAsset.spawnableShapes.Length == 0) return;

        var pool = gridAsset.spawnableShapes;
        var asset = (boardConfig != null && boardConfig.randomShapeOnSelect)
            ? pool[UnityEngine.Random.Range(0, pool.Length)]
            : pool[0];

        var shape = Instantiate(shapePrefab, shapeHost);
        shape.ApplyAsset(asset);

        var rt = shape.transform as RectTransform;

        rt.anchoredPosition = Vector2.zero; 
        rt.localScale = Vector3.one; // 스케일 통일

        shape.ApplyAsset(asset);
        shape.CacheStartTransform();
        activeShapes.Add(shape);
    }

    private void SpawnAllShapes(LeeGridAssetSO gridAsset)
    {
        if (gridAsset == null || gridAsset.spawnableShapes == null || gridAsset.spawnableShapes.Length == 0) return;

        // If the user added a LayoutGroup on shapeHost, it will handle non-overlap.
        // We still set a reasonable anchored position (0,0) and let LayoutGroup place it.
        // If there is NO LayoutGroup, we do manual left-to-right placement.
        bool hasLayoutGroup = shapeHost.GetComponent<UnityEngine.UI.LayoutGroup>() != null;

        float cursorX = 0f;
        float spacing = (boardConfig != null) ? boardConfig.shapeSpacing : 60f;

        foreach (var asset in gridAsset.spawnableShapes)
        {
            if (asset == null) continue;

            var shape = Instantiate(shapePrefab, shapeHost);
            var rt = shape.transform as RectTransform;
            if (rt != null)
            {
                rt.localScale = Vector3.one * GetGameplayScale();
                rt.localRotation = Quaternion.identity;

                if (!hasLayoutGroup)
                {
                    // Manual layout: compute approximate width from bounds in cells
                    var b = LeeShapeBoundsUtility.GetBoundsInCells(asset);
                    float w = Mathf.Max(1, b.size.x) * asset.cellSize;
                    rt.anchoredPosition = new Vector2(cursorX + w * 0.5f, 0f);
                    cursorX += w + spacing;
                }
                else
                {
                    rt.anchoredPosition = Vector2.zero;
                }
            }

            shape.ApplyAsset(asset);
            shape.CacheStartTransform();
            activeShapes.Add(shape);
        }
    }

    private GridSession CreateSession(LeeGridAssetSO asset)
    {
        var session = new GridSession();

        // Grid 생성 (처음 1회)
        session.gridInstance = Instantiate(gridPrefab, cacheRoot);
        var gridRT = session.gridInstance.transform as RectTransform;
        if (gridRT != null)
        {
            gridRT.anchoredPosition = Vector2.zero;
            gridRT.localScale = Vector3.one * GetGameplayScale();
            gridRT.localRotation = Quaternion.identity;
        }

        session.gridInstance.Initialize(asset);

        // Shapes 생성 (처음 1회)
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
                    rt.localScale = Vector3.one * GetGameplayScale();
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
        if (session.gridInstance != null) session.gridInstance.gameObject.SetActive(true);

        session.gridInstance.transform.SetParent(gridHost, false);
        var gridRT = session.gridInstance.transform as RectTransform;
        if (gridRT != null)
        {
            gridRT.anchoredPosition = Vector2.zero;
            gridRT.localScale = Vector3.one * GetGameplayScale();
        }

        foreach (var s in session.shapeInstances)
        {
            if (s == null) continue;

            bool placed = s.GetOccupiedSquares() != null && s.GetOccupiedSquares().Count > 0;

            var rt = s.transform as RectTransform;

            if (placed)
            {
                // 배치된 건 원래 위치 유지가 중요 (그리드쪽/placedHost로)
                s.transform.SetParent(gridHost, true);
                s.transform.localScale = Vector3.one * GetGameplayScale();
            }
            else
            {
                // 미배치만 패널로 보내고 튐 방지
                PlaceUnplacedShapeToSlot(session, s);
            }

            if (rt != null) rt.localScale = Vector3.one * GetGameplayScale();
            s.gameObject.SetActive(true);
        }
        SetSessionActive(session, true);

        if (leeGridManager.Instance != null)
            leeGridManager.Instance.SetActiveGrid(session.gridInstance);
    }

    private void DeactivateSession(LeeGridAssetSO asset)
    {
        if (!sessions.TryGetValue(asset, out var session)) return;
        
        SetSessionActive(session, false);
    }

    private static void SetSessionActive(GridSession session, bool active)
    {
        if (session.gridInstance != null)
            session.gridInstance.gameObject.SetActive(active);

        foreach (var s in session.shapeInstances)
            if (s != null) s.gameObject.SetActive(active);
    }

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
        {
            if (entry.grid == asset)
            {
                entry.onFilled?.Invoke();
                break;
            }
        }
    }

    private Vector2 GetSlotPos(int slotIndex)
    {
        // slotIndex 0 -> spawnOrigin, 1 -> origin + stepY, ...
        return new Vector2(spawnOrigin.x, spawnOrigin.y - spawnSlotStepY * slotIndex);
    }

    private int FindFirstFreeSlot(GridSession session)
    {
        for (int i = 0; i < maxSpawnSlots; i++)
            if (!session.occupiedSlots.Contains(i))
                return i;

        return maxSpawnSlots - 1;
    }

    private void AssignSlotIfNeeded(GridSession session, leeShape shape)
    {
        if (session.shapeToSlot.ContainsKey(shape))
            return;

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
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one * GetGameplayScale();

        shape.SetHome(shapeHost, rt.anchoredPosition);
    }
    public void ReSlotAndReturn(leeShape shape)
    {
        if (activeAsset == null) return;
        if (!sessions.TryGetValue(activeAsset, out var session)) return;

        PlaceUnplacedShapeToSlot(session, shape);
    }
    private void ReleaseSlot(GridSession session, leeShape shape)
    {
        if (!session.shapeToSlot.TryGetValue(shape, out var slot))
            return;

        session.occupiedSlots.Remove(slot);
    }
}
