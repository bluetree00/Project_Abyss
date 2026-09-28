using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;
using System.Threading;

/// <summary>
/// 우측 아이템 정보 패널 — 룬 선택 카드와 같은 얼굴(09-25 UX 시안 「룬 선택 규격」).
/// 등급 보석 테두리 · 문양과 등급빛 · 이름 · 등급/속성 칩 · 효과 전부 · 작은 모양 + 놓을 자리.
///
/// 표시 우선순위:
/// 1. 방금 획득한 아이템 (ShowItem isNew=true)
/// 2. 보관함 첫 번째 아이템 (slideIn=true, 뒤집기 연출)
/// 3. 사용자가 클릭한 아이템
/// 4. 빈 상태 (ShowEmpty)
/// </summary>
public sealed class ItemInfoPanel : MonoBehaviour
{
    // ── Constants ──
    private const float FLIP_HALF       = 0.12f;   // 다른 룬으로 바뀔 때 반 바퀴(접힘 → 펼침) — 룬 선택 FlipHalf와 같은 결
    private const float MINI_CELL_SIZE  = 34f;
    private const float MINI_CELL_GAP   = 3f;
    private const float GEM_CORNER_W    = 56f;
    private const float GEM_BAR_W       = 200f;

    private static readonly Color COLOR_RISK      = new(1f, 0.35f, 0.35f, 1f);
    private static readonly Color COLOR_NORMAL_FX = new(0.93f, 0.95f, 1f,  1f);

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

    [Header("룬 카드 규격 (없으면 예전 표기)")]
    [SerializeField] private RectTransform cardRoot;     // 등급 보석 테두리·뒤집기 대상
    [SerializeField] private Image         iconGlow;     // 문양 뒤 등급빛
    [SerializeField] private RectTransform chipRow;      // 등급·칸 수 / 속성 칩
    [SerializeField] private Image         fitBorder;    // 놓을 자리 배지
    [SerializeField] private Image         fitFill;
    [SerializeField] private TMP_Text      fitLabel;

    [Header("빈 상태")]
    [SerializeField] private GameObject  emptyRoot;
    [SerializeField] private TMP_Text    emptyText;

    [Header("NEW 뱃지")]
    [SerializeField] private GameObject newBadge;

    [Header("폰트")]
    [SerializeField] private TMP_FontAsset panelFont;

    // ── Private ──
    private RuntimeItemData _currentItem;
    private CancellationTokenSource _flipCts;
    private readonly List<GameObject> _effectRows = new();
    private readonly List<GameObject> _shapeCells = new();

    // ── Lifecycle ──
    private void Awake()
    {
        ShowEmpty();
    }

    private void OnDestroy()
    {
        _flipCts?.Cancel();
        _flipCts?.Dispose();
    }

    // ── Public API ──

    /// <summary>아이템 정보를 표시한다.</summary>
    public void ShowItem(RuntimeItemData item, bool isNew, bool slideIn = false)
    {
        if (item == null) { ShowEmpty(); return; }

        // 다른 룬으로 바뀔 때만 뒤집는다 — 같은 룬을 다시 그리는 갱신(배치·호버 복귀)마다 돌면 화면이 들썩인다.
        bool changed = item != _currentItem;
        _currentItem = item;

        if (emptyRoot != null) emptyRoot.SetActive(false);
        if (itemRoot  != null) itemRoot.SetActive(true);
        if (newBadge  != null) newBadge.SetActive(isNew);

        // 문양 — 룬은 컨셉 문양(RuneArt)이 얼굴이다. 룬 선택·보관함·판과 같은 그림.
        if (itemIcon != null)
        {
            var art = RuneArt.ResolveRuneIcon(item) ?? item.icon;
            itemIcon.sprite         = art;
            itemIcon.enabled        = art != null;
            itemIcon.preserveAspect = true;
        }

        var rarityColor = ShopUIStyle.Rarity(item.rarity);
        if (iconGlow != null)
        {
            iconGlow.sprite = UI_RuneSelectPopup.SoftDot;
            iconGlow.color  = new Color(rarityColor.r, rarityColor.g, rarityColor.b,
                                        item.rarity == ItemRarity.Common ? 0.18f : 0.45f);
        }

        // 이름
        if (itemName != null)
            itemName.text = item.displayName ?? item.itemId;

        // 등급·속성 — 칩 줄이 있으면 룬 선택과 같은 칩으로, 없으면 예전 한 줄 표기.
        if (chipRow != null)
            RuneCardKit.BuildChipRow(chipRow, item);
        else if (rarityText != null)
        {
            var elem = ElementDef.GetById(item.element);
            rarityText.richText = true;
            rarityText.text = elem != null
                ? $"{RewardPresentation.RarityLabel(item.rarity)}  ·  <color={ElementDef.IdHex(item.element)}>{elem.Icon}{elem.Name}</color>"
                : RewardPresentation.RarityLabel(item.rarity);
            rarityText.color = rarityColor;
        }
        if (rarityBar != null)
            rarityBar.color = rarityColor;

        // 효과 목록 — 전부 보여준다(한 개만 보이면 둘째 효과가 없는 룬으로 읽힌다)
        BuildEffectList(item);

        // 모양 + 놓을 자리
        BuildShapePreview(item);
        RefreshFitBadge(item);

        // 등급 보석 테두리·전설 광택 — 카드 위에 얹는다
        if (cardRoot != null)
        {
            RuneCardKit.BuildGemFrame(cardRoot, item.rarity, GEM_CORNER_W, GEM_BAR_W);
            RebuildLegendShine(item.rarity == ItemRarity.Legendary);
        }

        if (changed || slideIn)
            PlayFlipAsync().Forget();
    }

    /// <summary>빈 상태를 표시한다.</summary>
    public void ShowEmpty()
    {
        // 코드로 조립된 패널엔 itemRoot가 없어 아이콘이 그대로 남는다 — 스프라이트 없는 Image는 흰 사각형으로 보인다(2026-09-09 실측).
        if (itemIcon != null) itemIcon.enabled = false;
        _currentItem = null;
        if (itemRoot  != null) itemRoot.SetActive(false);
        if (emptyRoot != null) emptyRoot.SetActive(true);
        if (newBadge  != null) newBadge.SetActive(false);
        if (cardRoot  != null)
        {
            RuneCardKit.ClearGemFrame(cardRoot);
            RebuildLegendShine(false);
        }
    }

    // ── Effect List ──

    private void BuildEffectList(RuntimeItemData item)
    {
        // 기존 행 제거
        foreach (var row in _effectRows)
            if (row != null) Destroy(row);
        _effectRows.Clear();

        if (effectListRoot == null || item.effects == null) return;

        var style = EffectRowStyle.Default;
        style.fontAsset       = panelFont;
        style.fontSize        = 18f;   // 상세는 카드보다 크게 — 룬 선택 카드 16, 이 칸은 폭 400
        style.iconSize        = 22f;
        style.rowHeight       = 28f;
        style.wrap            = true;  // 조건부 효과 문장이 한 줄을 넘는다 — 행이 자라게
        style.usePrefixArrows = true;
        style.normalColor     = COLOR_NORMAL_FX;
        style.riskColor       = COLOR_RISK;

        foreach (var slot in item.effects)
        {
            if (string.IsNullOrEmpty(slot.effectType)) continue;
            var widget = EffectRowWidget.Create(effectListRoot, style, slot);
            _effectRows.Add(widget.gameObject);
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

        if (shapePreviewRoot == null) return;
        // 칸 크기는 자리(상자)에 맞춰 줄어든다 — 12칸 전설도 같은 상자에 들어간다.
        RuneCardKit.BuildShape(shapePreviewRoot, item.shapeId > 0 ? item : null, MINI_CELL_SIZE, MINI_CELL_GAP);
    }

    /// <summary>
    /// 놓을 자리 배지. 이미 판에 놓인 룬은 「판에 놓임」 — 놓인 룬을 다시 판정하면 자기 자리를 모르고 "없음"이 뜬다.
    /// </summary>
    private void RefreshFitBadge(RuntimeItemData item)
    {
        if (fitLabel == null) return;

        bool placed = IsPlaced(item);
        bool ok     = placed || RuneCardKit.CanPlace(item);
        RuneCardKit.ApplyFitBadge(fitBorder, fitFill, fitLabel, ok,
            placed ? "판에 놓임" : ok ? "놓을 자리 있음" : "놓을 자리 없음");
    }

    private static bool IsPlaced(RuntimeItemData item)
    {
        var placed = GameRunBootstrapper.Instance?.Run?.ItemInventory?.PlacedItems;
        if (placed == null || item == null) return false;
        foreach (var p in placed)
            if (p == item) return true;
        return false;
    }

    // ── 전설 광택 ──

    private void RebuildLegendShine(bool on)
    {
        var old = cardRoot.Find("LegendShine");
        if (old != null) { old.name = "LegendShine_old"; Destroy(old.gameObject); }
        if (!on) return;

        var go = new GameObject("LegendShine", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(cardRoot, false);
        ShopUIStyle.Stretch(rt);
        go.AddComponent<StagingSlotShimmer>()
          .Configure(StagingSlotShimmer.LegendaryTint, 0.22f, 3.2f, Mathf.Max(200f, cardRoot.rect.width), phase: 0f);
    }

    // ── 뒤집기 연출 ──

    /// <summary>
    /// 다른 룬으로 바뀔 때 카드를 반 바퀴 접었다 편다(가로 배율 1→0→1). 룬판은 시간정지라 unscaled.
    /// 카드 루트가 없으면 예전처럼 캔버스 그룹을 페이드한다.
    /// </summary>
    private async UniTaskVoid PlayFlipAsync()
    {
        _flipCts?.Cancel();
        _flipCts?.Dispose();
        _flipCts = new CancellationTokenSource();
        var ct = _flipCts.Token;

        var target = cardRoot != null ? cardRoot : transform as RectTransform;
        if (target == null) return;

        try
        {
            if (canvasGroup != null && cardRoot == null) canvasGroup.alpha = 0f;
            for (float t = 0f; t < 1f; )
            {
                t += Time.unscaledDeltaTime / (FLIP_HALF * 2f);
                float p = Mathf.Clamp01(t);
                // 앞 절반은 이미 새 내용이라 접힌 상태에서 시작해 펴기만 한다 — 옛 내용이 새 이름으로 비치지 않게.
                float sx = 1f - Mathf.Pow(1f - p, 3f);
                target.localScale = new Vector3(Mathf.Max(0.02f, sx), 1f, 1f);
                if (canvasGroup != null && cardRoot == null) canvasGroup.alpha = p;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (System.OperationCanceledException) { }
        finally
        {
            if (target != null) target.localScale = Vector3.one;
            if (canvasGroup != null) canvasGroup.alpha = 1f;
        }
    }
}
