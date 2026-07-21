using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 룬 선택 팝업 (Canvas_Popup, Addressable "UI_RuneSelectPopup").
///
/// 룬 획득 = <b>후보 N개 중 1개 선택</b>. 기존 <see cref="UI_ItemAcquisitionPopup"/>은 단일 알림이라
/// "고르는" 결정이 없었다 — 이 팝업이 매 방 보상을 결정으로 바꾼다.
///
/// 카드 구성(설계서 §4):
///  - <b>모양 미리보기</b> — 선택의 핵심. 효과가 좋아도 판에 안 들어가면 무의미하다.
///  - 이름 / 등급·칸수 / 효과
///  - <b>배치 가능 배지</b> — 인접 제약 때문에 실제로 못 놓는 룬이 생긴다. 경고일 뿐 선택은 막지 않는다.
///
/// 속성(불·얼음 등)은 <b>표시하지 않는다</b>. 확정 모델상 속성은 배치 위치가 정하며,
/// 카드에 속성을 붙이면 "효과=조각 / 시너지=위치" 2층 구조가 무너진다.
/// </summary>
public sealed class UI_RuneSelectPopup : UI_Popup
{
    public override bool BlocksGameplay => true;
    public override bool CloseOnEscape  => false;   // 보상 결정이라 실수로 닫히면 안 된다 — 선택/넘기기로만 종료

    // ── 레이아웃 ──
    private const float WindowW = 1100f;
    private const float WindowH = 620f;
    private const float CardW   = 300f;
    private const float CardH   = 380f;
    private const float CardGap = 24f;
    private const float CardY   = -40f;

    private const float MiniCell = 22f;
    private const float MiniGap  = 3f;

    private static readonly Color CardSelected  = new(0.20f, 0.17f, 0.10f, 1f);
    private static readonly Color SelectBorder  = new(0.88f, 0.72f, 0.32f, 1f);
    private static readonly Color OkColor       = new(0.37f, 0.81f, 0.52f, 1f);
    private static readonly Color NoColor       = new(0.88f, 0.33f, 0.25f, 1f);

    // ── 상태 ──
    private readonly List<CardView> _cards = new();
    private List<(RuntimeItemData data, ItemSO so)> _candidates;
    private RunItemInventory _inventory;
    private int _selected = -1;
    private bool _built;

    private UniTaskCompletionSource _interactionTcs;

    private TMP_Text _counterText;
    private Image    _confirmBtnImg;
    private TMP_Text _confirmLabel;

    /// <summary>선택된 룬. 넘겼으면 null.</summary>
    public RuntimeItemData Result { get; private set; }
    /// <summary>넘기기로 종료했는지(넘기기 보상 지급 판단용).</summary>
    public bool Skipped { get; private set; }

    private sealed class CardView
    {
        public Image      Border;
        public Image      Fill;
        public GameObject Root;
    }

    // ── Lifecycle ──

    public override void Init()
    {
        base.Init();
        BuildChrome();
    }

    // ── Public API ──

    /// <summary>버튼 클릭 즉시 resolve — 애니메이션을 기다리지 않는다.</summary>
    public UniTask WaitForInteractionAsync(System.Threading.CancellationToken ct)
    {
        _interactionTcs = new UniTaskCompletionSource();
        return _interactionTcs.Task.AttachExternalCancellation(ct);
    }

    /// <summary>후보를 주입해 카드를 구성한다. ShowPopupUIAndGetAsync 직후 호출.</summary>
    public void Setup(List<(RuntimeItemData data, ItemSO so)> candidates, RunItemInventory inventory)
    {
        _candidates = candidates;
        _inventory  = inventory;

        if (candidates == null || candidates.Count == 0)
        {
            Debug.LogWarning("[UI_RuneSelectPopup] 후보 없음 — 즉시 닫음");
            Skipped = true;
            _interactionTcs?.TrySetResult();
            ClosePopupUI();
            return;
        }

        BuildCards();
        RefreshCounter();
        SetSelected(-1);
    }

    // ── Build ──

    private void BuildChrome()
    {
        if (_built) return;
        _built = true;

        ShopUIStyle.Stretch(GetComponent<RectTransform>());

        var veil = ShopUIStyle.MakeImage(transform, "Veil", ShopUIStyle.Veil, raycast: true);
        ShopUIStyle.Stretch(veil.rectTransform);

        // MakeFrame은 inner(채움)를 반환한다 — 위치/크기는 부모(테두리)에 건다.
        var window = ShopUIStyle.MakeFrame(transform, "Window",
            ShopUIStyle.WindowBorder, ShopUIStyle.WindowFill, 2f, raycast: true);
        ShopUIStyle.Anchor((RectTransform)window.transform.parent,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(WindowW, WindowH));
        _windowRoot = window.transform;

        // 제목
        var title = ShopUIStyle.MakeText(_windowRoot, "Title", 26f, FontStyles.Bold,
            TextAlignmentOptions.Left, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(title.rectTransform,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(34f, -26f), new Vector2(600f, 36f));
        title.text = "룬 획득 — 하나를 고르세요";

        // 보관함/배치 카운터
        _counterText = ShopUIStyle.MakeText(_windowRoot, "Counter", 16f, FontStyles.Normal,
            TextAlignmentOptions.Right, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(_counterText.rectTransform,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-34f, -30f), new Vector2(420f, 28f));

        BuildFooter();
    }

    private Transform _windowRoot;

    private void BuildFooter()
    {
        // [선택]
        var confirm = ShopUIStyle.MakeFrame(_windowRoot, "ConfirmBtn",
            ShopUIStyle.BronzeLine, ShopUIStyle.BandFill, 2f, raycast: true);
        ShopUIStyle.Anchor((RectTransform)confirm.transform.parent,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-90f, 34f), new Vector2(200f, 56f));
        _confirmBtnImg = confirm;
        AddClick(confirm.transform.parent.gameObject, OnConfirmClicked);

        _confirmLabel = ShopUIStyle.MakeText(confirm.transform, "Label", 20f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Stretch(_confirmLabel.rectTransform);
        _confirmLabel.text = "선택";

        // [넘기기]
        var skip = ShopUIStyle.MakeFrame(_windowRoot, "SkipBtn",
            ShopUIStyle.CardBorder, ShopUIStyle.CardFill, 2f, raycast: true);
        ShopUIStyle.Anchor((RectTransform)skip.transform.parent,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(120f, 34f), new Vector2(140f, 56f));
        AddClick(skip.transform.parent.gameObject, OnSkipClicked);

        var skipLbl = ShopUIStyle.MakeText(skip.transform, "Label", 18f, FontStyles.Normal,
            TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        ShopUIStyle.Stretch(skipLbl.rectTransform);
        skipLbl.text = "넘기기";
    }

    private void BuildCards()
    {
        foreach (var c in _cards) if (c.Root != null) Destroy(c.Root);
        _cards.Clear();

        int n = _candidates.Count;
        float totalW = n * CardW + (n - 1) * CardGap;
        float startX = -totalW * 0.5f + CardW * 0.5f;

        for (int i = 0; i < n; i++)
        {
            int idx = i;   // 클로저 캡처
            var (data, so) = _candidates[i];

            var card = ShopUIStyle.MakeFrame(_windowRoot, $"Card{i}",
                ShopUIStyle.CardBorder, ShopUIStyle.CardFill, 2f, raycast: true);
            var cardRT = (RectTransform)card.transform.parent;   // 위치/크기는 테두리(outer)에
            ShopUIStyle.Anchor(cardRT,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(startX + i * (CardW + CardGap), CardY), new Vector2(CardW, CardH));

            AddClick(cardRT.gameObject, () => SetSelected(idx));

            var view = new CardView
            {
                Root   = cardRT.gameObject,                  // outer(테두리)가 루트
                Border = cardRT.GetComponent<Image>(),
                Fill   = card,
            };
            _cards.Add(view);

            BuildCardContent(card.transform, data, so);
        }
    }

    private void BuildCardContent(Transform card, RuntimeItemData data, ItemSO so)
    {
        // 등급 리본
        var ribbon = ShopUIStyle.MakeImage(card, "Ribbon", ShopUIStyle.RarityGlow(data.rarity));
        ShopUIStyle.Anchor(ribbon.rectTransform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, 4f));

        // 모양 미리보기
        var shapeBox = ShopUIStyle.MakeImage(card, "ShapeBox", ShopUIStyle.IconBg);
        ShopUIStyle.Anchor(shapeBox.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -18f), new Vector2(CardW - 40f, 130f));

        bool canPlace = BuildShapePreview(shapeBox.transform, data);

        // 이름
        var name = ShopUIStyle.MakeText(card, "Name", 19f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(name.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -158f), new Vector2(CardW - 24f, 26f));
        name.text = data.displayName ?? data.itemId;

        // 등급 · 칸수
        int cells = CellCount(data.shapeId);
        var meta = ShopUIStyle.MakeText(card, "Meta", 14f, FontStyles.Normal,
            TextAlignmentOptions.Center, ShopUIStyle.RarityGlow(data.rarity));
        ShopUIStyle.Anchor(meta.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -186f), new Vector2(CardW - 24f, 22f));
        meta.text = cells > 0 ? $"{RarityLabel(data.rarity)} · {cells}칸" : RarityLabel(data.rarity);

        // 효과 목록
        var fxRoot = ShopUIStyle.MakeRect(card, "Effects").GetComponent<RectTransform>();
        ShopUIStyle.Anchor(fxRoot,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -214f), new Vector2(CardW - 32f, 90f));
        var vlg = fxRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childControlHeight = false; vlg.childForceExpandHeight = false;
        vlg.spacing = 2f;
        BuildEffectRows(fxRoot, data);

        // 배치 가능 배지
        var badge = ShopUIStyle.MakeText(card, "PlaceBadge", 14f, FontStyles.Bold,
            TextAlignmentOptions.Center, canPlace ? OkColor : NoColor);
        ShopUIStyle.Anchor(badge.rectTransform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 14f), new Vector2(CardW - 24f, 24f));
        badge.text = canPlace ? "▸ 지금 판에 배치 가능" : "✕ 놓을 자리 없음";
    }

    private void BuildEffectRows(RectTransform parent, RuntimeItemData data)
    {
        if (data.effects == null) return;

        var style = EffectRowStyle.Default;
        style.fontSize        = 14f;
        style.iconSize        = 16f;
        style.rowHeight       = 20f;
        style.usePrefixArrows = true;

        foreach (var slot in data.effects)
        {
            if (string.IsNullOrEmpty(slot.effectType)) continue;
            EffectRowWidget.Create(parent, style, slot);
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
    }

    /// <summary>모양 셀을 그리고, 지금 판에 놓을 자리가 있는지 반환한다.</summary>
    private bool BuildShapePreview(Transform root, RuntimeItemData data)
    {
        var entry = Managers.RuneData?.GetShape(data.shapeId);
        if (entry == null) return true;   // 판정 불가 → 막지 않는다

        var offsets = RuneDataManager.ParseCellOffsets(entry);
        if (offsets == null || offsets.Length == 0) return true;

        bool canPlace = MerlinRuneBridge.Instance == null
            || MerlinRuneBridge.Instance.CanPlaceShape(offsets);

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var o in offsets)
        {
            if (o.x < minX) minX = o.x;  if (o.x > maxX) maxX = o.x;
            if (o.y < minY) minY = o.y;  if (o.y > maxY) maxY = o.y;
        }
        int cols = maxX - minX + 1, rows = maxY - minY + 1;

        float totalW = cols * (MiniCell + MiniGap) - MiniGap;
        float totalH = rows * (MiniCell + MiniGap) - MiniGap;
        float startX = -totalW * 0.5f + MiniCell * 0.5f;
        float startY =  totalH * 0.5f - MiniCell * 0.5f;

        // 룬별 고유색 — 그리드 썸네일(GridThumbnail.GetItemColor)과 같은 해시 팔레트를 써서 카드/보관함/판 색을 일치.
        Color runeColor = GridThumbnail.GetItemColor(data.instanceId);
        Color dimColor  = new Color(runeColor.r * 0.5f, runeColor.g * 0.5f, runeColor.b * 0.5f, 0.7f);

        foreach (var o in offsets)
        {
            int col = o.x - minX;
            int row = maxY - o.y;

            var cell = ShopUIStyle.MakeImage(root, $"C{o.x}_{o.y}",
                canPlace ? runeColor : dimColor);
            ShopUIStyle.Anchor(cell.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(startX + col * (MiniCell + MiniGap), startY - row * (MiniCell + MiniGap)),
                Vector2.one * MiniCell);
        }

        return canPlace;
    }

    // ── 선택 상태 ──

    private void SetSelected(int index)
    {
        _selected = index;

        for (int i = 0; i < _cards.Count; i++)
        {
            bool on = (i == index);
            if (_cards[i].Border != null)
                _cards[i].Border.color = on ? SelectBorder : ShopUIStyle.CardBorder;
            if (_cards[i].Fill != null)
                _cards[i].Fill.color = on ? CardSelected : ShopUIStyle.CardFill;
        }

        bool hasSel = index >= 0;
        if (_confirmLabel != null)
            _confirmLabel.color = hasSel ? ShopUIStyle.TextPrimary : ShopUIStyle.TextDim;
        if (_confirmBtnImg != null)
            _confirmBtnImg.color = hasSel ? ShopUIStyle.GoldPillBg : ShopUIStyle.BandFill;
    }

    private void RefreshCounter()
    {
        if (_counterText == null) return;
        int staged = _inventory?.StagingItems?.Count ?? 0;
        int placed = _inventory?.PlacedItems?.Count ?? 0;
        _counterText.text = $"보관함 {staged}/{RunItemInventory.MaxStagingCapacity}   ·   배치 {placed}";
    }

    // ── 버튼 ──

    private void OnConfirmClicked()
    {
        if (_selected < 0 || _candidates == null || _selected >= _candidates.Count)
            return;   // 미선택 — 아무 일도 하지 않는다

        var item = _candidates[_selected].data;
        Result  = item;
        Skipped = false;
        _interactionTcs?.TrySetResult();

        // 보관함 만차 시 조용히 사라지지 않도록, 실패해도 그리드가 '보류'로 들고 간다.
        bool added = _inventory != null && _inventory.AddToStaging(item);
        if (!added)
            ItemEffectVfxHelper.ShowNotice(
                $"<color=#FFCC44>보관함 가득 참</color> ({RunItemInventory.MaxStagingCapacity}칸) — 자리를 비우면 자동으로 추가됩니다");

        ClosePopupUI();

        if (UI_GridPanel.Instance == null)
            Managers.UI?.ShowOverlayUI<UI_GridPanel>();
        if (UI_GridPanel.Instance == null) return;

        if (added) UI_GridPanel.Instance.ShowWithNewItem(item);
        else       UI_GridPanel.Instance.ShowWithPendingItem(item);
    }

    private void OnSkipClicked()
    {
        Result  = null;
        Skipped = true;
        _interactionTcs?.TrySetResult();
        ClosePopupUI();
    }

    // ── Helpers ──

    private static void AddClick(GameObject go, Action onClick)
    {
        var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => onClick?.Invoke());
    }

    private static int CellCount(int shapeId)
    {
        var entry = Managers.RuneData?.GetShape(shapeId);
        if (entry == null) return 0;
        var offsets = RuneDataManager.ParseCellOffsets(entry);
        return offsets?.Length ?? 0;
    }

    private static string RarityLabel(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Rare      => "◇ Rare",
        ItemRarity.Epic      => "◆ Epic",
        ItemRarity.Legendary => "✦ Legendary",
        _                    => "· Common",
    };
}
