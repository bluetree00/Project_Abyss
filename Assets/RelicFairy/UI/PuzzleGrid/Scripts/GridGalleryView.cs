using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 갤러리 뷰 — 보드 썸네일 목록 표시.
///
/// ■ BlockSynergyBridge가 등록한 GridAssetData 기반으로 썸네일 생성.
/// ■ 썸네일 클릭 → UI_GridPanel.EnterEditMode(gridId).
/// ■ RefreshThumbnails(): 채움 상태(occupied) 반영.
/// </summary>
public sealed class GridGalleryView : MonoBehaviour
{
    // ── Constants ──
    private const float THUMB_WIDTH   = 200f;
    private const float THUMB_HEIGHT  = 240f;
    private const float THUMB_SPACING = 16f;
    private const int   COLUMNS       = 3;

    private static readonly Color COLOR_FILL_TEXT  = new(0.85f, 0.92f, 1f,  1f);
    private static readonly Color COLOR_FULL_TEXT  = new(0.3f,  1f,   0.55f, 1f);
    private static readonly Color COLOR_BORDER_SEL = new(1f,  0.85f, 0.3f, 1f);
    private static readonly Color COLOR_BORDER_NRM = new(0.35f, 0.35f, 0.45f, 0.7f);

    // ── SerializeField ──
    [Header("컨텐츠 루트")]
    [SerializeField] private RectTransform gridContent;
    [SerializeField] private TMP_FontAsset thumbFont;

    // ── Events ──
    public event System.Action<string> OnGridSelected;

    // ── Private ──
    private readonly List<ThumbEntry> _thumbs = new();
    private string _selectedGridId;

    private sealed class ThumbEntry
    {
        public string         gridId;
        public GridAssetData  data;
        public GameObject     go;
        public GridThumbnail  thumb;
        public TMP_Text       fillText;
        public Image          border;
    }

    // ── Lifecycle ──
    private void OnEnable()
    {
        Refresh();
    }

    // ── Public API ──

    /// <summary>썸네일 목록 전체 재빌드.</summary>
    public void Refresh()
    {
        ClearThumbs();

        var bridge = BlockSynergyBridge.Instance;
        if (bridge == null || gridContent == null) return;

        var grids = bridge.GetRegisteredGrids();
        if (grids == null) return;

        int index = 0;
        foreach (var pair in grids)
        {
            CreateThumb(pair.Key, pair.Value, index);
            index++;
        }

        RebuildLayout();

        if (_thumbs.Count > 0)
        {
            bool stillValid = _thumbs.Exists(e => e.gridId == _selectedGridId);
            if (!stillValid)
                _selectedGridId = _thumbs[0].gridId;
            RefreshBorderHighlights();
        }
    }

    /// <summary>각 썸네일의 채움 상태만 갱신. Shape 배치/제거 시 호출.</summary>
    public void RefreshThumbnails()
    {
        var bridge = BlockSynergyBridge.Instance;
        if (bridge == null) return;

        foreach (var entry in _thumbs)
        {
            var squares = BoardManager.Instance?.GetGridSquares(entry.gridId);
            if (squares == null) continue;
            entry.thumb?.RefreshOccupied(squares);
            RefreshFillText(entry, squares);
        }
    }

    // ── Thumb Creation ──

    private void CreateThumb(string gridId, GridAssetData data, int index)
    {
        if (gridContent == null) return;

        var thumbGO = new GameObject($"Thumb_{gridId}", typeof(RectTransform));
        thumbGO.transform.SetParent(gridContent, false);

        var rt = thumbGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(THUMB_WIDTH, THUMB_HEIGHT);

        // 배경
        var bgImg = thumbGO.AddComponent<Image>();
        bgImg.color = new Color(0.1f, 0.1f, 0.16f, 0.95f);

        // 테두리
        var borderGO  = new GameObject("Border", typeof(RectTransform));
        borderGO.transform.SetParent(thumbGO.transform, false);
        var borderRT  = borderGO.GetComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero; borderRT.anchorMax = Vector2.one;
        borderRT.sizeDelta = Vector2.zero;
        var borderImg = borderGO.AddComponent<Image>();
        borderImg.color = COLOR_BORDER_NRM;
        var outline     = borderGO.AddComponent<Outline>();
        outline.effectColor    = COLOR_BORDER_NRM;
        outline.effectDistance = new Vector2(2f, -2f);

        // GridThumbnail 컴포넌트 + 미니 그리드 컨테이너
        var containerGO = new GameObject("MiniGrid", typeof(RectTransform));
        containerGO.transform.SetParent(thumbGO.transform, false);
        var containerRT = containerGO.GetComponent<RectTransform>();
        containerRT.anchorMin        = new Vector2(0.05f, 0.25f);
        containerRT.anchorMax        = new Vector2(0.95f, 0.90f);
        containerRT.sizeDelta        = Vector2.zero;
        containerRT.anchoredPosition = Vector2.zero;

        var nameTxtGO = new GameObject("NameText", typeof(RectTransform));
        nameTxtGO.transform.SetParent(thumbGO.transform, false);
        var nameRT  = nameTxtGO.GetComponent<RectTransform>();
        nameRT.anchorMin        = new Vector2(0f, 0f);
        nameRT.anchorMax        = new Vector2(1f, 0.22f);
        nameRT.sizeDelta        = Vector2.zero;
        nameRT.anchoredPosition = new Vector2(0f, 4f);
        var nameTxt = nameTxtGO.AddComponent<TextMeshProUGUI>();
        if (thumbFont != null) nameTxt.font = thumbFont;
        nameTxt.fontSize   = 13f;
        nameTxt.color      = COLOR_FILL_TEXT;
        nameTxt.alignment  = TextAlignmentOptions.Center;

        // 채움 텍스트 (하단)
        var fillTxtGO = new GameObject("FillText", typeof(RectTransform));
        fillTxtGO.transform.SetParent(thumbGO.transform, false);
        var fillRT  = fillTxtGO.GetComponent<RectTransform>();
        fillRT.anchorMin        = new Vector2(0f, 0f);
        fillRT.anchorMax        = new Vector2(1f, 0.18f);
        fillRT.sizeDelta        = Vector2.zero;
        fillRT.anchoredPosition = Vector2.zero;
        var fillTxt = fillTxtGO.AddComponent<TextMeshProUGUI>();
        if (thumbFont != null) fillTxt.font = thumbFont;
        fillTxt.fontSize  = 12f;
        fillTxt.alignment = TextAlignmentOptions.Center;
        fillTxt.color     = COLOR_FILL_TEXT;

        var thumb = thumbGO.AddComponent<GridThumbnail>();
        thumb.Init(nameTxt, containerRT, borderImg);
        thumb.Setup(gridId, data, data.displayName ?? gridId, BoardManager.Instance);

        // 클릭 버튼
        var btn = thumbGO.AddComponent<Button>();
        btn.targetGraphic = bgImg;
        var capturedGridId = gridId;
        btn.onClick.AddListener(() => OnThumbClicked(capturedGridId));

        var entry = new ThumbEntry
        {
            gridId   = gridId,
            data     = data,
            go       = thumbGO,
            thumb    = thumb,
            fillText = fillTxt,
            border   = borderImg,
        };
        _thumbs.Add(entry);
    }

    private void RebuildLayout()
    {
        for (int i = 0; i < _thumbs.Count; i++)
        {
            int col = i % COLUMNS;
            int row = i / COLUMNS;

            var rt = _thumbs[i].go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(
                THUMB_SPACING + col * (THUMB_WIDTH  + THUMB_SPACING),
                -(THUMB_SPACING + row * (THUMB_HEIGHT + THUMB_SPACING)));
        }

        int totalRows = (_thumbs.Count + COLUMNS - 1) / COLUMNS;
        float totalH  = totalRows * (THUMB_HEIGHT + THUMB_SPACING) + THUMB_SPACING;
        if (gridContent != null)
            gridContent.sizeDelta = new Vector2(gridContent.sizeDelta.x, totalH);
    }

    private void ClearThumbs()
    {
        foreach (var entry in _thumbs)
            if (entry.go != null) Destroy(entry.go);
        _thumbs.Clear();
    }

    private static void RefreshFillText(ThumbEntry entry, List<GridSquare> squares)
    {
        if (entry.fillText == null) return;

        int total = 0, occupied = 0;
        foreach (var sq in squares)
        {
            if (!sq.isPlaceable) continue;
            total++;
            if (sq.isOccupied) occupied++;
        }

        bool isFull = total > 0 && occupied >= total;
        entry.fillText.text  = isFull ? "FULL ✓" : $"{occupied}/{total}";
        entry.fillText.color = isFull ? COLOR_FULL_TEXT : COLOR_FILL_TEXT;
    }

    /// <summary>현재 선택된 그리드 ID를 반환한다. UI_GridPanel이 초기 진입 시 기본 선택 설정에 사용.</summary>
    public string GetSelectedGridId() => _selectedGridId;

    // ── Event Handlers ──

    private void OnThumbClicked(string gridId)
    {
        _selectedGridId = gridId;
        RefreshBorderHighlights();
        OnGridSelected?.Invoke(gridId);
    }

    private void RefreshBorderHighlights()
    {
        foreach (var entry in _thumbs)
        {
            if (entry.border == null) continue;
            bool selected = entry.gridId == _selectedGridId;
            entry.border.color = selected ? COLOR_BORDER_SEL : COLOR_BORDER_NRM;
            var ol = entry.border.GetComponent<Outline>();
            if (ol != null) ol.effectColor = selected ? COLOR_BORDER_SEL : COLOR_BORDER_NRM;
        }
    }
}
