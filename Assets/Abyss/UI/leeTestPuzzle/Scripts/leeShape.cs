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
    private static int _idCounter = 0;

    [Header("Identity")]
    [Tooltip("Shape SO에서 복사된 표시 이름. 여러 Shape가 같은 이름을 가질 수 있다.")]
    public string shapeName;
    [Tooltip("인스턴스 생성 시 자동 부여되는 고유 ID. ex) Shape_01")]
    public string shapeId;

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
        shapeId = $"Shape_{++_idCounter:D2}";

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

        shapeName        = shapeAsset.shapeName;
        shapeBlockPrefab = shapeAsset.shapeBlockPrefab;
        cellOffsets      = new List<Vector2Int>(shapeAsset.cellOffsets ?? new Vector2Int[0]);
        cellSize         = shapeAsset.cellSize;

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
        // 전역 배치 상태에서 제거 (슬롯으로 돌아올 수 있도록)
        LeeBoardManager.Instance?.OnShapePickedUp(this);

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

        if (placed)
        {
            // 배치된 Shape는 scroll content 밖(gridHost)으로 이동 → 스크롤 시 따라 움직이지 않음
            if (LeeBoardManager.Instance?.gridHost != null)
                transform.SetParent(LeeBoardManager.Instance.gridHost, true);
            // 슬롯 해제 → shapeHost 콘텐츠 높이 갱신
            LeeBoardManager.Instance?.OnShapePlaced(this);
        }
        else
        {
            LeeBoardManager.Instance.ReSlotAndReturn(this);
        }

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
