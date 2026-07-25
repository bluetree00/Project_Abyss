using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 상점 — 심연의 행상 (Canvas_Popup, Addressable "UI/Popup/UI_ShopPanel").
///
/// 전체화면 구성(디자이너 완성본 0_상점_260724):
///  - 상단바: 등불 + "심연의 행상" + 대사 / 우측 골드 + [상품 돌리기]
///  - 오늘의 특가: 전 상품 중 무작위 1개를 할인해 상단 히어로로 노출(취소선 원가 + 할인가)
///  - 상품 2×3: 카테고리색 카드(버프=초록/룬=보라/재료=주황/포션=빨강) — 아이콘·이름·효과·가격
///  - 우측 "고른 물건": 미리보기 + 이름/효과/적용/값/고민 + [사겠네]
///
/// 데이터/구매는 <see cref="ShopRoomController"/>가 권위(Products·SpecialDeal·TryBuyProduct).
/// 아트는 <see cref="UISkin.Shop"/>, 미로드 시 색 폴백.
/// </summary>
public sealed class UI_ShopPanel : UI_Popup
{
    public override bool BlocksGameplay => true;
    public override bool CloseOnEscape  => true;

    // ── 레이아웃 ──
    // 완성본(전체샷.png)은 전체화면이 아니라 <b>장식 프레임을 두른 창</b>이다.
    // 창 크기 = 상점 테두리 아트 실치수(2471×1817@2x ÷ 2), 요소 크기도 전부 각 아트 실치수.
    // 위치는 완성본에서 창 좌상단 기준으로 재어 환산(가로 ×1.575 / 세로 ×1.566)했다.
    private const float WindowW    = 1235f;   // 상점 테두리(장식 포함 바깥 크기)
    private const float WindowH    = 908f;
    private const float BgW        = 1160f;   // 상점 배경(나무 좌판) — 의뢰서 04 "창 1160×800"과 일치
    private const float BgH        = 800f;
    private const float Margin     = 57f;
    private const float TopBarW    = 1103f;  // 심연의행상 초상 및 대사칸 2206×207@2x
    private const float TopBarH    = 103f;
    private const float RightColW  = 328f;   // 고른 물건 창 657×1143@2x
    private const float RightColH  = 571f;
    private const float DealW      = 760f;   // 특가 테두리 1521×193@2x
    private const float DealH      = 96f;
    private const float CardW      = 254f;   // 상품카드 배경 508×372@2x
    private const float CardH      = 186f;
    private const float CardStepX  = 257f;
    private const float CardStepY  = 219f;
    private const float GridTop    = 354f;   // 창 상단에서 첫 행 카드 위쪽까지
    private const int   GridCols   = 3;
    private const int   GridRows   = 2;

    private static readonly Color[] CatColor =
    {
        new(0.35f, 0.85f, 0.55f, 1f),   // 버프 초록
        new(0.72f, 0.52f, 0.95f, 1f),   // 룬  보라
        new(1.00f, 0.66f, 0.28f, 1f),   // 재료 주황
        new(0.95f, 0.40f, 0.38f, 1f),   // 포션 빨강
    };

    private ShopRoomController _controller;
    private ShopSkinSO _skin;

    private TMP_Text _goldText, _dialogText;
    private Button   _rerollBtn;

    // 특가
    private GameObject _dealRoot;
    private Image      _dealIcon;
    private TMP_Text   _dealCat, _dealName, _dealDesc, _dealOldPrice, _dealPrice;

    // 상품 카드
    private sealed class Card
    {
        public GameObject Root;
        public Image Bg, Accent, Badge, Icon;
        public TMP_Text BadgeText, Name, Effect, Price;
        public Button Btn;
    }
    private readonly List<Card> _cards = new();

    // 고른 물건
    private Image    _selPreview, _selBorder;
    private TMP_Text _selName, _selCat, _selEffect, _selApply, _selValue, _selWorry, _selFlavor;
    private Button   _buyBtn;
    private TMP_Text _buyLabel;

    private int _selectedIndex = -1;   // -1 = 특가 선택 없음/미선택, -2 = 특가
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
        if (_controller != null) _controller.OnShopChanged += RefreshAll;

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

    // ── Build ──

    private void BuildChrome()
    {
        if (_built) return;
        _built = true;

        ShopUIStyle.Stretch(GetComponent<RectTransform>());

        var veil = ShopUIStyle.MakeImage(transform, "Veil", ShopUIStyle.Veil, raycast: true);
        ShopUIStyle.Stretch(veil.rectTransform);

        // 창 = 배치 기준(1235×908). 그 안에 나무결 배경(1160×800)을 실치수로 놓는다 —
        // 배경을 프레임 크기로 늘리면 나뭇결이 5% 왜곡된다. 장식 프레임은 배경 밖으로 뻗으므로 별개 레이어.
        var half = new Vector2(0.5f, 0.5f);
        var window = ShopUIStyle.MakeImage(transform, "Window", new Color(0f, 0f, 0f, 0f), raycast: true);
        ShopUIStyle.Anchor(window.rectTransform, half, half, half, Vector2.zero, new Vector2(WindowW, WindowH));
        var w = window.transform;

        var bg = ShopUIStyle.MakeImage(w, "Bg", ShopUIStyle.WindowFill, raycast: true);
        ShopUIStyle.Anchor(bg.rectTransform, half, half, half, Vector2.zero, new Vector2(BgW, BgH));
        ShopUIStyle.Skin(bg, _skin?.background);

        BuildTopBar(w);
        BuildDeal(w);
        BuildGrid(w);
        BuildSelectedColumn(w);

        // 프레임은 마지막에 — 내용 위로 테두리 장식이 지나가야 완성본과 같은 액자가 된다.
        if (_skin?.windowBorder != null)
        {
            var border = ShopUIStyle.MakeImage(w, "Border", Color.white);
            ShopUIStyle.Anchor(border.rectTransform, half, half, half, Vector2.zero,
                               new Vector2(WindowW, WindowH));
            border.raycastTarget = false;
            ShopUIStyle.Skin(border, _skin.windowBorder);
            border.rectTransform.SetAsLastSibling();
        }

        // 별도 닫기 버튼은 두지 않는다 — 완성본에 없고, 닫기는 ESC와 상점 이탈로 이미 가능하다.
    }

    /// <summary>상단바 — 등불 + 심연의 행상 + 대사 / 골드 + 상품 돌리기.</summary>
    private void BuildTopBar(Transform w)
    {
        var band = ShopUIStyle.MakeImage(w, "TopBar", ShopUIStyle.BandFill);
        ShopUIStyle.Anchor(band.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -Margin), new Vector2(TopBarW, TopBarH));
        ShopUIStyle.Skin(band, _skin?.portraitBand, sliced: true);
        var b = band.transform;

        var lantern = ShopUIStyle.MakeImage(b, "Lantern", ShopUIStyle.Gold);
        ShopUIStyle.Anchor(lantern.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                           new Vector2(24, 4), new Vector2(28, 44));
        lantern.preserveAspect = true;
        ShopUIStyle.Skin(lantern, _skin?.lanternIcon);

        var name = ShopUIStyle.MakeText(b, "Name", 24f, FontStyles.Bold,
                                        TextAlignmentOptions.MidlineLeft, ShopUIStyle.TextPrimary);
        name.text = "심연의 행상";
        ShopUIStyle.Anchor(name.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                           new Vector2(62, -12), new Vector2(360, 34));

        _dialogText = ShopUIStyle.MakeText(b, "Dialog", 15f, FontStyles.Normal,
                                           TextAlignmentOptions.MidlineLeft, ShopUIStyle.TextDim);
        _dialogText.text = "어서 오게, 빛이여";
        ShopUIStyle.Anchor(_dialogText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                           new Vector2(62, -46), new Vector2(420, 26));

        // 상품 돌리기(우측 끝) — 아트에 "상품 돌리기"가 구워져 있어 라벨을 지운다.
        _rerollBtn = MakeButton(b, "Reroll", "상품 돌리기", _skin?.rerollButton, out var rerollLbl);
        ShopUIStyle.Anchor((RectTransform)_rerollBtn.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                           new Vector2(1, 0.5f), new Vector2(-22, 0), new Vector2(121, 43));
        HideLabelIfArtHasText(_rerollBtn, rerollLbl, _skin?.rerollButton);
        _rerollBtn.onClick.AddListener(OnRerollClicked);

        // 골드(리롤 왼쪽)
        _goldText = ShopUIStyle.MakeText(b, "Gold", 20f, FontStyles.Bold,
                                         TextAlignmentOptions.MidlineRight, ShopUIStyle.Gold);
        ShopUIStyle.Anchor(_goldText.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                           new Vector2(-176, 0), new Vector2(200, 34));

        BuildExitButton(w);
    }

    /// <summary>
    /// 나가기 버튼 — 창 우상단 모서리. 상단바 안쪽은 리롤·골드가 차지하고 있어 그 위 모서리에 둔다.
    /// ClosePopupUI로 상점을 닫는다(ESC와 같은 종료 경로).
    /// </summary>
    private void BuildExitButton(Transform w)
    {
        var exit = MakeButton(w, "Exit", "나가기", null, out var lbl);
        lbl.fontSize = 16f;
        lbl.color    = ShopUIStyle.TextPrimary;
        exit.GetComponent<Image>().color = new Color(0.42f, 0.16f, 0.16f, 0.96f);
        ShopUIStyle.Anchor((RectTransform)exit.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                           new Vector2(-Margin + 4f, -12f), new Vector2(96, 40));
        exit.onClick.AddListener(ClosePopupUI);
    }

    /// <summary>오늘의 특가 — 탭 라벨 + 히어로 카드(아이콘/이름/설명/원가·할인가).</summary>
    private void BuildDeal(Transform w)
    {
        var topLeft = new Vector2(0, 1);

        // "오늘의 특가" 탭 — 아트(특가 표시)에 글자가 구워져 있어 라벨은 아트가 없을 때만 그린다.
        var tab = ShopUIStyle.MakeImage(w, "DealTab", new Color(0.72f, 0.22f, 0.18f, 1f));
        ShopUIStyle.Anchor(tab.rectTransform, topLeft, topLeft, topLeft,
                           new Vector2(Margin, -209f), new Vector2(150, 36));
        ShopUIStyle.Skin(tab, _skin?.dealTab);
        if (_skin?.dealTab == null)
        {
            var tabTxt = ShopUIStyle.MakeText(tab.transform, "T", 15f, FontStyles.Bold,
                                              TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
            tabTxt.text = "오늘의 특가";
            ShopUIStyle.Stretch(tabTxt.rectTransform);
        }

        // 히어로 카드
        var card = ShopUIStyle.MakeImage(w, "Deal", new Color(0.20f, 0.09f, 0.08f, 0.96f), raycast: true);
        _dealRoot = card.gameObject;
        ShopUIStyle.Anchor(card.rectTransform, topLeft, topLeft, topLeft,
                           new Vector2(Margin, -241f), new Vector2(DealW, DealH));
        ShopUIStyle.Skin(card, _skin?.dealBorder, sliced: true);
        AddClick(card.gameObject, () => Select(SpecialIndex));

        var inner = ShopUIStyle.MakeImage(card.transform, "Inner", new Color(1f, 1f, 1f, 0f));
        ShopUIStyle.Stretch(inner.rectTransform, 8f);
        ShopUIStyle.Skin(inner, _skin?.dealContentBg, sliced: true);

        _dealIcon = ShopUIStyle.MakeImage(card.transform, "Icon", new Color(0.05f, 0.05f, 0.07f, 1f));
        ShopUIStyle.Anchor(_dealIcon.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                           new Vector2(26, 0), new Vector2(74, 74));
        _dealIcon.preserveAspect = true;
        ShopUIStyle.Skin(_dealIcon, _skin?.dealItemBg, sliced: true);

        _dealCat = ShopUIStyle.MakeText(card.transform, "Cat", 13f, FontStyles.Normal,
                                        TextAlignmentOptions.TopLeft, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(_dealCat.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                           new Vector2(116, -18), new Vector2(420, 20));

        _dealName = ShopUIStyle.MakeText(card.transform, "Name", 22f, FontStyles.Bold,
                                         TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(_dealName.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                           new Vector2(116, -40), new Vector2(420, 30));

        _dealDesc = ShopUIStyle.MakeText(card.transform, "Desc", 12.5f, FontStyles.Normal,
                                         TextAlignmentOptions.TopLeft, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(_dealDesc.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                           new Vector2(116, -72), new Vector2(460, 22));

        _dealOldPrice = ShopUIStyle.MakeText(card.transform, "OldPrice", 14f, FontStyles.Strikethrough,
                                             TextAlignmentOptions.MidlineRight, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(_dealOldPrice.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                           new Vector2(-28, -18), new Vector2(120, 22));

        _dealPrice = ShopUIStyle.MakeText(card.transform, "Price", 26f, FontStyles.Bold,
                                          TextAlignmentOptions.MidlineRight, ShopUIStyle.Gold);
        ShopUIStyle.Anchor(_dealPrice.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                           new Vector2(-28, -42), new Vector2(120, 34));
    }

    /// <summary>상품 2×3 그리드 — 카테고리 악센트/배지 카드.</summary>
    private void BuildGrid(Transform w)
    {
        // 카드 크기와 간격은 완성본 실측값 고정 — 영역을 꽉 채우려 셀을 역산하면
        // 카드 비율이 아트(508×372@2x)와 어긋나 배경이 늘어난다.
        for (int i = 0; i < GridCols * GridRows; i++)
            _cards.Add(BuildCard(w, i));
    }

    private Card BuildCard(Transform parent, int index)
    {
        int captured = index;
        var c = new Card();

        var topLeft = new Vector2(0, 1);
        var bg = ShopUIStyle.MakeImage(parent, $"Card{index}", new Color(0.13f, 0.12f, 0.16f, 0.96f), raycast: true);
        c.Root = bg.gameObject; c.Bg = bg;
        ShopUIStyle.Anchor(bg.rectTransform, topLeft, topLeft, topLeft,
                           new Vector2(Margin + (index % GridCols) * CardStepX,
                                       -(GridTop + (index / GridCols) * CardStepY)),
                           new Vector2(CardW, CardH));
        ShopUIStyle.Skin(bg, _skin?.cardBg, sliced: true);
        AddClick(bg.gameObject, () => Select(captured));

        // 좌측 카테고리 악센트(상품버프/룬/재료/포션 22×314@2x — 카드 높이를 타고 흐르는 세로 띠)
        c.Accent = ShopUIStyle.MakeImage(bg.transform, "Accent", CatColor[0]);
        ShopUIStyle.Anchor(c.Accent.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f),
                           new Vector2(8, 0), new Vector2(11, -22));

        // 아이콘
        c.Icon = ShopUIStyle.MakeImage(bg.transform, "Icon", new Color(0.05f, 0.05f, 0.07f, 1f));
        ShopUIStyle.Anchor(c.Icon.rectTransform, topLeft, topLeft, topLeft,
                           new Vector2(26, -16), new Vector2(58, 58));
        c.Icon.preserveAspect = true;

        // 카테고리 배지(우상단) — 아트는 색 알약뿐이라 글자는 우리가 얹는다. 120×43@2x
        c.Badge = ShopUIStyle.MakeImage(bg.transform, "Badge", CatColor[0]);
        ShopUIStyle.Anchor(c.Badge.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                           new Vector2(-12, -14), new Vector2(60, 22));
        c.BadgeText = ShopUIStyle.MakeText(c.Badge.transform, "T", 12f, FontStyles.Bold,
                                           TextAlignmentOptions.Center, new Color(0.1f, 0.09f, 0.12f));
        ShopUIStyle.Stretch(c.BadgeText.rectTransform);

        c.Name = ShopUIStyle.MakeText(bg.transform, "Name", 16f, FontStyles.Bold,
                                      TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(c.Name.rectTransform, topLeft, new Vector2(1, 1), topLeft,
                           new Vector2(26, -84), new Vector2(-38, 24));
        FitLine(c.Name);

        c.Effect = ShopUIStyle.MakeText(bg.transform, "Effect", 12.5f, FontStyles.Normal,
                                        TextAlignmentOptions.TopLeft, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(c.Effect.rectTransform, topLeft, new Vector2(1, 1), topLeft,
                           new Vector2(26, -110), new Vector2(-38, 20));
        FitLine(c.Effect);

        c.Price = ShopUIStyle.MakeText(bg.transform, "Price", 17f, FontStyles.Bold,
                                       TextAlignmentOptions.MidlineRight, ShopUIStyle.Gold);
        ShopUIStyle.Anchor(c.Price.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0),
                           new Vector2(0, 14), new Vector2(-28, 24));
        return c;
    }

    /// <summary>우측 "고른 물건" — 미리보기 + 상세 + [사겠네].</summary>
    private void BuildSelectedColumn(Transform w)
    {
        var topRight = new Vector2(1, 1);
        var col = ShopUIStyle.MakeImage(w, "Selected", new Color(0.11f, 0.11f, 0.14f, 0.96f));
        ShopUIStyle.Anchor(col.rectTransform, topRight, topRight, topRight,
                           new Vector2(-Margin + 7f, -247f), new Vector2(RightColW, RightColH));
        ShopUIStyle.Skin(col, _skin?.selectedWindow, sliced: true);
        var s = col.transform;

        var head = ShopUIStyle.MakeText(s, "Head", 15f, FontStyles.Bold,
                                        TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
        head.text = "고른 물건";
        ShopUIStyle.Anchor(head.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(20, -14), new Vector2(-40, 22));

        _selPreview = ShopUIStyle.MakeImage(s, "Preview", new Color(0.04f, 0.05f, 0.05f, 1f));
        ShopUIStyle.Anchor(_selPreview.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -44), new Vector2(-36, 190));
        ShopUIStyle.Skin(_selPreview, _skin?.selectedBg, sliced: true);

        _selBorder = ShopUIStyle.MakeImage(_selPreview.transform, "Border", new Color(1f, 1f, 1f, 0f));
        ShopUIStyle.Stretch(_selBorder.rectTransform);
        _selBorder.raycastTarget = false;

        _selName = ShopUIStyle.MakeText(s, "Name", 20f, FontStyles.Bold,
                                        TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(_selName.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(20, -244), new Vector2(-40, 28));

        _selCat = ShopUIStyle.MakeText(s, "Cat", 13f, FontStyles.Bold,
                                       TextAlignmentOptions.TopLeft, CatColor[0]);
        ShopUIStyle.Anchor(_selCat.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(20, -272), new Vector2(-40, 22));

        _selEffect = DetailRow(s, "Effect", -304);
        _selApply  = DetailRow(s, "Apply",  -334);
        _selValue  = DetailRow(s, "Value",  -364);
        _selWorry  = DetailRow(s, "Worry",  -394);

        _selFlavor = ShopUIStyle.MakeText(s, "Flavor", 13f, FontStyles.Italic,
                                          TextAlignmentOptions.TopLeft, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(_selFlavor.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(20, -430), new Vector2(-40, 40));
        _selFlavor.textWrappingMode = TextWrappingModes.Normal;

        // 구매버튼 아트에 "사겠네"가 구워져 있다 — 라벨을 지우지 않으면 두 벌이 겹쳐 읽힌다.
        _buyBtn = MakeButton(s, "Buy", "사겠네", _skin?.buyButton, out _buyLabel);
        ShopUIStyle.Anchor((RectTransform)_buyBtn.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                           new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(301, 61));
        HideLabelIfArtHasText(_buyBtn, _buyLabel, _skin?.buyButton);
        _buyBtn.onClick.AddListener(OnBuyClicked);
    }

    /// <summary>
    /// 아트에 글자가 이미 구워진 버튼의 코드 라벨을 끈다. 아트가 없으면(색 폴백) 라벨을 남겨
    /// 무슨 버튼인지 알 수 있게 한다 — 스킨 0장 상태에서도 화면이 성립해야 하기 때문.
    /// </summary>
    private static void HideLabelIfArtHasText(Button btn, TMP_Text label, Sprite art)
    {
        if (btn == null || label == null) return;
        if (art != null) label.gameObject.SetActive(false);
    }

    private static TMP_Text DetailRow(Transform s, string name, float y)
    {
        var t = ShopUIStyle.MakeText(s, name, 13f, FontStyles.Normal,
                                     TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
        t.richText = true;
        ShopUIStyle.Anchor(t.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(20, y), new Vector2(-40, 26));
        return t;
    }


    // ── 상태 ──

    private void Select(int index)
    {
        _selectedIndex = index;
        RefreshCards();      // 선택 표시(금색 이름·밝은 카드)가 카드 쪽에 있으므로 같이 갱신
        RefreshSelected();
    }

    private void RefreshAll()
    {
        if (_controller == null) return;

        if (_goldText != null) _goldText.text = $"● {_controller.PlayerGold} 골드";
        if (_rerollBtn != null) _rerollBtn.gameObject.SetActive(_controller.RerollEnabled);

        RefreshDeal();
        RefreshCards();
        RefreshSelected();
    }

    private void RefreshDeal()
    {
        var deal = _controller?.SpecialDeal;
        if (_dealRoot != null) _dealRoot.SetActive(deal != null);
        if (deal == null) return;

        int cat = (int)deal.Category;
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

            if (c.Accent != null) { c.Accent.color = col; ShopUIStyle.Skin(c.Accent, _skin?.CardAccent(cat), sliced: true); }
            if (c.Badge != null)  { c.Badge.color  = col; ShopUIStyle.Skin(c.Badge,  _skin?.CategoryBadge(cat), sliced: true); }
            if (c.BadgeText != null) c.BadgeText.text = p.CategoryLabel;
            if (c.Name != null)   c.Name.text   = p.DisplayName;
            if (c.Effect != null) c.Effect.text = p.EffectText;
            if (c.Price != null)
            {
                c.Price.text  = p.Sold ? "품절" : $"● {p.Price}";
                c.Price.color = p.Sold ? ShopUIStyle.TextDim
                              : (p.Price <= gold ? ShopUIStyle.Gold : ShopUIStyle.RejectRed);
            }
            if (c.Icon != null && p.Icon != null) ShopUIStyle.Skin(c.Icon, p.Icon);

            // 의뢰서 07 S14 상태 — 기본 / 선택됨(밝게) / 품절(어둡게).
            // 선택 표시가 없으면 우측 상세가 어느 카드 것인지 알 수 없다.
            bool selected = _selectedIndex == i;
            if (c.Bg != null)
            {
                if (p.Sold)              c.Bg.color = new Color(0.10f, 0.10f, 0.12f, 0.9f);
                else if (_skin?.cardBg != null)
                                         c.Bg.color = selected ? Color.white : new Color(0.72f, 0.72f, 0.76f, 1f);
                else                     c.Bg.color = selected ? new Color(0.22f, 0.19f, 0.12f, 0.98f)
                                                              : new Color(0.13f, 0.12f, 0.16f, 0.96f);
            }
            if (c.Name != null) c.Name.color = selected ? ShopUIStyle.Gold : ShopUIStyle.TextPrimary;
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
        if (_selCat != null) { _selCat.text = $"{p.CategoryLabel} · {p.EffectText}"; _selCat.color = CatColor[cat]; }
        SetText(_selEffect, $"효과  {p.EffectText}");
        SetText(_selApply, p.DetailText);

        int gold = _controller.PlayerGold;
        SetText(_selValue, $"값  골드 {p.Price}  <color=#9A94A6>(잔액 {Mathf.Max(0, gold - p.Price)})</color>");

        var deal = _controller.SpecialDeal;
        SetText(_selWorry, deal != null && deal != p && gold - p.Price < deal.Price
            ? $"고민  특가 {deal.DisplayName}({deal.Price})까지는 못 산다"
            : "");
        SetText(_selFlavor, gold < p.Price ? "골드가 모자라는군." : "힘을 살 것인가, 위의 특가를 챙길 것인가.");

        if (_selBorder != null)
        {
            var border = _skin?.SelectedBorder(cat);
            if (border != null) ShopUIStyle.Skin(_selBorder, border, sliced: true);
            else _selBorder.color = new Color(CatColor[cat].r, CatColor[cat].g, CatColor[cat].b, 0.35f);
        }
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
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => onClick?.Invoke());
    }

    private static Button MakeButton(Transform parent, string name, string label, Sprite skin, out TMP_Text labelText)
    {
        var go = ShopUIStyle.MakeRect(parent, name, typeof(Image), typeof(Button));
        var img = go.GetComponent<Image>();
        img.color = ShopUIStyle.BuyFill;
        ShopUIStyle.Skin(img, skin, sliced: true);

        var btn = go.GetComponent<Button>();
        var cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        cb.pressedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
        cb.disabledColor = new Color(1f, 1f, 1f, 0.4f);
        cb.fadeDuration = 0.08f;
        btn.colors = cb;

        labelText = ShopUIStyle.MakeText(go.transform, "Label", 16f, FontStyles.Bold,
                                         TextAlignmentOptions.Center, ShopUIStyle.Gold);
        labelText.text = label;
        ShopUIStyle.Stretch(labelText.rectTransform);
        return btn;
    }
}
