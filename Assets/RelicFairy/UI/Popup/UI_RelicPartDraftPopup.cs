using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 유물 파츠 드래프트 팝업 (Canvas_Popup, Addressable "UI_RelicPartDraftPopup").
///
/// 보스 클리어 보상 = <b>파츠 후보 3개 중 1개 선택</b>(개화 = 런 내 임시 성장).
/// 룬 선택 팝업(<see cref="UI_RuneSelectPopup"/>)과 3지선다 골격을 공유하되,
/// 카드 내용이 다르다 — 모양/인벤토리 대신 <b>종류(코어/효과/행동/트리거) + 기능 설명</b>을 보여준다.
///
/// 파츠는 무료 성장이라 '넘기기'가 이득이 없다 → 택1 강제(넘기기 버튼 없음).
/// 후보가 비면 호출자가 팝업을 열지 않는다(이 팝업은 최소 1개 후보를 전제).
/// </summary>
public sealed class UI_RelicPartDraftPopup : UI_Popup
{
    public override bool BlocksGameplay => true;
    public override bool CloseOnEscape  => false;   // 보상 결정이라 실수로 닫히면 안 된다 — 선택으로만 종료

    // ── 레이아웃 ──
    private const float WindowW = 1280f;
    private const float WindowH = 760f;
    private const float CardW   = 380f;   // 카드 폭 상한 — 후보가 많으면 이 아래로 줄어든다
    private const float CardH   = 520f;
    private const float CardGap = 28f;

    // 카드 안 세로 순서(카드 위끝 기준). 아트가 오기 전에 자리를 먼저 굳혀 둔다 —
    // 스프라이트만 꽂으면 되도록, 칸 크기는 납품 명세(문서 §개화 리소스)와 같은 값이다.
    private const float SymbolBox  = 168f;   // 문양 칸 — 계열 문양 또는 파츠 전용 문양
    private const float SymbolTop  = -62f;
    private const float NameTop    = -244f;
    private const float DividerTop = -288f;
    private const float DescTop    = -302f;
    private const float DescH      = 130f;
    private const float FootTop    = -444f;   // 꼬리말 띠 위끝(카드 바닥에서 18px 띄운다)
    private const float FootH      = 58f;
    private const float SelectedCardScale = 1.04f;
    // 카드 세로 중심. 카드는 CardY ± CardH/2를 차지한다. 창 760 기준으로 「장착」 버튼은
    // -350~-294, 부제 아래끝은 +270이다. -14면 카드가 +246~-274가 되어 부제와 24px,
    // 버튼과 20px가 남는다 — 카드는 raycast를 받고 나중에 그려지므로 겹치면 버튼 클릭을 가져간다.
    private const float CardY   = -14f;
    private const float CardSideMargin = 40f;   // 카드 열 좌우 여백(창 안쪽)

    private static readonly Color CardSelected = new(0.20f, 0.17f, 0.10f, 1f);
    private static readonly Color SelectBorder = new(0.88f, 0.72f, 0.32f, 1f);

    // 종류별 강조색 — 코어(진화)만 금색으로 격상해 클라이맥스임을 알린다.
    private static readonly Color KindCore     = new(0.93f, 0.78f, 0.35f, 1f);
    private static readonly Color KindEffect   = new(0.72f, 0.55f, 0.92f, 1f);
    private static readonly Color KindBehavior = new(0.45f, 0.72f, 0.92f, 1f);
    private static readonly Color KindTrigger  = new(0.45f, 0.85f, 0.55f, 1f);

    // ── 상태 ──
    private readonly List<CardView> _cards = new();
    private readonly List<GameObject> _lockedRoots = new();   // 잠긴 자리(해금하면 채워질 칸)
    private int _lockedSlots;
    private List<RelicPartEntry> _candidates;
    private int _selected = -1;
    private bool _built;
    private bool _skinned;          // 아트 로드 성공 — 선택 피드백을 색 틴트 대신 밝기로 처리
    private float _cardW = CardW;   // 후보 수에 맞춰 산출된 실제 카드 폭
    private bool _themed;           // 아트 없음 — 공통 언어(둥근 창·베벨 버튼)를 입혔다

    [SerializeField] private Transform _windowRoot;
    [SerializeField] private TMP_Text  _subtitle;
    [SerializeField] private Image     _confirmBtnImg;
    [SerializeField] private TMP_Text  _confirmLabel;

    private UniTaskCompletionSource _interactionTcs;

    /// <summary>선택된 파츠. 미선택 종료(취소 경로)는 없다.</summary>
    public RelicPartEntry Result { get; private set; }

    private sealed class CardView
    {
        public Image         Border;
        public Image         Fill;
        public GameObject    Root;
        public RectTransform Rt;
    }

    // ── Lifecycle ──

    public override void Init()
    {
        base.Init();
        BuildChrome();
    }

    // ── Public API ──

    public UniTask WaitForInteractionAsync(System.Threading.CancellationToken ct)
    {
        _interactionTcs = new UniTaskCompletionSource();
        return _interactionTcs.Task.AttachExternalCancellation(ct);
    }

    /// <summary>후보를 주입해 카드를 구성한다. ShowPopupUIAndGetAsync 직후 호출.</summary>
    /// <param name="lockedSlots">
    /// 해금하면 <b>실제로 더 열릴 수 있는</b> 칸 수. 빈 자리로 미리 그려 무엇이 늘어나는지 보여준다.
    /// 풀이 모자라 해금해도 안 늘어나는 경우에는 0을 넘겨야 한다 — 없는 확장을 약속하면 안 된다.
    /// </param>
    public void Setup(List<RelicPartEntry> candidates, int lockedSlots = 0)
    {
        _candidates   = candidates;
        _lockedSlots  = Mathf.Max(0, lockedSlots);

        if (candidates == null || candidates.Count == 0)
        {
            Debug.LogWarning("[UI_RelicPartDraftPopup] 후보 없음 — 즉시 닫음");
            _interactionTcs?.TrySetResult();
            ClosePopupUI();
            return;
        }

        // 코어(진화) 드래프트면 부제를 바꿔 무게감을 준다.
        bool isCore = candidates[0].part_kind == RelicPartKind.Core;
        if (_subtitle != null)
            _subtitle.text = isCore
                ? "유물이 각성합니다 — 진화의 방향을 정하세요"
                : "이번 런의 유물을 키울 파츠를 하나 고르세요";

        BuildCards();
        SetSelected(-1);
    }

    // ── Build ──

    private void BuildChrome()
    {
        if (_built) return;
        _built = true;
        // 프리팹이 구워져 있으면 <b>짓지 않고 잇기만 한다</b> — 다시 지으면 UI가 두 벌 겹친다.
        if (transform.childCount > 0) { BindBakedHierarchy(); return; }


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
        // 화면 맞춤은 빌더가 붙인다 — 프리팹에만 붙이면 재굽기 때 사라진다.
        // 크기를 가진 것은 테두리(부모)라 거기에 붙인다.
        window.transform.parent.gameObject.AddComponent<UIWindowFitter>().Configure();

        // 타이틀 띠 — 아트 자리(900×72). 미납품이면 띠 색만 남고 글자는 그대로 읽힌다.
        var titleBar = ShopUIStyle.MakeImage(_windowRoot, "TitleBar", ShopUIStyle.BandFill);
        ShopUIStyle.Anchor(titleBar.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -14f), new Vector2(900f, 72f));

        // 제목 — 띠 위 가운데. 개화는 런 중 몇 번 없는 사건이라 화면이 먼저 그렇게 말해야 한다.
        var title = ShopUIStyle.MakeText(titleBar.transform, "Title", 30f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Stretch(title.rectTransform);
        title.text = "유물 개화";

        // 부제(Setup에서 코어/기능에 맞게 갱신)
        _subtitle = ShopUIStyle.MakeText(_windowRoot, "Subtitle", 17f, FontStyles.Normal,
            TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(_subtitle.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -96f), new Vector2(900f, 26f));
        _subtitle.text = "이번 런의 유물을 키울 파츠를 하나 고르세요";

        BuildFooter();
        ApplySkin();
    }

    private void BuildFooter()
    {
        // [선택] — 파츠는 무료 성장이라 넘기기가 없다(택1 강제).
        var confirm = ShopUIStyle.MakeFrame(_windowRoot, "ConfirmBtn",
            ShopUIStyle.BronzeLine, ShopUIStyle.BandFill, 2f, raycast: true);
        ShopUIStyle.Anchor((RectTransform)confirm.transform.parent,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 30f), new Vector2(220f, 56f));
        _confirmBtnImg = confirm;
        AddClick(confirm.transform.parent.gameObject, OnConfirmClicked);

        _confirmLabel = ShopUIStyle.MakeText(confirm.transform, "Label", 20f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Stretch(_confirmLabel.rectTransform);
        _confirmLabel.text = "장착";
    }

    private void BuildCards()
    {
        foreach (var c in _cards) if (c.Root != null) Destroy(c.Root);
        _cards.Clear();
        foreach (var g in _lockedRoots) if (g != null) Destroy(g);
        _lockedRoots.Clear();

        // 창은 화면에 맞춰 커지지만 아래 배치는 전부 목업 px다 — 배율 레이어가 그 차이를 흡수한다.
        // 이게 없으면 넓어진 판에 원래 크기 카드가 떠 있게 된다.
        var layer = UIProportional.EnsureScaledLayer(_windowRoot, "CardLayer", WindowW, WindowH) ?? (RectTransform)_windowRoot;

        int n = _candidates.Count;
        // 잠긴 자리도 줄의 일부다 — 폭 산출과 중앙 정렬에 함께 넣어야 줄이 흐트러지지 않고,
        // 해금 뒤에 카드가 "그 자리로" 들어오는 것으로 보인다.
        int slots = n + _lockedSlots;

        // 카드 폭은 칸 수에 맞춰 줄인다. 300 고정이면 4지선다에서 카드가 창 밖으로 밀려난다.
        float avail = WindowW - CardSideMargin * 2f - (slots - 1) * CardGap;
        _cardW = Mathf.Min(CardW, avail / Mathf.Max(1, slots));

        float totalW = slots * _cardW + (slots - 1) * CardGap;
        float startX = -totalW * 0.5f + _cardW * 0.5f;

        for (int i = 0; i < n; i++)
        {
            int idx = i;   // 클로저 캡처
            var entry = _candidates[i];

            var card = ShopUIStyle.MakeFrame(layer, $"Card{i}",
                ShopUIStyle.CardBorder, ShopUIStyle.CardFill, 2f, raycast: true);
            var cardRT = (RectTransform)card.transform.parent;   // 위치/크기는 테두리(outer)에
            ShopUIStyle.Anchor(cardRT,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(startX + i * (_cardW + CardGap), CardY), new Vector2(_cardW, CardH));

            AddClick(cardRT.gameObject, () => SetSelected(idx));

            var view = new CardView
            {
                Root   = cardRT.gameObject,
                Border = cardRT.GetComponent<Image>(),
                Fill   = card,
                Rt     = cardRT,
            };
            // 카드 액자·채움 — 아트가 오면 여기로 들어온다(미납품이면 색 박스 유지).
            var cardSkin = UISkin.RelicPartDraft;
            ShopUIStyle.Skin(view.Border, cardSkin?.cardFrame, sliced: true);
            ShopUIStyle.Skin(view.Fill,   cardSkin?.cardFill,  sliced: true);
            if (!_skinned) UITheme.RoundFrame(view.Fill, 14f);
            _cards.Add(view);

            BuildCardContent(card.transform, entry);
        }

        for (int i = 0; i < _lockedSlots; i++)
        {
            int slot = n + i;
            _lockedRoots.Add(UILockedSlot.Build(layer, $"Locked{i}",
                new Vector2(startX + slot * (_cardW + CardGap), CardY),
                new Vector2(_cardW, CardH)));
        }
    }

    private void BuildCardContent(Transform card, RelicPartEntry entry)
    {
        Color kindColor = KindColorOf(entry.part_kind);
        int   kindIdx   = KindIndexOf(entry.part_kind);
        var   skin      = UISkin.RelicPartDraft;

        // 종류 리본(상단) — 끝이 흐려지는 빛줄기(네모 색 띠는 웹 카드처럼 읽혔다, 09-28 UI 톤 통일)
        var ribbon = ShopUIStyle.MakeImage(card, "Ribbon", kindColor);
        ShopUIStyle.Anchor(ribbon.rectTransform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -3f), new Vector2(-28f, 6f));
        ribbon.sprite = UITheme.SoftBand;

        // 종류 배지(칩)
        var kindBox = ShopUIStyle.MakeFrame(card, "KindChip", kindColor, ShopUIStyle.IconBg, 2f);
        ShopUIStyle.Anchor((RectTransform)kindBox.transform.parent,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -20f), new Vector2(150f, 36f));
        ShopUIStyle.Skin(kindBox, skin?.KindChip(kindIdx), sliced: true);
        var kindLabel = ShopUIStyle.MakeText(kindBox.transform, "KindLabel", 17f, FontStyles.Bold,
            TextAlignmentOptions.Center, kindColor);
        ShopUIStyle.Stretch(kindLabel.rectTransform);
        kindLabel.text = KindLabelOf(entry.part_kind);

        // 문양 자리 — 카드의 절반을 그림이 가져간다(아트 납품 전에 자리부터 굳힌다).
        // 아트가 없으면 계열색 원반 + 기호가 대신 선다. 빈 사각형으로 두면 "미완성"으로 읽히고,
        // 나중에 그림을 넣을 때 레이아웃을 다시 짜야 한다.
        var disc = ShopUIStyle.MakeImage(card, "SymbolDisc",
            new Color(kindColor.r * 0.22f, kindColor.g * 0.22f, kindColor.b * 0.22f, 1f));
        ShopUIStyle.Anchor(disc.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, SymbolTop), new Vector2(SymbolBox, SymbolBox));
        disc.sprite = UIProceduralSprites.RoundedRect(radius: 52f, feather: 26f, size: 168);

        var mark = ShopUIStyle.MakeText(disc.transform, "SymbolMark", 96f, FontStyles.Bold,
            TextAlignmentOptions.Center, kindColor);
        ShopUIStyle.Stretch(mark.rectTransform);
        mark.text = KindMarkOf(entry.part_kind);

        // 실제 문양 — 파츠 전용이 있으면 그것, 없으면 계열 문양. 둘 다 없으면 원반만 남는다.
        var art = skin?.PartSymbol(entry.part_id) ?? skin?.KindEmblem(kindIdx);
        if (art != null)
        {
            var symbol = ShopUIStyle.MakeImage(disc.transform, "Symbol", Color.white);
            ShopUIStyle.Stretch(symbol.rectTransform);
            symbol.sprite         = art;
            symbol.preserveAspect = true;
            mark.gameObject.SetActive(false);   // 아트가 오면 임시 기호는 물러난다
        }

        // 이름
        var name = ShopUIStyle.MakeText(card, "Name", 25f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(name.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, NameTop), new Vector2(_cardW - 32f, 36f));
        name.text = entry.part_name ?? entry.part_id;

        // 구분선
        var divider = ShopUIStyle.MakeImage(card, "Divider", ShopUIStyle.BronzeLine);
        ShopUIStyle.Anchor(divider.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, DividerTop), new Vector2(_cardW - 72f, 2f));

        // 설명(기능) — 카드의 핵심. 여러 줄 허용.
        var desc = ShopUIStyle.MakeText(card, "Desc", 17f, FontStyles.Normal,
            TextAlignmentOptions.Top, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(desc.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, DescTop), new Vector2(_cardW - 44f, DescH));
        desc.enableWordWrapping = true;
        desc.text = entry.description ?? string.Empty;

        // 꼬리말 — 선행 파츠가 있으면 그것을, 없으면 이 계열이 무엇을 바꾸는지 한 줄.
        // 카드 아래 3분의 1이 통째로 비어 있던 자리다(09-21 검수).
        var footBand = ShopUIStyle.MakeImage(card, "FootBand",
            new Color(kindColor.r * 0.16f, kindColor.g * 0.16f, kindColor.b * 0.16f, 1f));
        ShopUIStyle.Anchor(footBand.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, FootTop), new Vector2(_cardW - 24f, FootH));
        footBand.sprite = UIProceduralSprites.RoundedRect(radius: 12f, feather: 6f, size: 64);

        var foot = ShopUIStyle.MakeText(footBand.transform, "Foot", 16f, FontStyles.Normal,
            TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(foot.rectTransform,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(-24f, -10f));
        foot.enableWordWrapping = true;
        foot.text = string.IsNullOrEmpty(entry.requires)
            ? KindHintOf(entry.part_kind)
            : $"선행 · {Managers.RelicParts?.GetById(entry.requires)?.part_name ?? entry.requires}";   // id가 아니라 이름(09-29)
    }

    // ── 선택 상태 ──

    private void SetSelected(int index)
    {
        // 초기화(-1)에도 소리를 내면 팝업이 열리는 순간 아무도 안 눌렀는데 클릭음이 울린다.
        if (index >= 0) Managers.Sound.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();
        _selected = index;

        for (int i = 0; i < _cards.Count; i++)
        {
            bool on = (i == index);
            if (_cards[i].Border != null)
                _cards[i].Border.color = _skinned ? (on ? Color.white : new Color(0.62f, 0.62f, 0.66f))
                                                  : (on ? SelectBorder : ShopUIStyle.CardBorder);
            if (_cards[i].Fill != null)
                _cards[i].Fill.color = _skinned ? Color.white
                                                : (on ? CardSelected : ShopUIStyle.CardFill);
            // 고른 카드는 한 치수 커진다 — 재련소 탭·HUD 무기 칸과 같은 「선택 = 금테 + 확대」 규약.
            if (_cards[i].Rt != null)
                _cards[i].Rt.localScale = Vector3.one * (on ? SelectedCardScale : 1f);
        }

        bool hasSel = index >= 0;
        if (_confirmLabel != null)
        {
            _confirmLabel.color = hasSel ? ShopUIStyle.TextPrimary : ShopUIStyle.TextDim;
            if (hasSel) _confirmLabel.text = "장착";   // 미선택 안내를 띄웠다면 되돌린다
        }
        if (_confirmBtnImg != null)
            _confirmBtnImg.color = _themed ? (hasSel ? UITheme.CtaTint : UITheme.CtaTintOff)
                                           : (hasSel ? ShopUIStyle.GoldPillBg : ShopUIStyle.BandFill);
        UIAffordGlow.Set(_confirmBtnImg, hasSel);   // 고르면 장착 버튼에 은은한 불(09-29)
    }

    /// <summary>
    /// 아트가 없을 때의 공통 언어 — 둥근 창 + 금 가는 선, 둥근 카드, 공통 베벨 [장착]. 네모 판 · 윗단 색 띠 · 평판 버튼이
    /// 웹 관리 화면처럼 읽혔다(09-28 UI 톤 진단 D4). 아트(RelicPartDraftSkin)가 오면 그쪽이 우선이다.
    /// </summary>
    private void ApplyTheme()
    {
        if (_skinned) return;
        var window = ShopUIStyle.FindDeep(transform, "Window");
        var fill   = window != null ? window.Find("Fill") : null;
        if (fill != null && fill.TryGetComponent<Image>(out var fillImg))
        {
            UITheme.RoundFrame(fillImg, 18f);
            if (window.TryGetComponent<Image>(out var edge)) edge.color = UITheme.GoldLine;
        }
        _themed = UITheme.StyleFrameButton(_confirmBtnImg, UITheme.CtaTintOff);
        if (_confirmLabel != null) TMPOutlineHelper.ApplySoftShadow(_confirmLabel);
    }

    // ── 버튼 ──

    private void OnConfirmClicked()
    {
        if (_selected < 0 || _candidates == null || _selected >= _candidates.Count)
        {
            // 미선택. 조용히 return하면 버튼이 죽은 것으로 읽힌다 —
            // 무엇이 빠졌는지 버튼이 직접 말하게 한다(룬 선택 팝업과 같은 규약).
            ShopUIStyle.PlaySfx("shop_reject");
            if (_confirmLabel != null) _confirmLabel.text = "카드를 고르세요";
            return;
        }

        Managers.Sound.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();
        Result = _candidates[_selected];
        _interactionTcs?.TrySetResult();
        ClosePopupUI();
    }

    // ── Helpers ──

    /// <summary>
    /// 구워진 프리팹을 잇는다 — <b>계층·좌표·아트는 프리팹이 갖고, 코드는 배선만 한다.</b>
    /// 직렬화되지 않는 것(코드가 붙인 클릭 리스너·런타임 목록)만 되살린다.
    /// 이름으로 찾는다 — 빌더가 붙이던 이름 그대로 프리팹에 굳어 있다.
    /// </summary>
    private void BindBakedHierarchy()
    {
        var t = ShopUIStyle.FindDeep(transform, "ConfirmBtn");
        if (t != null) AddClick(t.gameObject, OnConfirmClicked);
        else Debug.LogWarning("[UI_RelicPartDraftPopup] 배선 실패 — 「ConfirmBtn」을 못 찾았다. 장착 버튼이 죽는다.");

        ApplySkin();
    }

    /// <summary>
    /// 창·타이틀 띠·버튼에 아트를 입힌다. 스프라이트가 없으면 아무것도 하지 않는다 —
    /// <b>아트 0장이어도 화면은 지금 모습 그대로다</b>(룬 선택 팝업과 같은 관례).
    /// 코드로 짓는 경로와 구운 프리팹 경로 양쪽에서 부른다.
    /// </summary>
    private void ApplySkin()
    {
        var skin = UISkin.RelicPartDraft;
        _skinned = skin != null && skin.cardFrame != null;

        var window = ShopUIStyle.FindDeep(transform, "Window");
        if (window != null) ShopUIStyle.Skin(window.GetComponent<Image>(), skin?.background);

        var titleBar = ShopUIStyle.FindDeep(transform, "TitleBar");
        if (titleBar != null) ShopUIStyle.Skin(titleBar.GetComponent<Image>(), skin?.titleBar, sliced: true);

        if (_confirmBtnImg != null) ShopUIStyle.Skin(_confirmBtnImg, skin?.confirmButton, sliced: true);
        ApplyTheme();
    }

    private static void AddClick(GameObject go, Action onClick)
    {
        var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => onClick?.Invoke());
    }

    private static Color KindColorOf(string kind) => kind switch
    {
        RelicPartKind.Core     => KindCore,
        RelicPartKind.Effect   => KindEffect,
        RelicPartKind.Behavior => KindBehavior,
        RelicPartKind.Trigger  => KindTrigger,
        _                      => ShopUIStyle.TextDim,
    };

    /// <summary>스킨 배열 순서 — core·effect·behavior·trigger. CSV part_kind와 1:1.</summary>
    private static int KindIndexOf(string kind) => kind switch
    {
        RelicPartKind.Core     => 0,
        RelicPartKind.Effect   => 1,
        RelicPartKind.Behavior => 2,
        RelicPartKind.Trigger  => 3,
        _                      => 1,
    };

    /// <summary>문양 아트가 오기 전까지 쓰는 임시 기호. 폰트 아틀라스에 이미 있는 글자만 쓴다.</summary>
    private static string KindMarkOf(string kind) => kind switch
    {
        RelicPartKind.Core     => "★",
        RelicPartKind.Effect   => "◆",
        RelicPartKind.Behavior => "◇",
        RelicPartKind.Trigger  => "□",
        _                      => "◆",
    };

    /// <summary>이 계열이 무엇을 바꾸는지 한 줄. 선행 파츠가 없는 카드의 꼬리말로 쓴다.</summary>
    private static string KindHintOf(string kind) => kind switch
    {
        RelicPartKind.Core     => "유물의 작동 원리를 다시 짠다",
        RelicPartKind.Effect   => "새 상태이상·부가 판정이 붙는다",
        RelicPartKind.Behavior => "Q·패시브가 작동하는 방식이 바뀐다",
        RelicPartKind.Trigger  => "새로운 발동 조건이 생긴다",
        _                      => string.Empty,
    };

    private static string KindLabelOf(string kind) => kind switch
    {
        RelicPartKind.Core     => "◆ 진화",
        RelicPartKind.Effect   => "효과",
        RelicPartKind.Behavior => "행동",
        RelicPartKind.Trigger  => "트리거",
        _                      => "파츠",
    };
}
