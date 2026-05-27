using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;
using System.Threading;

/// <summary>
/// 우측 아이템 정보 패널.
///
/// 표시 우선순위:
/// 1. 방금 획득한 아이템 (ShowItem isNew=true)
/// 2. 보관함 첫 번째 아이템 (slideIn=true, 슬라이드 애니메이션)
/// 3. 사용자가 클릭한 아이템
/// 4. 빈 상태 (ShowEmpty)
/// </summary>
public sealed class ItemInfoPanel : MonoBehaviour
{
    // ── Constants ──
    private const float SLIDE_DURATION  = 0.22f;
    private const float MINI_CELL_SIZE  = 22f;
    private const float MINI_CELL_GAP   = 2f;

    private static readonly Color COLOR_RISK      = new(1f, 0.35f, 0.35f, 1f);
    private static readonly Color COLOR_NORMAL_FX = new(0.85f, 0.92f, 1f,  1f);
    private static readonly Color COLOR_EMPTY_TXT = new(0.5f, 0.5f, 0.6f, 0.8f);

    private static readonly Color COLOR_COMMON    = new(0.75f, 0.75f, 0.75f, 1f);
    private static readonly Color COLOR_RARE      = new(0.3f,  0.6f,  1f,   1f);
    private static readonly Color COLOR_EPIC      = new(0.7f,  0.3f,  1f,   1f);
    private static readonly Color COLOR_LEGENDARY = new(1f,    0.7f,  0.2f, 1f);

    // ── SerializeField ──
    [Header("공통 루트")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("아이템 정보")]
    [SerializeField] private GameObject  itemRoot;
    [SerializeField] private Image       itemIcon;
    [SerializeField] private TMP_Text    itemName;
    [SerializeField] private TMP_Text    rarityText;
    [SerializeField] private Image       rarityBar;
    [SerializeField] private Transform   effectListRoot;
    [SerializeField] private TMP_Text    effectItemPrefabText;   // 풀링 대신 직접 생성
    [SerializeField] private RectTransform shapePreviewRoot;

    [Header("빈 상태")]
    [SerializeField] private GameObject  emptyRoot;
    [SerializeField] private TMP_Text    emptyText;

    [Header("NEW 뱃지")]
    [SerializeField] private GameObject newBadge;

    [Header("폰트")]
    [SerializeField] private TMP_FontAsset panelFont;

    // ── Private ──
    private RuntimeItemData _currentItem;
    private CancellationTokenSource _slideCts;
    private readonly List<GameObject> _effectRows = new();
    private readonly List<GameObject> _shapeCells = new();

    // ── Lifecycle ──
    private void Awake()
    {
        ShowEmpty();
    }

    private void OnDestroy()
    {
        _slideCts?.Cancel();
        _slideCts?.Dispose();
    }

    // ── Public API ──

    /// <summary>아이템 정보를 표시한다.</summary>
    public void ShowItem(RuntimeItemData item, bool isNew, bool slideIn = false)
    {
        if (item == null) { ShowEmpty(); return; }

        _currentItem = item;

        if (emptyRoot != null) emptyRoot.SetActive(false);
        if (itemRoot  != null) itemRoot.SetActive(true);
        if (newBadge  != null) newBadge.SetActive(isNew);

        // 아이콘
        if (itemIcon != null)
        {
            itemIcon.sprite  = item.icon;
            itemIcon.enabled = item.icon != null;
        }

        // 이름
        if (itemName != null)
            itemName.text = item.displayName ?? item.itemId;

        // 레어도
        var rarityColor = RarityColor(item.rarity);
        if (rarityText != null)
        {
            rarityText.text  = RarityLabel(item.rarity);
            rarityText.color = rarityColor;
        }
        if (rarityBar != null)
            rarityBar.color = rarityColor;

        // 효과 목록
        BuildEffectList(item);

        // Shape 미니 프리뷰
        BuildShapePreview(item);

        if (slideIn)
            PlaySlideInAsync().Forget();
    }

    /// <summary>빈 상태를 표시한다.</summary>
    public void ShowEmpty()
    {
        _currentItem = null;
        if (itemRoot  != null) itemRoot.SetActive(false);
        if (emptyRoot != null) emptyRoot.SetActive(true);
        if (newBadge  != null) newBadge.SetActive(false);
    }

    // ── Effect List ──

    private void BuildEffectList(RuntimeItemData item)
    {
        // 기존 행 제거
        foreach (var row in _effectRows)
            if (row != null) Destroy(row);
        _effectRows.Clear();

        if (effectListRoot == null || item.effects == null) return;

        foreach (var slot in item.effects)
        {
            if (string.IsNullOrEmpty(slot.effectType)) continue;

            bool isRisk = IsRiskEffect(slot.effectType);

            var rowGO = new GameObject("EffectRow", typeof(RectTransform));
            rowGO.transform.SetParent(effectListRoot, false);

            var txt = rowGO.AddComponent<TextMeshProUGUI>();
            if (panelFont != null) txt.font = panelFont;
            txt.fontSize   = 13f;
            txt.color      = isRisk ? COLOR_RISK : COLOR_NORMAL_FX;
            txt.text       = BuildEffectLabel(slot, isRisk);
            txt.enableWordWrapping = false;

            var rt = rowGO.GetComponent<RectTransform>();
            rt.anchorMin  = new Vector2(0f, 1f);
            rt.anchorMax  = new Vector2(1f, 1f);
            rt.sizeDelta  = new Vector2(0f, 20f);

            _effectRows.Add(rowGO);
        }

        // 수직 레이아웃 갱신
        if (effectListRoot.TryGetComponent<VerticalLayoutGroup>(out _))
            LayoutRebuilder.ForceRebuildLayoutImmediate(effectListRoot as RectTransform);
    }

    // ── Shape Preview ──

    private void BuildShapePreview(RuntimeItemData item)
    {
        foreach (var cell in _shapeCells)
            if (cell != null) Destroy(cell);
        _shapeCells.Clear();

        if (shapePreviewRoot == null || item.shapeId == 0) return;

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

            _shapeCells.Add(cellGO);
        }
    }

    // ── Slide Animation ──

    private async UniTaskVoid PlaySlideInAsync()
    {
        _slideCts?.Cancel();
        _slideCts?.Dispose();
        _slideCts = new CancellationTokenSource();
        var ct = _slideCts.Token;

        if (canvasGroup == null) return;

        var rt = GetComponent<RectTransform>();
        float originX = rt != null ? rt.anchoredPosition.x : 0f;
        const float SLIDE_OFFSET = 40f;

        try
        {
            canvasGroup.alpha = 0f;
            if (rt != null)
                rt.anchoredPosition = new Vector2(originX + SLIDE_OFFSET, rt.anchoredPosition.y);

            float t = 0f;
            while (t < 1f)
            {
                ct.ThrowIfCancellationRequested();
                t += Time.unscaledDeltaTime / SLIDE_DURATION;
                float eased = Mathf.Clamp01(t);
                canvasGroup.alpha = eased;
                if (rt != null)
                    rt.anchoredPosition = new Vector2(
                        Mathf.Lerp(originX + SLIDE_OFFSET, originX, eased),
                        rt.anchoredPosition.y);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            canvasGroup.alpha = 1f;
            if (rt != null)
                rt.anchoredPosition = new Vector2(originX, rt.anchoredPosition.y);
        }
        catch (System.OperationCanceledException)
        {
            canvasGroup.alpha = 1f;
            if (rt != null)
                rt.anchoredPosition = new Vector2(originX, rt.anchoredPosition.y);
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
