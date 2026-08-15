using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Runtime Shape root.
/// - Built from ShapeAssetSO (block prefab + offsets).
/// - Drag behavior from ShapeDragSO.
/// IMPORTANT: Put this script on the ROOT of the shape prefab.
/// Do NOT put drag scripts on child blocks.
/// </summary>
public class Shape : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    private static int _idCounter = 0;

    [Header("Identity")]
    [Tooltip("Shape SO에서 복사된 표시 이름. 여러 Shape가 같은 이름을 가질 수 있다.")]
    public string shapeName;
    [Tooltip("인스턴스 생성 시 자동 부여되는 고유 ID. ex) Shape_01")]
    public string shapeId;

    [Header("Assets")]
    public ShapeAssetSO shapeAsset;
    public ShapeDragSO dragAsset;

    [Header("Fallback (used if SO is null)")]
    public GameObject shapeBlockPrefab;
    public List<Vector2Int> cellOffsets = new();
    public float cellSize = 90f;

    [SerializeField] private Vector2 homeAnchoredPos;
    [SerializeField] private RectTransform homeParent;

    /// <summary>이 Shape에 연결된 아이템 데이터. 배치/제거 시 RunItemInventory와 동기화.</summary>
    public RuntimeItemData ItemData { get; private set; }

    private Canvas canvas;
    private RectTransform rt;
    private Vector3 cachedStartLocalScale;
    private Vector2 cachedStartAnchoredPos;

    private List<GridSquare> occupiedSquares = new();
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

    public void ApplyAsset(ShapeAssetSO asset)
    {
        shapeAsset = asset;
        if (shapeAsset == null) return;

        shapeName        = shapeAsset.shapeName;
        shapeBlockPrefab = shapeAsset.shapeBlockPrefab;
        cellOffsets      = new List<Vector2Int>(shapeAsset.cellOffsets ?? new Vector2Int[0]);
        cellSize         = (GridManager.Instance != null && GridManager.Instance.HasGrid)
                           ? GridManager.Instance.GetGap()
                           : shapeAsset.cellSize;

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

        // 간격은 스텝(cellSize)으로, 크기는 칸의 시각 크기로 그린다.
        // 둘을 같은 값으로 쓰면 블록이 칸 사이 여백까지 덮어 존 타일 경계선이 잠식된다.
        float visualSize = (GridManager.Instance != null && GridManager.Instance.HasGrid)
                           ? GridManager.Instance.GetSquareVisualSize()
                           : cellSize;

        foreach (var offset in cellOffsets)
        {
            var blockObj = Instantiate(shapeBlockPrefab, transform);
            var brt = blockObj.GetComponent<RectTransform>();
            if (brt != null)
            {
                brt.sizeDelta = new Vector2(visualSize, visualSize);
                brt.anchoredPosition = new Vector2(offset.x * cellSize, offset.y * cellSize);
            }

            // BoxCollider2D 크기도 시각 크기 기준으로 동기화 (약 80% 인셋)
            if (blockObj.TryGetComponent<BoxCollider2D>(out var col))
                col.size = new Vector2(visualSize * 0.8f, visualSize * 0.8f);
        }

        ApplyRuneArt();   // 재생성(칸 크기 변경 등) 후에도 룬 외형이 유지되도록
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        // 전역 배치 상태에서 제거 (슬롯으로 돌아올 수 있도록)
        BoardManager.Instance?.OnShapePickedUp(this);

        GridManager.Instance?.ReleaseShape(this);

        // ShapeScrollView 의 RectMask2D 클리핑 방지:
        // 드래그 중 GameplayRoot 로 리패런트 → 격자 방향으로 스냅해도 잘리지 않음
        var gameplayRoot = BoardManager.Instance?.gameplayRoot?.transform;
        if (gameplayRoot != null && transform.parent != gameplayRoot)
            transform.SetParent(gameplayRoot, true); // worldPositionStays=true

        // Visual feedback: scale multiplier
        float mul = (dragAsset != null) ? dragAsset.selectedScale.x : 1.1f;
        rt.localScale = cachedStartLocalScale * mul;

        // Apply pointer offset ONCE at drag start (so it doesn't accumulate)
        if (dragAsset != null && dragAsset.pointerOffset != Vector2.zero)
        {
            float scaleFactor = (canvas != null) ? canvas.scaleFactor : 1f;
            rt.anchoredPosition += dragAsset.pointerOffset / Mathf.Max(0.0001f, scaleFactor);
        }

        if (_layout != null) _layout.ignoreLayout = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        float scaleFactor = canvas != null ? canvas.scaleFactor : 1f;

        Vector2 delta = eventData.delta / Mathf.Max(0.0001f, scaleFactor);
        rt.anchoredPosition += delta;

        // 격자 근처이면 프리뷰만 표시 — 스냅은 손을 놓을 때만 (TryPlaceShape)
        GridManager.Instance?.TryPreviewAndSnap(this, out _);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // Restore base scale (placement manager will snap position)
        rt.localScale = cachedStartLocalScale;

        GridManager.Instance?.ClearPreview();

        if (GridManager.Instance == null) { ReturnToStart(); return; }

        bool placed = GridManager.Instance.TryPlaceShape(this);

        if (placed)
        {
            // gridHost로 이동 후 raycastTarget 활성화 → 배치된 셀에서 재드래그 가능
            if (BoardManager.Instance?.gridHost != null)
                transform.SetParent(BoardManager.Instance.gridHost, true);
            BoardManager.Instance?.OnShapePlaced(this);
            SetBlocksRaycastTarget(true);
        }
        else
        {
            BoardManager.Instance.ReSlotAndReturn(this);
        }

        if (_layout != null) _layout.ignoreLayout = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // 드래그 중이 아닌 순수 클릭 → 우측 아이템 정보 패널 갱신 (시나리오 5)
        if (eventData.dragging) return;
        if (ItemData != null)
            GridManager.Instance?.NotifyItemSelected(ItemData);
    }

    /// <summary>아이템 데이터를 이 Shape에 연결한다. StagingAreaView에서 보관함 아이템 드래그 시 호출.</summary>
    public void BindItem(RuntimeItemData item)
    {
        ItemData = item;
        ApplyRuneArt();
    }

    /// <summary>아이템 연결 해제. 보관함 반환 또는 폐기 시 호출.</summary>
    public void UnbindItem() => ItemData = null;

    /// <summary>
    /// <b>첫 블록을 (0,0)으로 본</b> 각 블록의 칸 오프셋. 클릭 배치가 "이 칸을 누르면 어디까지 덮이나"를
    /// 계산하는 데 쓴다. cellOffsets 원본은 기준점이 모양마다 달라(음수 포함) 그대로 못 쓴다.
    /// </summary>
    public List<Vector2Int> CellOffsetsFromFirstBlock()
    {
        if (cellOffsets == null || cellOffsets.Count == 0) return null;

        var origin = cellOffsets[0];
        var result = new List<Vector2Int>(cellOffsets.Count);
        foreach (var o in cellOffsets) result.Add(o - origin);
        return result;
    }

    /// <summary>
    /// 드래그하는 블록의 겉모습을 <b>그 아이템의 속성 룬</b>으로 바꾼다.
    /// 블록 프리팹은 속성을 모르는 공용 사각 타일이라, 아이템이 붙는 이 시점에만 알 수 있다.
    /// 스프라이트/틴트 규칙은 RuneArt.ResolveRuneCell에 맡긴다 — 예전엔 여기서만 틴트를
    /// 흰색으로 강제해서, 전용 아트가 없는 빛 룬이 손에 쥔 순간 흰 돌로 바뀌었다.
    /// 아트가 아예 없으면 프리팹 기본 외형을 그대로 둔다.
    /// </summary>
    private void ApplyRuneArt()
    {
        if (ItemData == null) return;

        // 1순위: 속성 블록 타일 — 룬이 차지한 칸은 그 룬의 속성으로 칠한다(룬 아이콘과 별개 슬롯).
        //        이미 속성색으로 그려진 타일이라 틴트를 곱하지 않는다.
        // 2순위: 타일이 없으면 기존 규칙(속성 룬 아트 → 없으면 등급 아트 + 속성색 틴트).
        var art  = RuneArt.GetBlockTile(ItemData.element);
        var tint = Color.white;

        if (art == null)
            RuneArt.ResolveRuneCell(ItemData.element, ItemData.rarity, Color.white, out art, out tint);

        if (art == null) return;

        foreach (Transform child in transform)
        {
            if (!child.TryGetComponent<Image>(out var img)) continue;
            img.sprite         = art;
            img.type           = Image.Type.Simple;
            img.color          = tint;
            img.preserveAspect = true;
        }
    }

    // Placement occupancy
    public void SetOccupiedSquares(List<GridSquare> squares) => occupiedSquares = squares;
    public List<GridSquare> GetOccupiedSquares() => occupiedSquares;

    public void RebuildWithCellSize(float newCellSize)
    {
        cellSize = newCellSize;
        BuildShapeBlocks();
    }

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

    private void SetBlocksRaycastTarget(bool enable)
    {
        foreach (Transform child in transform)
        {
            var img = child.GetComponent<Image>();
            if (img != null) img.raycastTarget = enable;
        }
    }
}
