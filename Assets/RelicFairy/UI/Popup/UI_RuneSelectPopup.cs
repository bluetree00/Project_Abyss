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
/// 속성은 <b>상단 리본색 + 이름/태그</b>로 표시한다 — 어느 존에 놓을지 판단하는 근거이자
/// 디자이너 완성본(0_룬 획득_260723)의 확정 표현. 효과=조각 / 시너지=위치 2층 구조는 그대로다.
/// </summary>
public sealed class UI_RuneSelectPopup : UI_Popup
{
    public override bool BlocksGameplay => true;
    public override bool CloseOnEscape  => false;   // 보상 결정이라 실수로 닫히면 안 된다 — 선택/넘기기로만 종료

    // ── 레이아웃 ──
    private const float WindowW = 1040f;   // 룬 획득 바탕 아트 실측(2081×1241 @2x → 1040×620)
    private const float WindowH = 620f;
    private const float CardW   = 300f;
    private const float CardH   = 380f;
    private const float CardGap = 24f;
    private const float CardY   = -26f;   // 하단 [선택]/[넘기기]와 겹치지 않게 카드를 살짝 올린다

    // 모양 미리보기 셀은 고정 크기가 아니라 <b>박스에 맞춰 확대</b>한다.
    // 고정 22px이던 시절엔 1칸 룬이 점처럼 보여 무슨 모양인지 분간이 안 됐다.
    private const float ShapeBoxH   = 150f;  // 모양 미리보기 박스 높이
    private const float MiniGap     = 4f;
    private const float MiniCellMax = 62f;   // 1~2칸 룬이 시원하게 보이는 상한
    private const float MiniCellMin = 18f;   // 9칸(3×3)도 박스를 안 넘도록 하한

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
    private bool _skinned;   // 아트 로드 성공 — 선택 피드백을 색 틴트 대신 밝기로 처리

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

        var skin = UISkin.RuneSelect;
        _skinned = skin != null;
        // 창 바탕 — 전면 일러스트로 교체(9-slice 아님). 미로드면 기존 색 창 유지.
        ShopUIStyle.Skin(window, skin?.background);

        // 타이틀바 — 제목/카운터 뒤에 깔린다. 아트 없으면 표시 안 됨(투명 폴백).
        if (skin?.titleBar != null)
        {
            var titleBar = ShopUIStyle.MakeImage(_windowRoot, "TitleBar", Color.white);
            ShopUIStyle.Skin(titleBar, skin.titleBar, sliced: true);
            ShopUIStyle.Anchor(titleBar.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -14f), new Vector2(WindowW - 48f, 52f));
        }

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
        var skin = UISkin.RuneSelect;

        // [선택]
        var confirm = ShopUIStyle.MakeFrame(_windowRoot, "ConfirmBtn",
            ShopUIStyle.BronzeLine, ShopUIStyle.BandFill, 2f, raycast: true);
        ShopUIStyle.Anchor((RectTransform)confirm.transform.parent,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-90f, 14f), new Vector2(200f, 52f));
        _confirmBtnImg = confirm;
        ShopUIStyle.Skin(_confirmBtnImg, skin?.confirmButton, sliced: true);
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
            new Vector2(120f, 14f), new Vector2(140f, 52f));
        ShopUIStyle.Skin(skip, skin?.skipButton, sliced: true);
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
            // 카드 테두리 라인아트 + 채움 — 미로드면 색 박스 유지
            var skin = UISkin.RuneSelect;
            ShopUIStyle.Skin(view.Border, skin?.cardFrame, sliced: true);
            ShopUIStyle.Skin(view.Fill,   skin?.cardFill,  sliced: true);
            _cards.Add(view);

            BuildCardContent(card.transform, data, so);
        }
    }

    private void BuildCardContent(Transform card, RuntimeItemData data, ItemSO so)
    {
        // 상단 속성 리본 — 완성본의 색 막대. 어느 존에 놓을지 알려주는 근거색이다.
        int elemIdx = ElementIndex(data.element);
        Color ribbonCol = ElementDef.IdColor(data.element, ShopUIStyle.RarityGlow(data.rarity));
        var ribbon = ShopUIStyle.MakeImage(card, "Ribbon", ribbonCol);
        ShopUIStyle.Anchor(ribbon.rectTransform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -10f), new Vector2(-32f, 14f));
        ShopUIStyle.Skin(ribbon, UISkin.RuneSelect?.ElementRibbon(elemIdx), sliced: true);

        // 속성 엠블럼(룬조각) — 카드 좌상단 배지. 어느 속성 룬인지 한눈에.
        var piece = UISkin.RuneSelect?.ElementPiece(elemIdx);
        if (piece != null)
        {
            var emblem = ShopUIStyle.MakeImage(card, "Emblem", Color.white);
            ShopUIStyle.Skin(emblem, piece);
            emblem.preserveAspect = true;
            ShopUIStyle.Anchor(emblem.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f, -28f), new Vector2(34f, 34f));
        }

        // 모양 미리보기
        var shapeBox = ShopUIStyle.MakeImage(card, "ShapeBox", ShopUIStyle.IconBg);
        ShopUIStyle.Anchor(shapeBox.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -18f), new Vector2(CardW - 40f, ShapeBoxH));

        bool canPlace = BuildShapePreview(shapeBox.transform, data);

        // 이름
        var name = ShopUIStyle.MakeText(card, "Name", 19f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(name.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -172f), new Vector2(CardW - 24f, 26f));
        name.text = data.displayName ?? data.itemId;
        FitSingleLine(name);

        // 등급 · 칸수 · 속성 — 속성명은 속성색으로 표시(어느 존에 놓을지 판단 근거)
        int cells = CellCount(data.shapeId);
        var elem  = ElementDef.GetById(data.element);
        string elemTag = elem != null ? $"  ·  <color={ElementDef.IdHex(data.element)}>{elem.Icon}{elem.Name}</color>" : "";
        var meta = ShopUIStyle.MakeText(card, "Meta", 14f, FontStyles.Normal,
            TextAlignmentOptions.Center, ShopUIStyle.RarityGlow(data.rarity));
        meta.richText = true;
        ShopUIStyle.Anchor(meta.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -200f), new Vector2(CardW - 24f, 22f));
        meta.text = (cells > 0 ? $"{RarityLabel(data.rarity)} · {cells}칸" : RarityLabel(data.rarity)) + elemTag;
        FitSingleLine(meta);

        // 효과 칸 배경 — 아트 있으면 박스로, 없으면 표시 안 함(투명 폴백)
        var fxSkin = UISkin.RuneSelect?.effectBox;
        if (fxSkin != null)
        {
            var fxBg = ShopUIStyle.MakeImage(card, "EffectBox", Color.white);
            ShopUIStyle.Skin(fxBg, fxSkin, sliced: true);
            ShopUIStyle.Anchor(fxBg.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -224f), new Vector2(CardW - 28f, 96f));
        }

        // 효과 목록
        var fxRoot = ShopUIStyle.MakeRect(card, "Effects").GetComponent<RectTransform>();
        ShopUIStyle.Anchor(fxRoot,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -228f), new Vector2(CardW - 32f, 86f));
        var vlg = fxRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childControlHeight = false; vlg.childForceExpandHeight = false;
        vlg.spacing = 2f;
        BuildEffectRows(fxRoot, data);

        // 배치 상태 바 — 완성본의 하단 초록/빨강 막대. 아트 있으면 바로, 없으면 텍스트만.
        var placeSkin = canPlace ? UISkin.RuneSelect?.placeOk : UISkin.RuneSelect?.placeNo;
        Transform badgeParent = card;
        if (placeSkin != null)
        {
            var bar = ShopUIStyle.MakeImage(card, "PlaceBar", Color.white);
            ShopUIStyle.Skin(bar, placeSkin, sliced: true);
            ShopUIStyle.Anchor(bar.rectTransform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 16f), new Vector2(CardW - 28f, 32f));
            badgeParent = bar.transform;
        }

        var badge = ShopUIStyle.MakeText(badgeParent, "PlaceBadge", 14f, FontStyles.Bold,
            TextAlignmentOptions.Center, placeSkin != null ? Color.white : (canPlace ? OkColor : NoColor));
        if (placeSkin != null)
            ShopUIStyle.Stretch(badge.rectTransform);
        else
            ShopUIStyle.Anchor(badge.rectTransform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 14f), new Vector2(CardW - 24f, 24f));
        badge.text = canPlace ? "놓을 자리 있음" : "놓을 자리 없음";
        FitSingleLine(badge);
    }

    /// <summary>
    /// 한 줄 라벨의 넘침을 말줄임으로 가둔다.
    ///
    /// 카드 안의 값(룬 이름·등급/칸수/속성 태그·배지)은 전부 <b>길이가 가변</b>인데 박스는 1줄 높이로 고정돼 있다.
    /// 기본 설정에서는 긴 이름이 줄바꿈되며 두 번째 줄이 박스를 뚫고 아래 요소 위로 겹쳤다.
    /// </summary>
    private static void FitSingleLine(TMP_Text t)
    {
        if (t == null) return;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode     = TextOverflowModes.Ellipsis;
    }

    /// <summary>효과 목록 박스(높이 86 · 행 20 + 간격 2)에 들어가는 최대 행 수.</summary>
    private const int MaxEffectRows = 4;

    private void BuildEffectRows(RectTransform parent, RuntimeItemData data)
    {
        if (data.effects == null) return;

        var style = EffectRowStyle.Default;
        style.fontSize        = 14f;
        style.iconSize        = 16f;
        style.rowHeight       = 20f;
        style.usePrefixArrows = true;

        // 효과가 많은 룬은 행이 박스를 넘어 아래 배지·카드 밖까지 밀고 나갔다(레이아웃이 뭉개진 주범).
        // 박스에 들어가는 만큼만 그리고, 잘린 개수는 마지막 줄에 알린다.
        int shown = 0, hidden = 0;
        foreach (var slot in data.effects)
        {
            if (string.IsNullOrEmpty(slot.effectType)) continue;
            if (shown >= MaxEffectRows) { hidden++; continue; }
            EffectRowWidget.Create(parent, style, slot);
            shown++;
        }

        if (hidden > 0 && shown > 0)
        {
            // 마지막 행을 "+N개 더"로 대체 — 잘렸다는 사실이 화면에 보여야 한다.
            var last = parent.GetChild(parent.childCount - 1);
            if (last != null) Destroy(last.gameObject);

            var more = ShopUIStyle.MakeText(parent, "MoreEffects", 13f, FontStyles.Italic,
                TextAlignmentOptions.Center, ShopUIStyle.TextDim);
            more.text = $"+{hidden + 1}개 더";
            FitSingleLine(more);
            var le = more.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = style.rowHeight;
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

        // 속성까지 넘긴다 — 룬은 자기 속성 존(또는 중앙)에만 놓이므로,
        // 모양만 보고 판정하면 "자리 있음"으로 뜬 룬이 막상 판에서는 들어갈 곳이 없다.
        bool canPlace = MerlinRuneBridge.Instance == null
            || MerlinRuneBridge.Instance.CanPlaceShape(offsets, data.element);

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var o in offsets)
        {
            if (o.x < minX) minX = o.x;  if (o.x > maxX) maxX = o.x;
            if (o.y < minY) minY = o.y;  if (o.y > maxY) maxY = o.y;
        }
        int cols = maxX - minX + 1, rows = maxY - minY + 1;

        // 박스에 꽉 차도록 셀 크기를 역산 — 1칸 룬은 크게, 큰 모양은 줄여서 항상 형태가 읽히게 한다.
        float boxW = CardW - 40f - 16f;   // ShapeBox 폭 - 여백
        float boxH = ShapeBoxH   - 16f;
        float fitW = (boxW - (cols - 1) * MiniGap) / Mathf.Max(1, cols);
        float fitH = (boxH - (rows - 1) * MiniGap) / Mathf.Max(1, rows);
        float cellSize = Mathf.Clamp(Mathf.Min(fitW, fitH), MiniCellMin, MiniCellMax);

        float totalW = cols * (cellSize + MiniGap) - MiniGap;
        float totalH = rows * (cellSize + MiniGap) - MiniGap;
        float startX = -totalW * 0.5f + cellSize * 0.5f;
        float startY =  totalH * 0.5f - cellSize * 0.5f;

        // 스프라이트/틴트 규칙은 RuneArt.ResolveRuneCell 한곳에서 정한다(네 경로 동일 규칙).
        // 각인석이 아예 없을 때만 공용 룬 타일로 한 단계 더 떨어진다 —
        // 룬 타일은 단색 둥근 사각형이라 먼저 잡으면 모든 룬이 색 블록으로 보인다.
        RuneArt.ResolveRuneCell(data.element, data.rarity, new Color(0.7f, 0.7f, 0.75f),
            out Sprite art, out Color tint);
        if (art == null) art = UISkin.RuneSelect?.runeTile;
        Color dimTint = new Color(tint.r * 0.5f, tint.g * 0.5f, tint.b * 0.5f, 0.7f);

        foreach (var o in offsets)
        {
            int col = o.x - minX;
            int row = maxY - o.y;

            var cell = ShopUIStyle.MakeImage(root, $"C{o.x}_{o.y}",
                canPlace ? tint : dimTint);
            if (art != null) { cell.sprite = art; cell.preserveAspect = true; }
            ShopUIStyle.Anchor(cell.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(startX + col * (cellSize + MiniGap), startY - row * (cellSize + MiniGap)),
                Vector2.one * cellSize);
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
            // 스킨 시: 아트를 물들이지 않도록 선택=흰색·비선택=살짝 어둡게(밝기)로 피드백.
            if (_cards[i].Border != null)
                _cards[i].Border.color = _skinned ? (on ? Color.white : new Color(0.62f, 0.62f, 0.66f))
                                                  : (on ? SelectBorder : ShopUIStyle.CardBorder);
            if (_cards[i].Fill != null)
                _cards[i].Fill.color = _skinned ? Color.white
                                                : (on ? CardSelected : ShopUIStyle.CardFill);
        }

        bool hasSel = index >= 0;
        if (_confirmLabel != null)
            _confirmLabel.color = hasSel ? ShopUIStyle.TextPrimary : ShopUIStyle.TextDim;
        if (_confirmBtnImg != null)
            _confirmBtnImg.color = _skinned ? (hasSel ? Color.white : new Color(1f, 1f, 1f, 0.5f))
                                            : (hasSel ? ShopUIStyle.GoldPillBg : ShopUIStyle.BandFill);
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

        // 보관함 만차 시 조용히 사라지지 않도록, 실패해도 그리드가 '보류'로 들고 간다.
        bool added = _inventory != null && _inventory.AddToStaging(item);
        if (!added)
            ItemEffectVfxHelper.ShowNotice(
                $"<color=#FFCC44>보관함 가득 참</color> ({RunItemInventory.MaxStagingCapacity}칸) — 자리를 비우면 자동으로 추가됩니다");

        // 닫기(ClosePopupUI)를 resolve(TrySetResult)보다 먼저 — 순서가 뒤집히면 이벤트방에서 시간이 고착된다.
        // TrySetResult는 대기자(ClearRewardTrigger의 다중 라운드 3지선다)를 동기로 이어 곧바로 다음 라운드
        // 팝업을 push한다. 그 뒤에 ClosePopupUI를 부르면 이 팝업은 더 이상 스택 최상단이 아니라 닫기가
        // 무시되고("Close Popup Failed!"), 살아있는 좀비로 남아 BlocksGameplay가 계속 걸린 채 timeScale=0이
        // 영구 고착된다(이벤트방 Gold/Platinum 2라운드 이상 보상에서 재현). 그리드는 다음 라운드 팝업보다
        // 밑에 깔리도록 resolve 전에 연다.
        ClosePopupUI();

        if (UI_GridPanel.Instance == null)
            Managers.UI?.ShowOverlayUI<UI_GridPanel>();
        if (UI_GridPanel.Instance != null)
        {
            if (added) UI_GridPanel.Instance.ShowWithNewItem(item);
            else       UI_GridPanel.Instance.ShowWithPendingItem(item);
        }

        _interactionTcs?.TrySetResult();
    }

    private void OnSkipClicked()
    {
        Result  = null;
        Skipped = true;
        // 닫기를 resolve보다 먼저 — OnConfirmClicked와 동일 이유(다중 라운드 좀비 팝업 → timeScale 고착 방지).
        ClosePopupUI();
        _interactionTcs?.TrySetResult();
    }

    // ── Helpers ──

    private static void AddClick(GameObject go, Action onClick)
    {
        var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => onClick?.Invoke());
    }

    /// <summary>ElementDef.Order 내 인덱스(리본 아트 배열 접근용). 미지정/미발견은 -1 → 색 폴백.</summary>
    private static int ElementIndex(string elementId)
    {
        if (string.IsNullOrEmpty(elementId)) return -1;
        var order = ElementDef.Order;
        for (int i = 0; i < order.Count; i++)
            if (order[i] == elementId) return i;
        return -1;
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
        ItemRarity.Legendary => "◆ Legendary",
        _                    => "· Common",
    };
}
