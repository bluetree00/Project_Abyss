using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// EscBookPopup의 인벤토리 페이지.
/// RunItemInventory의 아이템을 그리드로 표시 + 호버 툴팁.
/// pageInventory 오브젝트에 부착.
/// </summary>
public class InventoryPageView : MonoBehaviour
{
    // ── Constants ──
    private const float SLOT_SIZE = 64f;
    private const float SLOT_SPACING = 6f;
    private const int COLUMNS = 5;
    private const float TOOLTIP_WIDTH = 220f;

    // ── Private ──
    private RunItemInventory _inventory;
    private RectTransform _gridRoot;
    private GameObject _tooltip;
    private TMP_Text _tooltipText;
    private readonly List<GameObject> _slots = new();
    private bool _bound;

    // ── Lifecycle ──

    private void OnEnable()
    {
        Bind();
        Refresh();
    }

    private void OnDisable()
    {
        HideTooltip();
    }

    // ── Public Methods ──

    public void Refresh()
    {
        if (!_bound) Bind();
        if (_inventory == null) return;

        ClearSlots();

        foreach (var item in _inventory.Items)
            CreateSlot(item);
    }

    // ── Private Methods ──

    private void Bind()
    {
        if (_bound) return;

        var run = GameRunBootstrapper.Instance?.Run;
        if (run == null) return;

        _inventory = run.ItemInventory;
        _inventory.OnInventoryChanged -= Refresh;
        _inventory.OnInventoryChanged += Refresh;

        EnsureGridRoot();
        EnsureTooltip();
        _bound = true;
    }

    private void EnsureGridRoot()
    {
        if (_gridRoot != null) return;

        var gridGO = new GameObject("ItemGrid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        gridGO.transform.SetParent(transform, false);

        _gridRoot = gridGO.GetComponent<RectTransform>();
        _gridRoot.anchorMin = new Vector2(0, 1);
        _gridRoot.anchorMax = new Vector2(1, 1);
        _gridRoot.pivot = new Vector2(0.5f, 1);
        _gridRoot.anchoredPosition = new Vector2(0, -10);
        _gridRoot.sizeDelta = new Vector2(0, 0);

        var layout = gridGO.GetComponent<GridLayoutGroup>();
        layout.cellSize = Vector2.one * SLOT_SIZE;
        layout.spacing = Vector2.one * SLOT_SPACING;
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = COLUMNS;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.padding = new RectOffset(10, 10, 10, 10);

        var fitter = gridGO.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private void EnsureTooltip()
    {
        if (_tooltip != null) return;

        // 툴팁 패널
        _tooltip = new GameObject("ItemTooltip", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        _tooltip.transform.SetParent(transform.root, false);

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

        // 이벤트 트리거 (호버)
        var trigger = slotGO.AddComponent<EventTrigger>();

        var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        var capturedItem = item;
        enterEntry.callback.AddListener(e => ShowTooltip(capturedItem, slotGO.GetComponent<RectTransform>()));
        trigger.triggers.Add(enterEntry);

        var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exitEntry.callback.AddListener(e => HideTooltip());
        trigger.triggers.Add(exitEntry);

        _slots.Add(slotGO);
    }

    private void ShowTooltip(RuntimeItemData item, RectTransform slotRT)
    {
        if (_tooltip == null || _tooltipText == null) return;

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

        _tooltipText.text = text;

        // 위치 (슬롯 오른쪽)
        var tooltipRT = _tooltip.GetComponent<RectTransform>();
        Vector3 worldPos = slotRT.position;
        tooltipRT.position = worldPos + new Vector3(SLOT_SIZE * 0.6f, 0, 0);

        _tooltip.SetActive(true);
    }

    private void HideTooltip()
    {
        if (_tooltip != null)
            _tooltip.SetActive(false);
    }

    private void ClearSlots()
    {
        foreach (var slot in _slots)
            if (slot != null) Destroy(slot);
        _slots.Clear();
    }

    private void OnDestroy()
    {
        if (_inventory != null)
            _inventory.OnInventoryChanged -= Refresh;
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
