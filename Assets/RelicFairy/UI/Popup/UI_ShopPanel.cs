using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 상점 — 심연의 행상 (Canvas_Popup, Addressable "UI/Popup/UI_ShopPanel").
///
/// 10-21 흑요석 새 레이아웃(사용자 「상점의 새로운 레이아웃부터 필요한 리소스를 제작해서 만들고 빌드」 ·
/// 레이아웃 판 https://claude.ai/artifact/N69kJVyQsF1T2U8AoUkRbt 「레이아웃은 괜찮다」):
///  - 머리: 행상 초상(힉스필드) · 이름 · 인사 / 골드 · 상인의 인장 · 돌발 이벤트
///  - 「내 상태」 띠: 체력 · 포션 · 룬 보관함 · 룬판 — 사기 전에 무엇이 가득인지 본다(보스 정보는 넣지 않는다 — 10-21 사용자 반론)
///  - 가호 2칸(넓은 카드 — 문장 판 + 흰 아이콘) · 진열 5칸(룬 2 · 재료 1 · 포션 1 + 오늘의 특가)
///  - 카드마다 자물쇠 1칸(다음 상점까지 같은 값) · 오른쪽 상세 + [구매]
///  - 아래 줄: 보유 가호 · 잠금 수 · 새로고침(지금 값 · 다음 값) · 나가기
///
/// 창은 목업 px(<see cref="WinW"/>×<see cref="WinH"/>, 1920×1080 화면 기준)로 짓고 <see cref="UIWindowFitter"/>가 통째로 배율을 건다(scaleTransform).
/// 프리팹은 루트만 — 런타임에 짓는다(옛 구운 나무 · 양피지 계층이 남아 있으면 지우고 짓는다).
/// 창 · 카드 틀은 10-19 룬 획득 팝업과 같은 흑요석 가족(상점 창 틀은 호박색 등불 상감 · 카드는 등급 카드 틀).
/// 데이터/구매는 <see cref="ShopRoomController"/>가 권위(Products · SpecialDeal · TryBuy* · TryReroll · ToggleLock).
/// </summary>
public sealed class UI_ShopPanel : UI_Popup
{
    public override bool BlocksGameplay => true;
    public override bool CloseOnEscape  => true;

    // ── 배치(목업 px) ──
    private const float WinW = 1580f, WinH = 992f;
    private const float FrameCorner    = 36f;    // 창 틀 9칸 모서리(그림 72 → 36)
    private const float CardFrameScale = 3.4f;   // 등급 카드 틀 — 그림 px ÷ 이 값(룬 획득 팝업과 같은 두께)
    private const float L = 52f, R = 1528f, InnerW = R - L;
    private const float BandY = 160f, BandH = 58f;
    private const float ExchangeW = 560f;   // 「내 상태」 띠 오른쪽 원석 환전소 몫(10-22)
    private const float BodyY = 234f;
    private const float LeftW = 1008f;
    private const float WideY = 266f, WideW = 494f, WideH = 200f, WideGap = 20f;
    private const float SmallY = 520f, SmallW = 186f, SmallH = 346f, SmallStep = 205.5f;
    private const float DetailX = 1084f, DetailW = R - DetailX, DetailH = 632f;
    private const float FootY = 890f, FootH = 56f;
    private const float CardLift = 6f;
    private const int   SpecialIndex = -2;
    private const int   OwnedChipMax = 5;
    private const float VeilAlpha    = 0.95f;   // 공용 막 0.8은 선형 색공간에서 체감 절반 — 판 밖 HUD가 비쳤다(09-28)

    // ── 연출 ──
    private const float WindowInSec = 0.26f, WindowInScale = 0.965f;
    private const float DealStagger = 0.06f, DealDur = 0.26f, DealRise = 22f;
    private const float CoinFallDur = 0.55f, ItemFlyDur = 0.62f, SoldPopDur = 0.38f, DealHoldDur = 0.75f;
    private const int   CoinCount   = 5;
    private const int   ReactionMs  = 2600;

    // 행상 = 웃는 버섯(10-01 NPC 교체) — 밝은 반말 장사꾼.
    private const string Greeting = "어서 와! 오늘도 살아 있네?";
    private const string BuyLabel = "구매";   // 10-18 사용자 「직관적인 표현으로」 — 하는 일 그대로

    // ── 색(톤 v2 — 금색은 「누를 것 · 고른 것」에만) ──
    private static readonly Color Ink       = UITheme.Thin.Ink;
    private static readonly Color Mute      = UITheme.Thin.InkA(0.64f);
    private static readonly Color Faint     = UITheme.Thin.InkA(0.42f);
    private static readonly Color Gold      = UITheme.Thin.Gold;
    private static readonly Color Amber     = new(0.91f, 0.73f, 0.33f, 1f);   // 등불 호박색 — 띠 이름표 · 할인 줄
    private static readonly Color Line      = UITheme.Thin.LineIdle;
    private static readonly Color LineGold  = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.40f);
    private static readonly Color SelEdge   = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.85f);
    private static readonly Color PanelFill = new(0.040f, 0.048f, 0.082f, 0.72f);
    private static readonly Color WellFill  = new(0.024f, 0.028f, 0.050f, 0.78f);
    private static readonly Color CardFill  = new(0.060f, 0.066f, 0.105f, 0.96f);
    private static readonly Color BuyFill   = new(0.910f, 0.730f, 0.330f, 0.14f);
    private static readonly Color BtnFill   = new(0.040f, 0.045f, 0.075f, 0.80f);
    private static readonly Color Danger    = UITheme.Thin.Danger;
    private static readonly Color Fit       = new(0.50f, 0.80f, 0.56f, 1f);
    private static readonly Color Parchment = new(0.851f, 0.769f, 0.604f, 1f);   // 특가 딱지 — 양피지는 이 한 곳만
    private static readonly Color ParchInk  = new(0.227f, 0.165f, 0.094f, 1f);
    private static readonly List<(string name, int count)> s_owned = new();   // 보유 가호(이름별 묶음) — 새로 그릴 때마다 다시 채운다

    private sealed class CardView
    {
        public int Index;                   // 진열 칸 번호 · 특가 = SpecialIndex
        public bool Wide;
        public RectTransform Root;
        public Image Frame, Plate, Icon, Coin, LockBg, LockIcon, Edge;
        public CanvasGroup Group, Content;
        public TMP_Text Tag, Name, Effect, Note, Price, OldPrice, Sold;
        public Button LockBtn;
        public Vector2 Base;
    }

    private ShopRoomController _controller;
    private ShopSkinSO _skin;
    private RectTransform _win, _fx;
    private Image _veil;
    private TMP_Text _goldText, _dialogText, _subLine;
    private TMP_Text _hpText, _potionText, _storageText, _boardText;
    private TMP_Text _oreText, _exStepLabel, _exAllLabel;   // 원석 환전소(10-22)
    private Button   _exStepBtn, _exAllBtn;
    private readonly List<CardView> _cards = new();
    private CardView _deal;
    private ShopProduct _dealShown;       // 특가 품절 연출 동안 좌판에 붙잡아 둘 물건(컨트롤러는 사는 즉시 비운다)

    // 고른 물건
    private Image _selPlate, _selIcon;
    private RectTransform _selShape;
    private TMP_Text _selName, _selCat, _selEffect, _selFlavor, _selPrice;
    private Button _buyBtn;
    private TMP_Text _buyLabel;

    // 아래 줄
    private readonly List<TMP_Text> _ownedChips = new();
    private TMP_Text _ownedNone, _lockText;
    private Button _rerollBtn;
    private TMP_Text _rerollLabel, _rerollNext;

    private int  _selectedIndex = -1;    // -1 = 미선택, -2 = 특가
    private bool _built, _closing, _buying, _dealing, _dealHold;
    private int  _reactionGen;
    private CancellationTokenSource _openFxCts;
    private Image[] _coins;
    private Image   _flyIcon;

    // ── Properties ──
    private string GreetingNow => _controller != null && !string.IsNullOrEmpty(_controller.EventGreeting) ? _controller.EventGreeting : Greeting;
    private Sprite CoinSprite  => _skin != null ? _skin.goldCoin : null;

    // ── Lifecycle ──

    public override void Init()
    {
        base.Init();
        _skin = UISkin.Shop;
        BuildChrome();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        _openFxCts?.Cancel();
        _openFxCts?.Dispose();
        if (_controller != null)
        {
            _controller.OnShopChanged -= RefreshAll;
            _controller.NotifyPanelClosed();
        }
    }

    // ── Public Methods ──

    public void Bind(ShopRoomController controller)
    {
        _controller = controller;
        if (_controller != null) _controller.OnShopChanged += RefreshAll;

        ShopUIStyle.PlaySfx("shop_open");
        // 열면 오늘의 특가를 상세 칸에 먼저(10-19) · 팔렸으면 첫 가호
        var deal = _controller?.SpecialDeal;
        _selectedIndex = deal != null && !deal.Sold ? SpecialIndex : (_controller != null && _controller.Products.Count > 0 ? 0 : -1);
        if (_dialogText != null) _dialogText.text = GreetingNow;
        RefreshAll();
        PlayWindowInAsync().Forget();
        PlayOpenFxAsync().Forget();
    }

    public override void ClosePopupUI()
    {
        if (_closing) return;
        _closing = true;
        ShopUIStyle.PlaySfx("shop_close");
        base.ClosePopupUI();
    }

    // ── 짓기 ──

    private void BuildChrome()
    {
        if (_built) return;
        _built = true;
        // 옛 구운 계층(나무 · 양피지 · 10-20까지)이 남아 있으면 지운다 — 새 레이아웃은 늘 여기서 짓는다.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var old = transform.GetChild(i).gameObject;
            old.SetActive(false);
            Destroy(old);
        }

        ShopUIStyle.Stretch((RectTransform)transform);
        _veil = ShopUIStyle.MakeImage(transform, "Veil", new Color(0f, 0f, 0f, VeilAlpha), raycast: true);
        ShopUIStyle.Stretch(_veil.rectTransform);

        var win = ShopUIStyle.MakeImage(transform, "Window", UITheme.Window, raycast: true);
        _win = win.rectTransform;
        var half = new Vector2(0.5f, 0.5f);
        ShopUIStyle.Anchor(_win, half, half, half, Vector2.zero, new Vector2(WinW, WinH));   // 맞춤이 Awake에서 이 크기를 기준으로 잡는다 — 먼저
        _win.gameObject.AddComponent<UIWindowFitter>().Configure(maxScale: UIWindowFitter.ContentScreen, scaleTransform: true);

        var frame = _skin != null ? _skin.WindowFrame : null;
        if (frame != null)
        {
            win.sprite = frame;
            win.type   = Image.Type.Sliced;
            win.color  = Color.white;
            win.pixelsPerUnitMultiplier = frame.border.x > 0f ? frame.border.x / FrameCorner : 1f;
        }
        else UITheme.StylePanel(win, UITheme.Window, LineGold, 6f);

        BuildHeader();
        BuildBand();
        BuildCards();
        BuildDetail();
        BuildFooter();

        // 연출 층은 맨 위 — 산 물건과 동전이 카드 위로 지나간다.
        _fx = (RectTransform)ShopUIStyle.MakeRect(_win, "PurchaseFx").transform;
        ShopUIStyle.Stretch(_fx);
    }

    private void BuildHeader()
    {
        var portrait = _skin != null ? _skin.PeddlerPortrait : null;
        var p = Img(_win, "Portrait", 54f, 24f, 82f, 123f, Color.white, portrait);
        p.gameObject.SetActive(portrait != null);
        float tx = portrait != null ? 154f : 64f;

        var name = Text(_win, "Name", tx, 50f, 520f, 38f, 27f, UIWeight.Medium, Ink);
        name.text = "심연의 행상";
        _dialogText = Text(_win, "Dialog", tx, 92f, 760f, 28f, 17f, UIWeight.Regular, Mute);
        _dialogText.text = Greeting;

        var gold = Row(_win, "GoldRow", R - 360f, 48f, 360f, 40f, TextAnchor.MiddleRight, 10f);
        RowIcon(gold, "Coin", CoinSprite, 26f, Color.white);
        _goldText = RowText(gold, "Gold", 27f, UIWeight.Medium, Gold);

        _subLine = Text(_win, "SubLine", R - 620f, 94f, 620f, 26f, 15f, UIWeight.Medium, Amber, TextAlignmentOptions.MidlineRight);
        Img(_win, "HeadLine", L, 150f, InnerW, 1f, LineGold);
    }

    private void BuildBand()
    {
        Panel(_win, "Band", L, BandY, InnerW, BandH, PanelFill, Line);
        var row = Row(_win, "BandRow", L, BandY, InnerW, BandH, TextAnchor.MiddleLeft, 0f, new RectOffset(26, 26, 0, 0));
        var label = RowText(row, "Label", 14f, UIWeight.Medium, Amber);
        label.text = "내 상태";
        label.characterSpacing = 6f;
        Spacer(row, 40f);
        _hpText      = BandItem(row, "Hp",      _skin != null ? _skin.HpIcon : null,      "체력");
        Spacer(row, 40f);
        _potionText  = BandItem(row, "Potion",  _skin != null ? _skin.PotionIcon : null,  "포션");
        Spacer(row, 40f);
        _storageText = BandItem(row, "Storage", _skin != null ? _skin.StorageIcon : null, "룬 보관함");
        Spacer(row, 40f);
        _boardText   = BandItem(row, "Board",   _skin != null ? _skin.BoardIcon : null,   "룬판");

        // 원석 환전소(10-22 사용자 「룬을 다 맞추거나 맞추기 싫을 때 — 원석을 골드로」) — 띠 오른쪽: 저울 문장 · 환율 · 가진 원석 · [10개] · [전부]
        var ex = Row(_win, "Exchange", R - ExchangeW, BandY, ExchangeW, BandH, TextAnchor.MiddleRight, 10f, new RectOffset(0, 8, 0, 0));
        RowIcon(ex, "Emblem", _skin != null ? _skin.ExchangeEmblem : null, 46f, Color.white);
        var rate = RowText(ex, "Rate", 15f, UIWeight.Regular, Mute);
        rate.text = $"원석 환전  1 → {ShopRoomController.OreToGold}";
        Spacer(ex, 4f);
        RowIcon(ex, "OreIcon", _skin != null ? _skin.OreIcon : null, 22f, Color.white);
        _oreText = RowText(ex, "Ore", 20f, UIWeight.Medium, Ink);
        Spacer(ex, 4f);
        _exStepBtn = FootButton(ex, "ExStep", 124f, out var es);
        _exStepLabel = RowText(es, "Label", 16f, UIWeight.Medium, Ink);
        RowIcon(es, "Coin", CoinSprite, 18f, Color.white);
        _exStepBtn.onClick.AddListener(() => OnExchange(ShopRoomController.ExchangeStep));
        _exAllBtn = FootButton(ex, "ExAll", 132f, out var ea);
        _exAllLabel = RowText(ea, "Label", 16f, UIWeight.Medium, Ink);
        RowIcon(ea, "Coin", CoinSprite, 18f, Color.white);
        _exAllBtn.onClick.AddListener(() => OnExchange(_controller != null ? _controller.Ore : 0));
    }

    private void BuildCards()
    {
        var h1 = Text(_win, "BlessHead", L, BodyY, 80f, 26f, 18f, UIWeight.Medium, Ink);
        h1.text = "가호";
        var n1 = Text(_win, "BlessNote", L + 52f, BodyY + 1f, 500f, 24f, 15f, UIWeight.Regular, Mute);
        n1.text = "런이 끝날 때까지 유지 · 버프 칸";
        var h2 = Text(_win, "ShelfHead", L, SmallY - 32f, 80f, 26f, 18f, UIWeight.Medium, Ink);
        h2.text = "진열";
        var n2 = Text(_win, "ShelfNote", L + 52f, SmallY - 31f, 420f, 24f, 15f, UIWeight.Regular, Mute);
        n2.text = "룬 2 · 재료 1 · 포션 1 + 오늘의 특가";
        // 오른쪽 끝은 특가 카드 앞까지 — 특가 딱지(카드 위로 14 솟음)와 겹쳤다(10-21 1차 촬영)
        var n3 = Text(_win, "LockNote", L + 4 * SmallStep - 20f - 460f, SmallY - 31f, 460f, 24f, 14f, UIWeight.Regular, Faint, TextAlignmentOptions.MidlineRight);
        n3.text = "자물쇠 — 다음 상점까지 같은 값으로 남긴다";

        for (int i = 0; i < AbyssPeddlerCatalog.ProductCount; i++)
        {
            bool wide = i < 2;
            _cards.Add(wide ? BuildWideCard(i, L + i * (WideW + WideGap), WideY)
                            : BuildSmallCard(i, L + (i - 2) * SmallStep, SmallY));
        }
        _deal = BuildSmallCard(SpecialIndex, L + 4 * SmallStep, SmallY);
    }

    private CardView BuildWideCard(int index, float x, float y)
    {
        var c = NewCard(index, x, y, WideW, WideH, wide: true);
        var t = c.Content.transform;
        var emblem = _skin != null ? _skin.BlessingEmblem : null;
        c.Plate = Img(t, "Plate", 24f, 32f, 136f, 136f, emblem != null ? Color.white : WellFill, emblem);
        if (emblem == null) { c.Plate.sprite = UIProceduralSprites.Circle(); c.Plate.preserveAspect = true; }
        c.Icon = CenterIcon(c.Plate.transform, 64f);
        c.Tag    = Text(t, "Tag",    180f, 24f, 240f, 22f, 14f, UIWeight.Medium, Mute);
        c.Name   = Text(t, "Name",   180f, 50f, 290f, 34f, 23f, UIWeight.Medium, Ink);
        c.Effect = Text(t, "Effect", 180f, 90f, 290f, 28f, 19f, UIWeight.Regular, Ink);
        c.Note   = Text(t, "Note",   180f, 122f, 290f, 22f, 14f, UIWeight.Regular, Faint);
        BuildPrice(c, WideW - 24f, 150f);
        BuildLock(c, WideW - 58f, 18f);
        BuildOverlay(c, WideW, WideH);
        return c;
    }

    private CardView BuildSmallCard(int index, float x, float y)
    {
        var c = NewCard(index, x, y, SmallW, SmallH, wide: false);
        var t = c.Content.transform;
        bool deal = index == SpecialIndex;
        c.Tag = Text(t, "Tag", 18f, deal ? 30f : 22f, SmallW - 36f - (deal ? 0f : 40f), 22f, 14f, UIWeight.Medium, Mute);
        c.Tag.enableAutoSizing = true;   // 「◇ Legendary」만 줄어든다
        c.Tag.fontSizeMin = 11f;
        c.Tag.fontSizeMax = 14f;
        c.Plate = Img(t, "Well", 16f, 56f, SmallW - 32f, 124f, WellFill);
        c.Plate.sprite = UIProceduralSprites.RoundedRect(radius: 4f, feather: 1f);
        c.Plate.type   = Image.Type.Sliced;
        c.Icon = CenterIcon(c.Plate.transform, 92f);
        c.Name = Text(t, "Name", 18f, 190f, SmallW - 36f, 50f, 17f, UIWeight.Medium, Ink, TextAlignmentOptions.TopLeft);
        c.Name.textWrappingMode = TextWrappingModes.Normal;
        c.Effect = Text(t, "Effect", 18f, 244f, SmallW - 36f, 44f, 14f, UIWeight.Regular, Mute, TextAlignmentOptions.TopLeft);
        c.Effect.textWrappingMode = TextWrappingModes.Normal;
        BuildPrice(c, SmallW - 18f, SmallH - 50f);
        if (deal)
        {
            // 오늘의 특가 딱지 — 양피지는 이 한 곳만(10-20 개편안 · 결 = 흑요석)
            var tab = Img(c.Root, "DealTab", -10f, -14f, 150f, 30f, Parchment);
            tab.sprite = UIProceduralSprites.RoundedRect(radius: 3f, feather: 1f);
            tab.type   = Image.Type.Sliced;
            var tt = Text(tab.transform, "Label", 0f, 0f, 150f, 30f, 14f, UIWeight.Medium, ParchInk, TextAlignmentOptions.Center);
            tt.text = "오늘의 특가 −50%";
        }
        else BuildLock(c, SmallW - 54f, 16f);
        BuildOverlay(c, SmallW, SmallH);
        if (deal) c.Root.Find("DealTab")?.SetAsLastSibling();   // 고른 금 테가 딱지 글자를 지나갔다(10-21 2차 촬영) — 딱지를 맨 위로
        return c;
    }

    private CardView NewCard(int index, float x, float y, float w, float h, bool wide)
    {
        var frame = Img(_win, index == SpecialIndex ? "Deal" : $"Card{index}", x, y, w, h, CardFill, null, raycast: true);
        var c = new CardView { Index = index, Wide = wide, Root = frame.rectTransform, Frame = frame, Base = frame.rectTransform.anchoredPosition };
        c.Group = frame.gameObject.AddComponent<CanvasGroup>();
        var content = ShopUIStyle.MakeRect(frame.transform, "Content", typeof(CanvasGroup));
        ShopUIStyle.Stretch((RectTransform)content.transform);
        c.Content = content.GetComponent<CanvasGroup>();
        c.Content.blocksRaycasts = false;
        var btn = frame.gameObject.AddComponent<Button>();
        ShopUIStyle.ApplyButtonColors(btn, frame);
        int captured = index;
        btn.onClick.AddListener(() => Select(captured));
        return c;
    }

    private void BuildPrice(CardView c, float right, float y)
    {
        var row = Row(c.Content.transform, "PriceRow", right - 220f, y, 220f, 34f, TextAnchor.MiddleRight, 8f);
        c.OldPrice = RowText(row, "Old", 15f, UIWeight.Regular, Faint);
        c.OldPrice.fontStyle = FontStyles.Strikethrough;
        c.Coin  = RowIcon(row, "Coin", CoinSprite, 20f, Color.white);
        c.Price = RowText(row, "Price", 21f, UIWeight.Medium, Ink);
    }

    private void BuildLock(CardView c, float x, float y)
    {
        var bg = Img(c.Root, "Lock", x, y, 38f, 38f, BtnFill, null, raycast: true);
        bg.sprite = UIProceduralSprites.RoundedRect(radius: 4f, feather: 1f);
        bg.type   = Image.Type.Sliced;
        var edge = ShopUIStyle.MakeImage(bg.transform, "Edge", Line);
        edge.sprite = UIProceduralSprites.RoundedOutline(radius: 4f, stroke: 1f);
        edge.type   = Image.Type.Sliced;
        ShopUIStyle.Stretch(edge.rectTransform);
        c.LockBg   = bg;
        c.LockIcon = Img(bg.transform, "Icon", 9f, 9f, 20f, 20f, Mute, _skin != null ? _skin.LockOpen : null);
        c.LockBtn  = bg.gameObject.AddComponent<Button>();
        ShopUIStyle.ApplyButtonColors(c.LockBtn, bg);
        int captured = c.Index;
        c.LockBtn.onClick.AddListener(() => OnLockClicked(captured));
    }

    /// <summary>품절 글자 · 고른 금 테 — 내용 위, 자물쇠 아래(자물쇠는 품절이면 끈다).</summary>
    private static void BuildOverlay(CardView c, float w, float h)
    {
        c.Sold = Text(c.Root, "Sold", 0f, 0f, w, h, 28f, UIWeight.Medium, Ink, TextAlignmentOptions.Center);
        c.Sold.text = "품절";
        c.Sold.gameObject.SetActive(false);
        c.Edge = ShopUIStyle.MakeImage(c.Root, "SelEdge", SelEdge);
        c.Edge.sprite = UIProceduralSprites.RoundedOutline(radius: 6f, stroke: 2f);
        c.Edge.type   = Image.Type.Sliced;
        ShopUIStyle.Stretch(c.Edge.rectTransform, -5f);
        c.Edge.gameObject.SetActive(false);
        if (c.LockBg != null) c.LockBg.transform.SetAsLastSibling();
    }

    private void BuildDetail()
    {
        var p = Panel(_win, "Selected", DetailX, BodyY, DetailW, DetailH, PanelFill, Line);
        var t = p.transform;
        _selPlate = Img(t, "Plate", 24f, 24f, 112f, 112f, WellFill);
        _selIcon  = CenterIcon(_selPlate.transform, 72f);
        _selName  = Text(t, "Name", 152f, 30f, DetailW - 176f, 36f, 25f, UIWeight.Medium, Ink);
        _selCat   = Text(t, "Cat",  152f, 70f, DetailW - 176f, 24f, 15f, UIWeight.Medium, Mute);
        _selShape = Box(t, "Shape", 152f, 98f, DetailW - 176f, 42f);
        Img(t, "Line", 24f, 152f, DetailW - 48f, 1f, Line);

        _selEffect = Text(t, "Detail", 24f, 168f, DetailW - 48f, 292f, 17f, UIWeight.Regular, Ink, TextAlignmentOptions.TopLeft);
        _selEffect.textWrappingMode = TextWrappingModes.Normal;
        _selEffect.richText         = true;
        _selEffect.lineSpacing      = 6f;

        _selFlavor = Text(t, "Flavor", 24f, 466f, DetailW - 48f, 26f, 15f, UIWeight.Regular, Mute, TextAlignmentOptions.Center);
        var pl = Text(t, "PriceLabel", 24f, 502f, 120f, 38f, 15f, UIWeight.Regular, Mute);
        pl.text = "값";
        var row = Row(t, "PriceRow", DetailW - 24f - 260f, 502f, 260f, 38f, TextAnchor.MiddleRight, 10f);
        RowIcon(row, "Coin", CoinSprite, 26f, Color.white);
        _selPrice = RowText(row, "Price", 29f, UIWeight.Medium, Ink);

        var buy = Img(t, "Buy", 24f, 552f, DetailW - 48f, 58f, BuyFill, null, raycast: true);
        buy.sprite = UIProceduralSprites.RoundedRect(radius: 4f, feather: 1f);
        buy.type   = Image.Type.Sliced;
        var edge = ShopUIStyle.MakeImage(buy.transform, "Edge", SelEdge);
        edge.sprite = UIProceduralSprites.RoundedOutline(radius: 4f, stroke: 1f);
        edge.type   = Image.Type.Sliced;
        ShopUIStyle.Stretch(edge.rectTransform);
        _buyBtn = buy.gameObject.AddComponent<Button>();
        ShopUIStyle.ApplyButtonColors(_buyBtn, buy);
        buy.gameObject.AddComponent<UIButtonFeedback>();   // 런타임에 지어 UI_Popup.Init이 붙이지 못한다
        _buyBtn.onClick.AddListener(OnBuyClicked);
        _buyLabel = Text(buy.transform, "Label", 0f, 0f, DetailW - 48f, 58f, 22f, UIWeight.Medium, Gold, TextAlignmentOptions.Center);
        _buyLabel.text = BuyLabel;
        _buyLabel.characterSpacing = 8f;
    }

    private void BuildFooter()
    {
        Img(_win, "FootLine", L, FootY - 8f, InnerW, 1f, LineGold);
        var owned = Row(_win, "Owned", L, FootY, 820f, FootH, TextAnchor.MiddleLeft, 10f);
        var lbl = RowText(owned, "Label", 15f, UIWeight.Regular, Mute);
        lbl.text = "보유 가호";
        Spacer(owned, 4f);
        _ownedNone = RowText(owned, "None", 15f, UIWeight.Regular, Faint);
        _ownedNone.text = "없음";
        for (int i = 0; i < OwnedChipMax + 1; i++)   // 마지막 하나는 「+n」
        {
            var chip = ShopUIStyle.MakeImage(owned, $"Chip{i}", new Color(1f, 1f, 1f, 0f));
            chip.sprite = UIProceduralSprites.RoundedOutline(radius: 4f, stroke: 1f);
            chip.type   = Image.Type.Sliced;
            chip.color  = LineGold;
            var hlg = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(12, 12, 6, 6);
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;
            var ct = RowText(chip.transform, "Label", 15f, UIWeight.Regular, Ink);
            _ownedChips.Add(ct);
            chip.gameObject.SetActive(false);
        }

        var right = Row(_win, "Actions", R - 640f, FootY, 640f, FootH, TextAnchor.MiddleRight, 12f);
        RowIcon(right, "LockIcon", _skin != null ? _skin.LockClosed : null, 18f, Mute);
        _lockText = RowText(right, "LockText", 15f, UIWeight.Regular, Mute);
        Spacer(right, 14f);

        _rerollBtn = FootButton(right, "Reroll", 250f, out var rr);
        RowIcon(rr, "Icon", _skin != null ? _skin.RerollIcon : null, 18f, Ink);
        _rerollLabel = RowText(rr, "Label", 17f, UIWeight.Medium, Ink);
        _rerollNext  = RowText(rr, "Next",  14f, UIWeight.Regular, Mute);
        _rerollBtn.onClick.AddListener(OnRerollClicked);

        var exit = FootButton(right, "Exit", 128f, out var er);
        var el = RowText(er, "Label", 17f, UIWeight.Medium, Ink);
        el.text = "나가기";
        exit.onClick.AddListener(ClosePopupUI);
    }

    // ── 새로 그리기 ──

    private void Select(int index)
    {
        if (_selectedIndex != index) ShopUIStyle.PlaySfx("shop_select");
        _selectedIndex = index;
        RefreshCards();
        RefreshSelected();
    }

    private void RefreshAll()
    {
        if (_controller == null) return;
        CurrencyCounter.Apply(_goldText, _controller.PlayerGold);

        var sub = new StringBuilder();
        if (AbyssPeddlerCatalog.MerchantSigil.Active)
            sub.Append("◆ 상인의 인장  −").Append((AbyssPeddlerCatalog.MerchantSigil.Discount * 100f).ToString("F0")).Append('%');
        if (_controller.ActiveEvent != ShopEvent.None)
        {
            if (sub.Length > 0) sub.Append("    ");
            sub.Append("◆ ").Append(_controller.EventTitle).Append("  ").Append(_controller.EventLine);
        }
        if (_subLine != null) _subLine.text = sub.ToString();

        RefreshBand();
        RefreshCards();
        RefreshSelected();
        RefreshFooter();
    }

    private void RefreshBand()
    {
        var run = GameRunBootstrapper.Instance != null ? GameRunBootstrapper.Instance.Run : null;
        var st  = run?.PlayerState;
        if (_hpText != null)     _hpText.text     = st != null ? $"{st.Hp} / {st.MaxHp}" : "-";
        if (_potionText != null)
        {
            bool full = st != null && st.PotionCount >= st.PotionCapacity;
            _potionText.text  = st != null ? $"{st.PotionCount} / {st.PotionCapacity}" : "-";
            _potionText.color = full ? Amber : Ink;
        }
        var inv = run?.ItemInventory;
        if (_storageText != null)
        {
            _storageText.text  = inv != null ? $"{inv.StagingCount} / {RunItemInventory.StagingCapacity}" : "-";
            _storageText.color = inv != null && inv.IsStagingFull ? Amber : Ink;
        }
        if (_boardText != null)
        {
            int used = RuneBlockBudget.Used;
            _boardText.text  = $"{used} / {RuneBlockBudget.Max}";
            _boardText.color = used >= RuneBlockBudget.Max ? Amber : Ink;
        }
        // 원석 환전소 — 가진 원석 · 버튼마다 받을 골드
        int ore = _controller != null ? _controller.Ore : 0;
        if (_oreText != null) _oreText.text = ore.ToString("N0");
        if (_exStepLabel != null) _exStepLabel.text = $"{ShopRoomController.ExchangeStep}개 → {ShopRoomController.ExchangeStep * ShopRoomController.OreToGold}";
        if (_exAllLabel != null)  _exAllLabel.text  = $"전부 → {(ore * ShopRoomController.OreToGold):N0}";
        if (_exStepBtn != null) _exStepBtn.interactable = ore >= ShopRoomController.ExchangeStep;
        if (_exAllBtn != null)  _exAllBtn.interactable  = ore > 0;
    }

    private void RefreshCards()
    {
        var products = _controller?.Products;
        int gold = _controller?.PlayerGold ?? 0;
        for (int i = 0; i < _cards.Count; i++)
        {
            var c = _cards[i];
            bool has = products != null && i < products.Count && products[i] != null;
            c.Root.gameObject.SetActive(has);
            if (has) FillCard(c, products[i], gold, _selectedIndex == i);
        }
        var deal = _controller?.SpecialDeal;
        if (deal != null) _dealShown = deal;
        bool showDeal = deal != null || (_dealHold && _dealShown != null);
        _deal.Root.gameObject.SetActive(showDeal);
        if (showDeal) FillCard(_deal, deal ?? _dealShown, gold, _selectedIndex == SpecialIndex);
    }

    private void FillCard(CardView c, ShopProduct p, int gold, bool selected)
    {
        ApplyCardFrame(c, p.Rarity);
        bool bless = p.Category == ShopProductCategory.Buff;

        // 그림 — 가호는 문장 판 위 흰 아이콘(등급색), 나머지는 상품 그림
        Sprite icon = p.Icon;
        Color iconTint = Color.white;
        if (bless && c.Wide && TryBlessingStat(p, out var stat) && _skin != null && _skin.BlessingIcon(stat) != null)
        {
            icon = _skin.BlessingIcon(stat);
            iconTint = BlessingTint(p.Rarity);
        }
        c.Icon.sprite  = icon;
        c.Icon.color   = iconTint;
        c.Icon.enabled = icon != null;

        c.Name.text  = p.DisplayName;
        c.Tag.text   = TagText(p);
        c.Tag.color  = TagColor(p);
        c.Effect.text = p.Category == ShopProductCategory.Rune ? RuneLine(p.Rune) : UIKeywordInk.Values(p.EffectText);
        if (c.Note != null) c.Note.text = bless ? "런이 끝날 때까지 유지" : string.Empty;

        bool deal = c.Index == SpecialIndex;
        int orig = deal && _controller != null ? _controller.SpecialOriginalPrice : 0;
        c.OldPrice.text = orig > p.Price ? orig.ToString() : string.Empty;
        c.OldPrice.gameObject.SetActive(orig > p.Price && !p.Sold);
        c.Price.text  = p.Price.ToString();
        c.Price.color = p.Price <= gold ? Ink : Danger;

        // 품절 — 내용을 누르고 글자 하나
        c.Content.alpha = p.Sold ? 0.30f : 1f;
        c.Sold.gameObject.SetActive(p.Sold);

        // 잠금
        if (c.LockBtn != null)
        {
            bool locked = _controller != null && _controller.IsLocked(p);
            c.LockBtn.gameObject.SetActive(!p.Sold);
            c.LockBg.color   = locked ? UITheme.Thin.WithA(Gold, 0.22f) : BtnFill;
            c.LockIcon.color = locked ? Gold : Mute;
            var sp = _skin != null ? (locked ? _skin.LockClosed : _skin.LockOpen) : null;
            if (sp != null) c.LockIcon.sprite = sp;
            if (c.LockBg.transform.Find("Edge") is Transform e && e.TryGetComponent<Image>(out var ei)) ei.color = locked ? SelEdge : Line;
        }

        c.Edge.gameObject.SetActive(selected);
        if (!_dealing) c.Root.anchoredPosition = c.Base + (selected ? new Vector2(0f, CardLift) : Vector2.zero);
    }

    /// <summary>등급 카드 틀(10-19 룬 획득 팝업과 같은 그림) — 없으면 둥근 판 + 등급색 선.</summary>
    private static void ApplyCardFrame(CardView c, ItemRarity rarity)
    {
        var art = UISkin.RuneSelect != null ? UISkin.RuneSelect.RarityCardFrame(rarity) : null;
        if (art != null)
        {
            c.Frame.sprite = art;
            c.Frame.type   = Image.Type.Sliced;
            c.Frame.pixelsPerUnitMultiplier = CardFrameScale;
            c.Frame.color  = Color.white;
            return;
        }
        c.Frame.sprite = UIProceduralSprites.RoundedRect(radius: 6f, feather: 1f);
        c.Frame.type   = Image.Type.Sliced;
        c.Frame.color  = CardFill;
    }

    private void RefreshSelected()
    {
        var p = Selected();
        bool has = p != null;
        if (_buyBtn != null)
        {
            _buyBtn.interactable = has && p.Purchasable && !_controller.IsBlockedByFullPotions(p) && _controller.PlayerGold >= p.Price;
            UIAffordGlow.Set(_buyBtn, _buyBtn.interactable);
        }
        if (_buyLabel != null)
        {
            _buyLabel.text  = has && p.Sold ? "품절"
                            : has && _controller.IsBlockedByFullPotions(p) ? (_controller.PotionsFull ? "포션 가득" : "포션 칸 부족")
                            : BuyLabel;
            _buyLabel.color = _buyBtn != null && _buyBtn.interactable ? Gold : Mute;
        }
        _selPlate.gameObject.SetActive(has);
        if (_selShape != null) RuneCardKit.BuildShape(_selShape, null, 0f, 0f);   // 지운다(룬이면 아래에서 다시)
        if (!has)
        {
            _selName.text = _selCat.text = _selEffect.text = _selPrice.text = string.Empty;
            _selFlavor.text = "마음에 드는 걸 골라 봐.";
            return;
        }

        int gold = _controller.PlayerGold;
        bool bless = p.Category == ShopProductCategory.Buff;
        var emblem = _skin != null ? _skin.BlessingEmblem : null;
        if (bless && emblem != null && TryBlessingStat(p, out var stat) && _skin.BlessingIcon(stat) != null)
        {
            _selPlate.sprite = emblem; _selPlate.type = Image.Type.Simple; _selPlate.preserveAspect = true; _selPlate.color = Color.white;
            _selIcon.sprite = _skin.BlessingIcon(stat); _selIcon.color = BlessingTint(p.Rarity);
        }
        else
        {
            _selPlate.sprite = UIProceduralSprites.RoundedRect(radius: 4f, feather: 1f); _selPlate.type = Image.Type.Sliced; _selPlate.color = WellFill;
            _selIcon.sprite = p.Icon; _selIcon.color = Color.white;
        }
        _selIcon.enabled = _selIcon.sprite != null;

        _selName.text = p.DisplayName;
        _selPrice.text = p.Price.ToString();
        _selPrice.color = gold >= p.Price ? Ink : Danger;

        var sb = new StringBuilder();
        var rune = p.Rune;
        if (rune != null)
        {
            var rc = ShopUIStyle.Rarity(rune.rarity);
            var elem = ElementDef.GetById(rune.element);
            _selCat.text  = RuneCardKit.RarityChipText(rune) + (elem != null ? $" · {elem.Name}" : string.Empty);
            _selCat.color = rune.rarity == ItemRarity.Common ? Mute : rc;
            if (rune.effects != null)
                foreach (var slot in rune.effects)
                    if (!string.IsNullOrEmpty(slot.effectType))
                        sb.Append("▲ ").Append(UIKeywordInk.Values(EffectDescriptionFormatter.Describe(slot).Combined)).Append('\n');
            bool fit = RuneBlockBudget.Fits(RuneBlockBudget.Used) && RuneBlockBudget.FitsSame(rune);
            if (_selShape != null) RuneCardKit.BuildShape(_selShape, rune, 12f, 2f, dim: !fit);
            sb.Append(fit ? Hex(Fit) : Hex(Danger)).Append(RuneBlockBudget.SameLabel(rune) ?? RuneBlockBudget.Label()).Append("</color>")
              .Append("  ").Append(Hex(Mute)).Append("사면 판이 열린다</color>\n");
        }
        else
        {
            _selCat.text  = bless ? $"가호 · {TierName(p.Rarity)}" : p.CategoryLabel;
            _selCat.color = bless ? TagColor(p) : Mute;
            sb.Append("▲ ").Append(UIKeywordInk.Values(p.EffectText)).Append('\n');
            if (!string.IsNullOrEmpty(p.DetailText)) sb.Append(Hex(Mute)).Append(p.DetailText).Append("</color>\n");
        }

        if (_selectedIndex == SpecialIndex && _controller.SpecialOriginalPrice > p.Price)
            sb.Append(Hex(Amber)).Append("오늘의 특가 — 원래 값 ").Append(_controller.SpecialOriginalPrice).Append("</color>\n");
        if (_controller.IsLocked(p))
            sb.Append(Hex(Gold)).Append("잠금 — 다음 상점까지 이 값으로 남는다</color>\n");

        if (gold < p.Price) sb.Append(Hex(Danger)).Append(p.Price - gold).Append(" 골드 모자람</color>");
        else                sb.Append(Hex(Mute)).Append("사면 잔액 ").Append(gold - p.Price).Append("</color>");
        var deal = _controller.SpecialDeal;
        if (deal != null && deal != p && gold >= p.Price && gold - p.Price < deal.Price)
            sb.Append('\n').Append(Hex(Mute)).Append("사면 특가 ").Append(deal.DisplayName).Append("(").Append(deal.Price).Append(")는 못 산다</color>");

        _selEffect.text = sb.ToString();
        _selFlavor.text = gold < p.Price ? "골드가 모자라는군." : bless ? "몸에 스미는 힘이야. 런 끝까지 가." : "힘을 살 텐가, 특가를 챙길 텐가.";
    }

    private void RefreshFooter()
    {
        // 보유 가호 — 버프 칸 중 상점 가호(상점 표에 있는 id)만, 이름별로 묶는다
        s_owned.Clear();
        var buffs = GameRunBootstrapper.Instance != null ? GameRunBootstrapper.Instance.Run?.BuffHandler?.ActiveBuffs : null;
        if (buffs != null)
            foreach (var b in buffs)
            {
                var row = FindBuffRow(b.BuffType);
                if (row == null) continue;
                int at = s_owned.FindIndex(o => o.name == row.buffName);
                if (at >= 0) s_owned[at] = (row.buffName, s_owned[at].count + 1);
                else s_owned.Add((row.buffName, 1));
            }
        _ownedNone.gameObject.SetActive(s_owned.Count == 0);
        for (int i = 0; i < _ownedChips.Count; i++)
        {
            var chip = _ownedChips[i];
            bool more = i == OwnedChipMax && s_owned.Count > OwnedChipMax;
            bool on   = i < OwnedChipMax ? i < s_owned.Count : more;
            chip.transform.parent.gameObject.SetActive(on);
            if (!on) continue;
            chip.text = more ? $"+{s_owned.Count - OwnedChipMax}"
                             : s_owned[i].count > 1 ? $"{s_owned[i].name} ×{s_owned[i].count}" : s_owned[i].name;
        }

        if (_lockText != null && _controller != null) _lockText.text = $"잠금 {_controller.LockedCount} / {_controller.LockCapacity}";

        // 새로고침 — 제단에서 안 열었으면 잠긴 버튼으로 둔다(제단에서 산 것이 상점에 보이게)
        if (_rerollBtn == null || _controller == null) return;
        bool open  = _controller.RerollEnabled;
        bool left  = _controller.RerollAvailable;
        int  cost  = _controller.RerollCost;
        bool afford = _controller.PlayerGold >= cost;
        _rerollBtn.interactable = open && left && afford;
        UIAffordGlow.Set(_rerollBtn, _rerollBtn.interactable);
        if (!open)
        {
            _rerollLabel.text = "새로고침";
            _rerollLabel.color = Faint;
            _rerollNext.text = "기억의 제단에서 연다";
            return;
        }
        _rerollLabel.text  = !left ? "새로고침 끝" : cost == 0 ? "새로고침 무료" : $"새로고침 {cost}";
        _rerollLabel.color = left && !afford ? Danger : Ink;
        int next = _controller.NextRerollCost;
        _rerollNext.text = !left ? string.Empty : next < 0 ? "마지막" : $"다음 {next}";
    }

    // ── 핸들러 ──

    private void OnBuyClicked()
    {
        if (_controller == null || _buying) return;
        bool special = _selectedIndex == SpecialIndex;
        int  index   = _selectedIndex;
        var product  = Selected();
        var card     = special ? _deal : (index >= 0 && index < _cards.Count ? _cards[index] : null);

        var result = special ? _controller.TryBuySpecial() : _controller.TryBuyProduct(index);
        if (result != ShopPurchaseResult.Success)
        {
            ShopUIStyle.PlaySfx("shop_reject");
            RefreshAll();
            return;
        }
        ShopUIStyle.PlaySfx(special ? "shop_deal" : "shop_buy");
        if (special) { _dealShown = product; _dealHold = true; _selectedIndex = -1; }
        PurchaseFxAsync(product, special, card).Forget();
        RefreshAll();
    }

    private void OnExchange(int ore)
    {
        if (_controller == null) return;
        bool ok = _controller.TryExchangeOre(ore);
        ShopUIStyle.PlaySfx(ok ? "shop_buy" : "shop_reject");
        RefreshAll();
    }

    private void OnRerollClicked()
    {
        if (_controller == null) return;
        bool ok = _controller.TryReroll();
        ShopUIStyle.PlaySfx(ok ? "shop_reroll" : "shop_reject");
        if (ok) _selectedIndex = _controller.SpecialDeal != null ? SpecialIndex : 0;
        RefreshAll();
        if (!ok) return;
        // 새로고침도 골드를 쓴다 — 동전이 빠지고 새 물건이 좌판에 다시 깔린다(10-01 「소모하는 버튼은 연출이 있어야」)
        SpendCoinsAsync().Forget();
        PlayOpenFxAsync().Forget();
    }

    private void OnLockClicked(int index)
    {
        if (_controller == null || index < 0) return;
        if (!_controller.ToggleLock(index)) { ShopUIStyle.PlaySfx("shop_reject"); return; }
        ShopUIStyle.PlaySfx("shop_select");
        _selectedIndex = index;
        RefreshAll();   // 컨트롤러가 OnShopChanged로도 부른다 — 고른 칸을 옮긴 뒤 한 번 더
    }

    // ── 연출 ──

    /// <summary>막이 차오르고 판이 살짝 커지며 떠오른다(0.965 → 1) — 창 맞춤 배율 위에 곱한다.</summary>
    private async UniTaskVoid PlayWindowInAsync()
    {
        if (_win == null) return;
        if (!_win.TryGetComponent<CanvasGroup>(out var cg)) cg = _win.gameObject.AddComponent<CanvasGroup>();
        var baseScale = _win.localScale;
        var basePos   = _win.anchoredPosition;
        try
        {
            for (float t = 0f; t < WindowInSec; )
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / WindowInSec);
                float e = 1f - Mathf.Pow(1f - k, 3f);
                cg.alpha = Mathf.Clamp01(k * 1.6f);
                _win.localScale = baseScale * Mathf.Lerp(WindowInScale, 1f, e);
                _win.anchoredPosition = basePos + new Vector2(0f, -14f * (1f - e));
                if (_veil != null) _veil.color = new Color(0f, 0f, 0f, VeilAlpha * Mathf.Clamp01(k * 2f));
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (cg != null) cg.alpha = 1f;
            if (_win != null) { _win.localScale = baseScale; _win.anchoredPosition = basePos; }
            if (_veil != null) _veil.color = new Color(0f, 0f, 0f, VeilAlpha);
        }
    }

    /// <summary>카드가 60ms 간격으로 좌판에 내려앉는다(아래 22px에서) — 룬 선택 팝업과 같은 결.</summary>
    private async UniTaskVoid PlayOpenFxAsync()
    {
        _openFxCts?.Cancel();
        _openFxCts?.Dispose();
        _openFxCts = new CancellationTokenSource();
        var ct = CancellationTokenSource.CreateLinkedTokenSource(_openFxCts.Token, destroyCancellationToken).Token;

        var all = new List<CardView>(_cards) { _deal };
        foreach (var c in all) if (c?.Group != null) c.Group.alpha = 0f;
        _dealing = true;
        float total = (all.Count - 1) * DealStagger + DealDur;
        try
        {
            for (float t = 0f; t < total; )
            {
                t += Time.unscaledDeltaTime;
                for (int i = 0; i < all.Count; i++)
                {
                    var c = all[i];
                    if (c?.Root == null) continue;
                    float k = Mathf.Clamp01((t - i * DealStagger) / DealDur);
                    float e = 1f - Mathf.Pow(1f - k, 3f);
                    c.Group.alpha = k;
                    var lift = IsSelected(c) ? new Vector2(0f, CardLift) : Vector2.zero;
                    c.Root.anchoredPosition = c.Base + lift + new Vector2(0f, -DealRise * (1f - e));
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _dealing = false;
            foreach (var c in all)
            {
                if (c?.Root == null) continue;
                c.Group.alpha = 1f;
                c.Root.anchoredPosition = c.Base + (IsSelected(c) ? new Vector2(0f, CardLift) : Vector2.zero);
            }
        }
    }

    private bool IsSelected(CardView c) => c != null && c.Index == _selectedIndex;

    /// <summary>구매 직후 한 벌 — 동전이 빠지고, 상인이 반응하고, 「품절」이 찍히고, 산 물건이 받는 곳으로 날아간다.</summary>
    private async UniTaskVoid PurchaseFxAsync(ShopProduct p, bool special, CardView card)
    {
        if (p == null) return;
        _buying = true;
        SpendCoinsAsync().Forget();
        SetMerchantReaction(p);
        if (card != null) PopSoldAsync(card.Sold).Forget();
        if (special) DealSoldAsync().Forget();
        await FlyItemAsync(p, card != null ? card.Root : null);
        _buying = false;
    }

    private async UniTaskVoid DealSoldAsync()
    {
        try { await UniTask.Delay((int)(DealHoldDur * 1000f), ignoreTimeScale: true, cancellationToken: destroyCancellationToken); }
        catch (OperationCanceledException) { return; }
        _dealHold = false;
        RefreshCards();
    }

    private async UniTaskVoid PopSoldAsync(TMP_Text sold)
    {
        if (sold == null) return;
        sold.gameObject.SetActive(true);
        var rt = sold.rectTransform;
        try
        {
            for (float t = 0f; t < SoldPopDur; )
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / SoldPopDur);
                float s = Mathf.Lerp(1.6f, 1f, 1f - (1f - k) * (1f - k));
                rt.localScale = new Vector3(s, s, 1f);
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { return; }
        rt.localScale = Vector3.one;
    }

    /// <summary>골드 칸에서 동전 몇 닢이 튀어 떨어진다(숫자만 줄면 「빠져나간 느낌」이 없다).</summary>
    private async UniTaskVoid SpendCoinsAsync()
    {
        EnsureCoins();
        if (_coins == null || _goldText == null || _fx == null) return;
        var pos = new Vector2[CoinCount];
        var vel = new Vector2[CoinCount];
        Vector2 origin = LocalInFx(_goldText.rectTransform.position);
        for (int i = 0; i < CoinCount; i++)
        {
            pos[i] = origin + new Vector2(UnityEngine.Random.Range(-16f, 16f), UnityEngine.Random.Range(-8f, 8f));
            vel[i] = new Vector2(UnityEngine.Random.Range(-150f, 150f), UnityEngine.Random.Range(170f, 320f));
            _coins[i].color = Color.white;
            _coins[i].rectTransform.anchoredPosition = pos[i];
            _coins[i].gameObject.SetActive(true);
        }
        try
        {
            for (float t = 0f; t < CoinFallDur; )
            {
                float dt = Time.unscaledDeltaTime;
                t += dt;
                for (int i = 0; i < CoinCount; i++)
                {
                    vel[i].y -= 1500f * dt;
                    pos[i]   += vel[i] * dt;
                    var rt = _coins[i].rectTransform;
                    rt.anchoredPosition = pos[i];
                    rt.localRotation    = Quaternion.Euler(0f, 0f, t * 420f * (i % 2 == 0 ? 1f : -1f));
                    var c = _coins[i].color;
                    c.a = 1f - t / CoinFallDur;
                    _coins[i].color = c;
                }
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { return; }
        foreach (var coin in _coins) if (coin != null) coin.gameObject.SetActive(false);
    }

    /// <summary>산 물건이 카드에서 떠올라 받는 곳(HUD 칸)으로 날아가 빨려든다.</summary>
    private async UniTask FlyItemAsync(ShopProduct p, RectTransform source)
    {
        EnsureFlyIcon();
        if (_flyIcon == null || _fx == null || source == null) return;
        Vector2 from = LocalInFx(source.position);
        Vector2 to   = DestinationInFx(p);
        var rt = _flyIcon.rectTransform;
        _flyIcon.sprite = p.Icon;
        _flyIcon.color  = p.Icon != null ? Color.white : Gold;
        rt.sizeDelta        = new Vector2(96f, 96f);
        rt.anchoredPosition = from;
        rt.localScale       = Vector3.one;
        _flyIcon.gameObject.SetActive(true);
        try
        {
            for (float t = 0f; t < ItemFlyDur; )
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / ItemFlyDur);
                float e = 1f - (1f - k) * (1f - k);
                rt.anchoredPosition = Vector2.Lerp(from, to, e) + new Vector2(0f, Mathf.Sin(k * Mathf.PI) * 90f);
                float s = Mathf.Lerp(1f, 0.35f, e);
                rt.localScale = new Vector3(s, s, 1f);
                var c = _flyIcon.color;
                c.a = k < 0.75f ? 1f : 1f - (k - 0.75f) / 0.25f;
                _flyIcon.color = c;
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { return; }
        _flyIcon.gameObject.SetActive(false);
    }

    /// <summary>받는 곳 — HUD에서 실제 칸(버프창 · 포션 칸 · 재화 칸)을 찾는다. 없으면 화면 가장자리 방향.</summary>
    private Vector2 DestinationInFx(ShopProduct p)
    {
        string hudName = p.Category switch
        {
            ShopProductCategory.Buff     => "BuffGridRoot",
            ShopProductCategory.Potion   => "HUD_Active_01",
            ShopProductCategory.Material => p.DisplayName != null && p.DisplayName.StartsWith("원석") ? "Pill_RuneOre" : "Pill_EnhanceMat",
            _                            => null,
        };
        var hud = hudName != null ? FindHudSlot(hudName) : null;
        if (hud != null) return LocalInFx(hud.position);
        var half = _fx.rect.size * 0.5f;
        return p.Category switch
        {
            ShopProductCategory.Buff     => new Vector2(-half.x, -half.y),
            ShopProductCategory.Material => new Vector2( half.x,  half.y),
            _                            => new Vector2( half.x, -half.y),
        };
    }

    private static Transform FindHudSlot(string name)
    {
        var hud = UnityEngine.Object.FindFirstObjectByType<HudView>(FindObjectsInactive.Include);
        if (hud == null) return null;
        var all = hud.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
            if (all[i].name == name) return all[i];
        return null;
    }

    private Vector2 LocalInFx(Vector3 world)
    {
        var cam = GetComponentInParent<Canvas>()?.worldCamera;
        var screen = RectTransformUtility.WorldToScreenPoint(cam, world);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_fx, screen, cam, out var local);
        return local;
    }

    /// <summary>상인 반응 — 무엇을 어디에 넣었는지 말로 알린다(받는 칸은 막 뒤라 안 보인다). 잠깐 뒤 인사말로.</summary>
    private void SetMerchantReaction(ShopProduct p)
    {
        if (_dialogText == null) return;
        _dialogText.text = p.Category switch
        {
            ShopProductCategory.Buff     => $"{p.DisplayName} — 몸에 스몄어. 버프 칸을 봐.",
            ShopProductCategory.Rune     => $"{p.DisplayName} — 보관함에 넣어 뒀어.",
            ShopProductCategory.Material => $"{p.DisplayName} — 주머니에 챙겼어.",
            ShopProductCategory.Potion   => $"{p.DisplayName} — 포션 칸에 넣었어.",
            _                            => "고마워! 잘 써.",
        };
        RestoreGreetingAsync(++_reactionGen).Forget();
    }

    private async UniTaskVoid RestoreGreetingAsync(int gen)
    {
        try { await UniTask.Delay(ReactionMs, ignoreTimeScale: true, cancellationToken: destroyCancellationToken); }
        catch (OperationCanceledException) { return; }
        if (gen != _reactionGen || _dialogText == null) return;
        _dialogText.text = GreetingNow;
    }

    private void EnsureCoins()
    {
        if (_coins != null || _fx == null) return;
        _coins = new Image[CoinCount];
        for (int i = 0; i < CoinCount; i++)
        {
            var img = ShopUIStyle.MakeImage(_fx, "Coin", Gold);
            img.rectTransform.sizeDelta = new Vector2(22f, 22f);
            img.preserveAspect = true;
            if (CoinSprite != null) { img.sprite = CoinSprite; img.color = Color.white; }
            img.gameObject.SetActive(false);
            _coins[i] = img;
        }
    }

    private void EnsureFlyIcon()
    {
        if (_flyIcon != null || _fx == null) return;
        _flyIcon = ShopUIStyle.MakeImage(_fx, "FlyItem", Color.white);
        _flyIcon.preserveAspect = true;
        _flyIcon.gameObject.SetActive(false);
    }

    // ── 도움 ──

    private ShopProduct Selected()
    {
        if (_controller == null) return null;
        if (_selectedIndex == SpecialIndex) return _controller.SpecialDeal;
        var list = _controller.Products;
        return _selectedIndex >= 0 && _selectedIndex < list.Count ? list[_selectedIndex] : null;
    }

    private static string TagText(ShopProduct p)
    {
        if (p.Rune != null) return RuneCardKit.RarityChipText(p.Rune);   // 「◇ Rare · 3칸」 — 칸 수는 칩 글에 이미 있다
        return p.Category == ShopProductCategory.Buff ? $"가호 · {TierName(p.Rarity)}" : p.CategoryLabel;
    }

    private static Color TagColor(ShopProduct p)
    {
        if (p.Category != ShopProductCategory.Rune && p.Category != ShopProductCategory.Buff) return Mute;
        return p.Rarity == ItemRarity.Common ? Mute : ShopUIStyle.Rarity(p.Rarity);
    }

    /// <summary>가호 등급 이름 — 상점 표의 하급 · 중급 · 상급(카드 틀은 Common · Rare · Epic).</summary>
    private static string TierName(ItemRarity r) => r switch
    {
        ItemRarity.Epic => "상급",
        ItemRarity.Rare => "중급",
        _               => "하급",
    };

    private static Color BlessingTint(ItemRarity r) => r == ItemRarity.Common ? Ink : Color.Lerp(Ink, ShopUIStyle.Rarity(r), 0.55f);

    /// <summary>룬 작은 카드 둘째 줄 — 속성(효과 전부는 오른쪽 상세에서).</summary>
    private static string RuneLine(RuntimeItemData rune)
    {
        if (rune == null) return string.Empty;
        var elem = ElementDef.GetById(rune.element);
        return elem != null ? $"{elem.Name} 속성 · 눌러서 효과 보기" : "눌러서 효과 보기";
    }

    private static bool TryBlessingStat(ShopProduct p, out StatType stat)
    {
        stat = default;
        if (p == null || p.Category != ShopProductCategory.Buff) return false;
        foreach (var row in ShopBuffTable.Rows)
            if (row.buffName == p.DisplayName) { stat = row.statType; return true; }
        return false;
    }

    private static ShopBuffRow FindBuffRow(string buffId)
    {
        if (string.IsNullOrEmpty(buffId)) return null;
        foreach (var row in ShopBuffTable.Rows)
            if (row.buffId == buffId) return row;
        return null;
    }

    private static string Hex(Color c) => $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>";

    // ── 배치 도움(목업 px · 부모 왼쪽 위 기준) ──

    private static void Put(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private static RectTransform Box(Transform parent, string name, float x, float y, float w, float h)
    {
        var rt = (RectTransform)ShopUIStyle.MakeRect(parent, name).transform;
        Put(rt, x, y, w, h);
        return rt;
    }

    private static Image Img(Transform parent, string name, float x, float y, float w, float h, Color color, Sprite sprite = null, bool raycast = false)
    {
        var img = ShopUIStyle.MakeImage(parent, name, color, raycast);
        if (sprite != null) { img.sprite = sprite; img.preserveAspect = true; }
        Put(img.rectTransform, x, y, w, h);
        return img;
    }

    private static Image Panel(Transform parent, string name, float x, float y, float w, float h, Color fill, Color line)
    {
        var bg = Img(parent, name, x, y, w, h, fill);
        bg.sprite = UIProceduralSprites.RoundedRect(radius: 4f, feather: 1f);
        bg.type   = Image.Type.Sliced;
        var edge = ShopUIStyle.MakeImage(bg.transform, "Edge", line);
        edge.sprite = UIProceduralSprites.RoundedOutline(radius: 4f, stroke: 1f);
        edge.type   = Image.Type.Sliced;
        ShopUIStyle.Stretch(edge.rectTransform);
        return bg;
    }

    private static Image CenterIcon(Transform parent, float size)
    {
        var img = ShopUIStyle.MakeImage(parent, "Icon", Color.white);
        img.preserveAspect = true;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(size, size);
        return img;
    }

    private static TMP_Text Text(Transform parent, string name, float x, float y, float w, float h, float size, UIWeight weight, Color color,
                                 TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    {
        var t = ShopUIStyle.MakeText(parent, name, size, FontStyles.Normal, align, color);
        t.enableAutoSizing = false;
        UITheme.StyleThinText(t, weight, size, color);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode     = TextOverflowModes.Ellipsis;
        t.richText         = true;
        // 칸이 한 줄 높이(Noto ≈ 1.45em)보다 낮으면 TMP 말줄임이 줄을 통째로 지운다 — 제목 · 가호 · 진열 · 상세 이름이 사라졌다(10-21 1차 촬영).
        float minH = size * 1.5f;
        if (h < minH) { y -= (minH - h) * 0.5f; h = minH; }
        Put(t.rectTransform, x, y, w, h);
        return t;
    }

    private static RectTransform Row(Transform parent, string name, float x, float y, float w, float h, TextAnchor align, float spacing, RectOffset pad = null)
    {
        var rt = Box(parent, name, x, y, w, h);
        var hlg = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = align;
        hlg.spacing = spacing;
        hlg.padding = pad ?? new RectOffset();
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;
        return rt;
    }

    private static Image RowIcon(Transform row, string name, Sprite sprite, float size, Color color)
    {
        var img = ShopUIStyle.MakeImage(row, name, color);
        img.sprite = sprite;
        img.preserveAspect = true;
        var le = img.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = size;
        le.minHeight = le.preferredHeight = size;
        img.gameObject.SetActive(sprite != null);
        return img;
    }

    private static TMP_Text RowText(Transform row, string name, float size, UIWeight weight, Color color)
    {
        var t = ShopUIStyle.MakeText(row, name, size, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, color);
        t.enableAutoSizing = false;
        UITheme.StyleThinText(t, weight, size, color);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode     = TextOverflowModes.Overflow;
        t.richText         = true;
        return t;
    }

    private static void Spacer(Transform row, float w)
    {
        var go = ShopUIStyle.MakeRect(row, "Gap");
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = w;
    }

    private TMP_Text BandItem(Transform row, string name, Sprite icon, string label)
    {
        RowIcon(row, name + "Icon", icon, 22f, Mute);
        Spacer(row, 8f);
        var l = RowText(row, name + "Label", 15f, UIWeight.Regular, Mute);
        l.text = label;
        Spacer(row, 10f);
        return RowText(row, name + "Value", 20f, UIWeight.Medium, Ink);
    }

    /// <summary>아래 줄 버튼 — 고정 폭 판(얇은 선) · 안쪽은 가로 줄(아이콘 · 글자).</summary>
    private static Button FootButton(Transform row, string name, float width, out RectTransform inner)
    {
        var img = ShopUIStyle.MakeImage(row, name, BtnFill, raycast: true);
        img.sprite = UIProceduralSprites.RoundedRect(radius: 4f, feather: 1f);
        img.type   = Image.Type.Sliced;
        var le = img.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = width;
        le.minHeight = le.preferredHeight = 46f;
        var edge = ShopUIStyle.MakeImage(img.transform, "Edge", Line);
        edge.sprite = UIProceduralSprites.RoundedOutline(radius: 4f, stroke: 1f);
        edge.type   = Image.Type.Sliced;
        ShopUIStyle.Stretch(edge.rectTransform);
        var hlg = img.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 8f;
        hlg.padding = new RectOffset(14, 14, 0, 0);
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;
        // 선(Edge)은 줄 배치에서 뺀다
        edge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var btn = img.gameObject.AddComponent<Button>();
        ShopUIStyle.ApplyButtonColors(btn, img);
        img.gameObject.AddComponent<UIButtonFeedback>();
        inner = img.rectTransform;
        return btn;
    }
}
