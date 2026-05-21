using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 그리드 썸네일 UI 컴포넌트.
/// 미니 그리드 패턴 프리뷰 + 그리드 이름 표시.
/// 클릭 시 BoardManager.EnterGrid() 호출하여 확대 뷰로 전환.
/// </summary>
public class GridThumbnail : MonoBehaviour, IPointerClickHandler
{
    // ── Constants ──
    private static readonly Color PLACEABLE_COLOR     = new(0.3f, 0.7f, 1f, 0.9f);
    private static readonly Color BLOCKED_COLOR       = new(0f, 0f, 0f, 0f);
    private static readonly Color SELECTED_BORDER_COLOR = new(1f, 0.85f, 0.3f, 1f);
    private static readonly Color NORMAL_BORDER_COLOR   = new(0.4f, 0.4f, 0.5f, 0.6f);

    // 아이템별 구분 색상 팔레트
    private static readonly Color[] SHAPE_PALETTE = new[]
    {
        new Color(0.25f, 0.88f, 0.38f, 0.92f),  // 초록
        new Color(0.95f, 0.48f, 0.18f, 0.92f),  // 주황
        new Color(0.72f, 0.28f, 0.95f, 0.92f),  // 보라
        new Color(0.95f, 0.88f, 0.18f, 0.92f),  // 노랑
        new Color(0.18f, 0.58f, 0.95f, 0.92f),  // 파랑
        new Color(0.95f, 0.28f, 0.48f, 0.92f),  // 분홍
        new Color(0.28f, 0.92f, 0.88f, 0.92f),  // 청록
        new Color(0.95f, 0.72f, 0.18f, 0.92f),  // 황금
    };

    // ── SerializeField ──
    [Header("UI 참조")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private RectTransform gridContainer;
    [SerializeField] private Image borderImage;

    // ── Private ──
    private GridAssetData _runtimeData;
    private BoardManager _boardManager;
    private string _gridId;
    private bool _isSelected;
    private Image[,] _cellImages;
    private int _rows;
    private int _cols;

    // ── Properties ──
    public string GridId => _gridId;

    // ── Lifecycle ──
    private void OnDestroy()
    {
        ClearMiniGrid();
    }

    // ── Public Methods ──

    /// <summary>
    /// 프로그래밍 방식 생성 시 UI 참조를 주입한다.
    /// </summary>
    public void Init(TMP_Text name, RectTransform container, Image border)
    {
        nameText = name;
        gridContainer = container;
        borderImage = border;
    }

    /// <summary>
    /// 썸네일 데이터를 설정하고 미니 그리드를 빌드한다.
    /// </summary>
    public void Setup(string gridId, GridAssetData data, string displayName, BoardManager board)
    {
        _gridId = gridId;
        _runtimeData = data;
        _boardManager = board;

        if (nameText != null)
            nameText.text = displayName ?? gridId;

        BuildMiniGrid(data.pattern);
        SetSelected(false);
    }

    /// <summary>선택 상태 시각 피드백.</summary>
    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        if (borderImage != null)
            borderImage.color = selected ? SELECTED_BORDER_COLOR : NORMAL_BORDER_COLOR;
    }

    /// <summary>
    /// 그리드의 점유 상태를 썸네일에 반영한다.
    /// </summary>
    public void RefreshOccupied(System.Collections.Generic.List<GridSquare> squares)
    {
        if (_cellImages == null || squares == null) return;

        // 먼저 모든 셀을 원래 색으로 리셋
        for (int r = 0; r < _rows; r++)
            for (int c = 0; c < _cols; c++)
            {
                if (_cellImages[r, c] == null) continue;
                bool placeable = IsPlaceable(_runtimeData?.pattern, r, c);
                _cellImages[r, c].color = placeable ? PLACEABLE_COLOR : BLOCKED_COLOR;
            }

        // 점유된 셀을 아이템별 색상으로 표시
        foreach (var sq in squares)
        {
            if (sq == null || !sq.isOccupied) continue;
            int r = sq.row, c = sq.col;
            if (r >= 0 && r < _rows && c >= 0 && c < _cols && _cellImages[r, c] != null)
                _cellImages[r, c].color = ItemColor(sq.occupyingItem);
        }
    }

    // ── Private Methods ──

    private void BuildMiniGrid(GridPatternData pattern)
    {
        if (gridContainer == null || pattern == null) return;

        ClearMiniGrid();

        int rows = pattern.rows;
        int cols = pattern.columns;
        if (rows <= 0 || cols <= 0) return;

        // 컨테이너 크기에 맞춰 셀 크기 계산
        float containerW = gridContainer.rect.width;
        float containerH = gridContainer.rect.height;

        // rect가 아직 계산 안 됐을 수 있으므로 sizeDelta 폴백
        if (containerW <= 0) containerW = gridContainer.sizeDelta.x;
        if (containerH <= 0) containerH = gridContainer.sizeDelta.y;
        if (containerW <= 0) containerW = 120f;
        if (containerH <= 0) containerH = 120f;

        float gap = 2f;
        float cellSize = Mathf.Min(
            (containerW - gap * (cols - 1)) / cols,
            (containerH - gap * (rows - 1)) / rows
        );
        cellSize = Mathf.Max(cellSize, 4f);

        float totalW = cols * cellSize + (cols - 1) * gap;
        float totalH = rows * cellSize + (rows - 1) * gap;
        float startX = -totalW * 0.5f + cellSize * 0.5f;
        float startY = totalH * 0.5f - cellSize * 0.5f;

        _rows = rows;
        _cols = cols;
        _cellImages = new Image[rows, cols];

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                bool placeable = IsPlaceable(pattern, r, c);

                var cellGO = new GameObject($"C{r}_{c}", typeof(RectTransform), typeof(Image));
                cellGO.transform.SetParent(gridContainer, false);

                var rt = cellGO.GetComponent<RectTransform>();
                rt.sizeDelta = Vector2.one * cellSize;
                rt.anchoredPosition = new Vector2(
                    startX + c * (cellSize + gap),
                    startY - r * (cellSize + gap)
                );

                var img = cellGO.GetComponent<Image>();
                img.color = placeable ? PLACEABLE_COLOR : BLOCKED_COLOR;
                img.raycastTarget = false;

                // placeable 셀에만 테두리 (그리드 모양이 또렷이 보이도록 — blocked는 투명)
                if (placeable)
                {
                    var outline = cellGO.AddComponent<Outline>();
                    outline.effectColor = new Color(0.05f, 0.05f, 0.1f, 0.9f);
                    outline.effectDistance = new Vector2(1.5f, -1.5f);
                }

                _cellImages[r, c] = img;
            }
        }
    }

    private void ClearMiniGrid()
    {
        if (gridContainer == null) return;
        for (int i = gridContainer.childCount - 1; i >= 0; i--)
            Destroy(gridContainer.GetChild(i).gameObject);
    }

    public static Color GetItemColor(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId)) return SHAPE_PALETTE[0];
        int hash = System.Math.Abs(instanceId.GetHashCode());
        return SHAPE_PALETTE[hash % SHAPE_PALETTE.Length];
    }

    private static Color ItemColor(RuntimeItemData item)
    {
        return GetItemColor(item?.instanceId);
    }

    private static bool IsPlaceable(GridPatternData pattern, int row, int col)
    {
        if (pattern.rows01 == null) return true;
        if (row >= pattern.rows01.Length) return false;
        if (col >= pattern.rows01[row].Length) return false;
        // rows01[row][col]은 char ('1' 또는 '0'). char vs int 비교는 항상 false라 char로 비교
        return pattern.rows01[row][col] == '1';
    }

    // ── Event Handlers ──

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_boardManager == null || _runtimeData == null) return;
        _boardManager.gameObject.SetActive(true);
        _boardManager.EnterGrid(_runtimeData);
    }
}
