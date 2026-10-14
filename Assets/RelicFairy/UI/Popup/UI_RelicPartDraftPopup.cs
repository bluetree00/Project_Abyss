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
    // 10-11 v2 — 380×520 · 간격 28 → 340×480 · 32(리서치 B §6: 3장 폭 화면의 81% → 73%)
    private const float CardW   = 340f;   // 카드 폭 상한 — 후보가 많으면 이 아래로 줄어든다
    private const float CardH   = 480f;
    private const float CardGap = 32f;

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
    private const float SelectedCardScale = 1.03f;
    private const float OthersAlpha       = 0.55f;   // 고르면 나머지 카드 — 선택 표시는 테 하나 + 확대 + 이것뿐(10-11 v2)
    // 고른 뒤 연출(10-06) — 번쩍 0.28초 · 날아감 0.45초
    private const float ChosenPulseSec   = 0.28f;
    private const float ChosenPulseScale = 0.08f;
    private const float ChosenFlySec     = 0.45f;
    private const float ChosenEndScale   = 0.08f;
    private const float ChosenArc        = 60f;
    private const float OthersSinkY      = -70f;
    // 카드 세로 중심. 카드는 CardY ± CardH/2를 차지한다. 창 760 기준으로 「장착」 버튼은
    // -350~-294, 부제 아래끝은 +270이다. -14면 카드(480)가 +226~-254가 되어 부제와 44px,
    // 버튼과 40px가 남는다 — 카드는 raycast를 받고 나중에 그려지므로 겹치면 버튼 클릭을 가져간다.
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
    private RelicDraft _draft;                 // 유물 성장 v2 — 카드 3장(새 조각 · 선명하게 · 메아리 · 잔향)
    private GameObject _redrawBtn;
    private TMP_Text _titleText;
    private int _selected = -1;
    private bool _built;
    private bool _skinned;          // 아트 로드 성공 — 선택 피드백을 색 틴트 대신 밝기로 처리
    private float _cardW = CardW;   // 후보 수에 맞춰 산출된 실제 카드 폭
    private bool _themed;           // 아트 없음 — 공통 언어(둥근 창·베벨 버튼)를 입혔다
    private bool _closing;          // 고른 뒤 연출 중 — 두 번 눌러도 한 번만
    private System.Threading.CancellationTokenSource _selCts;   // 고르기 연출(확대 · 나머지 흐려짐)

    [SerializeField] private Transform _windowRoot;
    [SerializeField] private TMP_Text  _subtitle;
    [SerializeField] private Image     _confirmBtnImg;
    [SerializeField] private TMP_Text  _confirmLabel;

    private UniTaskCompletionSource _interactionTcs;

    /// <summary>선택된 파츠. 미선택 종료(취소 경로)는 없다.</summary>
    public RelicPartEntry Result { get; private set; }

    /// <summary>[v2] 고른 카드.</summary>
    public RelicDraftCard ResultCard { get; private set; }

    /// <summary>[v2] 「다시 떠올리기」를 눌렀다 — 부르는 쪽이 새로 굴려 창을 다시 연다.</summary>
    public bool RedrawRequested { get; private set; }

    private sealed class CardView
    {
        public Image         Border;
        public Image         Fill;
        public GameObject    Root;
        public RectTransform Rt;
        public CanvasGroup   Group;     // 고른 뒤 — 안 고른 카드가 가라앉으며 흐려진다
        public Vector2       BasePos;
        public Color         Tone = ShopUIStyle.CardBorder;   // 기억 카드 = 등급색(테)
        public UIThinFrame   Frame;     // 기억 카드(10-11 v2) — 1px 테 · 고르면 금 85% + 모서리 눈금
    }

    // ── Lifecycle ──

    public override void Init()
    {
        base.Init();
        BuildChrome();
    }

    protected override void OnDestroy()
    {
        CancelSelectionMotion();
        base.OnDestroy();
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

    /// <summary>
    /// [유물 성장 v2] 굴려 둔 카드 3장으로 「되살아난 기억」 창을 꾸민다. ShowPopupUIAndGetAsync 직후 호출.
    /// <paramref name="canRedraw"/> = 제단 「다시 떠올리기」가 열려 있고 이번 런에 아직 안 썼다.
    /// </summary>
    public void Setup(RelicDraft draft, bool canRedraw, string relicName)
    {
        _draft        = draft;
        _candidates   = null;
        _lockedSlots  = 0;
        ResultCard    = null;
        RedrawRequested = false;

        if (draft == null || draft.Cards.Count == 0)
        {
            Debug.LogWarning("[UI_RelicPartDraftPopup] 카드 없음 — 즉시 닫음");
            _interactionTcs?.TrySetResult();
            ClosePopupUI();
            return;
        }

        // 창 이름 · 부제 — 문구 정본 d6 시나리오 §2-4(「기억을 되찾아간다」)
        if (_titleText == null) _titleText = ShopUIStyle.FindDeep(transform, "Title")?.GetComponent<TMP_Text>();
        if (_titleText != null) _titleText.text = "되살아난 기억";
        if (_subtitle != null)
            // 할 일을 먼저 말한다 — 「무엇을 먼저 떠올릴까 — 하나를 붙잡는다」는 시였지 안내가 아니었다(10-14 사용자 「어떻게 해야 하는지 모르겠다」)
            _subtitle.text = $"카드 한 장을 누르고 [가져간다] — 이번 런 동안 {(string.IsNullOrEmpty(relicName) ? "유물" : relicName)}에 새겨진다";

        BuildCards();
        EnsureRedrawButton(canRedraw);
        ApplyThinChrome();
        SetSelected(-1);
        if (_confirmLabel != null) _confirmLabel.text = "카드를 먼저 고른다";   // 고르기 전 — 버튼이 할 일을 말한다(구운 프리팹의 「장착」이 남지 않게)
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
        CancelSelectionMotion();   // 지난 카드를 붙잡고 도는 고르기 연출이 남지 않게
        foreach (var c in _cards) if (c.Root != null) Destroy(c.Root);
        _cards.Clear();
        foreach (var g in _lockedRoots) if (g != null) Destroy(g);
        _lockedRoots.Clear();

        // 창은 화면에 맞춰 커지지만 아래 배치는 전부 목업 px다 — 배율 레이어가 그 차이를 흡수한다.
        // 이게 없으면 넓어진 판에 원래 크기 카드가 떠 있게 된다.
        var layer = UIProportional.EnsureScaledLayer(_windowRoot, "CardLayer", WindowW, WindowH) ?? (RectTransform)_windowRoot;

        int n = _draft != null ? _draft.Cards.Count : _candidates.Count;
        // 잠긴 자리도 줄의 일부다 — 폭 산출과 중앙 정렬에 함께 넣어야 줄이 흐트러지지 않고,
        // 해금 뒤에 카드가 "그 자리로" 들어오는 것으로 보인다.
        int slots = n + _lockedSlots;

        // 카드 폭은 칸 수에 맞춰 줄인다. 300 고정이면 4지선다에서 카드가 창 밖으로 밀려난다.
        float avail = WindowW - CardSideMargin * 2f - (slots - 1) * CardGap;
        _cardW = Mathf.Min(CardW, avail / Mathf.Max(1, slots));

        float totalW = slots * _cardW + (slots - 1) * CardGap;
        float startX = -totalW * 0.5f + _cardW * 0.5f;
        bool thin = _draft != null && !_skinned;   // 기억 카드 코드 폴백 = 톤 v2 글래스 판(아트 스킨이 오면 그쪽)

        for (int i = 0; i < n; i++)
        {
            int idx = i;   // 클로저 캡처
            var entry = _draft != null ? _draft.Cards[i].Entry : _candidates[i];

            var card = ShopUIStyle.MakeFrame(layer, $"Card{i}",
                ShopUIStyle.CardBorder, ShopUIStyle.CardFill, 2f, raycast: true);
            var cardRT = (RectTransform)card.transform.parent;   // 위치/크기는 테두리(outer)에
            ShopUIStyle.Anchor(cardRT,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(startX + i * (_cardW + CardGap), CardY), new Vector2(_cardW, CardH));

            AddClick(cardRT.gameObject, () => SetSelected(idx));

            var view = new CardView
            {
                Root    = cardRT.gameObject,
                Border  = cardRT.GetComponent<Image>(),
                Fill    = card,
                Rt      = cardRT,
                Group   = cardRT.gameObject.AddComponent<CanvasGroup>(),
                BasePos = cardRT.anchoredPosition,
            };
            if (_draft != null) view.Tone = GradeColor(_draft.Cards[i].Grade);
            // 카드 액자·채움 — 아트가 오면 여기로 들어온다(미납품이면 색 박스 유지).
            var cardSkin = UISkin.RelicPartDraft;
            ShopUIStyle.Skin(view.Border, cardSkin?.cardFrame, sliced: true);
            ShopUIStyle.Skin(view.Fill,   cardSkin?.cardFill,  sliced: true);
            if (thin) StyleThinCard(view);
            else if (!_skinned) UITheme.RoundFrame(view.Fill, 14f);
            _cards.Add(view);

            if (_draft != null)
            {
                BuildMemoryCardContent(card.transform, _draft.Cards[i]);
                BuildGradeHalo(view, _draft.Cards[i].Grade);
                if (_draft.Cards[i].Kind == RelicDraftCardKind.Evolution) BuildCompletionFrame(view);   // 「완성」 겹선(10-14 C · 83 UI 1)
            }
            else BuildCardContent(card.transform, entry);
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
            // 계열 문양(흰 아이콘 — 10-13 Artsystack)은 잉크 · 진화만 금. 파츠 전용 그림은 색 그대로.
            bool master = skin?.PartSymbol(entry.part_id) == null;
            var symbol = ShopUIStyle.MakeImage(disc.transform, "Symbol",
                !master ? Color.white : kindIdx == 0 ? UITheme.Thin.Gold : UITheme.Thin.Ink);
            ShopUIStyle.Stretch(symbol.rectTransform, master ? 34f : 0f);
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

    // ── v2 카드(유물 기억) ──

    private static readonly Color GradeFaint   = new(0.62f, 0.64f, 0.72f, 1f);   // 흐릿 — 옅은 회청
    private static readonly Color GradeClear   = new(0.55f, 0.80f, 0.95f, 1f);   // 선명 — 맑은 하늘
    private static readonly Color GradeRadiant = new(0.98f, 0.82f, 0.38f, 1f);   // 찬란 — 금
    private const string LockedLineColor = "#9A98A4";   // 잠긴 줄 — #6E6E78(2.8~3.5:1) → 5.0~6.2:1(10-11 v2 · 4.5:1 이상)
    private const string NewLineColor    = "#F2D27A";

    private static Color GradeColor(RelicMemoryGrade g) => g switch
    {
        RelicMemoryGrade.Radiant => GradeRadiant,
        RelicMemoryGrade.Clear   => GradeClear,
        _                        => GradeFaint,
    };

    /// <summary>매달리는 자리 기호(카드 문양 자리 임시 — 전용 아트는 계획 4).</summary>
    private static string AnchorMark(RelicDraftCard c)
    {
        if (c.Kind == RelicDraftCardKind.Residue) return "잔";
        if (c.Kind == RelicDraftCardKind.Evolution) return "진";
        string a = c.Entry != null ? c.Entry.anchor : c.Anchor;
        if (c.Entry != null && c.Entry.IsReaction)
            return c.Entry.rune_element switch
            {
                "fire" => "화", "ice" => "빙", "electric" => "뇌", "grass" => "독", "light" => "광", "dark" => "암", _ => "응",
            };
        return a switch
        {
            RelicPartAnchor.Dawn => "새", RelicPartAnchor.Noon => "낮", RelicPartAnchor.Dusk => "놀",
            RelicPartAnchor.DawnNoon or RelicPartAnchor.NoonDusk => "궤",
            RelicPartAnchor.R10 => "10", RelicPartAnchor.R20 => "20", RelicPartAnchor.R30 => "30", RelicPartAnchor.R40 => "40",
            RelicPartAnchor.Frenzy => "광", RelicPartAnchor.Judgment => "심",
            _ => "·",
        };
    }

    /// <summary>자리 · 반응 문양 키(<see cref="RelicPartDraftSkinSO.AnchorArt"/>). 숫자 단계(r10~r40)는 null — 글자를 쓴다.</summary>
    private static string AnchorArtKey(RelicDraftCard c)
    {
        if (c.Kind == RelicDraftCardKind.Residue) return "residue";
        if (c.Kind == RelicDraftCardKind.Evolution) return null;   // 진화 = 유물 문장(EvolutionEmblem) — 문장이 없을 때만 글자 「진」
        if (c.Entry != null && c.Entry.IsReaction) return c.Entry.rune_element;
        string a = c.Entry != null ? c.Entry.anchor : c.Anchor;
        return a switch
        {
            RelicPartAnchor.Dawn or RelicPartAnchor.Noon or RelicPartAnchor.Dusk or RelicPartAnchor.DawnNoon or RelicPartAnchor.NoonDusk
                or RelicPartAnchor.Frenzy or RelicPartAnchor.Judgment => a,
            _ => null,
        };
    }

    private static string RuneElementLabel(string el) => el switch
    {
        "fire" => "불", "ice" => "물", "electric" => "전기", "grass" => "풀", "light" => "빛", "dark" => "어둠", _ => el,
    };

    /// <summary>
    /// 기억 카드(10-11 v2 — 사용자 「퀄리티 · 가독성」, 리서치 B §6 「고칠 값」): 카드 하나 = 어두운 글래스 판 하나.
    /// 테를 두른 상자는 카드 테 + 효과 바탕 둘뿐이고, 나머지는 여백과 1px 구분선으로 나눈다. 글자는 전부 Noto(가짜 굵게 · 기울임 없음).
    /// 위→아래: 등급 줄(등급 라벨 = 등급색이 쓰이는 한 곳 | 카드 종류) / 문양(3px 고리 · 자리 기호 + 유물 배지) + 꼬리표(8px 색 점 + 글자) /
    /// 이름 / <b>핵심 한 줄(①)</b> — 테 없는 효과 바탕 위, 숫자만 금 / ②③(등급이 연 줄만 밝게) / 구분선 / 꼬리말(흐린 글자) / 기억 한 줄.
    /// </summary>
    private void BuildMemoryCardContent(Transform card, RelicDraftCard c)
    {
        var entry  = c.Entry;
        Color tone = GradeColor(c.Grade);
        var top    = new Vector2(0.5f, 1f);
        var tl     = new Vector2(0f, 1f);
        var bottom = new Vector2(0.5f, 0f);

        // 등급 줄 — 띠는 등급색 12%로 아주 옅게, 등급색은 라벨 글자 한 곳에만(색 하나에 뜻 하나)
        var band = ShopUIStyle.MakeImage(card, "GradeBand", new Color(tone.r * GradeBandTint, tone.g * GradeBandTint, tone.b * GradeBandTint, 0.92f));
        ShopUIStyle.Anchor(band.rectTransform, top, top, top, new Vector2(0f, -10f), new Vector2(_cardW - 24f, 30f));
        var gradeText = ThinText(band.transform, "Grade", UIWeight.Medium, 15f, TextAlignmentOptions.MidlineLeft, tone);
        ShopUIStyle.Stretch(gradeText.rectTransform);
        gradeText.rectTransform.offsetMin = new Vector2(12f, 0f);
        gradeText.characterSpacing = LabelSpacing;
        gradeText.text = GradeTitle(c);
        var kindText = ThinText(band.transform, "Kind", UIWeight.Regular, 15f, TextAlignmentOptions.MidlineRight, UITheme.Thin.InkA(0.6f));
        ShopUIStyle.Stretch(kindText.rectTransform);
        kindText.rectTransform.offsetMax = new Vector2(-12f, 0f);
        kindText.text = KindTitle(c);

        // 문양 — 장식이 모이는 한 곳. 3px 원 테(자리색 채도 70%) + 어두운 속(자리색 10%) + 자리 기호 + 어느 유물의 기억인지 작은 배지
        Color anchorCol = AnchorColor(c);
        Color ringCol   = Desaturate(anchorCol, RingSaturation);
        var ring = ShopUIStyle.MakeImage(card, "SymbolRing", new Color(anchorCol.r * DiscTint, anchorCol.g * DiscTint, anchorCol.b * DiscTint, 1f));
        ring.sprite = UIThinShapes.Disc(EmblemSize);
        ShopUIStyle.Anchor(ring.rectTransform, tl, tl, tl, new Vector2(20f, -50f), new Vector2(EmblemSize, EmblemSize));
        var edge = ShopUIStyle.MakeImage(ring.transform, "Edge", ringCol);
        edge.sprite = UIThinShapes.Ring(EmblemSize, RingStroke);
        ShopUIStyle.Stretch(edge.rectTransform);
        var draftSkin = UISkin.RelicPartDraft;
        var anchorArt = draftSkin?.AnchorArt(AnchorArtKey(c));
        var relic     = AppBootstrapper.Instance?.Loadout?.Relic;
        bool evolution = c.Kind == RelicDraftCardKind.Evolution;
        var evoCrest  = evolution ? EvolutionEmblem(relic, draftSkin) : null;
        if (evoCrest != null)
        {
            // 진화 문양(10-14 C · 83 UI 1) — 그 유물의 문장(흰 선화)을 진화 계열색으로. 바깥에 금 1px 고리를 한 겹 더 닫는다(「완성」).
            var crestImg = ShopUIStyle.MakeImage(ring.transform, "SymbolCrest", ringCol);
            crestImg.sprite = evoCrest;
            crestImg.preserveAspect = true;
            crestImg.raycastTarget  = false;
            ShopUIStyle.Stretch(crestImg.rectTransform, EmblemSize * 0.2f);
            BuildCompletionRing(card, new Vector2(20f, -50f));
        }
        else if (anchorArt != null)
        {
            // 자리 · 반응 문양(10-13 Artsystack 흰 아이콘) — 글자 기호 대신. 자리색 그대로라 고리와 한 뜻으로 읽힌다.
            var glyph = ShopUIStyle.MakeImage(ring.transform, "SymbolGlyph", ringCol);
            glyph.sprite = anchorArt;
            glyph.preserveAspect = true;
            glyph.raycastTarget  = false;
            ShopUIStyle.Stretch(glyph.rectTransform, EmblemSize * 0.24f);
        }
        else
        {
            var mark = ThinText(ring.transform, "SymbolMark", UIWeight.Medium, 32f, TextAlignmentOptions.Center, ringCol);
            ShopUIStyle.Stretch(mark.rectTransform, RingStroke + 4f);
            mark.text = AnchorMark(c);
        }
        var badgeArt  = relic != null ? draftSkin?.RelicBadge(relic.Id.ToString()) : null;
        var relicIcon = badgeArt != null ? badgeArt : relic?.QSkillIcon;
        if (relicIcon != null && evoCrest == null)   // 진화 카드는 문양 자체가 유물 문장 — 작은 배지를 겹치지 않는다
        {
            // 흰 문장(태양 · 검)은 유물 테마색으로, 옛 Q 아이콘은 그대로
            var badge = ShopUIStyle.MakeImage(ring.transform, "RelicBadge", badgeArt != null ? relic.ThemeColor(UITheme.Thin.Gold) : Color.white);
            badge.sprite = relicIcon;
            badge.preserveAspect = true;
            var br = new Vector2(1f, 0f);
            ShopUIStyle.Anchor(badge.rectTransform, br, br, new Vector2(0.75f, 0.25f), Vector2.zero, Vector2.one * 30f);
        }

        // 꼬리표 — 자리 · 계열(또는 반응 속성). 테 두른 칩 대신 8px 색 점 + 글자(색은 점에만)
        float tagX = 20f + EmblemSize + 16f, tagW = _cardW - tagX - 16f;
        var evoDef = c.Kind == RelicDraftCardKind.Evolution ? RelicEvolution.ById(c.EvolutionId) : null;
        string anchorLabel = c.Kind == RelicDraftCardKind.Residue ? "정수"
                           : evoDef != null ? $"{BuildFamilyRules.Label(evoDef.Family)} 진화"   // 등급 줄이 이미 「유물 진화」 — 꼬리표는 방향을
                           : RelicPartAnchor.Label(entry?.anchor ?? c.Anchor);
        MemoryTag(card, "AnchorTag", anchorLabel, ringCol, new Vector2(tagX, -66f), tagW);
        if (evoDef != null)
            MemoryTag(card, "FamilyTag", $"{BuildFamilyRules.Label(evoDef.Family)} 각인 +{RelicEvolution.ImprintBonus}", UITheme.Thin.Gold, new Vector2(tagX, -94f), tagW);
        else if (entry != null && entry.IsReaction)
            MemoryTag(card, "ReactTag", $"{RuneElementLabel(entry.rune_element)} 반응", ringCol, new Vector2(tagX, -94f), tagW);
        else if (entry != null)
        {
            var fam = BuildFamilyRules.OfRelicPart(entry.part_id);
            if (fam != BuildFamily.None)
                MemoryTag(card, "FamilyTag", BuildFamilyRules.Label(fam), BuildFamilyRules.ColorOf(fam), new Vector2(tagX, -94f), tagW);
        }

        // 이름 — 24 · 500(칸 36 = Noto 행 높이)
        var name = ThinText(card, "Name", UIWeight.Medium, 24f, TextAlignmentOptions.Center, UITheme.Thin.Ink);
        ShopUIStyle.Anchor(name.rectTransform, top, top, top, new Vector2(0f, -142f), new Vector2(_cardW - 28f, 36f));
        name.text = c.Kind switch
        {
            RelicDraftCardKind.Echo      => "메아리",
            RelicDraftCardKind.Residue   => "잔향",
            RelicDraftCardKind.Evolution => RelicEvolution.ById(c.EvolutionId)?.Name ?? "진화",
            _                          => entry?.part_name ?? entry?.part_id ?? string.Empty,
        };
        name.textWrappingMode = TextWrappingModes.NoWrap;
        name.overflowMode     = TextOverflowModes.Ellipsis;

        // 핵심 한 줄(①) — 테 없는 효과 바탕(카드 안 두 번째이자 마지막 상자) 위 19 · 400. 메아리 · 잔향은 하는 일 한 줄
        var head = ShopUIStyle.MakeImage(card, "Headline", EffectFill);
        ShopUIStyle.Anchor(head.rectTransform, top, top, top, new Vector2(0f, -HeadlineTop), new Vector2(_cardW - 24f, HeadlineH));
        var headText = ThinText(head.transform, "Text", UIWeight.Regular, 19f, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
        ShopUIStyle.Stretch(headText.rectTransform);
        headText.rectTransform.offsetMin = new Vector2(12f, 8f);   // 세 줄(19 × Noto 행 높이 1.45 ≈ 83)이 줄지 않고 들어가게 위아래 여백은 8
        headText.rectTransform.offsetMax = new Vector2(-12f, -8f);
        headText.textWrappingMode = TextWrappingModes.Normal;
        headText.fontSizeMin = 15f;
        headText.text = c.Kind == RelicDraftCardKind.Echo || c.Kind == RelicDraftCardKind.Residue || c.Kind == RelicDraftCardKind.Evolution
            ? MemoryCardBody(c)
            : Gild(entry?.Line(1) ?? string.Empty);

        // ②③ — 16 · 400(잠긴 줄은 흐리게 「◇」, 이번에 열리는 줄은 금). 왼쪽 끝을 핵심 한 줄 글자에 맞춘다
        if (c.Kind == RelicDraftCardKind.NewFragment || c.Kind == RelicDraftCardKind.Sharpen)
        {
            var rest = ThinText(card, "Rest", UIWeight.Regular, 16f, TextAlignmentOptions.TopLeft, UITheme.Thin.InkA(0.92f));
            ShopUIStyle.Anchor(rest.rectTransform, top, top, top, new Vector2(0f, -(HeadlineTop + HeadlineH + 10f)), new Vector2(_cardW - 48f, RestH));
            rest.textWrappingMode = TextWrappingModes.Normal;
            rest.fontSizeMin = 14f;   // ×1.3 창 맞춤 = 18px 아래로 안 줄어든다
            rest.text = MemoryCardRest(c);
        }

        // 구분선 — 고를 근거(위)와 꼬리말 · 기억(아래)을 상자 대신 1px 선으로 가른다(꼬리말 58~102 · 기억 18~54)
        var rule = ShopUIStyle.MakeImage(card, "FootRule", UITheme.Thin.LineIdle);
        ShopUIStyle.Anchor(rule.rectTransform, bottom, bottom, bottom, new Vector2(0f, 106f), new Vector2(_cardW - 48f, 1f));

        // 꼬리말 — 반응 조건 · 공명 미리 보기 · 메아리 효과 · 잔향 정수. 띠 판 없이 흐린 글자(15) — 공명 단계 문구가 길어 두 줄까지
        if (evoDef != null)
        {
            // 진화 카드의 빈 가운데(효과 상자 아래 ~ 조건 위) — 고르면 무엇이 되는지 한 번 더 또렷이(10-14 사용자 「어떻게 해야 하는지 모르겠다」)
            var what = ThinText(card, "EvoWhat", UIWeight.Medium, 17f, TextAlignmentOptions.Center, UITheme.Thin.Gold);
            ShopUIStyle.Anchor(what.rectTransform, top, top, top, new Vector2(0f, -(HeadlineTop + HeadlineH + 14f)), new Vector2(_cardW - 32f, 52f));
            what.textWrappingMode = TextWrappingModes.Normal;
            what.fontSizeMin = 15f;
            what.text = $"고르면 유물이 진화한다\n<color=#ECE6D8>{BuildFamilyRules.Label(evoDef.Family)} 각인 +{RelicEvolution.ImprintBonus} · 런 끝까지</color>";
            BuildEvolutionChecks(card, evoDef);   // 진화 = 열린 까닭 두 줄(조건 체크 표시 — 설계 §4-3)
        }
        else
        {
            // 흐린 회색(TextDim) 15는 카드 넷이면 읽히지 않았다 — 잉크 78% 16 · 「고르면」으로 시작해 무엇이 바뀌는지 한 줄로(10-14)
            var foot = ThinText(card, "Foot", UIWeight.Regular, 16f, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.78f));
            ShopUIStyle.Anchor(foot.rectTransform, bottom, bottom, bottom, new Vector2(0f, 56f), new Vector2(_cardW - 32f, 46f));
            foot.textWrappingMode = TextWrappingModes.Normal;
            foot.fontSizeMin = 14f;
            string footText = MemoryCardFoot(c);
            foot.text = string.IsNullOrEmpty(footText) ? footText : "고르면 · " + footText;
        }

        // 기억 한 줄 — 맛(카드 바닥 18~54 · 300 · 기울임 · 따옴표 없음)
        string memory = c.Kind == RelicDraftCardKind.NewFragment || c.Kind == RelicDraftCardKind.Sharpen
            ? RelicMemoryRecall.MemoryLineFor(entry, c.Grade) : string.Empty;
        if (!string.IsNullOrEmpty(memory))
        {
            var mem = ThinText(card, "MemoryLine", UIWeight.Light, 15f, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.6f));
            ShopUIStyle.Anchor(mem.rectTransform, bottom, bottom, bottom, new Vector2(0f, 18f), new Vector2(_cardW - 40f, 36f));
            mem.textWrappingMode = TextWrappingModes.NoWrap;
            mem.fontSizeMin = 12f;
            mem.overflowMode = TextOverflowModes.Ellipsis;
            mem.text = memory;
        }
    }

    // 카드 안 자리(카드 위끝 · 1080 목업 px) — 10-11 v2(카드 340×480)
    private const float EmblemSize     = 84f;     // 문양 칸 104 → 84
    private const float RingStroke     = 3f;      // 문양 고리 4 → 3px(장식이 모이는 한 곳 = v2 「특수 한 곳 3px」)
    private const float HeadlineTop    = 186f;
    private const float HeadlineH      = 100f;
    private const float RestH          = 72f;     // 위끝 296 · 아래끝 = 카드 바닥에서 112(구분선 106 위)
    private const float TagDot         = 8f;
    private const float TagRowH        = 26f;     // 17 × Noto 행 높이(약 1.45em) — 이보다 낮으면 자동 크기가 글자를 줄인다
    private const float LabelSpacing   = 10f;     // 등급 라벨 자간(작은 라벨 = 500 + 자간)
    private const float GradeBandTint  = 0.12f;   // 등급 띠 채움 0.30 → 0.12
    private const float DiscTint       = 0.10f;   // 문양 속 채움 0.16 → 0.10
    private const float RingSaturation = 0.70f;   // 문양 고리 채도 70% — 광기 · 심판 빨강이 「경고」로 읽히지 않게
    private const float RadiantHaloAlpha = 0.5f;  // 등급 후광 — 찬란만
    private const float RadiantHaloPx    = 8f;
    private static readonly Color EffectFill = new(0.10f, 0.09f, 0.12f, 1f);   // 효과 바탕 — 테 없이 채움만

    // 숫자만 금(3장 「숫자만 금」) — 핵심어 옅은 금은 10-11 v2에서 뺐다(색 뜻 줄이기, 리서치 B §6).
    private const string NumberColor  = "#FFD54A";

    /// <summary>숫자를 금으로 물들인다. 색 태그 안 숫자는 건드리지 않는다.</summary>
    private static string Gild(string line)
    {
        if (string.IsNullOrEmpty(line)) return line;
        return System.Text.RegularExpressions.Regex.Replace(line,
            @"(?<![<#=\w.])([+\-−×]?\d+(?:\.\d+)?(?:%|초|m|체|개|스택|중첩)?)", $"<color={NumberColor}>$1</color>");
    }

    private static string GradeTitle(RelicDraftCard c) => c.Kind switch
    {
        RelicDraftCardKind.Evolution => "유물 진화",
        RelicDraftCardKind.Echo    => "메아리",
        RelicDraftCardKind.Residue => "잔향",
        RelicDraftCardKind.Sharpen => $"더 {RelicMemoryOdds.Label(c.Grade)}하게",
        _                          => $"{RelicMemoryOdds.Label(c.Grade)}한 기억",
    };

    private static string KindTitle(RelicDraftCard c) => c.Kind switch
    {
        RelicDraftCardKind.Sharpen => "다시 떠올림",
        RelicDraftCardKind.Echo    => "공명 한 칸",
        RelicDraftCardKind.Residue => "남은 빛",
        RelicDraftCardKind.Evolution => "빌드의 완성",
        _                          => "새 조각",
    };

    /// <summary>자리색 — 여명 노을빛 · 정오 금 · 황혼 주홍 · 광기 계단 핏빛 · 반응 = 룬 속성색.</summary>
    private static Color AnchorColor(RelicDraftCard c)
    {
        var e = c.Entry;
        if (e != null && e.IsReaction) return ElementDef.IdColor((e.rune_element ?? string.Empty).ToUpperInvariant(), GradeClear);
        if (c.Kind == RelicDraftCardKind.Evolution && RelicEvolution.ById(c.EvolutionId) is { } evo)
            return BuildFamilyRules.ColorOf(evo.Family);   // 진화 = 진화 계열색(문양 고리 · 문장 · 꼬리표 점이 한 색)
        string a = e != null ? e.anchor : c.Anchor;
        return a switch
        {
            RelicPartAnchor.Dawn  => new Color(1.00f, 0.67f, 0.47f, 1f),
            RelicPartAnchor.Noon  => new Color(0.98f, 0.78f, 0.32f, 1f),
            RelicPartAnchor.Dusk  => new Color(0.92f, 0.47f, 0.35f, 1f),
            RelicPartAnchor.DawnNoon or RelicPartAnchor.NoonDusk => new Color(0.98f, 0.70f, 0.40f, 1f),
            RelicPartAnchor.Frenzy or RelicPartAnchor.Judgment   => new Color(0.92f, 0.30f, 0.30f, 1f),
            RelicPartAnchor.R10 or RelicPartAnchor.R20 or RelicPartAnchor.R30 or RelicPartAnchor.R40 => new Color(0.86f, 0.36f, 0.40f, 1f),
            _ => GradeColor(c.Grade),
        };
    }

    /// <summary>꼬리표 한 줄 — 8px 색 점 + 글자(17 · 500 · 잉크). 테 두른 칩을 없앴다(카드 안 상자 5 → 2, 리서치 B §6).</summary>
    private void MemoryTag(Transform card, string name, string text, Color col, Vector2 pos, float maxW)
    {
        if (string.IsNullOrEmpty(text)) return;
        var tl = new Vector2(0f, 1f);
        var dot = ShopUIStyle.MakeImage(card, name + "Dot", col);
        dot.sprite = UIThinShapes.Disc(TagDot);
        ShopUIStyle.Anchor(dot.rectTransform, tl, tl, new Vector2(0f, 0.5f), new Vector2(pos.x, pos.y - TagRowH * 0.5f), Vector2.one * TagDot);
        var label = ThinText(card, name, UIWeight.Medium, 17f, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.88f));
        ShopUIStyle.Anchor(label.rectTransform, tl, tl, tl, new Vector2(pos.x + TagDot + 8f, pos.y), new Vector2(maxW - TagDot - 8f, TagRowH));
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode     = TextOverflowModes.Ellipsis;
        label.text = text;
    }

    // ── 진화 카드(10-14 빌드 시너지 C · 83 UI 요청 1) — 「빌드의 완성」으로 읽히게 ──
    // 문양 = 그 유물의 문장(진화 계열색) + 바깥 금 1px 고리 한 겹(닫힌 두 겹 원) · 카드 = 6px 안쪽 금 1px 겹선 · 찬란 후광(등급 찬란) ·
    // 꼬리말 = 열린 까닭 두 줄(체크 + 「조각 n/4」 · 「계열 각인 n/4」). 열릴 때 금 고리 · 겹선이 시계 방향으로 한 번 그려진다(0.5초 · 실시간).
    private const float CompletionGap     = 6f;     // 문양 바깥 금 고리 · 카드 안쪽 겹선 간격
    private const float CompletionRingA   = 0.85f;
    private const float CompletionFrameA  = 0.40f;
    private const float CompletionTrace   = 0.5f;
    private const float CompletionDelay   = 0.15f;
    private const float EvoCheckSize      = 16f;
    private const float EvoCheckIcon      = 16f;
    private const float EvoChecksBottom   = 20f;    // 10-14 「진화 조건」 머리 줄이 붙어 세 줄 — 구분선(106) 아래에 들게 내렸다(묶음 아래끝 = 카드 바닥에서)

    /// <summary>진화 문양 — 유물 문장(Higgsfield 흰 선화) → 없으면 유물 배지(Artsystack 흰 아이콘). 둘 다 없으면 null(글자 「진」).</summary>
    private static Sprite EvolutionEmblem(RelicClassSO relic, RelicPartDraftSkinSO skin)
    {
        if (relic == null) return null;
        if (relic.Crest != null) return relic.Crest;
        return skin != null ? skin.RelicBadge(relic.Id.ToString()) : null;
    }

    /// <summary>문양 바깥 금 1px 고리 — 문양 고리와 함께 닫힌 두 겹 원. 열릴 때 그려진다.</summary>
    private void BuildCompletionRing(Transform card, Vector2 emblemTopLeft)
    {
        float d = EmblemSize + CompletionGap * 2f;
        var tl = new Vector2(0f, 1f);
        var img = ShopUIStyle.MakeImage(card, "CompletionRing", UITheme.Thin.WithA(UITheme.Thin.Gold, CompletionRingA));
        img.sprite = UIThinShapes.Ring(d, UITheme.Thin.Stroke);
        ShopUIStyle.Anchor(img.rectTransform, tl, tl, tl, emblemTopLeft + new Vector2(-CompletionGap, CompletionGap), new Vector2(d, d));
        TraceOnOpen(img);
    }

    /// <summary>카드 6px 안쪽 금 1px 겹선 — 고른 표시(카드 가장자리 금 테 + 눈금)와 겹치지 않는 안쪽 선. 열릴 때 그려진다.</summary>
    private void BuildCompletionFrame(CardView view)
    {
        if (view?.Rt == null) return;
        var size = new Vector2(_cardW - CompletionGap * 2f, CardH - CompletionGap * 2f);
        var img = ShopUIStyle.MakeImage(view.Rt, "CompletionFrame", UITheme.Thin.WithA(UITheme.Thin.Gold, CompletionFrameA));
        img.sprite = UIThinShapes.Outline(ThinShape.Square, size.x, size.y);
        var mid = new Vector2(0.5f, 0.5f);
        ShopUIStyle.Anchor(img.rectTransform, mid, mid, mid, Vector2.zero, size);
        TraceOnOpen(img);
    }

    /// <summary>시계 방향으로 한 번 그려진다(톤 v2 「켜짐 = 테가 그려짐」) — 시간정지 창이라 실시간.</summary>
    private void TraceOnOpen(Image img)
    {
        img.type          = Image.Type.Filled;
        img.fillMethod    = Image.FillMethod.Radial360;
        img.fillOrigin    = (int)Image.Origin360.Top;
        img.fillClockwise = true;
        img.fillAmount    = 0f;
        TraceAsync(img, destroyCancellationToken).Forget();
    }

    private static async UniTaskVoid TraceAsync(Image img, System.Threading.CancellationToken ct)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(CompletionDelay), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            await UIMotion.Tween(CompletionTrace, k => { if (img != null) img.fillAmount = k; }, ct);
        }
        catch (OperationCanceledException) { }
        finally { if (img != null) img.fillAmount = 1f; }
    }

    /// <summary>
    /// 열린 까닭 두 줄 — 「✓ 조각 4/4」 · 「✓ 연격 각인 4/4」(체크는 그린 그림 — ✓ 글리프는 글꼴 밖). 숫자는 로드아웃 · 각인에서 읽는다.
    /// 채운 줄 = 금 체크 · 못 채운 줄(실측 도구로 억지로 띄운 카드) = 빈 고리.
    /// </summary>
    private void BuildEvolutionChecks(Transform card, RelicEvolution.Def def)
    {
        var loadout = AppBootstrapper.Instance?.Loadout;
        int parts   = loadout?.RelicPartIds?.Count ?? 0;
        int imprint = BuildImprint.Count(def.Family);

        var box = (RectTransform)new GameObject("EvoChecks", typeof(RectTransform)).transform;
        box.SetParent(card, false);
        var bottom = new Vector2(0.5f, 0f);
        box.anchorMin = box.anchorMax = box.pivot = bottom;
        box.anchoredPosition = new Vector2(0f, EvoChecksBottom);
        var v = box.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 2f;
        v.childAlignment = TextAnchor.LowerLeft;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = v.childForceExpandHeight = false;
        var fit = box.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var head = ThinText(box, "EvoCheckHead", UIWeight.Regular, EvoCheckSize, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.6f));
        head.enableAutoSizing = false;
        head.textWrappingMode = TextWrappingModes.NoWrap;
        head.text = "진화 조건";
        EvoCheckRow(box, "EvoCheckParts", "조각", parts, RelicEvolution.FragmentsNeeded);
        EvoCheckRow(box, "EvoCheckImprint", $"{BuildFamilyRules.Label(def.Family)} 각인", imprint, RelicEvolution.ImprintNeeded);
        LayoutRebuilder.ForceRebuildLayoutImmediate(box);
    }

    private static void EvoCheckRow(RectTransform parent, string name, string label, int have, int need)
    {
        bool met = have >= need;
        var row = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        row.SetParent(parent, false);
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;

        var icon = ShopUIStyle.MakeImage(row, "Check", met ? UITheme.Thin.Gold : UITheme.Thin.InkA(0.5f));
        icon.sprite = met ? UIProceduralSprites.Check(64) : UIThinShapes.Ring(EvoCheckIcon, UITheme.Thin.Stroke);
        icon.preserveAspect = true;
        var le = icon.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = le.minHeight = le.preferredHeight = EvoCheckIcon;

        var t = ThinText(row, "Label", UIWeight.Regular, EvoCheckSize, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.88f));
        t.enableAutoSizing = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.text = $"{label} <color={NumberColor}>{have}</color>/{need}";
    }

    /// <summary>
    /// 등급 바깥 빛 — <b>찬란 하나만</b>(0.5 · 8px). 선명 후광(0.45)은 뺐다 — 고른 표시(금테)와 둘 다 「빛나는 테두리」라
    /// 무엇이 선택인지 헷갈렸다(리서치 B §6). 카드 뒤(첫 자식)라 몸체 밖으로만 번진다.
    /// </summary>
    private static void BuildGradeHalo(CardView view, RelicMemoryGrade grade)
    {
        if (grade != RelicMemoryGrade.Radiant || view.Rt == null) return;
        var img = ShopUIStyle.MakeImage(view.Rt, "GradeHalo", WithAlpha(view.Tone, RadiantHaloAlpha));
        img.sprite = UI_RuneSelectPopup.HaloRing;
        img.type   = Image.Type.Sliced;
        // 9-slice 경계(원본 24px)를 8px로 — 번짐 폭이 곧 경계 폭이다
        img.pixelsPerUnitMultiplier = img.sprite != null ? img.sprite.border.x / RadiantHaloPx : 1f;
        ShopUIStyle.Stretch(img.rectTransform, -RadiantHaloPx);
        img.transform.SetAsFirstSibling();
    }

    /// <summary>
    /// 기억 카드 판(10-11 v2) — 어두운 글래스 판 하나 + 1px 테(<see cref="UIThinFrame"/>). 둥근 판 · 2px 등급색 테를 걷는다.
    /// 바깥 테 Image는 투명으로 두고 클릭만 받는다(raycast는 알파를 보지 않는다). 등급은 테가 아니라 라벨 글자 · 찬란 후광으로 보인다.
    /// </summary>
    private void StyleThinCard(CardView view)
    {
        if (view.Border != null) view.Border.color = Color.clear;
        if (view.Fill != null)
        {
            ShopUIStyle.Stretch(view.Fill.rectTransform);
            var fillArt = UISkin.RelicPartDraft?.cardFill;
            if (fillArt != null) ShopUIStyle.Skin(view.Fill, fillArt, sliced: true);   // 10-13 Artsystack 남색 판 — 등급 테는 아래 1px 코드 테
            else
            {
                view.Fill.sprite = null;
                view.Fill.type   = Image.Type.Simple;
                view.Fill.color  = UITheme.Thin.GlassFill;
            }
        }
        view.Frame = UIThinFrame.Create(view.Rt, ThinShape.Square, new Vector2(_cardW, CardH));
    }

    /// <summary>톤 v2 글자 — Noto 굵기 글꼴 · 가짜 굵게 없음. MakeText는 기본 글꼴(DNF)로 지으므로 바로 갈아입힌다.</summary>
    private static TMP_Text ThinText(Transform parent, string name, UIWeight weight, float size, TextAlignmentOptions align, Color color)
    {
        var t = ShopUIStyle.MakeText(parent, name, size, FontStyles.Normal, align, color);
        UITheme.StyleThinText(t, weight, size, color);
        return t;
    }

    /// <summary>채도만 <paramref name="k"/>배로(명도 그대로).</summary>
    private static Color Desaturate(Color c, float k)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        var o = Color.HSVToRGB(h, s * k, v);
        o.a = c.a;
        return o;
    }

    /// <summary>②③ — 잠긴 줄은 흐리게 「◇」, 이번에 열리는 줄은 금, 드러난 줄은 밝게(숫자 금).</summary>
    private static string MemoryCardRest(RelicDraftCard c)
    {
        var e = c.Entry;
        if (e == null) return string.Empty;
        var loadout = AppBootstrapper.Instance?.Loadout;
        int shown = (int)c.Grade;
        int had   = c.Kind == RelicDraftCardKind.Sharpen && loadout != null ? (int)loadout.GetRelicPartGrade(e.part_id) : 0;
        var sb = new System.Text.StringBuilder();
        for (int n = 2; n <= 3; n++)
        {
            var line = e.Line(n);
            if (string.IsNullOrEmpty(line)) continue;
            if (sb.Length > 0) sb.Append('\n');
            string mark = n == 2 ? "②" : "③";
            if (n > shown)               sb.Append("<color=").Append(LockedLineColor).Append(">◇ ").Append(line).Append("</color>");
            else if (had > 0 && n > had) sb.Append("<color=").Append(NewLineColor).Append('>').Append(mark).Append(' ').Append(line).Append("</color>");
            else                         sb.Append(mark).Append(' ').Append(Gild(line));
        }
        return sb.ToString();
    }

    private static string MemoryCardBody(RelicDraftCard c)
    {
        var loadout = AppBootstrapper.Instance?.Loadout;
        switch (c.Kind)
        {
            case RelicDraftCardKind.Echo:
                return c.Anchor != null && c.Anchor.StartsWith("r")
                    ? $"계단 「{RelicPartAnchor.Label(c.Anchor)}」에 빈 받침을 놓는다 — 조각 없이도 계단이 이어진 것으로 센다."
                    : $"「{RelicPartAnchor.Label(c.Anchor)}」 공명을 하나 더 센다.";
            case RelicDraftCardKind.Residue:
                return "모든 기억이 선명하다. 남은 빛이 정수가 된다.";
            case RelicDraftCardKind.Evolution:
                return Gild(RelicEvolution.ById(c.EvolutionId)?.Desc ?? string.Empty);
        }
        var e = c.Entry;
        if (e == null) return string.Empty;
        int shown = (int)c.Grade;
        int had   = c.Kind == RelicDraftCardKind.Sharpen && loadout != null ? (int)loadout.GetRelicPartGrade(e.part_id) : 0;
        var sb = new System.Text.StringBuilder();
        for (int n = 1; n <= 3; n++)
        {
            var line = e.Line(n);
            if (string.IsNullOrEmpty(line)) continue;
            if (sb.Length > 0) sb.Append('\n');
            string mark = n == 1 ? "①" : n == 2 ? "②" : "③";
            if (n > shown)              sb.Append("<color=").Append(LockedLineColor).Append(">◇ ").Append(line).Append("</color>");
            else if (had > 0 && n > had) sb.Append("<color=").Append(NewLineColor).Append('>').Append(mark).Append(' ').Append(line).Append("</color>");
            else                        sb.Append(mark).Append(' ').Append(line);
        }
        return sb.ToString();
    }

    private static string MemoryCardFoot(RelicDraftCard c)
    {
        var loadout = AppBootstrapper.Instance?.Loadout;
        string relicId = c.Entry != null ? c.Entry.relic_id : loadout?.Relic != null ? loadout.Relic.Id.ToString().ToLower() : null;
        switch (c.Kind)
        {
            case RelicDraftCardKind.Residue: return $"정수 +{c.ResidueEssence}";
            case RelicDraftCardKind.Evolution:
            {
                var d = RelicEvolution.ById(c.EvolutionId);
                return d != null ? RelicEvolution.RecipeLine(loadout, d) : string.Empty;
            }
            case RelicDraftCardKind.Echo:
                return ResonanceFoot(loadout, relicId, c.Anchor) ?? "공명 칸이 하나 찬다";
        }
        var e = c.Entry;
        if (e == null) return string.Empty;
        if (e.IsReaction) return $"룬 {RuneElementLabel(e.rune_element)} 1단계 충족 — 반응 조각";   // ✓는 기호 화이트리스트 밖(□)
        if (c.Kind == RelicDraftCardKind.Sharpen) return "다음 줄이 떠오른다";
        return ResonanceFoot(loadout, relicId, e.anchor) ?? $"{RelicPartAnchor.Label(e.anchor)}에 매달린다";
    }

    /// <summary>
    /// 공명 숫자 꼬리말(10-12 4b 요청 5) — 단계를 넘으면 그 한 줄(<see cref="RelicResonance.PreviewLine"/>),
    /// 안 넘어도 숫자는 보인다: 「여명 공명 1 → 2 (2에서 단계)」 · 「이어진 계단 1 → 2」. 공명과 무관한 자리면 null.
    /// </summary>
    private static string ResonanceFoot(PlayerLoadout loadout, string relicId, string anchor)
    {
        if (loadout == null || string.IsNullOrEmpty(anchor)) return null;
        string cross = RelicResonance.PreviewLine(loadout, relicId, anchor);
        if (cross != null) return cross;

        if (relicId == "gawain")
        {
            var sb = new System.Text.StringBuilder();
            int covered = 0, lastAfter = 0;
            foreach (var phase in RelicPartAnchor.GawainPhases)
            {
                if (!RelicPartAnchor.Covers(anchor, phase)) continue;
                int before = RelicResonance.GawainCount(loadout, phase);
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(RelicPartAnchor.Label(phase)).Append(" 공명 ").Append(before).Append(" → ").Append(before + 1);
                covered++;
                lastAfter = before + 1;
            }
            if (covered == 0) return null;
            int next = lastAfter < 2 ? 2 : lastAfter < 4 ? 4 : 0;
            if (covered == 1 && next > 0) sb.Append(" (").Append(next).Append("에서 단계)");
            return sb.ToString();
        }
        if (relicId == "lancelot")
        {
            if (System.Array.IndexOf(RelicPartAnchor.LancelotRungs, anchor) < 0) return null;
            int before = RelicResonance.LancelotLadder(loadout);
            if (RelicResonance.LancelotRungFilled(loadout, anchor)) return $"이어진 계단 {before} — 이미 찬 칸";
            int after = RelicResonance.LadderIfFilled(loadout, anchor, null);
            if (after == before) return $"이어진 계단 {before} — 아래 계단부터 이어야 센다";
            return after < 2 ? $"이어진 계단 {before} → {after} (2에서 단계)" : $"이어진 계단 {before} → {after}";
        }
        return null;
    }

    /// <summary>「다시 떠올리기」(제단 노드, 런당 1회) — 장착 버튼 왼쪽. 없으면 숨긴다.</summary>
    private void EnsureRedrawButton(bool show)
    {
        if (_redrawBtn == null && show && _windowRoot != null)
        {
            var frame = ShopUIStyle.MakeFrame(_windowRoot, "RedrawBtn", ShopUIStyle.BronzeLine, ShopUIStyle.BandFill, 2f, raycast: true);
            ShopUIStyle.Anchor((RectTransform)frame.transform.parent,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-250f, 30f), new Vector2(220f, 56f));
            UITheme.StyleFrameButton(frame, UITheme.CtaTintOff);
            // 창 맞춤(UIWindowFitter)이 글꼴을 키운 뒤에 짓는 버튼이라 그 배율을 같이 먹인다(10-11 v2 · Noto 500)
            var label = ThinText(frame.transform, "Label", UIWeight.Medium, 17f * ChromeFontScale(), TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
            ShopUIStyle.Stretch(label.rectTransform);
            label.text = "다시 떠올리기";
            _redrawBtn = frame.transform.parent.gameObject;
            AddClick(_redrawBtn, OnRedrawClicked);
        }
        if (_redrawBtn != null) _redrawBtn.SetActive(show);
    }

    private void OnRedrawClicked()
    {
        if (_closing) return;
        Managers.Sound?.PlayUiEvent(SoundEvent.MemoryRedraw);
        RedrawRequested = true;
        _interactionTcs?.TrySetResult();
        ClosePopupUI();
    }

    // ── 선택 상태 ──

    private void SetSelected(int index)
    {
        if (_closing) return;   // 고른 뒤 연출 중
        // 초기화(-1)에도 소리를 내면 팝업이 열리는 순간 아무도 안 눌렀는데 클릭음이 울린다.
        if (index >= 0) Managers.Sound?.PlayUiEvent(SoundEvent.UiSelect);
        _selected = index;

        for (int i = 0; i < _cards.Count; i++)
        {
            bool on = (i == index);
            if (_cards[i].Frame != null)
            {
                // 톤 v2(10-11) — 선택 표시는 이 테 하나: 금 85% 1px + 모서리 눈금이 0.25초에 그려진다. 바탕 · 등급 후광은 그대로 둔다.
                _cards[i].Frame.SetState(on ? ThinFrameState.Ready : ThinFrameState.Idle, animate: index >= 0);
                continue;
            }
            if (_cards[i].Border != null)
                _cards[i].Border.color = _skinned ? (on ? Color.white : new Color(0.62f, 0.62f, 0.66f))
                                                  : (on ? SelectBorder : (_draft != null ? _cards[i].Tone : ShopUIStyle.CardBorder));
            if (_cards[i].Fill != null)
                _cards[i].Fill.color = _skinned ? Color.white
                                                : (on ? CardSelected : ShopUIStyle.CardFill);
        }
        ApplySelectionLook(index);

        bool hasSel = index >= 0;
        if (_confirmLabel != null)
        {
            _confirmLabel.color = hasSel ? ShopUIStyle.TextPrimary : ShopUIStyle.TextDim;
            _confirmLabel.text = _draft == null ? "장착" : hasSel ? "가져간다" : "카드를 먼저 고른다";
        }
        if (_confirmBtnImg != null)
            _confirmBtnImg.color = _themed ? (hasSel ? UITheme.CtaTint : UITheme.CtaTintOff)
                                           : (hasSel ? ShopUIStyle.GoldPillBg : ShopUIStyle.BandFill);
        UIAffordGlow.Set(_confirmBtnImg, hasSel);   // 고르면 장착 버튼에 은은한 불(09-29)
    }

    /// <summary>
    /// 고른 카드는 한 치수 커지고(1.03) 나머지는 0.55로 물러난다 — 재련소 탭·HUD 무기 칸과 같은 「선택 = 금테 + 확대」 규약.
    /// 0.18초 감속(실시간 — 시간정지 창). 처음 열 때(-1)는 연출 없이 바로 놓는다.
    /// </summary>
    private void ApplySelectionLook(int index)
    {
        CancelSelectionMotion();
        if (index < 0 || !isActiveAndEnabled)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                bool on = i == index;
                if (_cards[i].Rt != null)    _cards[i].Rt.localScale = Vector3.one * (on ? SelectedCardScale : 1f);
                if (_cards[i].Group != null) _cards[i].Group.alpha   = index < 0 || on ? 1f : OthersAlpha;
            }
            return;
        }
        _selCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        SelectionMotionAsync(index, _selCts.Token).Forget();
    }

    private async UniTaskVoid SelectionMotionAsync(int index, System.Threading.CancellationToken ct)
    {
        int n = _cards.Count;
        var s0 = new float[n];
        var a0 = new float[n];
        for (int i = 0; i < n; i++)
        {
            s0[i] = _cards[i].Rt != null ? _cards[i].Rt.localScale.x : 1f;
            a0[i] = _cards[i].Group != null ? _cards[i].Group.alpha : 1f;
        }
        try
        {
            await UIMotion.Tween(UIMotion.Swap, k =>
            {
                for (int i = 0; i < n && i < _cards.Count; i++)
                {
                    bool on = i == index;
                    if (_cards[i].Rt != null)    _cards[i].Rt.localScale = Vector3.one * Mathf.Lerp(s0[i], on ? SelectedCardScale : 1f, k);
                    if (_cards[i].Group != null) _cards[i].Group.alpha   = Mathf.Lerp(a0[i], on ? 1f : OthersAlpha, k);
                }
            }, ct);
        }
        catch (OperationCanceledException) { }
    }

    private void CancelSelectionMotion()
    {
        if (_selCts == null) return;
        _selCts.Cancel();
        _selCts.Dispose();
        _selCts = null;
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
            var bgArt = UISkin.RelicPartDraft?.background;
            if (bgArt != null) ShopUIStyle.Skin(fillImg, bgArt, sliced: true);   // 10-13 Artsystack 남색 판(제단 판과 같은 판)
            else UITheme.RoundFrame(fillImg, 18f);
            if (window.TryGetComponent<Image>(out var edge)) edge.color = UITheme.GoldLine;
        }
        _themed = UITheme.StyleFrameButton(_confirmBtnImg, UITheme.CtaTintOff);
        if (_confirmLabel != null) TMPOutlineHelper.ApplySoftShadow(_confirmLabel);
    }

    /// <summary>
    /// 창 머리 · 아래 버튼 글자를 톤 v2로 덮어쓴다(10-11) — 구운 프리팹이라 빌더를 고쳐도 반영되지 않으므로 <b>열 때마다 여기서</b> 입힌다.
    /// 두꺼운 제목 띠를 걷고 제목(Noto 300 30) + 아래 1px 금 30% 선 · 부제(17) · 확정 라벨(Noto 500 20). 가짜 굵게 없음.
    /// 창 맞춤(UIWindowFitter)이 이미 먹인 글꼴 배율을 그대로 곱한다 — 해상도가 바뀌어 다시 맞출 때도 어긋나지 않는다.
    /// </summary>
    private void ApplyThinChrome()
    {
        if (_skinned) return;
        float s = ChromeFontScale();

        var titleBar = ShopUIStyle.FindDeep(transform, "TitleBar");
        var barArt = UISkin.RelicPartDraft?.titleBar;
        if (titleBar != null && barArt != null && titleBar.TryGetComponent<Image>(out var artBand))
        {
            artBand.enabled = true;
            ShopUIStyle.Skin(artBand, barArt, sliced: true);
        }
        else if (titleBar != null)
        {
            if (titleBar.TryGetComponent<Image>(out var band)) band.enabled = false;
            if (titleBar.Find("TitleRule") == null)
            {
                var rt = ShopUIStyle.MakeImage(titleBar, "TitleRule", UITheme.Thin.LineGold).rectTransform;
                rt.anchorMin = new Vector2(0.2f, 0f);
                rt.anchorMax = new Vector2(0.8f, 0f);
                rt.pivot     = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(0f, UITheme.Thin.Stroke);
                rt.anchoredPosition = Vector2.zero;
            }
        }
        if (_titleText == null) _titleText = ShopUIStyle.FindDeep(transform, "Title")?.GetComponent<TMP_Text>();
        UITheme.StyleThinText(_titleText, UIWeight.Light, 30f * s, UITheme.Thin.Ink);
        UITheme.StyleThinText(_subtitle, UIWeight.Regular, 17f * s, UITheme.Thin.InkA(0.62f));
        if (_confirmLabel != null) UITheme.StyleThinText(_confirmLabel, UIWeight.Medium, 20f * s, _confirmLabel.color);
    }

    /// <summary>창 맞춤이 창 아래 글꼴에 먹인 배율(<see cref="UIFontScaleMemo"/>). 아직 안 맞췄으면 1 — 나중에 맞출 때 곱해진다.</summary>
    private float ChromeFontScale()
    {
        var memo = _windowRoot != null ? _windowRoot.GetComponentInParent<UIFontScaleMemo>() : null;
        return memo != null ? memo.Applied : 1f;
    }

    // ── 버튼 ──

    private void OnConfirmClicked()
    {
        int count = _draft != null ? _draft.Cards.Count : _candidates?.Count ?? 0;
        if (_selected < 0 || _selected >= count)
        {
            // 미선택. 조용히 return하면 버튼이 죽은 것으로 읽힌다 —
            // 무엇이 빠졌는지 버튼이 직접 말하게 한다(룬 선택 팝업과 같은 규약).
            ShopUIStyle.PlaySfx("shop_reject");
            if (_confirmLabel != null) _confirmLabel.text = "카드를 먼저 고른다";
            return;
        }

        if (_closing) return;
        Managers.Sound?.PlayUiEvent(SoundEvent.MemoryTake);
        if (_draft != null)
        {
            ResultCard = _draft.Cards[_selected];
            Result     = ResultCard.Entry;
        }
        else Result = _candidates[_selected];
        ConfirmSequenceAsync(_selected).Forget();
    }

    /// <summary>
    /// 고른 뒤(10-06 · 시나리오 §2-4) — 고른 카드가 등급색으로 번쩍이고 나머지는 가라앉으며 흐려진 뒤,
    /// 고른 카드가 빛으로 줄어 플레이어 쪽으로 날아 들어간다. 창이 닫히면 몸에 유물 메인 이펙트(<see cref="RelicMemoryRecall.PlayAcquiredAsync"/>).
    /// 창이 시간을 멈춰 두므로 전부 unscaled. 취소돼도 결과는 넘기고 닫는다.
    /// </summary>
    private async UniTaskVoid ConfirmSequenceAsync(int sel)
    {
        _closing = true;
        try { await PlayChosenAsync(sel, destroyCancellationToken); }
        catch (OperationCanceledException) { }
        catch (Exception e) { Debug.LogWarning($"[UI_RelicPartDraftPopup] 고른 뒤 연출 예외(건너뜀): {e.Message}"); }
        _interactionTcs?.TrySetResult();
        if (this != null) ClosePopupUI();
    }

    private async UniTask PlayChosenAsync(int sel, System.Threading.CancellationToken ct)
    {
        if (sel < 0 || sel >= _cards.Count || _cards[sel].Rt == null) return;
        var chosen = _cards[sel];
        var mid    = new Vector2(0.5f, 0.5f);
        if (_redrawBtn != null) _redrawBtn.SetActive(false);
        CancelSelectionMotion();   // 고르기 연출(확대 · 흐려짐)이 아래 연출을 덮어쓰지 않게
        var othersFrom = new float[_cards.Count];   // 나머지는 이미 0.55로 물러나 있다 — 거기서부터 사라진다
        for (int i = 0; i < _cards.Count; i++) othersFrom[i] = _cards[i].Group != null ? _cards[i].Group.alpha : 1f;

        var flash = ShopUIStyle.MakeImage(chosen.Rt, "ChosenFlash", WithAlpha(chosen.Tone, 0f));
        flash.sprite = UI_RuneSelectPopup.SoftDot;
        ShopUIStyle.Anchor(flash.rectTransform, mid, mid, mid, Vector2.zero, new Vector2(_cardW * 1.25f, CardH * 1.12f));

        // ① 번쩍 · 맥동 — 나머지는 가라앉으며 흐려진다
        float t = 0f;
        while (t < ChosenPulseSec)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / ChosenPulseSec), s = Mathf.Sin(k * Mathf.PI);
            chosen.Rt.localScale = Vector3.one * (SelectedCardScale + ChosenPulseScale * s);
            flash.color = WithAlpha(chosen.Tone, 0.6f * s);
            for (int i = 0; i < _cards.Count; i++)
            {
                if (i == sel || _cards[i].Rt == null) continue;
                _cards[i].Group.alpha = othersFrom[i] * (1f - k);
                _cards[i].Rt.anchoredPosition = _cards[i].BasePos + new Vector2(0f, OthersSinkY * (1f - (1f - k) * (1f - k)));
            }
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }

        // ② 빛으로 줄어 플레이어 쪽으로 날아간다
        Vector2 from = chosen.Rt.anchoredPosition, to = FlyTarget(chosen.Rt);
        flash.color = WithAlpha(chosen.Tone, 0.7f);
        t = 0f;
        while (t < ChosenFlySec)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / ChosenFlySec), e = k * k;
            chosen.Rt.anchoredPosition = Vector2.Lerp(from, to, e) + new Vector2(0f, Mathf.Sin(k * Mathf.PI) * ChosenArc);
            chosen.Rt.localScale       = Vector3.one * Mathf.Lerp(SelectedCardScale, ChosenEndScale, e);
            chosen.Group.alpha         = 1f - Mathf.Clamp01((k - 0.75f) / 0.25f);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }

    /// <summary>카드가 날아갈 자리 — 플레이어 가슴의 화면 위치(카드 레이어 기준). 못 구하면 창 아래 가운데.</summary>
    private Vector2 FlyTarget(RectTransform card)
    {
        var fallback = new Vector2(0f, -WindowH * 0.5f);
        var layer    = card.parent as RectTransform;
        var player   = GameRunBootstrapper.Instance?.Run?.Player;
        var cam      = Camera.main;
        if (layer == null || player == null || cam == null) return fallback;
        Vector3 sp = cam.WorldToScreenPoint(player.transform.position + Vector3.up * 1.2f);
        if (sp.z <= 0f) return fallback;
        var canvas = layer.GetComponentInParent<Canvas>();
        var uiCam  = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, sp, uiCam, out var local) ? local : fallback;
    }

    private static Color WithAlpha(Color c, float a) { c.a = a; return c; }

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
        if (window != null && _skinned) ShopUIStyle.Skin(window.GetComponent<Image>(), skin?.background);

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
        RelicPartKind.Core     => "■",   // ★는 폰트 밖(□로 깨짐) — 화이트리스트 기호로(10-13 4b 요청 9)
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
