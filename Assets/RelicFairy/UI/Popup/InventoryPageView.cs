using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// EscBookPopup의 인벤토리 페이지.
/// 왼쪽 페이지: 아이템 그리드 + 호버 툴팁.
/// 오른쪽 페이지: 선택된 아이템 상세 정보.
/// pageInventory 오브젝트에 부착.
/// </summary>
public class InventoryPageView : MonoBehaviour
{
    // ── Constants ──
    private const float SLOT_SIZE = 64f;
    private const float SLOT_SPACING = 6f;
    private const int COLUMNS = 5;
    private const float TOOLTIP_WIDTH = 220f;

    private static readonly Color SELECTED_BORDER_COLOR = new(1f, 0.85f, 0.4f, 1f);
    private static readonly Color DEFAULT_BORDER_COLOR = new(0f, 0f, 0f, 0f);

    // ── Private ──
    private RunItemInventory _inventory;
    private RectTransform _gridRoot;
    private GameObject _tooltip;
    private TMP_Text _tooltipText;
    private readonly List<GameObject> _slots = new();
    private readonly Dictionary<GameObject, Outline> _slotOutlines = new();
    private RuntimeItemData _hoveredItem;
    private RuntimeItemData _selectedItem;
    private GameObject _selectedSlotGO;
    private bool _bound;

    // ── Detail Panel ──
    private RectTransform _detailRoot;
    private TMP_Text _detailName;
    private TMP_Text _detailRarity;
    private TMP_Text _detailDesc;
    private TMP_Text _detailEffects;
    private TMP_Text _detailExtra;
    private Image _detailIcon;
    private TMP_Text _detailIconText;
    private GameObject _detailEmptyNotice;

    // ── Context Menu ──
    private GameObject _contextMenu;
    private RuntimeItemData _contextTarget;

    // ── Lifecycle ──

    private void OnEnable()
    {
        Bind();
        Refresh();
    }

    private void OnDisable()
    {
        HideTooltip();
        HideContextMenu();
    }

    private void Update()
    {
        UpdateTooltipPosition();
        HandleContextMenuDismiss();
    }

    private void LateUpdate()
    {
        UpdateDetailPanel();
    }

    // ── Public Methods ──

    public void Refresh()
    {
        if (!_bound) Bind();
        if (_inventory == null) return;

        ClearSlots();

        foreach (var item in _inventory.PlacedItems)
            CreateSlot(item);
        foreach (var item in _inventory.StagingItems)
            CreateSlot(item);

        // 기본 선택: 최신(마지막) 아이템, 없으면 상세 패널 비움
        AutoSelectDefault();
    }

    // ── Private Methods ──

    private void Bind()
    {
        if (_bound) return;

        var run = GameRunBootstrapper.Instance?.Run;
        if (run == null) return;

        _inventory = run.ItemInventory;
        _inventory.OnPlacedChanged  -= Refresh;
        _inventory.OnPlacedChanged  += Refresh;
        _inventory.OnStagingChanged -= Refresh;
        _inventory.OnStagingChanged += Refresh;

        EnsureGridRoot();
        EnsureDetailPanel();
        EnsureTooltip();
        _bound = true;
    }

    private void EnsureGridRoot()
    {
        if (_gridRoot != null) return;

        var existing = transform.Find("ItemGrid");
        if (existing != null)
            Destroy(existing.gameObject);

        var gridGO = new GameObject("ItemGrid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(RectMask2D));
        gridGO.transform.SetParent(transform, false);

        _gridRoot = gridGO.GetComponent<RectTransform>();
        _gridRoot.anchorMin = new Vector2(0.20f, 0.04f);
        _gridRoot.anchorMax = new Vector2(0.50f, 0.77f);
        _gridRoot.offsetMin = Vector2.zero;
        _gridRoot.offsetMax = Vector2.zero;

        var layout = gridGO.GetComponent<GridLayoutGroup>();
        layout.cellSize = Vector2.one * SLOT_SIZE;
        layout.spacing = Vector2.one * SLOT_SPACING;
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = COLUMNS;
        layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        layout.startAxis = GridLayoutGroup.Axis.Horizontal;
        layout.childAlignment = TextAnchor.UpperLeft;
    }

    private void EnsureDetailPanel()
    {
        if (_detailRoot != null) return;

        var existing = transform.Find("DetailPanel");
        if (existing != null)
            Destroy(existing.gameObject);

        // 루트 컨테이너 — 오른쪽 페이지 (RectMask2D로 넘침 방지)
        var detailGO = new GameObject("DetailPanel", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(RectMask2D));
        detailGO.transform.SetParent(transform, false);

        _detailRoot = detailGO.GetComponent<RectTransform>();
        _detailRoot.anchorMin = new Vector2(0.55f, 0.03f);
        _detailRoot.anchorMax = new Vector2(0.93f, 0.78f);
        _detailRoot.offsetMin = Vector2.zero;
        _detailRoot.offsetMax = Vector2.zero;

        var vlg = detailGO.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 8, 8);
        vlg.spacing = 6f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        // 아이콘 + 이름 행
        var headerGO = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        headerGO.transform.SetParent(detailGO.transform, false);
        var headerLayout = headerGO.GetComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 10f;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        var headerLE = headerGO.AddComponent<LayoutElement>();
        headerLE.preferredHeight = 60f;

        // 아이콘 박스
        var iconBoxGO = new GameObject("IconBox", typeof(RectTransform), typeof(Image));
        iconBoxGO.transform.SetParent(headerGO.transform, false);
        _detailIcon = iconBoxGO.GetComponent<Image>();
        _detailIcon.color = new Color(0.25f, 0.25f, 0.3f, 0.9f);
        var iconLE = iconBoxGO.AddComponent<LayoutElement>();
        iconLE.preferredWidth = 56f;
        iconLE.preferredHeight = 56f;

        // 아이콘 텍스트 (카테고리 이니셜)
        var iconTextGO = new GameObject("IconText", typeof(RectTransform), typeof(TextMeshProUGUI));
        iconTextGO.transform.SetParent(iconBoxGO.transform, false);
        var iconTextRT = iconTextGO.GetComponent<RectTransform>();
        iconTextRT.anchorMin = Vector2.zero;
        iconTextRT.anchorMax = Vector2.one;
        iconTextRT.offsetMin = Vector2.zero;
        iconTextRT.offsetMax = Vector2.zero;
        _detailIconText = iconTextGO.GetComponent<TMP_Text>();
        _detailIconText.fontSize = 22;
        _detailIconText.alignment = TextAlignmentOptions.Center;
        _detailIconText.color = Color.white;
        _detailIconText.raycastTarget = false;

        // 이름 + 레어리티 세로 그룹
        var nameGroupGO = new GameObject("NameGroup", typeof(RectTransform), typeof(VerticalLayoutGroup));
        nameGroupGO.transform.SetParent(headerGO.transform, false);
        var nameGroupVlg = nameGroupGO.GetComponent<VerticalLayoutGroup>();
        nameGroupVlg.spacing = 2f;
        nameGroupVlg.childForceExpandWidth = true;
        nameGroupVlg.childForceExpandHeight = false;
        var nameGroupLE = nameGroupGO.AddComponent<LayoutElement>();
        nameGroupLE.flexibleWidth = 1f;

        _detailName = CreateDetailText(nameGroupGO.transform, "Name", 20,
            new Color(0.15f, 0.1f, 0.05f), TextAlignmentOptions.Left, FontStyles.Bold);
        _detailRarity = CreateDetailText(nameGroupGO.transform, "Rarity", 13,
            new Color(0.4f, 0.35f, 0.3f), TextAlignmentOptions.Left);

        // 구분선
        CreateSeparator(detailGO.transform);

        // 설명 (displayName = 효과 설명 텍스트)
        _detailDesc = CreateDetailText(detailGO.transform, "Desc", 13,
            new Color(0.3f, 0.25f, 0.2f), TextAlignmentOptions.TopLeft);

        // 효과 목록
        _detailEffects = CreateDetailText(detailGO.transform, "Effects", 14,
            new Color(0.2f, 0.15f, 0.1f), TextAlignmentOptions.TopLeft);
        var effectsLE = _detailEffects.gameObject.AddComponent<LayoutElement>();
        effectsLE.flexibleHeight = 1f;

        // 추가 정보 (블록, 쿨다운 등)
        _detailExtra = CreateDetailText(detailGO.transform, "Extra", 12,
            new Color(0.25f, 0.35f, 0.5f), TextAlignmentOptions.TopLeft);

        // 빈 상태 안내
        _detailEmptyNotice = new GameObject("EmptyNotice", typeof(RectTransform), typeof(TextMeshProUGUI));
        _detailEmptyNotice.transform.SetParent(detailGO.transform, false);
        var emptyTmp = _detailEmptyNotice.GetComponent<TextMeshProUGUI>();
        emptyTmp.text = "아이템을 선택하세요";
        emptyTmp.fontSize = 16;
        emptyTmp.color = new Color(0.35f, 0.3f, 0.25f);
        emptyTmp.alignment = TextAlignmentOptions.Center;
        emptyTmp.raycastTarget = false;
        emptyTmp.outlineWidth = 0.15f;
        emptyTmp.outlineColor = new Color32(200, 190, 170, 100);
        var emptyLE = _detailEmptyNotice.AddComponent<LayoutElement>();
        emptyLE.flexibleHeight = 1f;
    }

    private TMP_Text CreateDetailText(Transform parent, string objName, float fontSize,
        Color color, TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
    {
        var go = new GameObject(objName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = align;
        tmp.fontStyle = style;
        tmp.enableWordWrapping = true;
        tmp.richText = true;
        tmp.raycastTarget = false;

        // 텍스트 외곽선으로 가독성 확보
        tmp.outlineWidth = 0.25f;
        tmp.outlineColor = new Color32(20, 15, 10, 180);

        return tmp;
    }

    private void CreateSeparator(Transform parent)
    {
        // LayoutGroup内でも幅が制限されるよう、wrapper で包む
        var wrapGO = new GameObject("SepWrap", typeof(RectTransform));
        wrapGO.transform.SetParent(parent, false);
        var wrapLE = wrapGO.AddComponent<LayoutElement>();
        wrapLE.preferredHeight = 6f;

        var sepGO = new GameObject("Separator", typeof(RectTransform), typeof(Image));
        sepGO.transform.SetParent(wrapGO.transform, false);
        sepGO.GetComponent<Image>().color = new Color(0.4f, 0.35f, 0.25f, 0.5f);

        var sepRT = sepGO.GetComponent<RectTransform>();
        sepRT.anchorMin = new Vector2(0f, 0.3f);
        sepRT.anchorMax = new Vector2(0.50f, 0.7f);
        sepRT.offsetMin = Vector2.zero;
        sepRT.offsetMax = Vector2.zero;
    }

    private void EnsureTooltip()
    {
        if (_tooltip != null) return;

        // 툴팁 패널 — 책 팝업의 최상위 Canvas에 배치 (가려지지 않도록)
        var tooltipParent = GetComponentInParent<Canvas>(true)?.transform ?? transform.parent;
        _tooltip = new GameObject("ItemTooltip", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        _tooltip.transform.SetParent(tooltipParent, false);
        _tooltip.transform.SetAsLastSibling();

        var tooltipRT = _tooltip.GetComponent<RectTransform>();
        tooltipRT.sizeDelta = new Vector2(TOOLTIP_WIDTH, 0);
        tooltipRT.pivot = new Vector2(0, 1);

        var bg = _tooltip.GetComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.15f, 0.95f);
        bg.raycastTarget = false;

        var cg = _tooltip.GetComponent<CanvasGroup>();
        cg.blocksRaycasts = false;

        // 텍스트
        var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(_tooltip.transform, false);

        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(8, 8);
        textRT.offsetMax = new Vector2(-8, -8);

        _tooltipText = textGO.GetComponent<TMP_Text>();
        _tooltipText.fontSize = 12;
        _tooltipText.color = Color.white;
        _tooltipText.alignment = TextAlignmentOptions.TopLeft;
        _tooltipText.richText = true;
        _tooltipText.enableWordWrapping = true;
        _tooltipText.raycastTarget = false;

        // ContentSizeFitter 추가 (텍스트 길이에 맞게)
        var tooltipFitter = _tooltip.AddComponent<ContentSizeFitter>();
        tooltipFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var vlg = _tooltip.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        _tooltip.SetActive(false);
    }

    private void CreateSlot(RuntimeItemData item)
    {
        var slotGO = new GameObject($"Slot_{item.displayName}", typeof(RectTransform), typeof(Image));
        slotGO.transform.SetParent(_gridRoot, false);

        var slotImg = slotGO.GetComponent<Image>();
        slotImg.color = GetRaritySlotColor(item.rarity);

        // 선택 하이라이트용 Outline
        var outline = slotGO.AddComponent<Outline>();
        outline.effectColor = DEFAULT_BORDER_COLOR;
        outline.effectDistance = new Vector2(2, -2);
        _slotOutlines[slotGO] = outline;

        // 아이콘 (임시 — 카테고리별 텍스트 이니셜)
        var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(TextMeshProUGUI));
        iconGO.transform.SetParent(slotGO.transform, false);
        var iconRT = iconGO.GetComponent<RectTransform>();
        iconRT.anchorMin = Vector2.zero;
        iconRT.anchorMax = Vector2.one;
        iconRT.offsetMin = new Vector2(4, 4);
        iconRT.offsetMax = new Vector2(-4, -4);

        var iconText = iconGO.GetComponent<TMP_Text>();
        iconText.text = GetCategoryIcon(item.category);
        iconText.fontSize = 24;
        iconText.alignment = TextAlignmentOptions.Center;
        iconText.color = Color.white;
        iconText.raycastTarget = false;

        // 이벤트 트리거
        var trigger = slotGO.AddComponent<EventTrigger>();
        var capturedItem = item;
        var capturedSlot = slotGO;

        // 호버 툴팁
        var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enterEntry.callback.AddListener(e => ShowTooltip(capturedItem, capturedSlot.GetComponent<RectTransform>()));
        trigger.triggers.Add(enterEntry);

        var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exitEntry.callback.AddListener(e => HideTooltip());
        trigger.triggers.Add(exitEntry);

        // 좌클릭: 선택 → 상세 패널 갱신 / 우클릭: 컨텍스트 메뉴
        var clickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
        clickEntry.callback.AddListener(e =>
        {
            var pe = (PointerEventData)e;
            if (pe.button == PointerEventData.InputButton.Left)
            {
                HideContextMenu();
                SelectItem(capturedItem, capturedSlot);
            }
            else if (pe.button == PointerEventData.InputButton.Right)
            {
                SelectItem(capturedItem, capturedSlot);
                ShowContextMenu(capturedItem);
            }
        });
        trigger.triggers.Add(clickEntry);

        _slots.Add(slotGO);
    }

    private void SelectItem(RuntimeItemData item, GameObject slotGO)
    {
        if (item == _selectedItem) return;

        // 이전 선택 해제
        if (_selectedSlotGO != null && _slotOutlines.TryGetValue(_selectedSlotGO, out var prevOutline))
            prevOutline.effectColor = DEFAULT_BORDER_COLOR;

        _selectedItem = item;
        _selectedSlotGO = slotGO;

        // 새 선택 하이라이트
        if (_slotOutlines.TryGetValue(slotGO, out var outline))
            outline.effectColor = SELECTED_BORDER_COLOR;

        ShowDetailInfo(item);
    }

    private void AutoSelectDefault()
    {
        int totalCount = _inventory == null ? 0 : _inventory.PlacedCount + _inventory.StagingCount;
        if (totalCount == 0)
        {
            _selectedItem = null;
            _selectedSlotGO = null;
            ShowDetailEmpty();
            return;
        }

        // 최신(마지막) 아이템 자동 선택 (Placed → Staging 순)
        int lastIdx = totalCount - 1;
        var lastItem = lastIdx < _inventory.PlacedCount
            ? _inventory.PlacedItems[lastIdx]
            : _inventory.StagingItems[lastIdx - _inventory.PlacedCount];
        var lastSlot = lastIdx < _slots.Count ? _slots[lastIdx] : null;

        if (lastSlot != null)
            SelectItem(lastItem, lastSlot);
        else
            ShowDetailEmpty();
    }

    private void ShowDetailInfo(RuntimeItemData item)
    {
        if (_detailRoot == null) return;

        bool hasItem = item != null;
        if (_detailEmptyNotice != null) _detailEmptyNotice.SetActive(!hasItem);

        SetDetailContentVisible(hasItem);
        if (!hasItem) return;

        // 아이콘
        if (_detailIcon != null)
            _detailIcon.color = GetRaritySlotColor(item.rarity);
        if (_detailIconText != null)
            _detailIconText.text = GetCategoryIcon(item.category);

        // 이름 (itemId)
        string rarityHex = GetRarityHexColorDark(item.rarity);
        if (_detailName != null)
            _detailName.text = $"<color={rarityHex}>{item.itemId}</color>";

        // 레어리티 + 카테고리
        if (_detailRarity != null)
            _detailRarity.text = $"{item.rarity}  ·  {item.category}";

        // 설명 (displayName)
        if (_detailDesc != null)
            _detailDesc.text = !string.IsNullOrEmpty(item.displayName) ? item.displayName : "";

        // 효과
        if (_detailEffects != null)
        {
            if (item.effects != null && item.effects.Count > 0)
            {
                string effectText = "";
                foreach (var eff in item.effects)
                {
                    string triggerLabel = eff.trigger == "Always" ? "" : $"  <color=#807060>({eff.trigger})</color>";
                    effectText += $"  <color=#8B4513>{eff.effectType}</color>  +{eff.value}{triggerLabel}\n";
                }
                _detailEffects.text = effectText.TrimEnd('\n');
            }
            else
            {
                _detailEffects.text = "<color=#998877>효과 없음</color>";
            }
        }

        // 추가 정보
        if (_detailExtra != null)
        {
            string extra = "";
            if (item.shapeId > 0)
                extra += $"블록 Shape #{item.shapeId}\n";
            if (item.cooldown > 0)
                extra += $"쿨다운 {item.cooldown:F1}초\n";
            _detailExtra.text = extra.TrimEnd('\n');
            _detailExtra.gameObject.SetActive(!string.IsNullOrEmpty(extra));
        }
    }

    private void ShowDetailEmpty()
    {
        SetDetailContentVisible(false);
        if (_detailEmptyNotice != null) _detailEmptyNotice.SetActive(true);
    }

    private void SetDetailContentVisible(bool visible)
    {
        if (_detailRoot == null) return;
        if (_detailName != null) _detailName.transform.parent.parent.gameObject.SetActive(visible); // Header
        if (_detailDesc != null) _detailDesc.gameObject.SetActive(visible);
        if (_detailEffects != null) _detailEffects.gameObject.SetActive(visible);
        if (_detailExtra != null) _detailExtra.gameObject.SetActive(visible);

        var sep = _detailRoot.Find("Separator");
        if (sep != null) sep.gameObject.SetActive(visible);
    }

    private void UpdateDetailPanel()
    {
        // 선택된 아이템이 인벤토리에서 사라졌으면 재선택
        if (_selectedItem != null && _inventory != null)
        {
            bool found = false;
            foreach (var item in _inventory.PlacedItems)
                if (item == _selectedItem) { found = true; break; }
            if (!found)
                foreach (var item in _inventory.StagingItems)
                    if (item == _selectedItem) { found = true; break; }
            if (!found) AutoSelectDefault();
        }
    }

    private void EnsureContextMenu()
    {
        if (_contextMenu != null) return;

        var menuParent = GetComponentInParent<Canvas>(true)?.transform ?? transform.parent;

        // 배경 패널
        _contextMenu = new GameObject("ContextMenu", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        _contextMenu.transform.SetParent(menuParent, false);

        var menuRT = _contextMenu.GetComponent<RectTransform>();
        menuRT.sizeDelta = new Vector2(100f, 0f);
        menuRT.pivot = new Vector2(0f, 1f);

        var menuBg = _contextMenu.GetComponent<Image>();
        menuBg.color = new Color(0.12f, 0.12f, 0.16f, 0.95f);
        menuBg.raycastTarget = true;

        var menuVlg = _contextMenu.GetComponent<VerticalLayoutGroup>();
        menuVlg.padding = new RectOffset(4, 4, 4, 4);
        menuVlg.spacing = 2f;
        menuVlg.childForceExpandWidth = true;
        menuVlg.childForceExpandHeight = false;
        menuVlg.childControlWidth = true;
        menuVlg.childControlHeight = true;

        var menuFitter = _contextMenu.AddComponent<ContentSizeFitter>();
        menuFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // "버리기" 버튼
        CreateContextButton("버리기", new Color(0.9f, 0.3f, 0.3f), () =>
        {
            var target = _contextTarget;
            HideContextMenu();
            RemoveItem(target);
        });

        _contextMenu.SetActive(false);
    }

    private void CreateContextButton(string label, Color textColor, System.Action onClick)
    {
        var btnGO = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(_contextMenu.transform, false);

        var btnImg = btnGO.GetComponent<Image>();
        btnImg.color = new Color(0.2f, 0.2f, 0.25f, 0.9f);

        var btnLE = btnGO.AddComponent<LayoutElement>();
        btnLE.preferredHeight = 28f;

        var textGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(btnGO.transform, false);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(8, 2);
        textRT.offsetMax = new Vector2(-8, -2);

        var tmp = textGO.GetComponent<TMP_Text>();
        tmp.text = label;
        tmp.fontSize = 13;
        tmp.color = textColor;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;

        var btn = btnGO.GetComponent<Button>();
        btn.onClick.AddListener(() => onClick?.Invoke());

        // 호버 색상
        var colors = btn.colors;
        colors.normalColor = new Color(0.2f, 0.2f, 0.25f, 0.9f);
        colors.highlightedColor = new Color(0.3f, 0.3f, 0.38f, 1f);
        colors.pressedColor = new Color(0.15f, 0.15f, 0.2f, 1f);
        btn.colors = colors;
        btn.targetGraphic = btnImg;
    }

    private void ShowContextMenu(RuntimeItemData item)
    {
        EnsureContextMenu();
        _contextTarget = item;

        var menuRT = _contextMenu.GetComponent<RectTransform>();
        Vector2 mousePos = Input.mousePosition;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            menuRT.parent as RectTransform, mousePos, null, out var localPos);
        menuRT.anchoredPosition = localPos;

        _contextMenu.transform.SetAsLastSibling();
        _contextMenu.SetActive(true);
    }

    private void HideContextMenu()
    {
        _contextTarget = null;
        if (_contextMenu != null)
            _contextMenu.SetActive(false);
    }

    private void HandleContextMenuDismiss()
    {
        if (_contextMenu == null || !_contextMenu.activeSelf) return;

        // 좌클릭 또는 우클릭으로 메뉴 밖을 누르면 닫기
        if (!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1)) return;

        var menuRT = _contextMenu.GetComponent<RectTransform>();
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            menuRT, Input.mousePosition, null, out var localPoint);

        if (!menuRT.rect.Contains(localPoint))
            HideContextMenu();
    }

    private void RemoveItem(RuntimeItemData item)
    {
        if (_inventory == null || item == null) return;

        HideTooltip();
        DropItemToWorld(item);
        if (_inventory.IsPlaced(item.instanceId))
            _inventory.RemovePlaced(item);
        else
            _inventory.DiscardFromStaging(item);
        // OnPlacedChanged / OnStagingChanged → Refresh 자동 호출
    }

    private void DropItemToWorld(RuntimeItemData item)
    {
        var player = Object.FindObjectOfType<PlayerController>();
        if (player == null) return;

        // 플레이어 앞쪽(facing 방향)에 드롭
        var t = player.transform;
        Vector3 dropPos = t.position + t.forward * 2f;

        WorldItemDisplay.SpawnFromData(item, dropPos);
    }

    private void ShowTooltip(RuntimeItemData item, RectTransform slotRT)
    {
        if (_tooltip == null || _tooltipText == null) return;
        _hoveredItem = item;

        // 내용 구성
        string rarityColor = item.rarity switch
        {
            ItemRarity.Rare => "#00FFFF",
            ItemRarity.Epic => "#CC66FF",
            _ => "#FFFFFF",
        };

        string text = $"<color={rarityColor}><b>{item.displayName}</b></color>\n";
        text += $"<size=10><color=#AAAAAA>{item.rarity} · {item.category}</color></size>\n";

        if (item.effects != null && item.effects.Count > 0)
        {
            text += "\n";
            foreach (var eff in item.effects)
            {
                string triggerLabel = eff.trigger == "Always" ? "" : $" ({eff.trigger})";
                text += $"  {eff.effectType} +{eff.value}{triggerLabel}\n";
            }
        }

        if (item.shapeId > 0)
            text += $"\n<size=10><color=#88AAFF>블록 Shape #{item.shapeId}</color></size>";

        text += "\n<size=9><color=#666666>우클릭: 메뉴</color></size>";

        _tooltipText.text = text;
        _tooltip.transform.SetAsLastSibling();
        _tooltip.SetActive(true);

        UpdateTooltipPosition();
    }

    private void HideTooltip()
    {
        _hoveredItem = null;
        if (_tooltip != null)
            _tooltip.SetActive(false);
    }

    private void UpdateTooltipPosition()
    {
        if (_tooltip == null || !_tooltip.activeSelf) return;

        var tooltipRT = _tooltip.GetComponent<RectTransform>();
        Vector2 mousePos = Input.mousePosition;

        // 마우스 오른쪽 위에 표시
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            tooltipRT.parent as RectTransform, mousePos, null, out var localPos);
        tooltipRT.anchoredPosition = localPos + new Vector2(15, 15);
    }

    private void ClearSlots()
    {
        foreach (var slot in _slots)
            if (slot != null) Destroy(slot);
        _slots.Clear();
        _slotOutlines.Clear();
        _selectedSlotGO = null;
    }

    private void OnDestroy()
    {
        if (_inventory != null)
        {
            _inventory.OnPlacedChanged  -= Refresh;
            _inventory.OnStagingChanged -= Refresh;
        }
    }

    // ── Static Helpers ──

    private static Color GetRaritySlotColor(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Common => new Color(0.25f, 0.25f, 0.3f, 0.9f),
            ItemRarity.Rare   => new Color(0.15f, 0.3f, 0.4f, 0.9f),
            ItemRarity.Epic   => new Color(0.3f, 0.15f, 0.4f, 0.9f),
            _                 => new Color(0.25f, 0.25f, 0.3f, 0.9f),
        };
    }

    private static string GetRarityHexColor(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Rare => "#00FFFF",
            ItemRarity.Epic => "#CC66FF",
            _               => "#FFFFFF",
        };
    }

    /// <summary>책 페이지(밝은 베이지) 위에서 잘 보이는 어두운 톤 레어리티 색상.</summary>
    private static string GetRarityHexColorDark(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Rare => "#006688",
            ItemRarity.Epic => "#6B2D8B",
            _               => "#2B1D10",
        };
    }

    private static string GetCategoryIcon(ItemCategory category)
    {
        return category switch
        {
            ItemCategory.Ring     => "R",
            ItemCategory.Necklace => "N",
            ItemCategory.Boots    => "B",
            ItemCategory.Gloves   => "G",
            ItemCategory.Belt     => "Bt",
            ItemCategory.Charm    => "C",
            ItemCategory.Active   => "A",
            _                     => "?",
        };
    }
}
