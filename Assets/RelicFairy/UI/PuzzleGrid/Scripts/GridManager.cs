using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Placement manager.
/// Works with the ACTIVE runtime grid instance (set by BoardManager).
/// Uses anchored-position based distance checks to avoid world/scale issues.
/// </summary>
public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    public bool HasGrid => grid != null && grid.gridAsset != null && grid.gridAsset.visual != null;

    [Header("Active Grid (set by BoardManager)")]
    public Grid grid;

    [Header("Rules (SO)")]
    public PlacementRulesSO placementRules;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>Shape가 그리드에 배치됐을 때. 연결된 RuntimeItemData를 인수로 전달.</summary>
    public event System.Action<RuntimeItemData> OnItemPlaced;

    /// <summary>Shape가 그리드에서 제거됐을 때. 연결된 RuntimeItemData를 인수로 전달.</summary>
    public event System.Action<RuntimeItemData> OnItemRemoved;

    /// <summary>배치된 Shape가 클릭(선택)됐을 때. Shape.OnPointerClick이 발생시킨다.</summary>
    public event System.Action<RuntimeItemData> OnItemSelected;

    public void NotifyItemSelected(RuntimeItemData item)
    {
        if (item != null)
            OnItemSelected?.Invoke(item);
    }

    // ── 클릭 배치 ─────────────────────────────────────────────────────
    // 드래그와 <b>같은 종착점</b>(TryPlaceShape)을 쓴다. 검증(존·인접·점유)·점유 마킹·
    // OnItemPlaced 통보가 전부 그쪽에 있어, 조작 방식만 바꾸고 규칙은 하나로 유지된다.

    /// <summary>빈 칸이 클릭됐다. 패널이 고른 룬을 이 칸 기준으로 놓는다.</summary>
    public event System.Action<GridSquare> OnSquareClicked;

    /// <summary>칸 위로 커서가 올라왔다(벗어나면 null). 패널이 배치 미리보기를 그린다.</summary>
    public event System.Action<GridSquare> OnSquareHovered;

    public void NotifySquareClicked(GridSquare square)
    {
        if (square != null) OnSquareClicked?.Invoke(square);
    }

    public void NotifySquareHovered(GridSquare square) => OnSquareHovered?.Invoke(square);

    /// <summary>
    /// 셰이프의 <b>첫 블록</b>이 <paramref name="anchor"/> 칸 중심에 오도록 옮긴 뒤 배치를 시도한다.
    /// 실제 판정·점유·통보는 전부 <see cref="TryPlaceShape"/>가 한다(드래그와 동일 경로).
    /// 실패하면 옮기기 전 위치로 되돌려 놓는다.
    /// </summary>
    public bool TryPlaceShapeAt(Shape shape, GridSquare anchor)
    {
        if (shape == null || anchor == null) return false;

        var shapeRT = (RectTransform)shape.transform;
        Vector2 before = shapeRT.anchoredPosition;

        if (!MoveShapeAnchorTo(shape, anchor)) return false;
        if (TryPlaceShape(shape)) return true;

        shapeRT.anchoredPosition = before;
        return false;
    }

    /// <summary>앵커 칸 기준으로 배치 가능 여부를 미리 칠한다(초록/빨강). 드래그 프리뷰와 같은 표현.</summary>
    public void PreviewShapeAt(Shape shape, GridSquare anchor)
    {
        ClearPreview();
        if (shape == null || anchor == null) return;

        var squares = ResolveFootprint(shape, anchor, out bool allValid);
        if (squares == null) return;

        Color color = allValid
            ? new Color(0.2f, 0.9f, 0.3f, 0.85f)
            : new Color(1f, 0.25f, 0.25f, 0.85f);

        foreach (var sq in squares) sq.SetPreviewHighlight(true, color);
        _previewSquares.AddRange(squares);
    }

    /// <summary>앵커 칸에 놓았을 때 덮게 될 칸들. allValid=false면 어딘가 막혀 있다.</summary>
    private List<GridSquare> ResolveFootprint(Shape shape, GridSquare anchor, out bool allValid)
    {
        allValid = true;
        if (grid == null) return null;

        var squares = grid.GetGridSquares();
        if (squares == null) return null;

        // 첫 블록을 기준(0,0)으로 본 나머지 블록의 칸 오프셋.
        var offsets = shape.CellOffsetsFromFirstBlock();
        if (offsets == null || offsets.Count == 0) return null;

        var result = new List<GridSquare>(offsets.Count);
        string element = RuneZoneRule.ElementOf(shape.ItemData);
        bool isLegendary = shape.ItemData?.rarity == ItemRarity.Legendary;

        foreach (var off in offsets)
        {
            GridSquare found = null;
            int targetCol = anchor.col + off.x;
            int targetRow = anchor.row - off.y;   // 화면 위(+y) = row 감소

            foreach (var sq in squares)
                if (sq != null && sq.col == targetCol && sq.row == targetRow) { found = sq; break; }

            if (found == null) { allValid = false; continue; }   // 판 밖
            if (!found.isPlaceable || found.isOccupied) allValid = false;
            if (!RuneZoneRule.Accepts(found, element, isLegendary)) allValid = false;
            if (!result.Contains(found)) result.Add(found);
        }

        return result;
    }

    /// <summary>셰이프의 첫 블록이 앵커 칸 중심에 오도록 이동.</summary>
    private static bool MoveShapeAnchorTo(Shape shape, GridSquare anchor)
    {
        if (shape.transform.childCount == 0) return false;
        if (shape.transform.GetChild(0) is not RectTransform firstBlock) return false;

        var shapeRT     = (RectTransform)shape.transform;
        var shapeParent = (RectTransform)shapeRT.parent;
        if (shapeParent == null) return false;

        var anchorRT = anchor.GetComponent<RectTransform>();
        if (anchorRT == null) return false;

        Vector3 worldDelta = anchorRT.position - firstBlock.position;
        Vector3 localDelta = shapeParent.InverseTransformVector(worldDelta);
        shapeRT.anchoredPosition += new Vector2(localDelta.x, localDelta.y);
        return true;
    }

    public void SetActiveGrid(Grid active)
    {
        grid = active;
    }

    /// <summary>PlacementRules를 런타임에 교체한다 (BoardManager.ApplyPlacementRules에서 호출).</summary>
    public void SetPlacementRules(PlacementRulesSO rules) => placementRules = rules;

    // ── 드래그 프리뷰 ──────────────────────────────────────────────────

    private readonly List<GridSquare> _previewSquares = new();

    /// <summary>셰이프 단위 프리뷰가 활성화 중인지 여부. GridSquare 물리 트리거가 참조.</summary>
    public bool IsPreviewingShape => _previewSquares.Count > 0;

    /// <summary>
    /// 드래그 중 셰이프를 가장 가까운 그리드 위치로 스냅하고 프리뷰를 표시한다.
    /// snapOffset 을 shape 의 anchoredPosition 에 더하면 격자에 정렬된다.
    /// 전체 배치 가능 → 초록, 불가능 → 빨강, 그리드 밖 → false 반환.
    /// </summary>
    public bool TryPreviewAndSnap(Shape shape, out Vector2 snapOffset)
    {
        snapOffset = Vector2.zero;
        ClearPreview();
        if (grid == null || shape == null) return false;

        float gap = GetGap();
        // drag scale(1.1×) 보정: 스냅 전에도 격자 칸을 충분히 감지하도록 넉넉한 거리 사용
        float previewDist = gap * 0.85f;

        RectTransform firstBlock = null;
        GridSquare firstTargetSq = null;
        bool allValid = true;
        var targets = new List<GridSquare>();
        string element = RuneZoneRule.ElementOf(shape.ItemData);
        bool isLegendary = shape.ItemData?.rarity == ItemRarity.Legendary;

        for (int i = 0; i < shape.transform.childCount; i++)
        {
            var block = shape.transform.GetChild(i) as RectTransform;
            if (block == null) continue;

            GridSquare sq = FindClosestSquare(block, previewDist);
            if (sq == null) return false; // 그리드 밖 — 스냅/프리뷰 없음

            if (firstBlock == null) { firstBlock = block; firstTargetSq = sq; }
            if (!sq.isPlaceable || sq.isOccupied) allValid = false;
            // 속성 불일치 칸은 프리뷰에서 빨강 — 손을 놓기 전에 왜 안 되는지 보이게 한다.
            if (!RuneZoneRule.Accepts(sq, element, isLegendary)) allValid = false;
            if (!targets.Contains(sq)) targets.Add(sq);
        }

        if (firstBlock == null || firstTargetSq == null) return false;

        // 스냅 오프셋: 첫 번째 블록을 타깃 Square 중심에 맞추는 shape 로컬 이동량
        var shapeRT     = (RectTransform)shape.transform;
        var shapeParent = (RectTransform)shapeRT.parent;
        var firstSqRT   = firstTargetSq.GetComponent<RectTransform>();
        Vector3 worldDelta = firstSqRT.position - firstBlock.position;
        Vector3 localDelta = shapeParent.InverseTransformVector(worldDelta);
        snapOffset = new Vector2(localDelta.x, localDelta.y);

        Color color = allValid
            ? new Color(0.2f, 0.9f, 0.3f, 0.85f)
            : new Color(1f, 0.25f, 0.25f, 0.85f);

        foreach (var sq in targets)
            sq.SetPreviewHighlight(true, color);
        _previewSquares.AddRange(targets);
        return true;
    }

    /// <summary>
    /// 이 셰이프를 지금 자리에 놓으려 할 때 <b>길을 막고 있는 룬들</b>을 모은다.
    ///
    /// <para>레전더리는 자기 속성 존의 대부분(14/19칸)을 먹으므로, 그 속성을 키워놨다면
    /// 배치에 앞서 <b>기존 룬을 다 걷어내야</b> 한다. 손으로 하나씩 빼게 두면
    /// "좋은 걸 얻었는데 노동이 시작된다"가 되므로, 한 번에 묻고 한 번에 처리하기 위한 조회다.</para>
    ///
    /// <para><b>점유 외의 이유</b>(판 밖 · 배치 불가 칸 · 속성 존 불일치)로 막혀 있으면
    /// 걷어내도 못 놓으므로 <c>false</c>를 돌려준다 — 헛되이 폐기시키지 않는다.</para>
    /// </summary>
    /// <returns>점유만 치우면 놓을 수 있으면 true. blockers는 그때 비워야 할 룬 목록.</returns>
    public bool TryGetBlockingItems(Shape shape, List<RuntimeItemData> blockers)
    {
        blockers?.Clear();
        if (shape == null) return false;

        string element     = RuneZoneRule.ElementOf(shape.ItemData);
        bool   isLegendary = shape.ItemData?.rarity == ItemRarity.Legendary;

        for (int i = 0; i < shape.transform.childCount; i++)
        {
            var block = shape.transform.GetChild(i) as RectTransform;
            if (block == null) continue;

            GridSquare square = FindClosestSquare(block);
            if (square == null) return false;                                   // 판 밖
            if (!square.isPlaceable) return false;                              // 애초에 못 놓는 칸
            if (!RuneZoneRule.Accepts(square, element, isLegendary)) return false;   // 속성 존 불일치

            if (!square.isOccupied) continue;

            var occupier = square.occupyingItem;
            if (occupier == null) return false;   // 점유인데 주인을 모른다 — 안전하게 포기
            if (blockers != null && !blockers.Contains(occupier)) blockers.Add(occupier);
        }
        return true;
    }

    /// <summary>셰이프 프리뷰를 지운다.</summary>
    public void ClearPreview()
    {
        foreach (var sq in _previewSquares)
            if (sq != null) sq.SetPreviewHighlight(false, Color.clear);
        _previewSquares.Clear();
    }

    public float GetGap() =>
        (grid != null && grid.gridAsset != null && grid.gridAsset.visual != null)
            ? grid.gridAsset.visual.squareGap : 90f;

    /// <summary>
    /// 칸 하나의 시각 크기. 배치 블록을 이 크기로 그리면 칸 사이 여백(= gap - visualSize)을
    /// 침범하지 않는다. 미설정(0)이면 gap과 동일 — 기존 보드는 동작이 바뀌지 않는다.
    /// </summary>
    public float GetSquareVisualSize()
    {
        float gap = GetGap();
        if (grid == null || grid.gridAsset == null || grid.gridAsset.visual == null) return gap;
        float v = grid.gridAsset.visual.squareVisualSize;
        return v > 0f ? v : gap;
    }

    public bool TryPlaceShape(Shape shape)
    {
        if (grid == null || shape == null) return false;

        var candidateSquares = new List<GridSquare>();

        // 직접 자식만 탐색 — GetComponentsInChildren은 손자 RT까지 포함되어
        // 블록 프리팹 내부 자식 오브젝트가 엉뚱한 Square에 매핑되는 버그가 생김
        RectTransform firstBlock = null;
        RectTransform firstTarget = null;
        string element = RuneZoneRule.ElementOf(shape.ItemData);
        bool isLegendary = shape.ItemData?.rarity == ItemRarity.Legendary;

        for (int i = 0; i < shape.transform.childCount; i++)
        {
            var block = shape.transform.GetChild(i) as RectTransform;
            if (block == null) continue;
            if (firstBlock == null) firstBlock = block;

            GridSquare square = FindClosestSquare(block);
            if (square == null) return false;
            if (!square.isPlaceable) return false;
            if (square.isOccupied) return false;
            // 룬은 자기 속성 존에만 놓인다 (레전드리는 중앙 금지).
            if (!RuneZoneRule.Accepts(square, element, isLegendary)) return false;

            if (!candidateSquares.Contains(square))
                candidateSquares.Add(square);

            if (firstTarget == null)
                firstTarget = square.GetComponent<RectTransform>();
        }

        // Snap by delta (keeps multi-block shape aligned)
        if (placementRules == null || placementRules.snapShapeToFirstSquare)
        {
        if (firstBlock != null && firstTarget != null)
        {
            var shapeRT = (RectTransform)shape.transform;
            var shapeParent = (RectTransform)shapeRT.parent;

            Vector3 worldDelta = firstTarget.position - firstBlock.position;

            Vector3 localDelta3 = shapeParent.InverseTransformVector(worldDelta);

            shapeRT.anchoredPosition += new Vector2(localDelta3.x, localDelta3.y);
        }
        }

        // Mark occupied
        foreach (var sq in candidateSquares)
        {
            sq.SetOccupied(true);
            sq.occupyingItem = shape.ItemData;
            sq.SetHighlight(false);
        }

        shape.SetOccupiedSquares(candidateSquares);
        CheckAllPlaceableFilled();

        if (shape.ItemData != null)
            OnItemPlaced?.Invoke(shape.ItemData);

        return true;
    }

    // maxDistOverride < 0 이면 PlacementRules 기본값(gap*0.5) 사용
    private GridSquare FindClosestSquare(RectTransform blockRT, float maxDistOverride = -1f)
    {
        float minDist = float.MaxValue;
        GridSquare result = null;

        var gridRoot = (RectTransform)grid.transform;
        Vector2 blockLocal = (Vector2)gridRoot.InverseTransformPoint(blockRT.position);

        foreach (var sq in grid.GetGridSquares())
        {
            var sqRT = sq.GetComponent<RectTransform>();
            Vector2 sqLocal = (Vector2)gridRoot.InverseTransformPoint(sqRT.position);
            float d = Vector2.Distance(blockLocal, sqLocal);
            if (d < minDist) { minDist = d; result = sq; }
        }

        if (result == null) return null;

        float gap = GetGap();
        float maxAllowed = maxDistOverride >= 0f
            ? maxDistOverride
            : gap * ((placementRules != null) ? placementRules.maxAllowedDistMultiplier : 0.5f);

        return minDist <= maxAllowed ? result : null;
    }

    private static Vector2 GetAnchoredDelta(RectTransform fromBlock, RectTransform toSquare)
    {
        var gridRoot = toSquare.transform.parent as RectTransform;
        if (gridRoot == null) return Vector2.zero;

        // 동일 좌표계로 계산
        Vector2 blockLocal = (Vector2)gridRoot.InverseTransformPoint(fromBlock.position);
        Vector2 targetLocal = toSquare.anchoredPosition;

        return (targetLocal - blockLocal);
    }


    private void CheckAllPlaceableFilled()
    {
        foreach (var sq in grid.GetGridSquares())
        {
            if (sq.isPlaceable && !sq.isOccupied) return;
        }
        if (BoardManager.Instance != null && grid != null && grid.gridAsset != null)
            BoardManager.Instance.NotifyGridFilled(grid.gridAsset);
    }

    public void ReleaseShape(Shape shape)
    {
        var squares = shape.GetOccupiedSquares();
        if (squares == null) return;

        foreach (var sq in squares)
        {
            if (sq == null) continue;
            sq.SetOccupied(false);
            sq.SetHighlight(false);
        }
        shape.SetOccupiedSquares(new List<GridSquare>());

        if (shape.ItemData != null)
            OnItemRemoved?.Invoke(shape.ItemData);
    }
}
