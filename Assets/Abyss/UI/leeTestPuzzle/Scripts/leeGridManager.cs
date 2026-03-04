using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Placement manager.
/// Works with the ACTIVE runtime grid instance (set by LeeBoardManager).
/// Uses anchored-position based distance checks to avoid world/scale issues.
/// </summary>
public class leeGridManager : MonoBehaviour
{
    public static leeGridManager Instance { get; private set; }

    [Header("Active Grid (set by LeeBoardManager)")]
    public leeGrid grid;

    [Header("Rules (SO)")]
    public LeePlacementRulesSO placementRules;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void SetActiveGrid(leeGrid active)
    {
        grid = active;
    }

    public bool TryPlaceShape(leeShape shape)
    {
        if (grid == null || shape == null) return false;

        var candidateSquares = new List<leeGridSquare>();

        // Collect child blocks (RectTransforms under shape root)
        var blocks = shape.GetComponentsInChildren<RectTransform>();
        RectTransform firstBlock = null;
        RectTransform firstTarget = null;

        foreach (var block in blocks)
        {
            if (block == shape.transform) continue;
            if (firstBlock == null) firstBlock = block;

            leeGridSquare square = FindClosestSquare(block);
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
            sq.SetHighlight(false);
        }

        shape.SetOccupiedSquares(candidateSquares);
        CheckAllPlaceableFilled();
        return true;
    }
    
    private leeGridSquare FindClosestSquare(RectTransform blockRT)
    {
        float minDist = float.MaxValue;
        leeGridSquare result = null;

        var gridRoot = (RectTransform)grid.transform;

        Vector2 blockLocal = (Vector2)gridRoot.InverseTransformPoint(blockRT.position);

        foreach (var sq in grid.GetGridSquares())
        {
            var sqRT = sq.GetComponent<RectTransform>();

        
            Vector2 sqLocal = (Vector2)gridRoot.InverseTransformPoint(sqRT.position);

            float d = Vector2.Distance(blockLocal, sqLocal);
            if (d < minDist)
            {
                minDist = d;
                result = sq;
            }
        }

        if (result == null) return null;

        float gap = (grid.gridAsset != null && grid.gridAsset.visual != null) ? grid.gridAsset.visual.squareGap : 80f;
        float maxAllowed = gap * ((placementRules != null) ? placementRules.maxAllowedDistMultiplier : 0.5f);
        if (minDist > maxAllowed) return null;

        return result;
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
        if (LeeBoardManager.Instance != null && grid != null && grid.gridAsset != null)
            LeeBoardManager.Instance.NotifyGridFilled(grid.gridAsset);
    }

    public void ReleaseShape(leeShape shape)
    {
        var squares = shape.GetOccupiedSquares();
        if (squares == null) return;

        foreach (var sq in squares)
        {
            if (sq == null) continue;
            sq.SetOccupied(false);
            sq.SetHighlight(false);
        }
        shape.SetOccupiedSquares(new List<leeGridSquare>());
    }
}
