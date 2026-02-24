using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems; 

public class BoardManager : MonoBehaviour
{
    public int width = 10;
    public int height = 10;
    public CellUI cellPrefab;
    public RectTransform boardRect;
    
    public GridLayoutGroup gridLayout;

    private CellUI[,] cells;

    void Awake()
    {
        InitBoard();
    }

    void InitBoard()
    {
        cells = new CellUI[width, height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var cell = Instantiate(cellPrefab, boardRect);
                cell.Init(new Vector2Int(x, y), true);
                cells[x, y] = cell;
            }
        }

        SetBlockedCells();
    }

    void SetBlockedCells()
    {
        SetCellPlaceable(new Vector2Int(2,2), false);
        SetCellPlaceable(new Vector2Int(3,2), false);
        SetCellPlaceable(new Vector2Int(4,5), false);
    }

    void SetCellPlaceable(Vector2Int pos, bool placeable)
    {
        if (!IsInBounds(pos)) return;
        cells[pos.x, pos.y].isPlaceable = placeable;
        cells[pos.x, pos.y].UpdateColor();
    }

     public bool IsInBounds(Vector2Int pos)
    {
        return pos.x >= 0 && pos.x < width && pos.y >= 0 && pos.y < height;
    }

    public CellUI GetCell(Vector2Int pos)
    {
        if (!IsInBounds(pos)) return null;
        return cells[pos.x, pos.y];
    }

    public bool TryPlaceBlockUI(TetrominoUI block, PointerEventData eventData)
    {
        Vector2 localPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                boardRect,
                eventData.position,
                eventData.pressEventCamera,
                out localPoint))
        {
            return false;
        }

        Vector2 boardSize = boardRect.rect.size;
        Vector2 originLocal = localPoint + boardSize * 0.5f;

        Vector2 cellSize = gridLayout.cellSize;
        Vector2 spacing = gridLayout.spacing;
        RectOffset padding = gridLayout.padding;

        float xPos = originLocal.x - padding.left;
        float yPos = originLocal.y - padding.bottom;

        int x = Mathf.FloorToInt(xPos / (cellSize.x + spacing.x));
        int y = Mathf.FloorToInt(yPos / (cellSize.y + spacing.y));

        Vector2Int origin = new Vector2Int(x, y);

        return TryPlaceBlockAt(block, origin);
    }

    public bool TryPlaceBlockAt(TetrominoUI block, Vector2Int origin)
    {
        foreach (var offset in block.cellOffsets)
        {
            Vector2Int pos = origin + offset;
            if (!IsInBounds(pos)) return false;

            var cell = GetCell(pos);
            if (!cell.isPlaceable) return false;
            if (cell.occupiedBlock != null) return false;
        }

        foreach (var offset in block.cellOffsets)
        {
            Vector2Int pos = origin + offset;
            var cell = GetCell(pos);
            cell.occupiedBlock = block;
            cell.UpdateColor();
        }

        SnapBlockToOriginCell(block, origin);

        block.placedOrigin = origin;
        block.rectTransform.SetParent(boardRect, true);

        return true;
    }

    public void ClearBlockCells(TetrominoUI block)
    {
        if (!block.placedOrigin.HasValue) return;

        Vector2Int origin = block.placedOrigin.Value;

        foreach (var offset in block.cellOffsets)
        {
            Vector2Int pos = origin + offset;
            if (!IsInBounds(pos)) continue;

            var cell = GetCell(pos);
            if (cell.occupiedBlock == block)
            {
                cell.occupiedBlock = null;
                cell.UpdateColor();
            }
        }
    }

    void SnapBlockToOriginCell(TetrominoUI block, Vector2Int origin)
    {
        var cell = GetCell(origin);
        if (cell == null) return;

        var cellRect = cell.GetComponent<RectTransform>();
        block.rectTransform.anchoredPosition = cellRect.anchoredPosition;
    }
}