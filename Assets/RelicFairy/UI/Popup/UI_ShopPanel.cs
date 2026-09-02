using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 상점 — 심연의 행상 (Canvas_Popup, Addressable "UI/Popup/UI_ShopPanel").
///
/// 완성본 목업(바탕화면 "상점 UI 리소스/상점 전체이미지.png", 1178×837) 배치를 그대로 옮긴다:
///  - 상단: 어두운 띠 + 간판(등불 일체) + 골드 / 우상단 문장, 네 모서리 장식
///  - 오늘의 특가: 리본이 포함된 가로 배너 1장
///  - 상품 3×2: 양피지 카드(아이콘 칸·카테고리 배지·이름·효과·가격) — 품절은 바탕 교체 + 도장
///  - 우측 "고른 물건": 겹친 양피지 + 대형 미리보기 + 이름 명판 + 상세 + [사겠네]
///  - 하단 중앙: 새로고침
///
/// <b>좌표계</b>: 모든 사각형을 <i>목업 픽셀</i>로 적고 <see cref="S"/>로 환산한다.
/// 목업을 열어 재면 코드의 숫자와 그대로 대응하므로, 재납품 때 눈으로 대조할 수 있다.
/// 크기는 항상 아트 원본 비율을 지킨다 — 칸에 맞추려 늘리면 나뭇결·양피지 결이 왜곡된다.
///
/// 데이터/구매는 <see cref="ShopRoomController"/>가 권위(Products·SpecialDeal·TryBuyProduct).
/// 아트는 <see cref="UISkin.Shop"/>, 미로드 시 색 폴백.
/// </summary>
public sealed class UI_ShopPanel : UI_Popup
{
    public override bool BlocksGameplay => true;
    public override bool CloseOnEscape  => true;

    // ── 좌표계 ──
    private const float MockW  = 1178f;   // 목업 캔버스
    private const float MockH  =  837f;
    private const float WindowW = 1272f;  // 1920×1080에서의 창 너비
    private const float S       = WindowW / MockW;
    private const float WindowH = MockH * S;

    // ── 배치 (목업 px) ──
    private const float BandY      =  14f, BandH     =  91f;
    private const float TitleX     =  95f, TitleY    =  15f, TitleW  = 360f, TitleH = 166f;
    private const float CrestX     = 946f, CrestY    =   2f, CrestW  =  80f, CrestH = 118f;
    private const float CornerW    =  73f, CornerH   = 108f, CornerInsetX = 58f;
    private const float CornerTopY =   8f, CornerBottomY = 690f;
    private const float GoldX      = 752f, GoldY     =  56f, GoldIconSize = 22f;
    // ★ 목업 아트를 전체샷에 템플릿 매칭해 실측한 값(상관 0.97/0.91).
    //   ⚠️ 바깥 rect를 고칠 때 <b>내부를 배율로 키우면 안 된다</b> — 내부 요소는 카드 아트의 일부가 아니라
    //   그 위에 얹힌 별개 이미지라, 목업에서 <b>절대 위치가 고정</b>이다. rect 원점이 움직인 만큼만 옮긴다.
    //   (배율로 키웠더니 코인·배지가 우하단으로 18px 밀려 카드 밖으로 걸쳤다.)
    //   예전 값은 <b>아트 rect가 아니라 눈으로 잰 "보이는 판"</b>이라 카드가 13% 작았다.
    //   아트에는 투명 여백이 있어(카드 좌2.1%·우4.9%·상2.2%·하5.2%) rect는 보이는 판보다 커야 한다.
    private const float DealX      =  95f, DealY     = 158f, DealW   = 625f, DealH  = 158f;
    private const float GridX      = 110f, GridY     = 322f;
    private const float CardW      = 205f, CardH     = 196f;
    private const float CardStepX  = 201.5f, CardStepY = 191f;
    private const int   GridCols   = 3, GridRows = 2;
    private const float RerollW    = 230f, RerollH   =  53f, RerollY = 718f;
    // 구매창도 카드와 같은 문제였다 — 아트 rect가 아니라 눈으로 잰 "보이는 종이"를 썼다.
    // 구매창.png(885×1811)는 좌1.7%·우3.8%·상0.8%·하1.8%가 투명이라 rect가 그만큼 커야 한다.
    private const float SelX       = 755f, SelY      = 141f, SelW    = 290f, SelH   = 594f;

    // 카테고리 색 — 아트 배지가 없는 룬·재료의 폴백이자, 배지 있는 칸의 선택 강조색.
    private static readonly Color[] CatColor =
    {
        new(0.35f, 0.85f, 0.55f, 1f),   // 버프 초록
        new(0.72f, 0.52f, 0.95f, 1f),   // 룬  보라
        new(1.00f, 0.66f, 0.28f, 1f),   // 재료 주황
        new(0.95f, 0.40f, 0.38f, 1f),   // 포션 빨강
    };

    /// <summary>새로고침 아트에 구워진 비용. 실제 비용이 다르면 표기가 거짓말이 된다.</summary>
    private const int RerollCostBakedInArt = 10;

    private ShopRoomController _controller;
    private ShopSkinSO _skin;

    [SerializeField] private TMP_Text _goldText, _dialogText;
    [SerializeField] private Button   _rerollBtn;
    [SerializeField] private Image    _rerollImg;
    [SerializeField] private TMP_Text _rerollLabel;

    // 특가
    [SerializeField] private GameObject _dealRoot;
    [SerializeField] private Image      _dealIcon;
    [SerializeField] private TMP_Text   _dealCat, _dealName, _dealDesc, _dealOldPrice, _dealPrice;

    // 상품 카드
    private sealed class Card
    {
        public GameObject Root;
        public Image Bg, Icon, BadgeBg, BadgeGlyph, SoldStamp, PriceCoin;
        public TMP_Text BadgeText, Name, Effect, Price;
    }
    private readonly List<Card> _cards = new();

    // 고른 물건
    [SerializeField] private Image    _selPreview, _selNamePlate;
    [SerializeField] private TMP_Text _selName, _selCat, _selEffect, _selApply, _selValue, _selWorry, _selFlavor;
    [SerializeField] private Button   _buyBtn;
    [SerializeField] private TMP_Text _buyLabel;

    private int _selectedIndex = -1;   // -1 = 미선택, -2 = 특가
    private const int SpecialIndex = -2;

    private bool _built, _closing;

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
        if (_controller != null)
        {
            _controller.OnShopChanged -= RefreshAll;
            _controller.NotifyPanelClosed();
        }
    }

    public void Bind(ShopRoomController controller)
    {
        _controller = controller;
        if (_controller != null)
        {
            _controller.OnShopChanged += RefreshAll;

            // 비용이 어긋나면 RefreshReroll이 아트를 걷어내고 실제 값을 그린다(표기는 항상 옳다).
            // 다만 완성 아트가 안 보이는 상태이므로, 원인을 남겨 아트/설정 중 하나를 맞추게 한다.
            if (_controller.RerollEnabled && _controller.RerollCost != RerollCostBakedInArt)
                Debug.LogWarning($"[UI_ShopPanel] 새로고침 비용 불일치 — 아트 표기 {RerollCostBakedInArt}G / 실제 {_controller.RerollCost}G. " +
                                 "아트를 숨기고 색 버튼으로 대체한다. 글자 없는 버튼 아트를 받거나 비용을 맞출 것.");
        }

        ShopUIStyle.PlaySfx("shop_open");
        _selectedIndex = -1;
        RefreshAll();
    }

    public override void ClosePopupUI()
    {
        if (_closing) return;
        _closing = true;
        ShopUIStyle.PlaySfx("shop_close");
        base.ClosePopupUI();
    }

    // ── 배치 헬퍼 ──

    /// <summary>
    /// 목업 좌상단 기준 사각형으로 배치한다. 창(<c>MockW</c>×<c>MockH</c>)에 대한 <b>비율 앵커</b>로 굳는다.
    ///
    /// <para>예전엔 목업 px에 <c>S</c>를 곱한 <b>절대 크기</b>를 점 앵커에 박았다. 그러면 창을 키워도
    /// 자식은 좌상단에 원래 크기로 붙어 있어, 넓어진 자리가 통째로 빈다 —
    /// 배경만 커지고 내용은 안 따라오는 원인이 이것이었다.</para>
    ///
    /// <para>목업 좌표계는 그 자체가 이미 비율계다(창이 정확히 <c>MockW</c>×<c>MockH</c>다).
    /// 그래서 나누기 한 번으로 정확히 옮겨진다 — 근사가 아니라 <b>같은 값</b>이다.
    /// 이제 창 <see cref="RectTransform"/> 하나만 인스펙터에서 끌면 내용 전체가 비율 그대로 따라온다.</para>
    /// </summary>
    private static void Place(Component c, float mx, float my, float mw, float mh)
        => PlaceFrac((RectTransform)c.transform, mx, my, mw, mh, MockW, MockH);

    private static void PlaceFrac(RectTransform rt, float mx, float my, float mw, float mh, float pw, float ph)
        => UIProportional.Place(rt, mx, my, mw, mh, pw, ph);

    // ── Build ──

    private void BuildChrome()
    {
        if (_built) return;
        _built = true;
        // 프리팹이 구워져 있으면 <b>짓지 않고 잇기만 한다</b> — 다시 지으면 UI가 두 벌 겹친다.
        if (transform.childCount > 0)
        {
            BindBakedHierarchy();
            return;
        }


        ShopUIStyle.Stretch(GetComponent<RectTransform>());

        var veil = ShopUIStyle.MakeImage(transform, "Veil", ShopUIStyle.Veil, raycast: true);
        ShopUIStyle.Stretch(veil.rectTransform);

        var half = new Vector2(0.5f, 0.5f);
        var window = ShopUIStyle.MakeImage(transform, "Window", new Color(0f, 0f, 0f, 0f), raycast: true);
        // 화면 맞춤은 빌더가 붙인다 — 프리팹에만 붙이면 재굽기 때 사라진다.
        window.gameObject.AddComponent<UIWindowFitter>().Configure(maxScale: UIWindowFitter.ContentScreen);
        ShopUIStyle.Anchor(window.rectTransform, half, half, half, Vector2.zero, new Vector2(WindowW, WindowH));
        var w = window.transform;

        // 배경은 창과 같은 비율이라 그대로 채운다.
        var bg = ShopUIStyle.MakeImage(w, "Bg", ShopUIStyle.WindowFill, raycast: true);
        Place(bg, 0f, 0f, MockW, MockH);
        ShopUIStyle.Skin(bg, _skin?.background);

        BuildTopBar(w);
        BuildDeal(w);
        BuildGrid(w);
        BuildSelectedColumn(w);
        BuildReroll(w);
        BuildExitButton(w);

        // 모서리 장식은 마지막 — 내용 위로 지나가야 액자가 된다.
        BuildCorners(w);
    }

    /// <summary>
    /// 구워진 프리팹을 잇는다 — <b>계층·좌표·아트는 프리팹이 갖고, 코드는 배선만 한다.</b>
    ///
    /// <para>클릭 리스너와 <see cref="_cards"/> 목록은 직렬화되지 않는다(리스너는 코드가 붙인 것이고,
    /// 목록은 <c>readonly</c> 런타임 상태다). 그 둘만 여기서 되살린다.
    /// 이름으로 찾는다 — 빌더가 붙이던 이름 그대로라 프리팹에도 같은 이름으로 굳어 있다.</para>
    /// </summary>
    private void BindBakedHierarchy()
    {
        _cards.Clear();

        for (int i = 0; i < GridCols * GridRows; i++)
        {
            var root = transform.Find($"Window/Card{i}");
            if (root == null) continue;

            int captured = i;
            // 배지의 글리프·글자는 카드가 아니라 <b>BadgeBg 아래</b>에 달린다(빌더가 그렇게 짓는다).
            // Img/Txt는 직속 자식만 찾으므로 카드에서 찾으면 둘 다 null이 되고,
            // 상품 6장의 카테고리 배지가 전부 빈 칸으로 남는다.
            var badge = root.Find("BadgeBg");
            var c = new Card
            {
                Root       = root.gameObject,
                Bg         = root.GetComponent<Image>(),
                Icon       = Img(root, "Icon"),
                BadgeBg    = badge != null ? badge.GetComponent<Image>() : null,
                BadgeGlyph = badge != null ? Img(badge, "BadgeGlyph") : null,
                SoldStamp  = Img(root, "SoldStamp"),
                PriceCoin  = Img(root, "PriceCoin"),
                BadgeText  = badge != null ? Txt(badge, "BadgeText") : null,
                Name       = Txt(root, "Name"),
                Effect     = Txt(root, "Effect"),
                Price      = Txt(root, "Price"),
            };
            _cards.Add(c);
            AddClick(root.gameObject, () => Select(captured));
        }

        if (_dealRoot != null) AddClick(_dealRoot, () => Select(SpecialIndex));

        Rewire(_buyBtn,    OnBuyClicked);
        Rewire(_rerollBtn, OnRerollClicked);
        Rewire(transform.Find("Window/Exit")?.GetComponent<Button>(), ClosePopupUI);
    }

    private static Image    Img(Transform p, string n) => p.Find(n)?.GetComponent<Image>();
    private static TMP_Text Txt(Transform p, string n) => p.Find(n)?.GetComponent<TMP_Text>();

    /// <summary>같은 팝업이 다시 열려도 리스너가 겹쳐 쌓이지 않도록 지우고 건다.</summary>
    private static void Rewire(Button btn, UnityEngine.Events.UnityAction fn)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(fn);
    }

    /// <summary>상단 — 어두운 띠 + 간판(등불 일체) + 골드 + 우상단 문장.</summary>
    private void BuildTopBar(Transform w)
    {
        var band = ShopUIStyle.MakeImage(w, "TopBand", new Color(0f, 0f, 0f, 0.55f));
        Place(band, 0f, BandY, MockW, BandH);
        ShopUIStyle.Skin(band, _skin?.topBand, sliced: true);

        // 간판 — 아트에 '심연의 행상' 글자는 없다. 판 위에 얹는다.
        var board = ShopUIStyle.MakeImage(w, "TitleBoard", new Color(1f, 1f, 1f, 0f));
        Place(board, TitleX, TitleY, TitleW, TitleH);
        board.raycastTarget = false;
        ShopUIStyle.Skin(board, _skin?.titleBoard);

        var name = ShopUIStyle.MakeText(w, "Name", 26f, FontStyles.Bold,
                                        TextAlignmentOptions.MidlineLeft, ShopUIStyle.Gold);
        name.text = "심연의 행상";
        Place(name, TitleX + 100f, TitleY + 27f, 230f, 34f);

        _dialogText = ShopUIStyle.MakeText(w, "Dialog", 16f, FontStyles.Normal,
                                           TextAlignmentOptions.MidlineLeft, ShopUIStyle.TextPrimary);
        _dialogText.text = "어서 오게, 빛이여";
        Place(_dialogText, TitleX + 100f, TitleY + 60f, 250f, 26f);

        // 골드 — 코인 + 수치. 상단 띠 중앙 우측.
        var coin = ShopUIStyle.MakeImage(w, "GoldCoin", ShopUIStyle.Gold);
        Place(coin, GoldX, GoldY, GoldIconSize, GoldIconSize);
        coin.preserveAspect = true;
        ShopUIStyle.Skin(coin, _skin?.goldCoin);

        _goldText = ShopUIStyle.MakeText(w, "Gold", 21f, FontStyles.Bold,
                                         TextAlignmentOptions.MidlineLeft, ShopUIStyle.Gold);
        Place(_goldText, GoldX + 30f, GoldY - 6f, 190f, 34f);

        var crest = ShopUIStyle.MakeImage(w, "Crest", new Color(1f, 1f, 1f, 0f));
        Place(crest, CrestX, CrestY, CrestW, CrestH);
        crest.raycastTarget = false;
        ShopUIStyle.Skin(crest, _skin?.crest);
    }

    private void BuildCorners(Transform w)
    {
        AddCorner(w, "CornerTL", _skin?.cornerTopLeft,     CornerInsetX,                       CornerTopY);
        AddCorner(w, "CornerTR", _skin?.cornerTopRight,    MockW - CornerInsetX - CornerW,     CornerTopY);
        AddCorner(w, "CornerBL", _skin?.cornerBottomLeft,  CornerInsetX,                       CornerBottomY);
        AddCorner(w, "CornerBR", _skin?.cornerBottomRight, MockW - CornerInsetX - CornerW,     CornerBottomY);
    }

    private static void AddCorner(Transform w, string name, Sprite art, float mx, float my)
    {
        if (art == null) return;   // 아트 없으면 장식 자체를 만들지 않는다(빈 흰 사각형 방지)
        var img = ShopUIStyle.MakeImage(w, name, Color.white);
        Place(img, mx, my, CornerW, CornerH);
        img.raycastTarget = false;
        ShopUIStyle.Skin(img, art);
    }

    /// <summary>오늘의 특가 — 리본이 아트에 포함된 배너 1장. 내용은 그 안에 얹는다.</summary>
    private void BuildDeal(Transform w)
    {
        var card = ShopUIStyle.MakeImage(w, "Deal", new Color(0.20f, 0.09f, 0.08f, 0.96f), raycast: true);
        _dealRoot = card.gameObject;
        Place(card, DealX, DealY, DealW, DealH);
        ShopUIStyle.Skin(card, _skin?.dealPanel, sliced: true);
        AddClick(card.gameObject, () => Select(SpecialIndex));

        // 아트가 없을 때만 "오늘의 특가" 글자를 그린다 — 있으면 리본에 이미 있다.
        if (_skin?.dealPanel == null)
        {
            var tab = ShopUIStyle.MakeText(card.transform, "Tab", 15f, FontStyles.Bold,
                                           TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
            tab.text = "오늘의 특가";
            PlaceIn(card.transform, tab, 27f, 8f, 150f, 26f, DealW, DealH);
        }

        // 리본이 좌상단을 덮으므로 내용은 그 아래·오른쪽에서 시작한다.
        _dealIcon = ShopUIStyle.MakeImage(card.transform, "Icon", new Color(0.05f, 0.05f, 0.07f, 1f));
        PlaceIn(card.transform, _dealIcon, 43f, 42f, 88f, 88f, DealW, DealH);
        _dealIcon.preserveAspect = true;

        _dealCat = ShopUIStyle.MakeText(card.transform, "Cat", 13f, FontStyles.Normal,
                                        TextAlignmentOptions.TopLeft, new Color(0.36f, 0.26f, 0.16f, 1f));
        PlaceIn(card.transform, _dealCat, 147f, 42f, 268f, 20f, DealW, DealH);

        _dealName = ShopUIStyle.MakeText(card.transform, "Name", 23f, FontStyles.Bold,
                                         TextAlignmentOptions.TopLeft, new Color(0.20f, 0.13f, 0.07f, 1f));
        PlaceIn(card.transform, _dealName, 147f, 62f, 268f, 30f, DealW, DealH);

        _dealDesc = ShopUIStyle.MakeText(card.transform, "Desc", 13f, FontStyles.Normal,
                                         TextAlignmentOptions.TopLeft, new Color(0.36f, 0.26f, 0.16f, 1f));
        PlaceIn(card.transform, _dealDesc, 147f, 94f, 268f, 22f, DealW, DealH);

        _dealOldPrice = ShopUIStyle.MakeText(card.transform, "OldPrice", 14f, FontStyles.Strikethrough,
                                             TextAlignmentOptions.MidlineRight, new Color(0.42f, 0.32f, 0.22f, 1f));
        PlaceIn(card.transform, _dealOldPrice, 427f, 44f, 150f, 22f, DealW, DealH);

        _dealPrice = ShopUIStyle.MakeText(card.transform, "Price", 27f, FontStyles.Bold,
                                          TextAlignmentOptions.MidlineRight, new Color(0.45f, 0.28f, 0.06f, 1f));
        PlaceIn(card.transform, _dealPrice, 427f, 68f, 150f, 34f, DealW, DealH);
    }

    /// <summary>부모 사각형 안쪽 기준 배치(목업 px). 부모의 좌상단이 원점.</summary>
    /// <summary>
    /// 자식을 부모 안에 <b>비율로</b> 앉힌다. <paramref name="pw"/>×<paramref name="ph"/>는
    /// 부모의 <b>목업 크기</b>다 — 부모가 화면에서 몇 px인지와 무관하게 목업 좌표만으로 비율이 나온다.
    ///
    /// <para>부모의 실제 rect를 읽어 추론하지 않는 이유: 부모도 이제 비율 앵커라 <c>sizeDelta</c>가 0이고,
    /// 실제 크기는 레이아웃이 한 번 돌아야 정해진다. 프리팹을 굽는 동안에는 그게 돌지 않아
    /// 추론하면 0으로 나눈다. 목업 크기는 <b>상수로 알고 있으니</b> 그대로 넘긴다.</para>
    /// </summary>
    private static void PlaceIn(Transform parent, Component c, float mx, float my, float mw, float mh,
                                float pw, float ph)
    {
        var rt = (RectTransform)c.transform;
        if (rt.parent != parent) rt.SetParent(parent, false);
        PlaceFrac(rt, mx, my, mw, mh, pw, ph);
    }

    private void BuildGrid(Transform w)
    {
        for (int i = 0; i < GridCols * GridRows; i++)
            _cards.Add(BuildCard(w, i));
    }

    /// <summary>
    /// 상품 카드 1장. 내용 좌표는 <b>양피지의 쓸 수 있는 안쪽</b>에 맞춘다 —
    /// 좌표는 <b>완성본 목업의 카드를 격자로 재서</b> 그대로 옮겼다(카드 179×171 기준):
    ///   아이콘 72×99@(11,7) · 배지 43×17@(122,10) · 이름 y113 · 효과 y136 · 가격 y139 · 코인 20×22@(148,138).
    /// 밝기 임계로 추정한 "쓸 수 있는 영역"보다 목업이 아래까지 쓰므로, 추정이 아니라 목업을 따른다.
    /// </summary>
    private Card BuildCard(Transform parent, int index)
    {
        int captured = index;
        var c = new Card();

        var bg = ShopUIStyle.MakeImage(parent, $"Card{index}", new Color(0.13f, 0.12f, 0.16f, 0.96f), raycast: true);
        c.Root = bg.gameObject; c.Bg = bg;
        Place(bg, GridX + (index % GridCols) * CardStepX,
                  GridY + (index / GridCols) * CardStepY, CardW, CardH);
        ShopUIStyle.Skin(bg, _skin?.cardBg, sliced: true);
        AddClick(bg.gameObject, () => Select(captured));

        // 아이콘 칸 — 채움 + 테두리 2겹, 그 사이에 상품 아이콘.
        var fill = ShopUIStyle.MakeImage(bg.transform, "IconFill", new Color(0.05f, 0.05f, 0.07f, 1f));
        PlaceIn(bg.transform, fill, 15f, 11f, 74f, 98f, CardW, CardH);
        ShopUIStyle.Skin(fill, _skin?.iconFill, sliced: true);

        c.Icon = ShopUIStyle.MakeImage(bg.transform, "Icon", new Color(1f, 1f, 1f, 0f));
        PlaceIn(bg.transform, c.Icon, 19f, 15f, 66f, 90f, CardW, CardH);
        c.Icon.preserveAspect = true;

        var frame = ShopUIStyle.MakeImage(bg.transform, "IconFrame", new Color(1f, 1f, 1f, 0f));
        PlaceIn(bg.transform, frame, 15f, 11f, 74f, 98f, CardW, CardH);
        frame.raycastTarget = false;
        ShopUIStyle.Skin(frame, _skin?.iconFrame, sliced: true);

        // 카테고리 배지 — 바탕(아트) + 글자(아트가 있으면 그 위 라벨은 끈다).
        c.BadgeBg = ShopUIStyle.MakeImage(bg.transform, "BadgeBg", CatColor[0]);
        PlaceIn(bg.transform, c.BadgeBg, 123f, 10f, 50f, 22f, CardW, CardH);

        c.BadgeGlyph = ShopUIStyle.MakeImage(c.BadgeBg.transform, "BadgeGlyph", new Color(1f, 1f, 1f, 0f));
        ShopUIStyle.Stretch(c.BadgeGlyph.rectTransform, 2f);
        c.BadgeGlyph.preserveAspect = true;
        c.BadgeGlyph.raycastTarget  = false;

        c.BadgeText = ShopUIStyle.MakeText(c.BadgeBg.transform, "BadgeText", 11f, FontStyles.Bold,
                                           TextAlignmentOptions.Center, new Color(0.1f, 0.09f, 0.12f));
        ShopUIStyle.Stretch(c.BadgeText.rectTransform);

        c.Name = ShopUIStyle.MakeText(bg.transform, "Name", 15f, FontStyles.Bold,
                                      TextAlignmentOptions.TopLeft, new Color(0.18f, 0.12f, 0.06f, 1f));
        PlaceIn(bg.transform, c.Name, 17f, 112f, 175f, 20f, CardW, CardH);
        FitLine(c.Name);

        c.Effect = ShopUIStyle.MakeText(bg.transform, "Effect", 11.5f, FontStyles.Normal,
                                        TextAlignmentOptions.TopLeft, new Color(0.38f, 0.28f, 0.18f, 1f));
        // 효과와 가격은 같은 높이에서 좌우로 갈린다 — 효과가 카드 폭을 다 먹으면 가격과 겹친다.
        PlaceIn(bg.transform, c.Effect, 17f, 135f, 120f, 17f, CardW, CardH);
        FitLine(c.Effect);

        c.Price = ShopUIStyle.MakeText(bg.transform, "Price", 16f, FontStyles.Bold,
                                       TextAlignmentOptions.MidlineRight, new Color(0.25f, 0.17f, 0.08f, 1f));
        PlaceIn(bg.transform, c.Price, 17f, 139f, 140f, 20f, CardW, CardH);

        c.PriceCoin = ShopUIStyle.MakeImage(bg.transform, "PriceCoin", ShopUIStyle.Gold);
        PlaceIn(bg.transform, c.PriceCoin, 152f, 142f, 21f, 22f, CardW, CardH);
        c.PriceCoin.preserveAspect = true;
        ShopUIStyle.Skin(c.PriceCoin, _skin?.goldCoin);

        // 품절 도장 — 카드 중앙에 겹친다. 기본은 꺼둔다.
        c.SoldStamp = ShopUIStyle.MakeImage(bg.transform, "SoldStamp", new Color(1f, 1f, 1f, 0f));
        PlaceIn(bg.transform, c.SoldStamp, (CardW - 175f) * 0.5f, (CardH - 92f) * 0.5f, 175f, 92f, CardW, CardH);
        c.SoldStamp.raycastTarget = false;
        ShopUIStyle.Skin(c.SoldStamp, _skin?.soldStamp);
        c.SoldStamp.gameObject.SetActive(false);

        return c;
    }

    /// <summary>우측 "고른 물건" — 겹친 양피지 + 미리보기 + 이름 명판 + 상세 + [사겠네].</summary>
    private void BuildSelectedColumn(Transform w)
    {
        var col = ShopUIStyle.MakeImage(w, "Selected", new Color(0.11f, 0.11f, 0.14f, 0.96f));
        Place(col, SelX, SelY, SelW, SelH);
        ShopUIStyle.Skin(col, _skin?.selectedPanel, sliced: true);
        var s = col.transform;

        _selPreview = ShopUIStyle.MakeImage(s, "Preview", new Color(1f, 1f, 1f, 0f));
        PlaceIn(s, _selPreview, 85f, 80f, 88f, 122f, SelW, SelH);
        _selPreview.preserveAspect = true;

        // 이름 명판 — 전용 아트가 납품되지 않았다. 단색 사각형 하나로 두면 양피지 위에서
        // '덜 그려진 칸'처럼 보이므로, 어두운 테두리 + 밝은 채움 2겹으로 판의 두께를 만든다.
        _selNamePlate = ShopUIStyle.MakeImage(s, "NamePlate", new Color(0.26f, 0.06f, 0.05f, 1f));
        PlaceIn(s, _selNamePlate, 14f, 222f, 214f, 52f, SelW, SelH);

        var plateFill = ShopUIStyle.MakeImage(_selNamePlate.transform, "Fill", new Color(0.48f, 0.11f, 0.10f, 1f));
        ShopUIStyle.Stretch(plateFill.rectTransform, 3f);

        _selName = ShopUIStyle.MakeText(plateFill.transform, "Name", 20f, FontStyles.Bold,
                                        TextAlignmentOptions.Center, new Color(0.96f, 0.88f, 0.72f, 1f));
        ShopUIStyle.Stretch(_selName.rectTransform, 6f);
        FitLine(_selName);

        _selCat = ShopUIStyle.MakeText(s, "Cat", 13f, FontStyles.Bold,
                                       TextAlignmentOptions.Center, CatColor[0]);
        PlaceIn(s, _selCat, 26f, 305f, 206f, 22f, SelW, SelH);

        // 4줄이 flavor(402)와 buy(424) 위에서 끝나야 한다 — 예전 값(274·300·334·360)은
        // 마지막 줄(360~386)이 flavor(372~406) 위로 올라타 글자가 겹쳐 찍혔다.
        _selEffect = DetailRow(s, "Effect", 331f);
        _selApply  = DetailRow(s, "Apply",  355f);
        _selValue  = DetailRow(s, "Value",  379f);
        _selWorry  = DetailRow(s, "Worry",  403f);

        _selFlavor = ShopUIStyle.MakeText(s, "Flavor", 12f, FontStyles.Italic,
                                          TextAlignmentOptions.Top, new Color(0.42f, 0.32f, 0.22f, 1f));
        PlaceIn(s, _selFlavor, 22f, 429f, 214f, 22f, SelW, SelH);
        _selFlavor.textWrappingMode = TextWrappingModes.Normal;

        // 구매버튼 아트는 <b>빈 명판</b>이다 — 라벨을 반드시 그려야 글자가 생긴다.
        _buyBtn = MakeButton(s, "Buy", "사겠네", _skin?.buyButton, out _buyLabel);
        // 목업의 [사겠네]는 종이 기준 437~497에 보인다. 아트에 투명 여백이 있어 424에 86을 놓으면
        // 그 자리에 정확히 앉는다(424+86=510, 종이 512 안).
        PlaceIn(s, _buyBtn, 12f, 451f, 235f, 86f, SelW, SelH);
        _buyLabel.fontSize = 20f;
        _buyLabel.color    = new Color(0.96f, 0.88f, 0.72f, 1f);
        _buyBtn.onClick.AddListener(OnBuyClicked);
    }

    /// <summary>하단 중앙 새로고침. 아트/라벨 중 무엇을 보일지는 비용에 달려 있어 <see cref="RefreshReroll"/>가 정한다.</summary>
    private void BuildReroll(Transform w)
    {
        _rerollBtn = MakeButton(w, "Reroll", "새로고침", null, out _rerollLabel);
        _rerollImg = _rerollBtn.GetComponent<Image>();
        Place(_rerollBtn, GridX + (GridCols * CardStepX - CardStepX + CardW - RerollW) * 0.5f,
                          RerollY, RerollW, RerollH);
        _rerollBtn.onClick.AddListener(OnRerollClicked);
    }

    /// <summary>
    /// 새로고침 버튼 표시.
    ///
    /// 아트에 비용 <b>"10"이 그림으로 구워져</b> 있다. 실제 비용(<see cref="ShopRoomController.RerollCost"/>)은
    /// 인스펙터 값이라 언제든 달라질 수 있고, 그러면 화면이 <b>거짓 가격</b>을 보여준다.
    /// 그래서 아트가 진실일 때만 아트를 쓰고, 어긋나면 아트를 걷어내고 실제 값을 글자로 그린다 —
    /// 어떤 설정에서도 표기가 틀리지 않는다. (글자 없는 버튼 아트가 들어오면 이 분기는 지워도 된다.)
    /// </summary>
    private void RefreshReroll()
    {
        if (_rerollBtn == null) return;

        bool enabled = _controller != null && _controller.RerollEnabled;
        _rerollBtn.gameObject.SetActive(enabled);
        if (!enabled) return;

        // 골드가 모자라면 눌러도 거절음만 났다 — 살 수 있는지를 버튼이 먼저 보여준다(구매 버튼과 같은 규약).
        // 그러면 아트에 구워진 가격만으로는 '왜 안 되는지'를 말할 수 없으므로, 그때만 아트를 걷어내고
        // 사유를 글자로 그린다(가격이 어긋날 때 아트를 걷어내는 이 메서드의 기존 처리와 같은 방식).
        bool affordable  = _controller.PlayerGold >= _controller.RerollCost;
        bool artTruthful = _skin?.rerollButton != null && _controller.RerollCost == RerollCostBakedInArt && affordable;

        _rerollBtn.interactable = affordable;

        if (_rerollImg != null)
        {
            _rerollImg.sprite = artTruthful ? _skin.rerollButton : null;
            _rerollImg.type   = Image.Type.Simple;
            _rerollImg.color  = artTruthful ? Color.white : ShopUIStyle.BuyFill;
        }
        if (_rerollLabel != null)
        {
            _rerollLabel.gameObject.SetActive(!artTruthful);
            _rerollLabel.text = affordable
                ? $"새로고침  {_controller.RerollCost}"
                : $"<color=#FF5250>골드 부족</color>  {_controller.RerollCost}";   // 230px 버튼 — 사유를 앞에 짧게
        }
    }

    /// <summary>
    /// 나가기 — 완성본에는 없지만 ESC를 모르는 플레이어의 유일한 탈출구라 남긴다.
    /// 우측 양피지 아래 빈 나무 바닥에 둬서 목업의 구성을 가리지 않는다.
    /// </summary>
    private void BuildExitButton(Transform w)
    {
        var exit = MakeButton(w, "Exit", "나가기", null, out var lbl);
        lbl.fontSize = 16f;
        lbl.color    = ShopUIStyle.TextPrimary;
        exit.GetComponent<Image>().color = new Color(0.30f, 0.11f, 0.11f, 0.94f);
        // 상세판(SelY+SelH = 735)보다 아래에 둔다. RerollY-4(=714)면 판을 21px 파고들어
        // 양피지 위에 버튼이 얹힌다 — 완성본의 그 자리는 비어 있다.
        Place(exit, SelX + SelW - 110f, SelY + SelH + 10f, 110f, 44f);
        exit.onClick.AddListener(ClosePopupUI);
    }

    private static TMP_Text DetailRow(Transform s, string name, float my)
    {
        var t = ShopUIStyle.MakeText(s, name, 12.5f, FontStyles.Normal,
                                     TextAlignmentOptions.Top, new Color(0.24f, 0.17f, 0.10f, 1f));
        t.richText = true;
        t.textWrappingMode = TextWrappingModes.Normal;
        PlaceIn(s, t, 26f, my, 206f, 24f, SelW, SelH);
        return t;
    }

    // ── 상태 ──

    private void Select(int index)
    {
        _selectedIndex = index;
        RefreshCards();
        RefreshSelected();
    }

    private void RefreshAll()
    {
        if (_controller == null) return;

        CurrencyCounter.Apply(_goldText, _controller.PlayerGold);

        // 「상인의 인장」이 붙어 있으면 상인의 인사말 자리에 할인 사실을 얹는다.
        // 값만 조용히 싸지면 무엇 덕분인지 알 수 없다.
        if (_dialogText != null && AbyssPeddlerCatalog.MerchantSigil.Active)
            _dialogText.text = $"어서 오게, 빛이여   ·   상인의 인장 −{AbyssPeddlerCatalog.MerchantSigil.Discount * 100f:F0}%";

        RefreshReroll();
        RefreshDeal();
        RefreshCards();
        RefreshSelected();
    }

    private void RefreshDeal()
    {
        var deal = _controller?.SpecialDeal;
        if (_dealRoot != null) _dealRoot.SetActive(deal != null);
        if (deal == null) return;

        if (_dealCat != null)   _dealCat.text   = $"{deal.CategoryLabel} · {deal.EffectText}";
        if (_dealName != null)  _dealName.text  = deal.DisplayName;
        if (_dealDesc != null)  _dealDesc.text  = deal.DetailText;
        if (_dealPrice != null) _dealPrice.text = deal.Price.ToString();
        if (_dealOldPrice != null)
        {
            int orig = _controller.SpecialOriginalPrice;
            _dealOldPrice.text = orig > deal.Price ? orig.ToString() : "";
        }
        if (_dealIcon != null && deal.Icon != null) ShopUIStyle.Skin(_dealIcon, deal.Icon);
    }

    private void RefreshCards()
    {
        var products = _controller?.Products;
        int gold = _controller?.PlayerGold ?? 0;

        for (int i = 0; i < _cards.Count; i++)
        {
            var c = _cards[i];
            bool has = products != null && i < products.Count;
            if (c.Root != null) c.Root.SetActive(has);
            if (!has) continue;

            var p = products[i];
            int cat = Mathf.Clamp((int)p.Category, 0, CatColor.Length - 1);
            var col = CatColor[cat];
            bool selected = _selectedIndex == i;

            // 배지 — 아트가 있으면 바탕+글리프, 없으면 색 알약 + 코드 라벨.
            var badgeBg    = _skin?.CategoryBadgeBg(cat);
            var badgeGlyph = _skin?.CategoryBadge(cat);
            if (c.BadgeBg != null)
            {
                if (badgeBg != null) ShopUIStyle.Skin(c.BadgeBg, badgeBg, sliced: true);
                else                 c.BadgeBg.color = col;
            }
            if (c.BadgeGlyph != null)
            {
                bool hasGlyph = badgeGlyph != null;
                c.BadgeGlyph.gameObject.SetActive(hasGlyph);
                if (hasGlyph) ShopUIStyle.Skin(c.BadgeGlyph, badgeGlyph);
            }
            if (c.BadgeText != null)
            {
                // 글리프 아트가 있으면 글자가 겹치므로 끈다.
                c.BadgeText.gameObject.SetActive(badgeGlyph == null);
                c.BadgeText.text = p.CategoryLabel;
            }

            if (c.Name != null)   c.Name.text   = p.DisplayName;
            if (c.Effect != null) c.Effect.text = p.EffectText;
            if (c.Price != null)
            {
                c.Price.text  = p.Sold ? "품절" : p.Price.ToString();
                c.Price.color = p.Sold ? new Color(0.45f, 0.38f, 0.30f, 1f)
                              : (p.Price <= gold ? new Color(0.25f, 0.17f, 0.08f, 1f)
                                                 : new Color(0.60f, 0.15f, 0.12f, 1f));
            }
            if (c.PriceCoin != null) c.PriceCoin.gameObject.SetActive(!p.Sold);
            if (c.Icon != null && p.Icon != null) ShopUIStyle.Skin(c.Icon, p.Icon);

            // 품절 — 바탕을 바랜 양피지로 갈고 도장을 겹친다(전용 아트 없으면 틴트로 대체).
            if (c.Bg != null)
            {
                var art = _skin?.CardBackground(p.Sold);
                if (art != null)
                {
                    ShopUIStyle.Skin(c.Bg, art, sliced: true);
                    bool hasSoldArt = _skin.cardBgSold != null;
                    c.Bg.color = p.Sold && !hasSoldArt ? new Color(0.55f, 0.52f, 0.50f, 1f)
                               : selected ? Color.white
                                          : new Color(0.88f, 0.88f, 0.90f, 1f);
                }
                else
                {
                    c.Bg.color = p.Sold      ? new Color(0.10f, 0.10f, 0.12f, 0.9f)
                               : selected    ? new Color(0.22f, 0.19f, 0.12f, 0.98f)
                                             : new Color(0.13f, 0.12f, 0.16f, 0.96f);
                }
            }
            if (c.SoldStamp != null)
                c.SoldStamp.gameObject.SetActive(p.Sold && _skin?.soldStamp != null);

            if (c.Name != null)
                c.Name.color = selected ? new Color(0.52f, 0.20f, 0.05f, 1f)
                                        : new Color(0.18f, 0.12f, 0.06f, 1f);
        }
    }

    private ShopProduct Selected()
    {
        if (_controller == null) return null;
        if (_selectedIndex == SpecialIndex) return _controller.SpecialDeal;
        var list = _controller.Products;
        return (_selectedIndex >= 0 && _selectedIndex < list.Count) ? list[_selectedIndex] : null;
    }

    private void RefreshSelected()
    {
        var p = Selected();
        bool has = p != null;

        if (_buyBtn != null) _buyBtn.interactable = has && p.Purchasable && _controller.PlayerGold >= p.Price;
        if (_buyLabel != null) _buyLabel.text = has && p.Sold ? "품절" : "사겠네";

        if (_selNamePlate != null) _selNamePlate.gameObject.SetActive(has);
        if (_selPreview != null)   _selPreview.gameObject.SetActive(has);

        if (!has)
        {
            SetText(_selName, "");    SetText(_selCat, "");
            SetText(_selEffect, "");  SetText(_selApply, "");
            SetText(_selValue, "");   SetText(_selWorry, "");
            SetText(_selFlavor, "물건을 골라 보게.");
            return;
        }

        int cat = Mathf.Clamp((int)p.Category, 0, CatColor.Length - 1);
        SetText(_selName, p.DisplayName);
        if (_selCat != null) { _selCat.text = p.CategoryLabel; _selCat.color = CatColor[cat]; }
        SetText(_selEffect, p.EffectText);
        SetText(_selApply, p.DetailText);

        int gold = _controller.PlayerGold;
        SetText(_selValue, $"값  {p.Price}  <color=#7A6A54>(잔액 {Mathf.Max(0, gold - p.Price)})</color>");

        var deal = _controller.SpecialDeal;
        SetText(_selWorry, deal != null && deal != p && gold - p.Price < deal.Price
            ? $"특가 {deal.DisplayName}({deal.Price})까지는 못 산다"
            : "");
        SetText(_selFlavor, gold < p.Price ? "골드가 모자라는군." : "힘을 살 것인가, 위의 특가를 챙길 것인가.");

        if (_selPreview != null && p.Icon != null) ShopUIStyle.Skin(_selPreview, p.Icon);
    }

    // ── 핸들러 ──

    private void OnBuyClicked()
    {
        if (_controller == null) return;
        var result = _selectedIndex == SpecialIndex
            ? _controller.TryBuySpecial()
            : _controller.TryBuyProduct(_selectedIndex);

        ShopUIStyle.PlaySfx(result == ShopPurchaseResult.Success ? "shop_buy" : "shop_reject");
        if (result == ShopPurchaseResult.Success && _selectedIndex == SpecialIndex) _selectedIndex = -1;
        RefreshAll();
    }

    private void OnRerollClicked()
    {
        if (_controller == null) return;
        if (_controller.TryReroll()) { ShopUIStyle.PlaySfx("shop_reroll"); _selectedIndex = -1; }
        else ShopUIStyle.PlaySfx("shop_reject");
        RefreshAll();
    }

    // ── Helpers ──

    private static void SetText(TMP_Text t, string s) { if (t != null) t.text = s; }

    private static void FitLine(TMP_Text t)
    {
        if (t == null) return;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode     = TextOverflowModes.Ellipsis;
    }

    private static void AddClick(GameObject go, System.Action onClick)
    {
        var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
        ShopUIStyle.ApplyButtonColors(btn);
        btn.onClick.AddListener(() => onClick?.Invoke());
    }

    private static Button MakeButton(Transform parent, string name, string label, Sprite skin, out TMP_Text labelText)
    {
        var go = ShopUIStyle.MakeRect(parent, name, typeof(Image), typeof(Button));
        var img = go.GetComponent<Image>();
        img.color = ShopUIStyle.BuyFill;
        ShopUIStyle.Skin(img, skin, sliced: true);

        var btn = go.GetComponent<Button>();
        ShopUIStyle.ApplyButtonColors(btn, img);

        labelText = ShopUIStyle.MakeText(go.transform, "Label", 16f, FontStyles.Bold,
                                         TextAlignmentOptions.Center, ShopUIStyle.Gold);
        labelText.text = label;
        ShopUIStyle.Stretch(labelText.rectTransform);
        return btn;
    }
}
