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

        for (int i = 0; i < shape.transform.childCount; i++)
        {
            var block = shape.transform.GetChild(i) as RectTransform;
            if (block == null) continue;

            GridSquare sq = FindClosestSquare(block, previewDist);
            if (sq == null) return false; // 그리드 밖 — 스냅/프리뷰 없음

            if (firstBlock == null) { firstBlock = block; firstTargetSq = sq; }
            if (!sq.isPlaceable || sq.isOccupied) allValid = false;
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

    public bool TryPlaceShape(Shape shape)
    {
        if (grid == null || shape == null) return false;

        var candidateSquares = new List<GridSquare>();

        // 직접 자식만 탐색 — GetComponentsInChildren은 손자 RT까지 포함되어
        // 블록 프리팹 내부 자식 오브젝트가 엉뚱한 Square에 매핑되는 버그가 생김
        RectTransform firstBlock = null;
        RectTransform firstTarget = null;

        for (int i = 0; i < shape.transform.childCount; i++)
        {
            var block = shape.transform.GetChild(i) as RectTransform;
            if (block == null) continue;
            if (firstBlock == null) firstBlock = block;

            GridSquare square = FindClosestSquare(block);
            if (square == null) return false;
            if (!square.isPlaceable) return false;
            if (square.isOccupied) return false;

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
