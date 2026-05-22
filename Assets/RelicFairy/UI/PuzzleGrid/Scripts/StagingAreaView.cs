using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 보관함 하단 패널.
///
/// ■ 5개 고정 슬롯을 항상 표시 (빈 슬롯 포함).
/// ■ RunItemInventory.StagingItems 기준으로 슬롯 0..n 에 아이템 카드 렌더링.
/// ■ 신규 아이템: 빛나는 테두리 + NEW 뱃지.
/// ■ [X] 버튼: 해당 아이템 폐기 (UI_GridPanel.OnDialogDiscardAll과 별개, 개별 폐기).
/// ■ 카드 클릭: ItemInfoPanel에 해당 아이템 정보 표시.
/// ■ Shape 생성: 카드 생성 시 BlockDataManager → ShapeAssetSO → BoardManager.SpawnSharedShape.
/// </summary>
public sealed class StagingAreaView : MonoBehaviour
{
    // ── Constants ──
    private const float SLOT_WIDTH    = 110f;
    private const float SLOT_HEIGHT   = 130f;
    private const float SLOT_SPACING  = 10f;
    private const float GRID_CELL_SIZE = 120f;

    private static readonly Color COLOR_NEW_BORDER      = new(1f, 0.92f, 0.3f, 1f);
    private static readonly Color COLOR_NORMAL_BORDER   = new(0.4f, 0.4f, 0.5f, 0.7f);
    private static readonly Color COLOR_SELECTED_BORDER = new(0.3f, 0.85f, 1f, 1f);
    private static readonly Color COLOR_EMPTY_BG        = new(0.08f, 0.08f, 0.12f, 0.6f);
    private static readonly Color COLOR_EMPTY_BORDER    = new(0.3f, 0.3f, 0.4f, 0.4f);

    private static readonly Color COLOR_COMMON    = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Color COLOR_RARE      = new(0.3f, 0.6f, 1f,   1f);
    private static readonly Color COLOR_EPIC      = new(0.7f, 0.3f, 1f,   1f);
    private static readonly Color COLOR_LEGENDARY = new(1f,   0.7f, 0.2f, 1f);

    // ── SerializeField ──
    [Header("스크롤 컨텐츠")]
    [SerializeField] private RectTransform scrollContent;

    [Header("카드 폰트 (없으면 기본 폰트)")]
    [SerializeField] private TMP_FontAsset cardFont;

    [Header("참조")]
    [SerializeField] private BoardManager boardManager;

    // ── Events ──
    /// <summary>보관함 카드 클릭 시 발생. UI_GridPanel에서 패널 전환을 처리한다.</summary>
    public event System.Action<RuntimeItemData> OnItemSelected;
    /// <summary>보관함 카드 호버 시 발생.</summary>
    public event System.Action<RuntimeItemData> OnItemHovered;
    /// <summary>보관함 카드 호버 해제 시 발생.</summary>
    public event System.Action                  OnItemUnhovered;

    // ── Private ──
    // 고정 슬롯 구조
    private readonly GameObject[] _slotGOs       = new GameObject[RunItemInventory.MaxStagingCapacity];
    private readonly RuntimeItemData[] _slotItems = new RuntimeItemData[RunItemInventory.MaxStagingCapacity];

    // Shape 관리
    private readonly Dictionary<string, Shape> _shapeByInstanceId = new();
    private readonly HashSet<string>           _newItemIds         = new();

    // 섹션 구분선
    private GameObject _sectionDivider;

    // 선택 하이라이트 (현재 하이라이트된 슬롯 인덱스)
    private RuntimeItemData _highlightedItem;

    // 폐기 확인 다이얼로그
    private RuntimeItemData _pendingDiscard;
    private GameObject      _discardDialog;
    private TMP_Text        _discardDialogText;

    // ── Lifecycle ──
    private void Awake()
    {
        if (boardManager == null)
            boardManager = BoardManager.Instance;
        BuildFixedSlots();
        BuildDiscardDialog();
    }

    private void OnDestroy()
    {
        HideDiscardDialog();
        _shapeByInstanceId.Clear();
        _newItemIds.Clear();
    }

    // ── Public API ──

    /// <summary>인벤토리 기반으로 슬롯을 동기화한다. UI_GridPanel.OnStagingChanged에서 호출.</summary>
    public void Refresh(RunItemInventory inventory)
    {
        if (inventory == null) return;

        var stagingItems = inventory.StagingItems;

        // NEW 감지 (전체 목록 선행 스캔)
        foreach (var item in stagingItems)
        {
            if (item == null || _newItemIds.Contains(item.instanceId)) continue;
            bool wasKnown = false;
            for (int j = 0; j < RunItemInventory.MaxStagingCapacity; j++)
                if (_slotItems[j] == item) { wasKnown = true; break; }
            if (!wasKnown) _newItemIds.Add(item.instanceId);
        }

        // 섹션 정렬: 미배치(non-new) 먼저, 새로 얻은 아이템 나중
        var nonNew  = new List<RuntimeItemData>();
        var newList = new List<RuntimeItemData>();
        foreach (var item in stagingItems)
        {
            if (item == null) continue;
            if (_newItemIds.Contains(item.instanceId)) newList.Add(item);
            else nonNew.Add(item);
        }
        var sorted = new List<RuntimeItemData>(nonNew);
        sorted.AddRange(newList);

        for (int i = 0; i < RunItemInventory.MaxStagingCapacity; i++)
        {
            RuntimeItemData item = i < sorted.Count ? sorted[i] : null;
            _slotItems[i] = item;
            RefreshSlotDisplay(i, item);
            if (item != null) EnsureShapeExists(item);
        }

        UpdateSectionDivider(nonNew.Count, newList.Count);

        // 더 이상 보관함에 없는 아이템의 추적 정보만 해제
        var activeIds = new HashSet<string>();
        foreach (var it in stagingItems)
            if (it != null) activeIds.Add(it.instanceId);

        var toRemove = new List<string>();
        foreach (var kvp in _shapeByInstanceId)
            if (!activeIds.Contains(kvp.Key)) toRemove.Add(kvp.Key);

        foreach (var id in toRemove)
        {
            _shapeByInstanceId.Remove(id);
            _newItemIds.Remove(id);
        }
    }

    /// <summary>카드 테두리를 하이라이트한다. RefreshInfoPanelDefault에서 선택된 아이템을 표시할 때 호출.</summary>
    public void HighlightItem(RuntimeItemData item)
    {
        // 이전 하이라이트 해제
        if (_highlightedItem != null)
        {
            int prevIdx = FindSlotIndex(_highlightedItem);
            if (prevIdx >= 0) ApplySlotBorderColor(prevIdx, _highlightedItem);
        }

        _highlightedItem = item;

        if (item != null)
        {
            int idx = FindSlotIndex(item);
            if (idx >= 0 && _slotGOs[idx] != null)
            {
                var border = _slotGOs[idx].transform.Find("Border")?.GetComponent<Image>();
                if (border != null) border.color = COLOR_SELECTED_BORDER;
            }
        }
    }

    /// <summary>특정 아이템의 Shape를 제거한다. 폐기 시 UI_GridPanel에서 호출.</summary>
    public void RemoveShapeForItem(RuntimeItemData item)
    {
        if (item == null) return;
        if (_shapeByInstanceId.TryGetValue(item.instanceId, out var shape))
        {
            if (boardManager == null) boardManager = BoardManager.Instance;
            boardManager?.RemoveSharedShape(shape);
            _shapeByInstanceId.Remove(item.instanceId);
        }
    }

    // ── Fixed Slot Build ──

    private void BuildFixedSlots()
    {
        if (scrollContent == null) return;

        for (int i = 0; i < RunItemInventory.MaxStagingCapacity; i++)
        {
            var slotGO = new GameObject($"Slot_{i}", typeof(RectTransform));
            slotGO.transform.SetParent(scrollContent, false);
            var rt = slotGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(SLOT_WIDTH, SLOT_HEIGHT);
            PositionSlot(rt, i);

            // 빈 슬롯 배경
            var bg = slotGO.AddComponent<Image>();
            bg.color = COLOR_EMPTY_BG;

            // 테두리
            var borderGO = new GameObject("Border", typeof(RectTransform));
            borderGO.transform.SetParent(slotGO.transform, false);
            var borderRT = borderGO.GetComponent<RectTransform>();
            borderRT.anchorMin = Vector2.zero;
            borderRT.anchorMax = Vector2.one;
            borderRT.sizeDelta = Vector2.zero;
            var borderImg = borderGO.AddComponent<Image>();
            borderImg.color = COLOR_EMPTY_BORDER;
            var outline = borderGO.AddComponent<Outline>();
            outline.effectColor    = COLOR_EMPTY_BORDER;
            outline.effectDistance = new Vector2(2f, -2f);

            // 빈 슬롯 텍스트 (기본 표시)
            var emptyTxtGO = new GameObject("EmptyLabel", typeof(RectTransform));
            emptyTxtGO.transform.SetParent(slotGO.transform, false);
            var emptyRT = emptyTxtGO.GetComponent<RectTransform>();
            emptyRT.anchorMin = Vector2.zero;
            emptyRT.anchorMax = Vector2.one;
            emptyRT.sizeDelta = Vector2.zero;
            var emptyTxt = emptyTxtGO.AddComponent<TextMeshProUGUI>();
            if (cardFont != null) emptyTxt.font = cardFont;
            emptyTxt.text      = "빈 슬롯";
            emptyTxt.fontSize  = 11f;
            emptyTxt.color     = new Color(0.4f, 0.4f, 0.5f, 0.5f);
            emptyTxt.alignment = TextAlignmentOptions.Center;
            emptyTxt.raycastTarget = false;

            var hover = slotGO.AddComponent<SlotHoverHandler>();
            hover.onEnter = item => OnItemHovered?.Invoke(item);
            hover.onExit  = ()   => OnItemUnhovered?.Invoke();

            _slotGOs[i] = slotGO;
        }

        BuildSectionDivider();

        // scrollContent 폭 설정
        float totalWidth = RunItemInventory.MaxStagingCapacity * (SLOT_WIDTH + SLOT_SPACING) + SLOT_SPACING;
        scrollContent.sizeDelta = new Vector2(totalWidth, scrollContent.sizeDelta.y);
    }

    private void RefreshSlotDisplay(int index, RuntimeItemData item)
    {
        if (index < 0 || index >= RunItemInventory.MaxStagingCapacity) return;
        var slotGO = _slotGOs[index];
        if (slotGO == null) return;

        // 기존 아이템 컨텐츠 제거 (Border, EmptyLabel 제외)
        for (int i = slotGO.transform.childCount - 1; i >= 0; i--)
        {
            var child = slotGO.transform.GetChild(i);
            string n = child.name;
            if (n != "Border" && n != "EmptyLabel")
                Destroy(child.gameObject);
        }

        var bgImg     = slotGO.GetComponent<Image>();
        var borderImg = slotGO.transform.Find("Border")?.GetComponent<Image>();
        var emptyLbl  = slotGO.transform.Find("EmptyLabel")?.gameObject;

        var hover = slotGO.GetComponent<SlotHoverHandler>();
        if (hover != null) hover.item = item;

        if (item == null)
        {
            // 빈 슬롯 상태
            if (bgImg != null)     bgImg.color     = COLOR_EMPTY_BG;
            if (borderImg != null)
            {
                borderImg.color = COLOR_EMPTY_BORDER;
                var ol = borderImg.GetComponent<Outline>();
                if (ol != null) ol.effectColor = COLOR_EMPTY_BORDER;
            }
            if (emptyLbl != null)  emptyLbl.SetActive(true);
        }
        else
        {
            // 아이템 슬롯 상태
            if (emptyLbl != null)  emptyLbl.SetActive(false);
            bool isNew = _newItemIds.Contains(item.instanceId);

            if (bgImg != null)     bgImg.color     = new Color(0.12f, 0.12f, 0.18f, 0.95f);
            if (borderImg != null)
            {
                var borderColor = (_highlightedItem == item) ? COLOR_SELECTED_BORDER
                                  : isNew ? COLOR_NEW_BORDER : COLOR_NORMAL_BORDER;
                borderImg.color = borderColor;
                var ol = borderImg.GetComponent<Outline>();
                if (ol != null) ol.effectColor = borderColor;
            }

            BuildCardContent(slotGO, item, isNew);
        }
    }

    private void BuildCardContent(GameObject slotGO, RuntimeItemData item, bool isNew)
    {
        // 레어도 라인 (상단)
        var rarityBar = new GameObject("RarityBar", typeof(RectTransform));
        rarityBar.transform.SetParent(slotGO.transform, false);
        var rbRT = rarityBar.GetComponent<RectTransform>();
        rbRT.anchorMin        = new Vector2(0f, 1f);
        rbRT.anchorMax        = new Vector2(1f, 1f);
        rbRT.sizeDelta        = new Vector2(0f, 5f);
        rbRT.anchoredPosition = Vector2.zero;
        var rbImg = rarityBar.AddComponent<Image>();
        rbImg.color = RarityColor(item.rarity);

        // 아이콘 (위쪽으로 올려 모양 프리뷰 공간 확보)
        if (item.icon != null)
        {
            var iconGO  = new GameObject("Icon", typeof(RectTransform));
            iconGO.transform.SetParent(slotGO.transform, false);
            var iconRT  = iconGO.GetComponent<RectTransform>();
            iconRT.anchorMin       = new Vector2(0.1f, 0.55f);
            iconRT.anchorMax       = new Vector2(0.9f, 0.88f);
            iconRT.sizeDelta       = Vector2.zero;
            var iconImg            = iconGO.AddComponent<Image>();
            iconImg.sprite         = item.icon;
            iconImg.preserveAspect = true;
        }

        // 모양 프리뷰 (아이콘 아래, 이름 위)
        var shapePreviewGO = new GameObject("ShapePreview", typeof(RectTransform));
        shapePreviewGO.transform.SetParent(slotGO.transform, false);
        var shapePreviewRT = shapePreviewGO.GetComponent<RectTransform>();
        shapePreviewRT.anchorMin        = new Vector2(0f, 0.30f);
        shapePreviewRT.anchorMax        = new Vector2(1f, 0.54f);
        shapePreviewRT.sizeDelta        = Vector2.zero;
        shapePreviewRT.anchoredPosition = Vector2.zero;
        BuildCardShapePreview(shapePreviewRT, item);

        // 이름
        var nameTxtGO = new GameObject("Name", typeof(RectTransform));
        nameTxtGO.transform.SetParent(slotGO.transform, false);
        var nameRT = nameTxtGO.GetComponent<RectTransform>();
        nameRT.anchorMin        = new Vector2(0f, 0.04f);
        nameRT.anchorMax        = new Vector2(1f, 0.28f);
        nameRT.sizeDelta        = Vector2.zero;
        nameRT.anchoredPosition = Vector2.zero;
        var nameTxt = nameTxtGO.AddComponent<TextMeshProUGUI>();
        if (cardFont != null) nameTxt.font = cardFont;
        nameTxt.text              = item.displayName ?? item.itemId;
        nameTxt.fontSize          = 11f;
        nameTxt.alignment         = TextAlignmentOptions.Center;
        nameTxt.enableWordWrapping = true;

        // NEW 뱃지
        if (isNew)
        {
            var badgeGO = new GameObject("NewBadge", typeof(RectTransform));
            badgeGO.transform.SetParent(slotGO.transform, false);
            var badgeBG = badgeGO.AddComponent<Image>();
            badgeBG.color = new Color(1f, 0.3f, 0.3f, 0.95f);
            var badgeRT = badgeGO.GetComponent<RectTransform>();
            badgeRT.anchorMin        = new Vector2(0f, 1f);
            badgeRT.anchorMax        = new Vector2(0f, 1f);
            badgeRT.pivot            = new Vector2(0f, 1f);
            badgeRT.sizeDelta        = new Vector2(34f, 16f);
            badgeRT.anchoredPosition = new Vector2(2f, -2f);

            var badgeTxtGO = new GameObject("NewBadgeText", typeof(RectTransform));
            badgeTxtGO.transform.SetParent(badgeGO.transform, false);
            var badgeTxtRT = badgeTxtGO.GetComponent<RectTransform>();
            badgeTxtRT.anchorMin = Vector2.zero;
            badgeTxtRT.anchorMax = Vector2.one;
            badgeTxtRT.sizeDelta = Vector2.zero;
            var badgeTxt = badgeTxtGO.AddComponent<TextMeshProUGUI>();
            if (cardFont != null) badgeTxt.font = cardFont;
            badgeTxt.text          = "NEW";
            badgeTxt.fontSize      = 9f;
            badgeTxt.alignment     = TextAlignmentOptions.Center;
            badgeTxt.color         = Color.white;
            badgeTxt.raycastTarget = false;
        }

        // [X] 폐기 버튼
        var xBtnGO = new GameObject("DiscardBtn", typeof(RectTransform));
        xBtnGO.transform.SetParent(slotGO.transform, false);
        var xRT = xBtnGO.GetComponent<RectTransform>();
        xRT.anchorMin        = new Vector2(1f, 1f);
        xRT.anchorMax        = new Vector2(1f, 1f);
        xRT.pivot            = new Vector2(1f, 1f);
        xRT.sizeDelta        = new Vector2(22f, 22f);
        xRT.anchoredPosition = new Vector2(-2f, -2f);
        var xImg = xBtnGO.AddComponent<Image>();
        xImg.color = new Color(0.8f, 0.2f, 0.2f, 0.9f);
        var xBtn = xBtnGO.AddComponent<Button>();
        xBtn.targetGraphic = xImg;

        var xTxtGO = new GameObject("X", typeof(RectTransform));
        xTxtGO.transform.SetParent(xBtnGO.transform, false);
        var xTxtRT = xTxtGO.GetComponent<RectTransform>();
        xTxtRT.anchorMin = Vector2.zero;
        xTxtRT.anchorMax = Vector2.one;
        xTxtRT.sizeDelta = Vector2.zero;
        var xTxt = xTxtGO.AddComponent<TextMeshProUGUI>();
        if (cardFont != null) xTxt.font = cardFont;
        xTxt.text          = "X";
        xTxt.fontSize      = 12f;
        xTxt.alignment     = TextAlignmentOptions.Center;
        xTxt.color         = Color.white;
        xTxt.raycastTarget = false;

        var capturedItem = item;
        xBtn.onClick.AddListener(() => OnDiscardClicked(capturedItem));

        // 슬롯 클릭 버튼 (배경 이미지에 Button 추가)
        var clickBtn = slotGO.GetComponent<Button>() ?? slotGO.AddComponent<Button>();
        var clickBtnImg = slotGO.GetComponent<Image>();
        clickBtn.targetGraphic = clickBtnImg;
        clickBtn.onClick.RemoveAllListeners();
        clickBtn.onClick.AddListener(() => OnCardClicked(capturedItem));
    }

    // ── Shape Management ──

    private void EnsureShapeExists(RuntimeItemData item)
    {
        if (item == null || item.shapeId == 0)
        {
            Debug.LogWarning($"[StagingAreaView] EnsureShapeExists 조기반환: shapeId={item?.shapeId} (item={item?.itemId})");
            return;
        }
        if (_shapeByInstanceId.ContainsKey(item.instanceId)) return;

        if (boardManager == null) boardManager = BoardManager.Instance;

        // 이미 풀에 있는 Shape(배치됐다가 다시 보관함으로 돌아온 경우)를 재사용해 복사 방지
        if (boardManager != null)
        {
            var existing = boardManager.GetSharedShapeByItem(item.instanceId);
            if (existing != null)
            {
                _shapeByInstanceId[item.instanceId] = existing;
                return;
            }
        }

        if (boardManager == null)
        {
            Debug.LogWarning($"[StagingAreaView] BoardManager null — shape 생성 불가 (item={item.itemId})");
            return;
        }

        var blockData = Managers.BlockData;
        if (blockData == null)
        {
            Debug.LogWarning($"[StagingAreaView] BlockDataManager null — shape 생성 불가 (item={item.itemId})");
            return;
        }

        var shapeEntry = blockData.GetShape(item.shapeId);
        if (shapeEntry == null)
        {
            Debug.LogWarning($"[StagingAreaView] shapeId={item.shapeId} 데이터 없음 (item={item.itemId})");
            return;
        }

        var offsets = BlockDataManager.ParseCellOffsets(shapeEntry);

        var shapeSO = ScriptableObject.CreateInstance<ShapeAssetSO>();
        shapeSO.shapeName        = shapeEntry.shape_name;
        shapeSO.shapeBlockPrefab = boardManager.defaultShapeBlockPrefab;
        shapeSO.cellOffsets      = offsets;
        shapeSO.cellSize         = shapeEntry.cell_size > 0 ? shapeEntry.cell_size : GRID_CELL_SIZE;

        var shape = boardManager.SpawnSharedShape(shapeSO);
        if (shape != null)
        {
            shape.BindItem(item);
            _shapeByInstanceId[item.instanceId] = shape;
        }
    }

    // ── Section Divider ──

    private void BuildSectionDivider()
    {
        if (scrollContent == null) return;

        _sectionDivider = new GameObject("SectionDivider", typeof(RectTransform));
        _sectionDivider.transform.SetParent(scrollContent, false);

        var rt = _sectionDivider.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 0.5f);
        rt.anchorMax        = new Vector2(0f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = new Vector2(SLOT_SPACING, SLOT_HEIGHT * 0.75f);
        rt.anchoredPosition = Vector2.zero;

        var lineGO = new GameObject("Line", typeof(RectTransform));
        lineGO.transform.SetParent(_sectionDivider.transform, false);
        var lineRT = lineGO.GetComponent<RectTransform>();
        lineRT.anchorMin = new Vector2(0.5f, 0.08f);
        lineRT.anchorMax = new Vector2(0.5f, 0.82f);
        lineRT.sizeDelta = new Vector2(2f, 0f);
        var lineImg = lineGO.AddComponent<Image>();
        lineImg.color         = new Color(0.95f, 0.82f, 0.3f, 0.55f);
        lineImg.raycastTarget = false;

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(_sectionDivider.transform, false);
        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0f, 0.80f);
        labelRT.anchorMax = new Vector2(1f, 1f);
        labelRT.sizeDelta = Vector2.zero;
        var labelTxt = labelGO.AddComponent<TextMeshProUGUI>();
        if (cardFont != null) labelTxt.font = cardFont;
        labelTxt.text          = "✦";
        labelTxt.fontSize      = 10f;
        labelTxt.alignment     = TextAlignmentOptions.Center;
        labelTxt.color         = new Color(0.95f, 0.82f, 0.3f, 0.85f);
        labelTxt.raycastTarget = false;

        _sectionDivider.SetActive(false);
    }

    private void UpdateSectionDivider(int nonNewCount, int newCount)
    {
        if (_sectionDivider == null) return;

        bool show = nonNewCount > 0 && newCount > 0;
        _sectionDivider.SetActive(show);
        if (!show) return;

        float x = SLOT_SPACING + nonNewCount * (SLOT_WIDTH + SLOT_SPACING) - SLOT_SPACING * 0.5f;
        _sectionDivider.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, 0f);
    }

    // ── Layout Helpers ──

    private static void PositionSlot(RectTransform rt, int index)
    {
        float x = SLOT_SPACING + index * (SLOT_WIDTH + SLOT_SPACING);
        rt.anchorMin        = new Vector2(0f, 0.5f);
        rt.anchorMax        = new Vector2(0f, 0.5f);
        rt.pivot            = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0f);
    }

    private int FindSlotIndex(RuntimeItemData item)
    {
        if (item == null) return -1;
        for (int i = 0; i < RunItemInventory.MaxStagingCapacity; i++)
            if (_slotItems[i] == item) return i;
        return -1;
    }

    private void ApplySlotBorderColor(int index, RuntimeItemData item)
    {
        if (index < 0 || index >= RunItemInventory.MaxStagingCapacity) return;
        var slotGO = _slotGOs[index];
        if (slotGO == null) return;
        var borderImg = slotGO.transform.Find("Border")?.GetComponent<Image>();
        if (borderImg == null) return;

        if (item == null)
        {
            borderImg.color = COLOR_EMPTY_BORDER;
        }
        else
        {
            bool isNew = _newItemIds.Contains(item.instanceId);
            borderImg.color = isNew ? COLOR_NEW_BORDER : COLOR_NORMAL_BORDER;
        }
    }

    // ── Card Shape Preview ──

    private void BuildCardShapePreview(RectTransform root, RuntimeItemData item)
    {
        if (item == null || item.shapeId == 0) return;

        var blockData = Managers.BlockData;
        if (blockData == null) return;

        var shapeEntry = blockData.GetShape(item.shapeId);
        if (shapeEntry == null) return;

        var offsets = BlockDataManager.ParseCellOffsets(shapeEntry);
        if (offsets == null || offsets.Length == 0) return;

        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        foreach (var o in offsets)
        {
            if (o.x < minX) minX = o.x; if (o.x > maxX) maxX = o.x;
            if (o.y < minY) minY = o.y; if (o.y > maxY) maxY = o.y;
        }
        int cols = maxX - minX + 1;
        int rows = maxY - minY + 1;

        const float CELL = 6f;
        const float GAP  = 1f;
        float totalW = cols * CELL + (cols - 1) * GAP;
        float totalH = rows * CELL + (rows - 1) * GAP;
        float startX = -totalW * 0.5f + CELL * 0.5f;
        float startY =  totalH * 0.5f - CELL * 0.5f;

        var color = GridThumbnail.GetItemColor(item.instanceId);

        foreach (var o in offsets)
        {
            int col = o.x - minX;
            int row = maxY - o.y;

            var cellGO = new GameObject($"P{o.x}_{o.y}", typeof(RectTransform), typeof(Image));
            cellGO.transform.SetParent(root, false);
            var rt  = cellGO.GetComponent<RectTransform>();
            rt.sizeDelta        = Vector2.one * CELL;
            rt.anchoredPosition = new Vector2(
                startX + col * (CELL + GAP),
                startY - row * (CELL + GAP));
            var img = cellGO.GetComponent<Image>();
            img.color         = color;
            img.raycastTarget = false;
        }
    }

    // ── Event Handlers ──

    private void OnCardClicked(RuntimeItemData item)
    {
        OnItemSelected?.Invoke(item);
    }

    private void OnDiscardClicked(RuntimeItemData item)
    {
        if (item == null) return;
        ShowDiscardDialog(item);
    }

    // ── Discard Confirm Dialog ──

    private void BuildDiscardDialog()
    {
        _discardDialog = new GameObject("DiscardConfirmDialog", typeof(RectTransform));
        _discardDialog.transform.SetParent(transform, false);

        var rt = _discardDialog.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        var overlay = _discardDialog.AddComponent<Image>();
        overlay.color = new Color(0f, 0f, 0f, 0.55f);
        overlay.raycastTarget = true;

        var panelGO = new GameObject("Panel", typeof(RectTransform));
        panelGO.transform.SetParent(_discardDialog.transform, false);
        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRT.pivot            = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta        = new Vector2(280f, 110f);
        panelRT.anchoredPosition = Vector2.zero;
        var panelBG = panelGO.AddComponent<Image>();
        panelBG.color = new Color(0.1f, 0.1f, 0.16f, 0.98f);

        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(panelGO.transform, false);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0f, 0.42f);
        textRT.anchorMax = Vector2.one;
        textRT.sizeDelta = Vector2.zero;
        _discardDialogText = textGO.AddComponent<TextMeshProUGUI>();
        if (cardFont != null) _discardDialogText.font = cardFont;
        _discardDialogText.fontSize  = 13f;
        _discardDialogText.alignment = TextAlignmentOptions.Center;
        _discardDialogText.color     = new Color(0.9f, 0.9f, 0.95f, 1f);

        BuildDialogButton(panelGO, "YesBtn",
            new Vector2(0.08f, 0f), new Vector2(0.45f, 0.4f),
            new Color(0.75f, 0.18f, 0.18f, 1f), "폐기", OnDiscardConfirm);

        BuildDialogButton(panelGO, "NoBtn",
            new Vector2(0.55f, 0f), new Vector2(0.92f, 0.4f),
            new Color(0.25f, 0.25f, 0.32f, 1f), "취소", HideDiscardDialog);

        _discardDialog.SetActive(false);
    }

    private void BuildDialogButton(GameObject parent, string goName,
        Vector2 anchorMin, Vector2 anchorMax, Color bgColor, string label,
        UnityEngine.Events.UnityAction callback)
    {
        var btnGO = new GameObject(goName, typeof(RectTransform));
        btnGO.transform.SetParent(parent.transform, false);
        var btnRT = btnGO.GetComponent<RectTransform>();
        btnRT.anchorMin        = anchorMin;
        btnRT.anchorMax        = anchorMax;
        btnRT.sizeDelta        = Vector2.zero;
        btnRT.anchoredPosition = new Vector2(0f, 6f);
        var btnBG = btnGO.AddComponent<Image>();
        btnBG.color = bgColor;
        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnBG;
        btn.onClick.AddListener(callback);

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(btnGO.transform, false);
        var lblRT = lblGO.GetComponent<RectTransform>();
        lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one; lblRT.sizeDelta = Vector2.zero;
        var lblTxt = lblGO.AddComponent<TextMeshProUGUI>();
        if (cardFont != null) lblTxt.font = cardFont;
        lblTxt.text      = label;
        lblTxt.fontSize  = 13f;
        lblTxt.alignment = TextAlignmentOptions.Center;
        lblTxt.color     = Color.white;
        lblTxt.raycastTarget = false;
    }

    private void ShowDiscardDialog(RuntimeItemData item)
    {
        _pendingDiscard = item;
        if (_discardDialogText != null)
            _discardDialogText.text = $"\"{item.displayName ?? item.itemId}\"\n폐기하시겠습니까?";
        if (_discardDialog != null)
            _discardDialog.SetActive(true);
    }

    private void HideDiscardDialog()
    {
        _pendingDiscard = null;
        if (_discardDialog != null)
            _discardDialog.SetActive(false);
    }

    private void OnDiscardConfirm()
    {
        var item = _pendingDiscard;
        HideDiscardDialog();
        if (item == null) return;

        RemoveShapeForItem(item);
        var run = GameRunBootstrapper.Instance?.Run;
        run?.ItemInventory?.DiscardFromStaging(item);
    }

    // ── Helpers ──

    private static Color RarityColor(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Rare      => COLOR_RARE,
        ItemRarity.Epic      => COLOR_EPIC,
        ItemRarity.Legendary => COLOR_LEGENDARY,
        _                    => COLOR_COMMON,
    };

    // ── Nested: Hover Handler ──

    private sealed class SlotHoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public RuntimeItemData                item;
        public System.Action<RuntimeItemData> onEnter;
        public System.Action                  onExit;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (item != null) onEnter?.Invoke(item);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            onExit?.Invoke();
        }
    }
}
