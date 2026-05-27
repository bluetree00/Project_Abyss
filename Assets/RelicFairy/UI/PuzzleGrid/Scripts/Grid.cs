using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime Grid instance.
/// Data comes from GridAssetSO (pattern + visual).
/// This component should live on the ROOT of the Grid prefab.
/// </summary>
public class Grid : MonoBehaviour
{
    [Header("Grid Asset")]
    public GridAssetSO gridAsset;

    private readonly List<GridSquare> gridSquares = new();

    /// <summary>
    /// Called by BoardManager right after instantiating the grid prefab.
    /// Safe to call multiple times.
    /// </summary>
    public void Initialize(GridAssetSO asset)
    {
        gridAsset = asset;
        Rebuild();
    }

    public void Rebuild()
    {
        if (gridAsset == null || gridAsset.pattern == null || gridAsset.visual == null)
        {
            Debug.LogError($"{name}: gridAsset/pattern/visual is not set");
            return;
        }
        if (gridAsset.visual.gridSquarePrefab == null)
        {
            Debug.LogError($"{name}: gridSquarePrefab is not set in visual SO");
            return;
        }

        ClearChildren();
        gridSquares.Clear();

        int rows = gridAsset.pattern.rows;
        int cols = gridAsset.pattern.columns;
        float gap = gridAsset.visual.squareGap;

        // Spawn squares
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var squareObj = Object.Instantiate(gridAsset.visual.gridSquarePrefab, transform);
                squareObj.transform.localScale = Vector3.one * gridAsset.visual.squareScale;

                var sq = squareObj.GetComponent<GridSquare>();
                if (sq == null)
                {
                    Debug.LogError("GridSquare prefab must have GridSquare component");
                    Destroy(squareObj);
                    continue;
                }

                // Apply visual colors from SO
                sq.placeableColor = gridAsset.visual.placeableColor;
                sq.blockedColor = gridAsset.visual.blockedColor;

                // Position
                var rt = squareObj.GetComponent<RectTransform>();
                if (rt != null)
                {
                    Vector2 start = gridAsset.visual.startPosition;
                    if (gridAsset.visual.autoCenter)
                    {
                        // Center grid around (0,0) of this grid root
                        float width = (cols - 1) * gap;
                        float height = (rows - 1) * gap;
                        start = new Vector2(-width * 0.5f, height * 0.5f) + gridAsset.visual.centerOffset;
                    }
                    float x = start.x + c * gap;
                    float y = start.y - r * gap;
                    rt.anchoredPosition = new Vector2(x, y);
                }

                // BoxCollider2D 크기를 gap 기준으로 동기화 (ShapeBlock과 동일한 80% 인셋)
                if (squareObj.TryGetComponent<BoxCollider2D>(out var col))
                    col.size = new Vector2(gap * 0.8f, gap * 0.8f);

                bool placeable = gridAsset.pattern.IsPlaceable(r, c);
                sq.Init(r, c, placeable);

                gridSquares.Add(sq);
            }
        }
    }

    private void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
    }

    public List<GridSquare> GetGridSquares() => gridSquares;

    /// <summary>
    /// 미리 생성·배치된 GridSquare 목록으로 그리드를 직접 초기화한다.
    /// Rebuild()를 호출하지 않으므로 gridSquarePrefab 불필요.
    /// MerlinRuneHexGridView가 헥사 셀 위치에 생성한 GridSquare를 등록할 때 사용.
    /// </summary>
    public void InitFromPrebuiltSquares(GridAssetSO asset, List<GridSquare> squares)
    {
        gridAsset = asset;
        gridSquares.Clear();
        if (squares != null)
            gridSquares.AddRange(squares);
    }
}
