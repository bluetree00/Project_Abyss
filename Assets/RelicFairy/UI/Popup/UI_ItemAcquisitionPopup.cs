using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;

/// <summary>
/// 아이템 획득 팝업.
///
/// ■ Canvas_Popup에 Addressable로 로드된다.
///   키: "UI/Popup/UI_ItemAcquisitionPopup"
/// ■ [그리드 열기] → 아이템을 보관함에 추가 + UI_GridPanel 열기.
/// ■ [거부]        → 아이템 폐기, 팝업 닫힘.
/// </summary>
public sealed class UI_ItemAcquisitionPopup : UI_Popup
{
    // ── SerializeField ──
    [Header("아이템 정보")]
    [SerializeField] private Image       itemIcon;
    [SerializeField] private TMP_Text    itemNameText;
    [SerializeField] private TMP_Text    rarityText;
    [SerializeField] private Transform   effectListRoot;
    [SerializeField] private RectTransform shapePreviewRoot;

    [Header("버튼")]
    [SerializeField] private Button openGridButton;
    [SerializeField] private Button rejectButton;

    [Header("폰트")]
    [SerializeField] private TMP_FontAsset popupFont;

    // ── Private ──
    private RuntimeItemData            _item;
    private RunItemInventory           _inventory;
    private UniTaskCompletionSource    _interactionTcs;

    private static readonly Color COLOR_RISK      = new(1f,    0.35f, 0.35f, 1f);
    private static readonly Color COLOR_NORMAL_FX = new(0.85f, 0.92f, 1f,   1f);
    private static readonly Color COLOR_COMMON    = new(0.75f, 0.75f, 0.75f, 1f);
    private static readonly Color COLOR_RARE      = new(0.3f,  0.6f,  1f,   1f);
    private static readonly Color COLOR_EPIC      = new(0.7f,  0.3f,  1f,   1f);
    private static readonly Color COLOR_LEGENDARY = new(1f,    0.7f,  0.2f, 1f);

    private const float MINI_CELL_SIZE = 26f;
    private const float MINI_CELL_GAP  = 3f;

    // ── Lifecycle ──
    public override void Init()
    {
        base.Init();

        if (openGridButton != null)
            openGridButton.onClick.AddListener(OnOpenGridClicked);
        if (rejectButton != null)
            rejectButton.onClick.AddListener(OnRejectClicked);
    }

    private void OnDestroy()
    {
        if (openGridButton != null)
            openGridButton.onClick.RemoveListener(OnOpenGridClicked);
        if (rejectButton != null)
            rejectButton.onClick.RemoveListener(OnRejectClicked);
    }

    // ── Public API ──

    /// <summary>버튼 클릭 즉시 resolve — 애니메이션 완료를 기다리지 않는다.</summary>
    public UniTask WaitForInteractionAsync(System.Threading.CancellationToken ct)
    {
        _interactionTcs = new UniTaskCompletionSource();
        return _interactionTcs.Task.AttachExternalCancellation(ct);
    }

    /// <summary>
    /// 팝업 데이터 설정.
    /// UIManager.ShowPopupUIAndGetAsync&lt;UI_ItemAcquisitionPopup&gt;() 후 호출.
    /// </summary>
    public void Setup(RuntimeItemData item, RunItemInventory inventory)
    {
        _item      = item;
        _inventory = inventory;

        if (item == null) { ClosePopupUI(); return; }

        // 아이콘
        if (itemIcon != null)
        {
            itemIcon.sprite  = item.icon;
            itemIcon.enabled = item.icon != null;
        }

        // 이름
        if (itemNameText != null)
            itemNameText.text = item.displayName ?? item.itemId;

        // 레어도
        if (rarityText != null)
        {
            rarityText.text  = RarityLabel(item.rarity);
            rarityText.color = RarityColor(item.rarity);
        }

        // 효과 목록
        BuildEffectList(item);

        // Shape 미니 프리뷰
        BuildShapePreview(item);
    }

    // ── Button Handlers ──

    private void OnOpenGridClicked()
    {
        _interactionTcs?.TrySetResult();

        if (_item == null || _inventory == null) { ClosePopupUI(); return; }

        // 보관함에 추가
        bool added = _inventory.AddToStaging(_item);
        if (!added)
            Debug.LogWarning($"[AcquisitionPopup] 스태킹 한계 초과로 추가 실패: {_item.itemId}");

        ClosePopupUI();

        // 프리팹이 비활성 상태여서 Instance가 null인 경우 ShowOverlayUI로 Awake를 트리거
        if (UI_GridPanel.Instance == null)
            Managers.UI?.ShowOverlayUI<UI_GridPanel>();

        // Awake가 실행되어 Instance가 설정되었으면 새 아이템을 강조하며 패널 열기
        if (UI_GridPanel.Instance != null)
            UI_GridPanel.Instance.ShowWithNewItem(_item);
    }

    private void OnRejectClicked()
    {
        _interactionTcs?.TrySetResult();
        ClosePopupUI();
    }

    // ── Effect List ──

    private void BuildEffectList(RuntimeItemData item)
    {
        if (effectListRoot == null || item.effects == null) return;

        for (int i = effectListRoot.childCount - 1; i >= 0; i--)
            Destroy(effectListRoot.GetChild(i).gameObject);

        foreach (var slot in item.effects)
        {
            if (string.IsNullOrEmpty(slot.effectType)) continue;

            bool isRisk = IsRiskEffect(slot.effectType);

            var rowGO = new GameObject("EffectRow", typeof(RectTransform));
            rowGO.transform.SetParent(effectListRoot, false);

            var txt = rowGO.AddComponent<TextMeshProUGUI>();
            if (popupFont != null) txt.font = popupFont;
            txt.fontSize = 14f;
            txt.color    = isRisk ? COLOR_RISK : COLOR_NORMAL_FX;
            txt.text     = BuildEffectLabel(slot, isRisk);
            txt.enableWordWrapping = false;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(effectListRoot as RectTransform);
    }

    // ── Shape Preview ──

    private void BuildShapePreview(RuntimeItemData item)
    {
        if (shapePreviewRoot == null || item.shapeId == 0) return;

        for (int i = shapePreviewRoot.childCount - 1; i >= 0; i--)
            Destroy(shapePreviewRoot.GetChild(i).gameObject);

        var blockData = Managers.RuneData;
        if (blockData == null) return;

        var shapeEntry = blockData.GetShape(item.shapeId);
        if (shapeEntry == null) return;

        var offsets = RuneDataManager.ParseCellOffsets(shapeEntry);
        if (offsets == null || offsets.Length == 0) return;

        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        foreach (var o in offsets)
        {
            if (o.x < minX) minX = o.x;  if (o.x > maxX) maxX = o.x;
            if (o.y < minY) minY = o.y;  if (o.y > maxY) maxY = o.y;
        }
        int cols = maxX - minX + 1;
        int rows = maxY - minY + 1;

        float totalW = cols * (MINI_CELL_SIZE + MINI_CELL_GAP) - MINI_CELL_GAP;
        float totalH = rows * (MINI_CELL_SIZE + MINI_CELL_GAP) - MINI_CELL_GAP;
        float startX = -totalW * 0.5f + MINI_CELL_SIZE * 0.5f;
        float startY =  totalH * 0.5f - MINI_CELL_SIZE * 0.5f;

        foreach (var o in offsets)
        {
            int col = o.x - minX;
            int row = maxY - o.y;

            var cellGO = new GameObject($"Cell_{o.x}_{o.y}", typeof(RectTransform), typeof(Image));
            cellGO.transform.SetParent(shapePreviewRoot, false);

            var rt  = cellGO.GetComponent<RectTransform>();
            rt.sizeDelta        = Vector2.one * MINI_CELL_SIZE;
            rt.anchoredPosition = new Vector2(
                startX + col * (MINI_CELL_SIZE + MINI_CELL_GAP),
                startY - row * (MINI_CELL_SIZE + MINI_CELL_GAP));

            var img  = cellGO.GetComponent<Image>();
            img.color         = new Color(0.3f, 0.85f, 0.45f, 0.9f);
            img.raycastTarget = false;
        }
    }

    // ── Helpers ──

    private static string BuildEffectLabel(ItemEffectSlot slot, bool isRisk)
    {
        string prefix = isRisk ? "▼ " : "▲ ";
        float  pct    = slot.value * 100f;
        return $"{prefix}{slot.effectType}  {(pct >= 0 ? "+" : "")}{pct:F0}%";
    }

    private static bool IsRiskEffect(string effectType)
    {
        if (string.IsNullOrEmpty(effectType)) return false;
        string lower = effectType.ToLowerInvariant();
        return lower.Contains("damage_taken") || lower.Contains("risk") || lower.Contains("penalty");
    }

    private static Color RarityColor(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Rare      => COLOR_RARE,
        ItemRarity.Epic      => COLOR_EPIC,
        ItemRarity.Legendary => COLOR_LEGENDARY,
        _                    => COLOR_COMMON,
    };

    private static string RarityLabel(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Rare      => "◇ Rare",
        ItemRarity.Epic      => "◆ Epic",
        ItemRarity.Legendary => "✦ Legendary",
        _                    => "· Common",
    };
}
