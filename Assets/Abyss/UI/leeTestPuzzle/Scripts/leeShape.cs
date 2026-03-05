using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Runtime Shape root.
/// - Built from LeeShapeAssetSO (block prefab + offsets).
/// - Drag behavior from LeeShapeDragSO.
/// IMPORTANT: Put this script on the ROOT of the shape prefab.
/// Do NOT put drag scripts on child blocks.
/// </summary>
public class leeShape : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Assets")]
    public LeeShapeAssetSO shapeAsset;
    public LeeShapeDragSO dragAsset;

    [Header("Fallback (used if SO is null)")]
    public GameObject shapeBlockPrefab;
    public List<Vector2Int> cellOffsets = new();
    public float cellSize = 80f;

    [SerializeField] private Vector2 homeAnchoredPos;
    [SerializeField] private RectTransform homeParent;

    private Canvas canvas;
    private RectTransform rt;
    private Vector3 cachedStartLocalScale;
    private Vector2 cachedStartAnchoredPos;

    private List<leeGridSquare> occupiedSquares = new();
    private LayoutElement _layout;

    void Awake()
    {
        rt = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>();
        _layout = GetComponent<LayoutElement>();
        CacheStartTransform();

        // If asset already assigned in inspector, build now.
        if (shapeAsset != null)
            ApplyAsset(shapeAsset);
        else
            BuildShapeBlocks();
    }

    public void ApplyAsset(LeeShapeAssetSO asset)
    {
        shapeAsset = asset;
        if (shapeAsset == null) return;

        shapeBlockPrefab = shapeAsset.shapeBlockPrefab;
        cellOffsets = new List<Vector2Int>(shapeAsset.cellOffsets ?? new Vector2Int[0]);
        cellSize = shapeAsset.cellSize;

        BuildShapeBlocks();
    }

    public void CacheStartTransform()
    {
        if (rt == null) rt = GetComponent<RectTransform>();
        cachedStartAnchoredPos = rt.anchoredPosition;
        cachedStartLocalScale = rt.localScale;
    }

    public void ReturnToStart()
    {
        rt.anchoredPosition = cachedStartAnchoredPos;
        rt.localScale = cachedStartLocalScale;
    }

    private void BuildShapeBlocks()
    {
        // Clear
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);

        if (shapeBlockPrefab == null) return;

        foreach (var offset in cellOffsets)
        {
            var blockObj = Instantiate(shapeBlockPrefab, transform);
            var brt = blockObj.GetComponent<RectTransform>();
            if (brt != null)
                brt.anchoredPosition = new Vector2(offset.x * cellSize, offset.y * cellSize);
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (leeGridManager.Instance != null)
            leeGridManager.Instance.ReleaseShape(this);

        // Visual feedback: scale multiplier
        float mul = (dragAsset != null) ? dragAsset.selectedScale.x : 1.1f;
        rt.localScale = cachedStartLocalScale * mul;

        // Apply pointer offset ONCE at drag start (so it doesn't accumulate)
        if (dragAsset != null && dragAsset.pointerOffset != Vector2.zero)
        {
            float scaleFactor = (canvas != null) ? canvas.scaleFactor : 1f;
            rt.anchoredPosition += dragAsset.pointerOffset / Mathf.Max(0.0001f, scaleFactor);
        }

        if (GetOccupiedSquares() != null && GetOccupiedSquares().Count > 0)
        {
            leeGridManager.Instance.ReleaseShape(this);
        }

        if (_layout != null) _layout.ignoreLayout = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        float scaleFactor = canvas != null ? canvas.scaleFactor : 1f;

        Vector2 delta = eventData.delta / Mathf.Max(0.0001f, scaleFactor);
        rt.anchoredPosition += delta;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // Restore base scale (placement manager will snap position)
        rt.localScale = cachedStartLocalScale;

        if (leeGridManager.Instance == null) { ReturnToStart(); return; }

        bool placed = leeGridManager.Instance.TryPlaceShape(this);

        if (!placed)
            LeeBoardManager.Instance.ReSlotAndReturn(this);

        if (_layout != null) _layout.ignoreLayout = false;
    }

    // Placement occupancy
    public void SetOccupiedSquares(List<leeGridSquare> squares) => occupiedSquares = squares;
    public List<leeGridSquare> GetOccupiedSquares() => occupiedSquares;

    public void SetHome(RectTransform parent, Vector2 anchoredPos)
    {
        homeParent = parent;
        homeAnchoredPos = anchoredPos;
    }

    public void ReturnHome()
    {
        if (rt == null) rt = (RectTransform)transform;

        if (homeParent != null)
            rt.SetParent(homeParent, false);

        rt.anchoredPosition = homeAnchoredPos;
    }
    public void SetIgnoreLayout(bool ignore)
    {
        var le = GetComponent<LayoutElement>();
        if (le != null)
            le.ignoreLayout = ignore;
    }
}
