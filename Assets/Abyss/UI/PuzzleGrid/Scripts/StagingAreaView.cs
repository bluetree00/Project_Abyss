using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 보관함 하단 패널.
///
/// ■ RunItemInventory.StagingItems 기준으로 아이템 카드를 렌더링.
/// ■ 신규 아이템: 빛나는 테두리 + NEW 뱃지.
/// ■ [X] 버튼: 해당 아이템 폐기 (UI_GridPanel.OnDialogDiscardAll과 별개, 개별 폐기).
/// ■ 카드 클릭: ItemInfoPanel에 해당 아이템 정보 표시.
/// ■ Shape 생성: 카드 생성 시 BlockDataManager → ShapeAssetSO → BoardManager.SpawnSharedShape.
/// </summary>
public sealed class StagingAreaView : MonoBehaviour
{
    // ── Constants ──
    private const float CARD_WIDTH      = 110f;
    private const float CARD_HEIGHT     = 130f;
    private const float CARD_SPACING    = 10f;
    private const float GRID_CELL_SIZE  = 120f;

    private static readonly Color COLOR_NEW_BORDER    = new(1f, 0.92f, 0.3f, 1f);
    private static readonly Color COLOR_NORMAL_BORDER = new(0.4f, 0.4f, 0.5f, 0.7f);

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
    [SerializeField] private ItemInfoPanel   itemInfoPanel;
    [SerializeField] private BoardManager    boardManager;

    // ── Private ──
    private readonly List<RuntimeItemData> _knownItems  = new();
    private readonly Dictionary<string, GameObject> _cardByInstanceId = new();
    private readonly Dictionary<string, Shape>      _shapeByInstanceId = new();
    private readonly HashSet<string>                _newItemIds = new();

    // 폐기 확인 다이얼로그
    private RuntimeItemData _pendingDiscard;
    private GameObject      _discardDialog;
    private TMP_Text        _discardDialogText;

    // ── Lifecycle ──
    private void Awake()
    {
        if (boardManager == null)
            boardManager = BoardManager.Instance;
        BuildDiscardDialog();
    }

    private void OnDestroy()
    {
        HideDiscardDialog();
        ClearSectionLabels();
        _knownItems.Clear();
        _cardByInstanceId.Clear();
        _shapeByInstanceId.Clear();
        _newItemIds.Clear();
    }

    // ── Public API ──

    /// <summary>인벤토리 기반으로 카드 목록을 동기화한다. UI_GridPanel.OnStagingChanged에서 호출.</summary>
    public void Refresh(RunItemInventory inventory)
    {
        if (inventory == null) return;

        // 신규 아이템 감지
        foreach (var item in inventory.StagingItems)
        {
            if (!_knownItems.Contains(item))
            {
                _newItemIds.Add(item.instanceId);
                _knownItems.Add(item);
                CreateCard(item);
                EnsureShapeExists(item);
            }
        }

        // 제거된 아이템 감지 (폐기 또는 배치 완료)
        for (int i = _knownItems.Count - 1; i >= 0; i--)
        {
            var known = _knownItems[i];
            bool stillStaging = false;
            foreach (var s in inventory.StagingItems)
                if (s == known) { stillStaging = true; break; }

            if (!stillStaging)
            {
                RemoveCard(known);
                _knownItems.RemoveAt(i);
            }
        }

        RebuildLayout();
    }

    /// <summary>특정 아이템의 Shape를 제거한다. 폐기 시 UI_GridPanel에서 호출.</summary>
    public void RemoveShapeForItem(RuntimeItemData item)
    {
        if (item == null) return;
        if (_shapeByInstanceId.TryGetValue(item.instanceId, out var shape))
        {
            if (boardManager == null)
                boardManager = BoardManager.Instance;
            boardManager?.RemoveSharedShape(shape);
            _shapeByInstanceId.Remove(item.instanceId);
        }
    }

    // ── Card Creation ──

    private void CreateCard(RuntimeItemData item)
    {
        if (item == null || scrollContent == null) return;
        if (_cardByInstanceId.ContainsKey(item.instanceId)) return;

        var card = BuildCardGO(item);
        card.transform.SetParent(scrollContent, false);
        _cardByInstanceId[item.instanceId] = card;
    }

    private void RemoveCard(RuntimeItemData item)
    {
        if (item == null) return;
        if (_cardByInstanceId.TryGetValue(item.instanceId, out var card))
        {
            Destroy(card);
            _cardByInstanceId.Remove(item.instanceId);
        }
        _newItemIds.Remove(item.instanceId);
    }

    private GameObject BuildCardGO(RuntimeItemData item)
    {
        bool isNew = _newItemIds.Contains(item.instanceId);

        // 카드 루트
        var cardGO = new GameObject($"Card_{item.instanceId[..8]}", typeof(RectTransform));
        var cardRT = cardGO.GetComponent<RectTransform>();
        cardRT.sizeDelta = new Vector2(CARD_WIDTH, CARD_HEIGHT);

        // 배경 이미지
        var bg = AddChild<Image>(cardGO, "BG");
        bg.GetComponent<RectTransform>().anchorMin = Vector2.zero;
        bg.GetComponent<RectTransform>().anchorMax = Vector2.one;
        bg.GetComponent<RectTransform>().sizeDelta  = Vector2.zero;
        bg.color = new Color(0.12f, 0.12f, 0.18f, 0.95f);

        // 테두리
        var borderImg = AddChild<Image>(cardGO, "Border");
        var borderRT  = borderImg.GetComponent<RectTransform>();
        borderRT.anchorMin  = Vector2.zero;
        borderRT.anchorMax  = Vector2.one;
        borderRT.sizeDelta  = Vector2.zero;
        borderImg.color     = isNew ? COLOR_NEW_BORDER : COLOR_NORMAL_BORDER;
        var borderOutline   = borderImg.gameObject.AddComponent<Outline>();
        borderOutline.effectColor    = isNew ? COLOR_NEW_BORDER : COLOR_NORMAL_BORDER;
        borderOutline.effectDistance = new Vector2(2f, -2f);

        // 레어도 색 라인 (상단)
        var rarityBar = AddChild<Image>(cardGO, "RarityBar");
        var rbRT = rarityBar.GetComponent<RectTransform>();
        rbRT.anchorMin  = new Vector2(0f, 1f);
        rbRT.anchorMax  = new Vector2(1f, 1f);
        rbRT.sizeDelta  = new Vector2(0f, 5f);
        rbRT.anchoredPosition = Vector2.zero;
        rarityBar.color = RarityColor(item.rarity);

        // 아이콘 (있으면)
        if (item.icon != null)
        {
            var iconImg = AddChild<Image>(cardGO, "Icon");
            var iconRT  = iconImg.GetComponent<RectTransform>();
            iconRT.anchorMin        = new Vector2(0.1f, 0.35f);
            iconRT.anchorMax        = new Vector2(0.9f, 0.85f);
            iconRT.sizeDelta        = Vector2.zero;
            iconImg.sprite          = item.icon;
            iconImg.preserveAspect  = true;
        }

        // 이름 텍스트
        var nameTxt = AddTMPText(cardGO, "Name");
        var nameRT  = nameTxt.GetComponent<RectTransform>();
        nameRT.anchorMin        = new Vector2(0f, 0f);
        nameRT.anchorMax        = new Vector2(1f, 0.35f);
        nameRT.sizeDelta        = Vector2.zero;
        nameRT.anchoredPosition = Vector2.zero;
        nameTxt.text            = item.displayName ?? item.itemId;
        nameTxt.fontSize        = 11f;
        nameTxt.alignment       = TextAlignmentOptions.Center;
        nameTxt.enableWordWrapping = true;

        // NEW 뱃지
        if (isNew)
        {
            var badgeGO  = new GameObject("NewBadge", typeof(RectTransform));
            badgeGO.transform.SetParent(cardGO.transform, false);
            var badgeBG  = badgeGO.AddComponent<Image>();
            badgeBG.color = new Color(1f, 0.3f, 0.3f, 0.95f);
            var badgeRT  = badgeGO.GetComponent<RectTransform>();
            badgeRT.anchorMin        = new Vector2(0f, 1f);
            badgeRT.anchorMax        = new Vector2(0f, 1f);
            badgeRT.pivot            = new Vector2(0f, 1f);
            badgeRT.sizeDelta        = new Vector2(34f, 16f);
            badgeRT.anchoredPosition = new Vector2(2f, -2f);

            // Image와 TMP를 같은 GO에 추가하면 TMP 초기화 실패 → 자식 GO로 분리
            var badgeTxt = AddTMPText(badgeGO, "NewBadgeText");
            var badgeTxtRT = badgeTxt.GetComponent<RectTransform>();
            badgeTxtRT.anchorMin = Vector2.zero;
            badgeTxtRT.anchorMax = Vector2.one;
            badgeTxtRT.sizeDelta = Vector2.zero;
            badgeTxt.text          = "NEW";
            badgeTxt.fontSize      = 9f;
            badgeTxt.alignment     = TextAlignmentOptions.Center;
            badgeTxt.color         = Color.white;
            badgeTxt.raycastTarget = false;
        }

        // [X] 폐기 버튼
        var xBtn = AddChild<Button>(cardGO, "DiscardBtn");
        var xRT  = xBtn.GetComponent<RectTransform>();
        xRT.anchorMin        = new Vector2(1f, 1f);
        xRT.anchorMax        = new Vector2(1f, 1f);
        xRT.pivot            = new Vector2(1f, 1f);
        xRT.sizeDelta        = new Vector2(22f, 22f);
        xRT.anchoredPosition = new Vector2(-2f, -2f);
        var xImg = xBtn.GetComponent<Image>();
        if (xImg == null) xImg = xBtn.gameObject.AddComponent<Image>();
        xImg.color = new Color(0.8f, 0.2f, 0.2f, 0.9f);

        var xTxt = AddTMPText(xBtn.gameObject, "X");
        xTxt.text      = "✕";
        xTxt.fontSize  = 12f;
        xTxt.alignment = TextAlignmentOptions.Center;
        xTxt.color     = Color.white;
        xTxt.raycastTarget = false;

        var capturedItem = item;
        xBtn.onClick.AddListener(() => OnDiscardClicked(capturedItem));

        // 카드 클릭 이벤트 (Button on root)
        var clickBtn = cardGO.AddComponent<Button>();
        var clickBtnImg = cardGO.GetComponent<Image>();
        if (clickBtnImg == null) clickBtnImg = cardGO.AddComponent<Image>();
        clickBtnImg.color = Color.clear;
        clickBtn.targetGraphic = clickBtnImg;
        clickBtn.onClick.AddListener(() => OnCardClicked(capturedItem));

        return cardGO;
    }

    // ── Shape Management ──

    private void EnsureShapeExists(RuntimeItemData item)
    {
        if (item == null || item.shapeId == 0) return;
        if (_shapeByInstanceId.ContainsKey(item.instanceId)) return;

        if (boardManager == null)
            boardManager = BoardManager.Instance;
        if (boardManager == null) return;

        var blockData = Managers.BlockData;
        if (blockData == null) return;

        var shapeEntry = blockData.GetShape(item.shapeId);
        if (shapeEntry == null)
        {
            Debug.LogWarning($"[StagingAreaView] shapeId={item.shapeId} 데이터 없음 (item={item.itemId})");
            return;
        }

        var offsets = BlockDataManager.ParseCellOffsets(shapeEntry);

        var shapeSO = ScriptableObject.CreateInstance<ShapeAssetSO>();
        shapeSO.shapeName       = shapeEntry.shape_name;
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

    // ── Section Labels ──

    private readonly List<GameObject> _sectionLabels = new();

    private void ClearSectionLabels()
    {
        foreach (var go in _sectionLabels)
            if (go != null) Destroy(go);
        _sectionLabels.Clear();
    }

    private GameObject CreateSectionLabel(string text, bool isNew)
    {
        var go  = new GameObject($"Section_{text}", typeof(RectTransform));
        go.transform.SetParent(scrollContent, false);
        var rt  = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(isNew ? 110f : 76f, CARD_HEIGHT);

        var txt = go.AddComponent<TMPro.TextMeshProUGUI>();
        if (cardFont != null) txt.font = cardFont;
        txt.text      = text;
        txt.fontSize  = 11f;
        txt.color     = isNew ? new Color(1f, 0.92f, 0.3f, 0.9f) : new Color(0.6f, 0.65f, 0.75f, 0.8f);
        txt.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
        txt.enableWordWrapping = false;
        txt.raycastTarget = false;
        _sectionLabels.Add(go);
        return go;
    }

    // ── Layout ──

    private void RebuildLayout()
    {
        if (scrollContent == null) return;

        ClearSectionLabels();

        // 미배치(기존)와 신규 아이템 분리
        var normalItems = new List<RuntimeItemData>();
        var newItems    = new List<RuntimeItemData>();
        foreach (var item in _knownItems)
        {
            if (_newItemIds.Contains(item.instanceId)) newItems.Add(item);
            else                                        normalItems.Add(item);
        }

        float x = CARD_SPACING;

        // 미배치 섹션
        if (normalItems.Count > 0)
        {
            var lbl = CreateSectionLabel("미배치", false);
            PositionElement(lbl.GetComponent<RectTransform>(), x);
            x += 76f + CARD_SPACING;

            foreach (var item in normalItems)
            {
                if (!_cardByInstanceId.TryGetValue(item.instanceId, out var card)) continue;
                PositionElement(card.GetComponent<RectTransform>(), x);
                x += CARD_WIDTH + CARD_SPACING;
            }
        }

        // 새로 얻은 아이템 섹션
        if (newItems.Count > 0)
        {
            var lbl = CreateSectionLabel("✦ 새로 얻은 아이템", true);
            PositionElement(lbl.GetComponent<RectTransform>(), x);
            x += 110f + CARD_SPACING;

            foreach (var item in newItems)
            {
                if (!_cardByInstanceId.TryGetValue(item.instanceId, out var card)) continue;
                PositionElement(card.GetComponent<RectTransform>(), x);
                x += CARD_WIDTH + CARD_SPACING;
            }
        }

        scrollContent.sizeDelta = new Vector2(x, scrollContent.sizeDelta.y);
    }

    private static void PositionElement(RectTransform rt, float x)
    {
        rt.anchorMin        = new Vector2(0f, 0.5f);
        rt.anchorMax        = new Vector2(0f, 0.5f);
        rt.pivot            = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0f);
    }

    // ── Event Handlers ──

    private void OnCardClicked(RuntimeItemData item)
    {
        itemInfoPanel?.ShowItem(item, isNew: false);
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
        var btnBG  = btnGO.AddComponent<Image>();
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
        RemoveCard(item);
        _knownItems.Remove(item);
        var run = GameRunBootstrapper.Instance?.Run;
        run?.ItemInventory?.DiscardFromStaging(item);
    }

    // ── Helpers ──

    private T AddChild<T>(GameObject parent, string name) where T : Component
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        return go.GetComponent<T>() ?? go.AddComponent<T>();
    }

    private TextMeshProUGUI AddTMPText(GameObject parent, string name)
    {
        var go  = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        var txt = go.AddComponent<TextMeshProUGUI>();
        if (cardFont != null) txt.font = cardFont;
        return txt;
    }

    private static Color RarityColor(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Rare      => COLOR_RARE,
        ItemRarity.Epic      => COLOR_EPIC,
        ItemRarity.Legendary => COLOR_LEGENDARY,
        _                    => COLOR_COMMON,
    };
}
