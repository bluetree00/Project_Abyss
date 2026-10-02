using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
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
    // 09-29: 내용(간판~고른 물건 종이)이 창 가운데보다 14px 왼쪽이었다 — 모서리 장식은 창 대칭이라 오른쪽 장식과 종이 사이만
    // 빈 나무가 넓게 남았다(사용자 「테두리 레이아웃 · 공간 비율이 안 맞는다」). 띠 · 배경 · 모서리를 뺀 내용 전체를 옮긴다.
    private const float ContentDX  =  14f;
    private const float BandY      =  14f, BandH     =  91f;
    private const float TitleX     =  95f + ContentDX, TitleY    =  15f, TitleW  = 360f, TitleH = 166f;
    private const float CrestX     = 946f + ContentDX, CrestY    =   2f, CrestW  =  80f, CrestH = 118f;
    private const float CornerW    =  73f, CornerH   = 108f, CornerInsetX = 58f;
    private const float CornerTopY =   8f, CornerBottomY = 690f;
    private const float GoldX      = 752f + ContentDX, GoldY     =  56f, GoldIconSize = 22f;
    // 간판 글자 칸 — 간판 아트의 쇠띠(판 폭의 약 78%) 앞까지. 인사말이 쇠띠를 넘어 꼬리까지 갔다(09-29).
    private const float TitleTextX = TitleX + 100f, TitleTextW = 172f;
    // ★ 목업 아트를 전체샷에 템플릿 매칭해 실측한 값(상관 0.97/0.91).
    //   ⚠️ 바깥 rect를 고칠 때 <b>내부를 배율로 키우면 안 된다</b> — 내부 요소는 카드 아트의 일부가 아니라
    //   그 위에 얹힌 별개 이미지라, 목업에서 <b>절대 위치가 고정</b>이다. rect 원점이 움직인 만큼만 옮긴다.
    //   (배율로 키웠더니 코인·배지가 우하단으로 18px 밀려 카드 밖으로 걸쳤다.)
    //   예전 값은 <b>아트 rect가 아니라 눈으로 잰 "보이는 판"</b>이라 카드가 13% 작았다.
    //   아트에는 투명 여백이 있어(카드 좌2.1%·우4.9%·상2.2%·하5.2%) rect는 보이는 판보다 커야 한다.
    private const float DealX      =  95f + ContentDX, DealY     = 158f, DealW   = 625f, DealH  = 158f;
    private const float DealInY    =   8f;   // 특가 안 글자 · 그림을 내리는 몫 — 분류 줄이 종이 윗가장자리(찢긴 테)에 붙었다(09-29)
    // 09-25 빈 공간 정리(사용자 지적 「하단 빈 공간이 많고 외곽이 낭비된다」): 카드는 세로로 늘려 카드 아래 빈 띠를 쓰고
    // (카드 아트는 9-slice라 세로로 늘려도 테두리가 그대로다), 새로고침·나가기는 그 아래 한 줄로 모은다.
    // 09-29: 카드 222 · 간격 217이면 새로고침 줄이 아래 테두리(826)에 20px까지 붙었다(위 여백 57과 어긋남).
    // 카드 205(보이는 190) · 간격 199.5 · 새로고침 725 → 아래 여백도 57. 카드 안 요소는 205 안으로 다시 앉혔다(BuildCard).
    private const float GridX      = 110f + ContentDX, GridY     = 316f;
    private const float CardW      = 205f, CardH     = 205f;
    private const float CardStepX  = 201.5f, CardStepY = 199.5f;
    private const int   GridCols   = 3, GridRows = 2;
    private const float RerollW    = 230f, RerollH   =  53f, RerollY = 725f;   // 아트는 rect 안 6~44에 보인다
    // 구매창도 카드와 같은 문제였다 — 아트 rect가 아니라 눈으로 잰 "보이는 종이"를 썼다.
    // 구매창.png(885×1811)는 좌1.7%·우3.8%·상0.8%·하1.8%가 투명이라 rect가 그만큼 커야 한다.
    // 09-25: 오른쪽 빈 띠를 쓰도록 290×594 → 312×638(아트 비율 0.489 유지). 안쪽 배치는 예전 설계 크기(SelDW×SelDH)
    // 기준 좌표 그대로 두고 비율로 따라 커진다 — 좌표를 다시 재지 않아도 된다.
    private const float SelX       = 736f + ContentDX, SelY      = 142f, SelW    = 312f, SelH   = 638f;
    private const float SelDW      = 290f, SelDH     = 594f;

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
        // 룬 선택 규격(09-25) — 아이콘 칸 등급 테두리·빛, 등급/칸 수 태그, 가격 알약
        public Image IconFrame, IconGlow, Tag0Bg, Tag1Bg, PricePill;
        public TMP_Text Tag0, Tag1;
        public Vector2 Base;   // 쉬는 자리(고르면 CardLift만큼 들린다)
    }
    private const float CardLift   = 6f;
    private const float DealStagger = 0.06f, DealDur = 0.26f, DealRise = 22f;
    private System.Threading.CancellationTokenSource _openFxCts;
    private readonly List<Card> _cards = new();

    // 고른 물건
    [SerializeField] private Image    _selPreview, _selNamePlate, _selGlow;
    [SerializeField] private RectTransform _selShape;   // 룬 블록 모양(큰 아이콘 오른쪽) — 룬 선택 카드처럼 모양을 보고 산다
    [SerializeField] private TMP_Text _selName, _selCat, _selEffect, _selFlavor;   // _selEffect = 상세 한 덩어리(09-25)
    [SerializeField] private Button   _buyBtn;
    [SerializeField] private TMP_Text _buyLabel;

    private int _selectedIndex = -1;   // -1 = 미선택, -2 = 특가
    private const int SpecialIndex = -2;

    private bool _built, _closing;

    // ── 거래의 손맛(기획_상점개편) — 동전 빠짐 · 상인 반응 · 물건 → 받는 곳 · 특가 딱지 → 품절 ──
    private const float CoinFallDur = 0.55f, ItemFlyDur = 0.62f, StampPopDur = 0.38f, DealHoldDur = 0.75f;
    private const int   CoinCount   = 5;
    private const int   ReactionMs  = 2600;   // 상인 반응이 인사말로 돌아가기까지

    [SerializeField] private RectTransform _fxRoot;      // 날아가는 물건·동전이 노는 층(창 맨 위)
    [SerializeField] private TMP_Text      _dealStock;   // 특가 「1개 남음」 → 「품절」
    [SerializeField] private Image         _dealStamp;   // 특가 품절 도장

    private Image[] _coins;       // 골드 칸에서 떨어지는 동전(풀)
    private Image   _flyIcon;     // 산 물건이 받는 곳으로 날아가는 그림
    private bool    _buying;      // 연출 중 재구매 잠금(한 번에 한 건)
    private bool    _dealHold;    // 특가 연출 동안 좌판에 붙잡아 둔다(컨트롤러는 이미 비웠다)

    // ── 창 액자 · 여는 연출 (09-28) ── 판이 사각형으로 뚝 잘려 「잘린 페이지」처럼 보였다
    private const float ShopVeilAlpha  = 0.95f;   // 공용 막 0.8은 선형 색공간에서 체감 절반 — 판 밖 HUD가 비쳤다(0.9도 읽혔다)
    private const float WindowInSec    = 0.26f;
    private const float WindowInScale  = 0.965f;
    private const float ShadowOutset   = 30f;
    private const float RibbonNotch    = 14f;     // 이름 명판 양끝이 파인 깊이(px)
    private static readonly Color FrameDark  = new(0.05f, 0.03f, 0.02f, 1f);
    private static readonly Color FrameTrim  = new(0.62f, 0.43f, 0.22f, 0.55f);
    private static readonly Color EdgeShade  = new(0f, 0f, 0f, 0.55f);
    private static Sprite s_edgeVignette;
    private int     _reactionGen; // 상인 반응 문구 세대
    private TMP_Text _sigilText;  // 골드 밑 「상인의 인장 −15%」(런타임 생성 — 구운 프리팹에 없다)
    private static Sprite s_ribbon;

    // ── Lifecycle ──

    public override void Init()
    {
        base.Init();
        _skin = UISkin.Shop;
        BuildChrome();
        EnsureWindowFrame();
        FitCardTags();
        StyleRuntime();
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
        PlayWindowInAsync().Forget();   // 판이 먼저 떠오르고
        PlayOpenFxAsync().Forget();     // 카드가 내려앉는다(겹쳐 흐른다)
    }

    private bool _dealing;   // 진열 연출 중 — 카드 자리는 연출이 쥔다(RefreshCards가 들림으로 덮지 않게)

    /// <summary>
    /// 열기 연출 — 상품 카드가 60ms 간격으로 좌판에 내려앉고(아래에서 22px), 고른 물건 종이가 옆에서 들어온다.
    /// 룬 선택 팝업의 카드 등장과 같은 결이다. 상점은 방문당 한 번 여는 화면이라 끝까지 0.6초 남짓.
    /// </summary>
    private async UniTaskVoid PlayOpenFxAsync()
    {
        _openFxCts?.Cancel();
        _openFxCts?.Dispose();
        _openFxCts = new System.Threading.CancellationTokenSource();
        var ct = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(_openFxCts.Token, destroyCancellationToken).Token;

        var sel = transform.Find("Window/Selected") as RectTransform;
        var selBase = sel != null ? sel.anchoredPosition : Vector2.zero;
        var groups = new CanvasGroup[_cards.Count];
        for (int i = 0; i < _cards.Count; i++)
        {
            if (_cards[i].Root == null) continue;
            // ?? 금지 — 에디터의 GetComponent는 없을 때 가짜 null을 돌려줘 ??를 통과한다(MissingComponentException, 09-25 실측)
            if (!_cards[i].Root.TryGetComponent(out groups[i])) groups[i] = _cards[i].Root.AddComponent<CanvasGroup>();
            groups[i].alpha = 0f;
        }

        _dealing = true;
        float total = (_cards.Count - 1) * DealStagger + DealDur;
        try
        {
            for (float t = 0f; t < total; )
            {
                t += Time.unscaledDeltaTime;
                for (int i = 0; i < _cards.Count; i++)
                {
                    var c = _cards[i];
                    if (c.Root == null || groups[i] == null) continue;
                    float k = Mathf.Clamp01((t - i * DealStagger) / DealDur);
                    float e = 1f - Mathf.Pow(1f - k, 3f);
                    groups[i].alpha = k;
                    var lift = _selectedIndex == i ? new Vector2(0f, CardLift) : Vector2.zero;
                    ((RectTransform)c.Root.transform).anchoredPosition = c.Base + lift + new Vector2(0f, -DealRise * (1f - e));
                }
                if (sel != null)
                {
                    float ks = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / DealDur), 3f);
                    sel.anchoredPosition = selBase + new Vector2(18f * (1f - ks), 0f);
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _dealing = false;
            for (int i = 0; i < _cards.Count; i++)
            {
                if (groups[i] != null) groups[i].alpha = 1f;
                var c = _cards[i];
                if (c.Root != null)
                    ((RectTransform)c.Root.transform).anchoredPosition = c.Base + (_selectedIndex == i ? new Vector2(0f, CardLift) : Vector2.zero);
            }
            if (sel != null) sel.anchoredPosition = selBase;
        }
    }

    /// <summary>
    /// 판을 액자로 — 바깥 그림자 · 어두운 테두리선 + 안쪽 나무 테두리 · 가장자리 음영. 목업(상점 전체이미지)의 가장자리처럼
    /// 판이 한 장의 물건으로 읽히게 한다. 구운 프리팹에도 붙도록 런타임에 한 번 짓는다. 막은 이 화면만 짙게.
    /// </summary>
    private void EnsureWindowFrame()
    {
        if (transform.Find("Veil") is RectTransform veilRT && veilRT.TryGetComponent<Image>(out var veil))
            veil.color = new Color(0f, 0f, 0f, ShopVeilAlpha);

        if (!(transform.Find("Window") is RectTransform win) || win.Find("FrameShadow") != null) return;

        // 바깥 그림자 — 창 밖으로 번지는 부드러운 어둠(창보다 먼저 = 판 뒤).
        var shadow = ShopUIStyle.MakeImage(win, "FrameShadow", new Color(0f, 0f, 0f, 0.75f));
        shadow.sprite = UIProceduralSprites.RoundedRect(radius: 30f, feather: 28f);
        shadow.type   = Image.Type.Sliced;
        shadow.raycastTarget = false;
        ShopUIStyle.Stretch(shadow.rectTransform, -ShadowOutset);
        shadow.transform.SetAsFirstSibling();

        // 배경(Bg) 바로 위 = 내용 아래: 가장자리 음영 → 테두리선 → 안쪽 나무 테두리
        var bg = win.Find("Bg");
        int at = bg != null ? bg.GetSiblingIndex() + 1 : 1;

        var shade = ShopUIStyle.MakeImage(win, "FrameEdgeShade", EdgeShade);
        shade.sprite = EdgeVignette();
        shade.type   = Image.Type.Sliced;
        shade.pixelsPerUnitMultiplier = 0.5f;   // 가장자리 폭 24 → 48px
        shade.raycastTarget = false;
        ShopUIStyle.Stretch(shade.rectTransform, 0f);
        shade.transform.SetSiblingIndex(at);

        var dark = ShopUIStyle.MakeImage(win, "FrameLine", FrameDark);
        dark.sprite = UIProceduralSprites.RoundedOutline(radius: 6f, stroke: 5f);
        dark.type   = Image.Type.Sliced;
        dark.raycastTarget = false;
        ShopUIStyle.Stretch(dark.rectTransform, 0f);
        dark.transform.SetSiblingIndex(at + 1);

        var trim = ShopUIStyle.MakeImage(win, "FrameTrim", FrameTrim);
        trim.sprite = UIProceduralSprites.RoundedOutline(radius: 4f, stroke: 1.5f);
        trim.type   = Image.Type.Sliced;
        trim.raycastTarget = false;
        ShopUIStyle.Stretch(trim.rectTransform, 6f);
        trim.transform.SetSiblingIndex(at + 2);
    }

    /// <summary>
    /// 구운 프리팹에도 입히는 모양 — 코드로 그린 스프라이트는 프리팹에 저장되지 않는다(09-29).
    /// 이름 명판 = 양끝이 파인 띠(목업의 붉은 리본) · 나가기 = 새로고침과 같은 짙은 명판 · 인사말은 간판 쇠띠 앞까지 · 인장 할인 줄.
    /// </summary>
    private void StyleRuntime()
    {
        if (_selNamePlate != null)
        {
            var rib = RibbonSprite();
            _selNamePlate.sprite = rib;
            _selNamePlate.type   = Image.Type.Sliced;
            if (_selNamePlate.transform.Find("Fill") is RectTransform f && f.TryGetComponent<Image>(out var fill))
            {
                fill.sprite = rib;
                fill.type   = Image.Type.Sliced;
            }
            if (_selName != null) _selName.margin = new Vector4(RibbonNotch, 0f, RibbonNotch, 0f);
        }

        if (transform.Find("Window/Exit") is RectTransform exitRt && exitRt.TryGetComponent<Image>(out var exitImg))
        {
            // 새로고침 아트(짙은 먹 판 + 금빛 테)와 같은 말투 — 붉은 네모가 이 화면에서만 따로 놀았다.
            UITheme.StylePanel(exitImg, new Color(0.11f, 0.085f, 0.07f, 0.96f), new Color(0.80f, 0.62f, 0.32f, 0.75f), 6f);
            var lbl = exitRt.GetComponentInChildren<TMP_Text>(true);
            if (lbl != null) lbl.color = new Color(0.93f, 0.86f, 0.70f, 1f);
        }

        if (_dialogText != null)
        {
            Place(_dialogText, TitleTextX, TitleY + 58f, TitleTextW, 30f);
            _dialogText.textWrappingMode = TextWrappingModes.Normal;
            _dialogText.enableAutoSizing = true;
            _dialogText.fontSizeMax = 16f;
            _dialogText.fontSizeMin = 12f;
        }

        if (_sigilText == null && transform.Find("Window") is RectTransform win)
        {
            var found = win.Find("SigilLine");
            _sigilText = found != null ? found.GetComponent<TMP_Text>()
                       : ShopUIStyle.MakeText(win, "SigilLine", 16f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft,
                                              new Color(0.88f, 0.72f, 0.42f, 1f));
            Place(_sigilText, GoldX, GoldY + 24f, 240f, 22f);
            _sigilText.textWrappingMode = TextWrappingModes.NoWrap;
            _sigilText.raycastTarget    = false;
            _sigilText.text = string.Empty;
        }
    }

    /// <summary>이름 명판 — 양끝이 제비꼬리로 파인 띠(목업의 붉은 리본). 전용 아트가 납품되지 않아 한 번 굽는다.</summary>
    private static Sprite RibbonSprite()
    {
        if (s_ribbon != null) return s_ribbon;
        const int W = 128, H = 48;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float cy = Mathf.Abs(y + 0.5f - H * 0.5f) / (H * 0.5f);   // 0 = 가운데, 1 = 위아래 끝
            float dx = Mathf.Min(x + 0.5f, W - x - 0.5f) - RibbonNotch * (1f - cy);   // 가운데가 가장 깊게 파인다
            float dy = Mathf.Min(y + 0.5f, H - y - 0.5f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Min(dx, dy))));
        }
        tex.Apply();
        float b = RibbonNotch + 6f;
        s_ribbon = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect, new Vector4(b, 0f, b, 0f));
        s_ribbon.hideFlags = HideFlags.HideAndDontSave;
        return s_ribbon;
    }

    /// <summary>가장자리가 어둡고 안쪽 투명한 9-slice 판(64px, 테두리 24). 한 번 구워 공유.</summary>
    private static Sprite EdgeVignette()
    {
        if (s_edgeVignette != null) return s_edgeVignette;
        const int N = 64; const float B = 24f;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(N - 1 - x, N - 1 - y)) / B;   // 가장자리에서 0 → 안쪽 1
            float a = Mathf.Clamp01(1f - d);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
        }
        tex.Apply();
        s_edgeVignette = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect, new Vector4(B, B, B, B));
        s_edgeVignette.hideFlags = HideFlags.HideAndDontSave;
        return s_edgeVignette;
    }

    /// <summary>
    /// 여는 연출 — 막이 차오르고 판이 살짝 커지며 떠오른다(0.965 → 1, 아래 14px → 제자리). 카드가 내려앉는 연출(PlayOpenFxAsync)과
    /// 겹쳐 흐른다. 판이 한 번에 나타나 「상점을 여는 느낌」이 없었다(09-28). 표시 전용 — 구매 로직과 무관.
    /// </summary>
    private async UniTaskVoid PlayWindowInAsync()
    {
        if (!(transform.Find("Window") is RectTransform win)) return;
        if (!win.TryGetComponent<CanvasGroup>(out var cg)) cg = win.gameObject.AddComponent<CanvasGroup>();
        Image veil = null;
        if (transform.Find("Veil") is RectTransform vr) vr.TryGetComponent(out veil);
        var basePos = win.anchoredPosition;

        try
        {
            for (float t = 0f; t < WindowInSec; )
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / WindowInSec);
                float e = 1f - Mathf.Pow(1f - k, 3f);
                cg.alpha = Mathf.Clamp01(k * 1.6f);
                win.localScale = Vector3.one * Mathf.Lerp(WindowInScale, 1f, e);
                win.anchoredPosition = basePos + new Vector2(0f, -14f * (1f - e));
                if (veil != null) veil.color = new Color(0f, 0f, 0f, ShopVeilAlpha * Mathf.Clamp01(k * 2f));
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (cg != null) cg.alpha = 1f;
            if (win != null) { win.localScale = Vector3.one; win.anchoredPosition = basePos; }
            if (veil != null) veil.color = new Color(0f, 0f, 0f, ShopVeilAlpha);
        }
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

        // 연출 층은 그 위 — 산 물건과 동전이 카드·장식 위로 지나간다.
        _fxRoot = ShopUIStyle.MakeRect(w, "PurchaseFx").GetComponent<RectTransform>();
        ShopUIStyle.Stretch(_fxRoot);
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
                IconFrame  = Img(root, "IconFrame"),
                IconGlow   = Img(root, "IconGlow"),
                PricePill  = Img(root, "PricePill"),
                Tag0Bg     = Img(root, "Tag0"),
                Tag1Bg     = Img(root, "Tag1"),
                Tag0       = root.Find("Tag0") != null ? Txt(root.Find("Tag0"), "Label") : null,
                Tag1       = root.Find("Tag1") != null ? Txt(root.Find("Tag1"), "Label") : null,
                Base       = ((RectTransform)root).anchoredPosition,
            };
            // 코드로 그린 빛(SoftDot)은 구운 프리팹에 저장되지 않는다 — 안 입히면 흰 네모 판으로 뜬다(09-25 캡처).
            if (c.IconGlow != null) c.IconGlow.sprite = UI_RuneSelectPopup.SoftDot;
            _cards.Add(c);
            AddClick(root.gameObject, () => Select(captured));
        }
        if (_selGlow != null) _selGlow.sprite = UI_RuneSelectPopup.SoftDot;
        var selCol = transform.Find("Window/Selected");
        if (selCol != null) _selShape = EnsureSelShape(selCol);

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
        _dialogText.text = Greeting;
        Place(_dialogText, TitleTextX, TitleY + 58f, TitleTextW, 30f);

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
        PlaceIn(card.transform, _dealIcon, 43f, 42f + DealInY, 88f, 88f, DealW, DealH);
        _dealIcon.preserveAspect = true;
        _dealIcon.enabled = false;   // 특가가 채워질 때 켠다 — 구워진 기본 상태가 검은 네모로 남지 않게

        _dealCat = ShopUIStyle.MakeText(card.transform, "Cat", 16f, FontStyles.Normal,
                                        TextAlignmentOptions.TopLeft, new Color(0.36f, 0.26f, 0.16f, 1f));
        PlaceIn(card.transform, _dealCat, 147f, 42f + DealInY, 268f, 20f, DealW, DealH);

        _dealName = ShopUIStyle.MakeText(card.transform, "Name", 23f, FontStyles.Bold,
                                         TextAlignmentOptions.TopLeft, new Color(0.20f, 0.13f, 0.07f, 1f));
        PlaceIn(card.transform, _dealName, 147f, 62f + DealInY, 268f, 30f, DealW, DealH);

        _dealDesc = ShopUIStyle.MakeText(card.transform, "Desc", 16f, FontStyles.Normal,
                                         TextAlignmentOptions.TopLeft, new Color(0.36f, 0.26f, 0.16f, 1f));
        PlaceIn(card.transform, _dealDesc, 147f, 94f + DealInY, 268f, 24f, DealW, DealH);   // 16px 줄높이(≈21) + 여유

        _dealOldPrice = ShopUIStyle.MakeText(card.transform, "OldPrice", 16f, FontStyles.Strikethrough,
                                             TextAlignmentOptions.MidlineRight, new Color(0.42f, 0.32f, 0.22f, 1f));
        PlaceIn(card.transform, _dealOldPrice, 427f, 44f + DealInY, 150f, 22f, DealW, DealH);

        _dealPrice = ShopUIStyle.MakeText(card.transform, "Price", 27f, FontStyles.Bold,
                                          TextAlignmentOptions.MidlineRight, new Color(0.45f, 0.28f, 0.06f, 1f));
        PlaceIn(card.transform, _dealPrice, 427f, 68f + DealInY, 150f, 34f, DealW, DealH);

        // 「1개 남음」 — 특가는 1회성이다. 사고 나면 이 자리가 「품절」로 바뀌고 도장이 찍힌다(기획: 거래의 손맛).
        _dealStock = ShopUIStyle.MakeText(card.transform, "Stock", 16f, FontStyles.Bold,
                                          TextAlignmentOptions.MidlineRight, DealStockInk);
        _dealStock.text = "1개 남음";
        PlaceIn(card.transform, _dealStock, 427f, 102f + DealInY, 150f, 22f, DealW, DealH);

        _dealStamp = ShopUIStyle.MakeImage(card.transform, "DealStamp", new Color(1f, 1f, 1f, 0f));
        PlaceIn(card.transform, _dealStamp, (DealW - 240f) * 0.5f, (DealH - 120f) * 0.5f, 240f, 120f, DealW, DealH);
        _dealStamp.raycastTarget = false;
        ShopUIStyle.Skin(_dealStamp, _skin?.soldStamp);
        _dealStamp.gameObject.SetActive(false);
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
    /// 상품 카드 1장(목업 px 205×205, 09-29). 종이 안쪽은 x 16~176 · y 22~183다. 아이콘 칸 74 · 이름 100 · 효과 122(두 줄) · 가격 161.
    /// 09-25 룬 선택 규격: 아이콘 칸 등급 테두리·빛(룬) · 아이콘 오른쪽 빈 종이에 등급·칸 수 태그(룬) ·
    /// 이름 16 굵게 · 효과 16 두 줄 · 가격은 오른쪽 아래 짙은 알약 안(예전엔 효과 끝과 물렸다).
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
        c.Base = ((RectTransform)bg.transform).anchoredPosition;

        // 등급빛 — 룬만 켠다(RefreshCards). 아이콘 칸 뒤에서 번진다.
        c.IconGlow = ShopUIStyle.MakeImage(bg.transform, "IconGlow", new Color(1f, 1f, 1f, 0f));
        PlaceIn(bg.transform, c.IconGlow, 8f, 10f, 102f, 96f, CardW, CardH);
        c.IconGlow.sprite = UI_RuneSelectPopup.SoftDot;

        // 아이콘 칸 — 채움 + 테두리 2겹, 그 사이에 상품 아이콘.
        var fill = ShopUIStyle.MakeImage(bg.transform, "IconFill", new Color(0.05f, 0.05f, 0.07f, 1f));
        PlaceIn(bg.transform, fill, 24f, 22f, 70f, 74f, CardW, CardH);
        ShopUIStyle.Skin(fill, _skin?.iconFill, sliced: true);

        c.Icon = ShopUIStyle.MakeImage(bg.transform, "Icon", new Color(1f, 1f, 1f, 0f));
        PlaceIn(bg.transform, c.Icon, 28f, 26f, 62f, 66f, CardW, CardH);
        c.Icon.preserveAspect = true;

        c.IconFrame = ShopUIStyle.MakeImage(bg.transform, "IconFrame", new Color(1f, 1f, 1f, 0f));
        PlaceIn(bg.transform, c.IconFrame, 24f, 22f, 70f, 74f, CardW, CardH);
        c.IconFrame.raycastTarget = false;
        ShopUIStyle.Skin(c.IconFrame, _skin?.iconFrame, sliced: true);   // 룬이면 등급색을 곱한다(RefreshCards)

        // 카테고리 배지 — 바탕(아트) + 글자(아트가 있으면 그 위 라벨은 끈다).
        c.BadgeBg = ShopUIStyle.MakeImage(bg.transform, "BadgeBg", CatColor[0]);
        PlaceIn(bg.transform, c.BadgeBg, 112f, 22f, 50f, 22f, CardW, CardH);

        c.BadgeGlyph = ShopUIStyle.MakeImage(c.BadgeBg.transform, "BadgeGlyph", new Color(1f, 1f, 1f, 0f));
        ShopUIStyle.Stretch(c.BadgeGlyph.rectTransform, 2f);
        c.BadgeGlyph.preserveAspect = true;
        c.BadgeGlyph.raycastTarget  = false;

        c.BadgeText = ShopUIStyle.MakeText(c.BadgeBg.transform, "BadgeText", 16f, FontStyles.Bold,
                                           TextAlignmentOptions.Center, new Color(0.1f, 0.09f, 0.12f));
        ShopUIStyle.Stretch(c.BadgeText.rectTransform);

        // 등급·칸 수 태그(룬만) — 완성본에서 비어 있던 아이콘 오른쪽 종이. 룬 선택 카드의 칩과 같은 말.
        (c.Tag0Bg, c.Tag0) = MakeTag(bg.transform, "Tag0", TagX, Tag0Y, Tag0W);
        (c.Tag1Bg, c.Tag1) = MakeTag(bg.transform, "Tag1", TagX, Tag1Y, Tag1W);

        c.Name = ShopUIStyle.MakeText(bg.transform, "Name", 16f, FontStyles.Bold,
                                      TextAlignmentOptions.TopLeft, new Color(0.18f, 0.12f, 0.06f, 1f));
        PlaceIn(bg.transform, c.Name, 22f, 100f, 160f, 22f, CardW, CardH);
        FitLine(c.Name);

        // 효과 — 16px 두 줄까지(넘치면 말줄임). 자동 축소는 끈다 — 줄어들면 하한(16) 아래로 내려간다.
        c.Effect = ShopUIStyle.MakeText(bg.transform, "Effect", 16f, FontStyles.Normal,
                                        TextAlignmentOptions.TopLeft, new Color(0.30f, 0.21f, 0.12f, 1f));
        PlaceIn(bg.transform, c.Effect, 22f, 122f, 160f, 40f, CardW, CardH);
        c.Effect.enableAutoSizing = false;
        c.Effect.textWrappingMode = TextWrappingModes.Normal;
        c.Effect.overflowMode     = TextOverflowModes.Ellipsis;

        // 가격 — 짙은 알약 안(코인 + 수치). 효과 문장과 자리를 나눈다.
        c.PricePill = ShopUIStyle.MakeImage(bg.transform, "PricePill", new Color(0.12f, 0.07f, 0.03f, 0.88f));
        PlaceIn(bg.transform, c.PricePill, 100f, 161f, 80f, 21f, CardW, CardH);
        c.PricePill.raycastTarget = false;

        c.PriceCoin = ShopUIStyle.MakeImage(bg.transform, "PriceCoin", ShopUIStyle.Gold);
        PlaceIn(bg.transform, c.PriceCoin, 104f, 162.5f, 18f, 18f, CardW, CardH);
        c.PriceCoin.preserveAspect = true;
        ShopUIStyle.Skin(c.PriceCoin, _skin?.goldCoin);

        c.Price = ShopUIStyle.MakeText(bg.transform, "Price", 17f, FontStyles.Bold,
                                       TextAlignmentOptions.MidlineLeft, PriceInk);
        PlaceIn(bg.transform, c.Price, 125f, 160.5f, 54f, 22f, CardW, CardH);
        c.Price.enableAutoSizing = false;

        // 품절 도장 — 카드 중앙에 겹친다. 기본은 꺼둔다.
        c.SoldStamp = ShopUIStyle.MakeImage(bg.transform, "SoldStamp", new Color(1f, 1f, 1f, 0f));
        PlaceIn(bg.transform, c.SoldStamp, (CardW - 175f) * 0.5f, (CardH - 92f) * 0.5f, 175f, 92f, CardW, CardH);
        c.SoldStamp.raycastTarget = false;
        ShopUIStyle.Skin(c.SoldStamp, _skin?.soldStamp);
        c.SoldStamp.gameObject.SetActive(false);

        return c;
    }

    /// <summary>
    /// 고른 물건의 블록 모양 칸 — 큰 아이콘 <b>왼쪽</b> 안쪽 종이(옅은 판 위). 오른쪽은 겹친 종이 장식이라 걸쳐 보였다(09-25 캡처).
    /// 구운 프리팹에 없으면 만든다. 자리는 LayoutSelArt가 정한다.
    /// </summary>
    private static RectTransform EnsureSelShape(Transform selected)
    {
        var found = selected.Find("ShapeBox") as RectTransform;
        if (found != null) return found;
        var plate = ShopUIStyle.MakeImage(selected, "ShapeBox", ShapePlate);
        plate.raycastTarget = false;
        return plate.rectTransform;
    }

    /// <summary>
    /// 그림·빛·모양 칸 자리. 룬이면 모양 칸을 왼쪽 절반에 크게 두고 그림은 오른쪽으로 비킨다 —
    /// 70×104 칸(칸 20px)에선 모양이 점 몇 개로 보였다(09-25 캡처). 룬이 아니면 그림이 가운데.
    /// 고를 때마다 다시 놓으므로 구운 프리팹의 옛 자리와 상관없다(다시 굽지 않아도 된다).
    /// </summary>
    private void LayoutSelArt(bool rune)
    {
        if (_selPreview == null) return;
        var s = _selPreview.transform.parent;
        if (rune)
        {
            if (_selGlow != null)  PlaceIn(s, _selGlow, 116f, 60f, 140f, 150f, SelDW, SelDH);
            PlaceIn(s, _selPreview, 138f, 80f, 96f, 116f, SelDW, SelDH);
            if (_selShape != null) PlaceIn(s, _selShape, 22f, 82f, 110f, 118f, SelDW, SelDH);
        }
        else
        {
            if (_selGlow != null)  PlaceIn(s, _selGlow, 60f, 62f, 170f, 156f, SelDW, SelDH);
            PlaceIn(s, _selPreview, 90f, 78f, 110f, 124f, SelDW, SelDH);
        }
    }

    private static readonly Color ShapePlate = new(0.22f, 0.13f, 0.06f, 0.16f);   // 양피지 위 옅은 판

    // 등급·칸 수 태그 자리(카드 목업 px). 등급 태그는 「★ Legendary」까지 들어가게 종이 오른쪽 안(186)까지 — 76이던 때
    // 「□ Common」(16px)이 칸을 넘어 종이 밖으로 번졌다(09-28 캡처).
    private const float TagX = 98f, Tag0Y = 48f, Tag0W = 88f, Tag1Y = 71f, Tag1W = 48f;   // 아이콘 칸(22~96) 옆

    private static readonly Color PriceInk   = new(0.98f, 0.84f, 0.45f, 1f);   // 알약 위 금색
    private static readonly Color PriceShort = new(0.95f, 0.52f, 0.45f, 1f);   // 골드 모자람(색만으로 알리지 않게 상세에 사유 문장)

    /// <summary>작은 태그(짙은 판 + 16px 글자). 룬 카드에서만 켠다.</summary>
    private static (Image bg, TMP_Text label) MakeTag(Transform card, string name, float mx, float my, float mw)
    {
        var bg = ShopUIStyle.MakeImage(card, name, new Color(0.16f, 0.11f, 0.07f, 0.92f));
        PlaceIn(card, bg, mx, my, mw, 20f, CardW, CardH);
        bg.raycastTarget = false;
        var label = ShopUIStyle.MakeText(bg.transform, "Label", 16f, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
        FitTagLabel(label);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        ShopUIStyle.Stretch(label.rectTransform);
        bg.gameObject.SetActive(false);
        return (bg, label);
    }

    /// <summary>
    /// 우측 "고른 물건" — 겹친 양피지 + 미리보기 + 이름 명판 + 상세 + [사겠네].
    /// 09-25: 상세 네 줄(12.5px)과 인용(12px)을 16px로 올리고, 줄 수가 물건마다 달라 <b>한 덩어리</b>(_selEffect)로 흘린다 —
    /// 칸을 줄마다 고정하면 효과가 둘인 룬에서 아래 줄과 겹친다. 룬은 룬 선택 카드와 같은 내용(칩 줄·효과 전부·놓을 자리).
    /// </summary>
    private void BuildSelectedColumn(Transform w)
    {
        var col = ShopUIStyle.MakeImage(w, "Selected", new Color(0.11f, 0.11f, 0.14f, 0.96f));
        Place(col, SelX, SelY, SelW, SelH);
        ShopUIStyle.Skin(col, _skin?.selectedPanel, sliced: true);
        var s = col.transform;

        _selGlow = ShopUIStyle.MakeImage(s, "PreviewGlow", new Color(1f, 1f, 1f, 0f));
        _selGlow.sprite = UI_RuneSelectPopup.SoftDot;
        _selGlow.raycastTarget = false;

        _selPreview = ShopUIStyle.MakeImage(s, "Preview", new Color(1f, 1f, 1f, 0f));
        _selPreview.preserveAspect = true;
        _selShape = EnsureSelShape(s);
        LayoutSelArt(false);

        // 이름 명판 — 전용 아트가 납품되지 않았다. 단색 사각형 하나로 두면 양피지 위에서
        // '덜 그려진 칸'처럼 보이므로, 어두운 테두리 + 밝은 채움 2겹으로 판의 두께를 만든다.
        _selNamePlate = ShopUIStyle.MakeImage(s, "NamePlate", new Color(0.26f, 0.06f, 0.05f, 1f));
        PlaceIn(s, _selNamePlate, 14f, 212f, 214f, 50f, SelDW, SelDH);

        var plateFill = ShopUIStyle.MakeImage(_selNamePlate.transform, "Fill", new Color(0.48f, 0.11f, 0.10f, 1f));
        ShopUIStyle.Stretch(plateFill.rectTransform, 3f);

        _selName = ShopUIStyle.MakeText(plateFill.transform, "Name", 22f, FontStyles.Bold,
                                        TextAlignmentOptions.Center, new Color(0.96f, 0.88f, 0.72f, 1f));
        ShopUIStyle.Stretch(_selName.rectTransform, 6f);
        FitLine(_selName);

        // 칩 줄 — 룬: 「◇ Rare · 3칸 · 전기」 / 그 밖: 카테고리
        _selCat = ShopUIStyle.MakeText(s, "Cat", 17f, FontStyles.Bold,
                                       TextAlignmentOptions.Center, CatColor[0]);
        PlaceIn(s, _selCat, 20f, 268f, 214f, 22f, SelDW, SelDH);
        _selCat.enableAutoSizing = false;

        // 상세 한 덩어리 — 효과(전부) · 적용/놓을 자리 · 값 · 걱정. 위에서부터 흐른다.
        _selEffect = ShopUIStyle.MakeText(s, "Detail", 16f, FontStyles.Normal,
                                          TextAlignmentOptions.TopLeft, new Color(0.22f, 0.15f, 0.08f, 1f));
        PlaceIn(s, _selEffect, 30f, 296f, 204f, 128f, SelDW, SelDH);   // 종이 찢긴 왼쪽 가장자리에서 한 칸 띄운다(09-29)
        _selEffect.enableAutoSizing = false;
        _selEffect.richText         = true;
        _selEffect.textWrappingMode = TextWrappingModes.Normal;
        _selEffect.overflowMode     = TextOverflowModes.Ellipsis;
        _selEffect.lineSpacing      = -6f;

        // 상인의 말 — 버튼 바로 위 한 줄
        _selFlavor = ShopUIStyle.MakeText(s, "Flavor", 16f, FontStyles.Italic,
                                          TextAlignmentOptions.Top, new Color(0.42f, 0.30f, 0.18f, 1f));
        PlaceIn(s, _selFlavor, 18f, 426f, 222f, 22f, SelDW, SelDH);
        _selFlavor.enableAutoSizing = false;
        FitLine(_selFlavor);

        // 구매버튼 아트는 <b>빈 명판</b>이다 — 라벨을 반드시 그려야 글자가 생긴다.
        _buyBtn = MakeButton(s, "Buy", BuyLabel, _skin?.buyButton, out _buyLabel);
        // 목업의 [사겠네]는 종이 기준 437~497에 보인다. 아트에 투명 여백이 있어 451에 86을 놓으면
        // 그 자리에 정확히 앉는다.
        PlaceIn(s, _buyBtn, 12f, 451f, 235f, 86f, SelDW, SelDH);
        _buyLabel.fontSize = 22f;
        _buyLabel.fontSizeMax = 22f;
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
        bool used        = !_controller.RerollAvailable;   // 방문당 1회 — 이미 돌렸다
        bool artTruthful = _skin?.rerollButton != null && _controller.RerollCost == RerollCostBakedInArt && affordable && !used;

        _rerollBtn.interactable = affordable && !used;
        UIAffordGlow.Set(_rerollBtn, _rerollBtn.interactable);

        if (_rerollImg != null)
        {
            // 아트가 거짓이 되는 때(골드 부족 · 이미 돌림 · 가격 불일치)는 나가기와 같은 짙은 명판 — 예전 금색 판은
            // 「누를 수 있음」(금빛 · 불)으로 읽혔다(09-29). 크기도 아트가 보이는 폭·높이(rect 안 6~44)에 맞춘다.
            float rx = GridX + (GridCols * CardStepX - CardStepX + CardW - RerollW) * 0.5f;
            if (artTruthful)
            {
                Place(_rerollBtn, rx, RerollY, RerollW, RerollH);
                _rerollImg.sprite = _skin.rerollButton;
                _rerollImg.type   = Image.Type.Simple;
                _rerollImg.color  = Color.white;
            }
            else
            {
                Place(_rerollBtn, rx + 10f, RerollY + 6f, RerollW - 20f, 38f);
                UITheme.StylePanel(_rerollImg, new Color(0.11f, 0.085f, 0.07f, 0.96f), new Color(0.80f, 0.62f, 0.32f, 0.75f), 6f);
            }
            var edge = _rerollImg.transform.Find("Edge");
            if (edge != null) edge.gameObject.SetActive(!artTruthful);
        }
        if (_rerollLabel != null)
        {
            _rerollLabel.gameObject.SetActive(!artTruthful);
            _rerollLabel.text = used ? "<color=#8A7E6A>새로고침 완료</color>"
                : affordable
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
        // 09-25: 새로고침과 같은 줄(카드 아래), 카드 열 오른쪽 끝. 고른 물건 종이가 아래로 길어져 예전 자리(종이 밑)가 없다.
        // 09-29: 새로고침 아트가 보이는 높이(rect 안 6~44)에 맞춘다. 모양은 StyleRuntime이 새로고침과 같은 짙은 명판으로.
        lbl.fontSize = 18f;
        lbl.fontSizeMax = 18f;
        Place(exit, GridX + (GridCols - 1) * CardStepX + CardW - 122f, RerollY + 6f, 112f, 38f);
        exit.onClick.AddListener(ClosePopupUI);
    }

    // ── 상태 ──

    private void Select(int index)
    {
        if (_selectedIndex != index) ShopUIStyle.PlaySfx("shop_select");   // 좌판에서 물건을 집는 소리(기획: 소리 구분)
        _selectedIndex = index;
        RefreshCards();
        RefreshSelected();
    }

    private void RefreshAll()
    {
        if (_controller == null) return;

        CurrencyCounter.Apply(_goldText, _controller.PlayerGold);

        // 「상인의 인장」이 붙어 있으면 할인 사실을 골드 밑에 적는다 — 값만 조용히 싸지면 무엇 덕분인지 알 수 없다.
        // (예전엔 인사말 뒤에 붙였는데 간판 쇠띠를 넘어 꼬리까지 갔다, 09-29)
        if (_sigilText != null)
            _sigilText.text = AbyssPeddlerCatalog.MerchantSigil.Active
                ? $"◆ 상인의 인장  −{AbyssPeddlerCatalog.MerchantSigil.Discount * 100f:F0}%" : string.Empty;

        RefreshReroll();
        RefreshDeal();
        RefreshCards();
        RefreshSelected();
    }

    private void RefreshDeal()
    {
        var deal = _controller?.SpecialDeal;
        // 특가 연출 중에는 좌판에 붙잡아 둔다 — 컨트롤러는 사는 즉시 비우므로, 그대로 두면 도장이 찍히기 전에 사라진다.
        if (_dealRoot != null) _dealRoot.SetActive(deal != null || _dealHold);
        if (deal == null) return;

        if (_dealStock != null) { _dealStock.text = "1개 남음"; _dealStock.color = DealStockInk; }
        if (_dealStamp != null) { _dealStamp.gameObject.SetActive(false); _dealStamp.rectTransform.localScale = Vector3.one; }

        if (_dealCat != null)   _dealCat.text   = $"{deal.CategoryLabel} · {deal.EffectText}";
        if (_dealName != null)  _dealName.text  = deal.DisplayName;
        if (_dealDesc != null)  _dealDesc.text  = deal.DetailText;
        if (_dealPrice != null) _dealPrice.text = deal.Price.ToString();
        if (_dealOldPrice != null)
        {
            int orig = _controller.SpecialOriginalPrice;
            _dealOldPrice.text = orig > deal.Price ? orig.ToString() : "";
        }
        if (_dealIcon != null)
        {
            _dealIcon.enabled = deal.Icon != null;   // 아이콘 없는 특가는 검은 네모 대신 빈 자리로
            if (deal.Icon != null) ShopUIStyle.Skin(_dealIcon, deal.Icon);
        }
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
                c.Price.color = p.Sold ? new Color(0.70f, 0.64f, 0.56f, 1f)
                              : (p.Price <= gold ? PriceInk : PriceShort);
            }
            if (c.PriceCoin != null) c.PriceCoin.gameObject.SetActive(!p.Sold);
            if (c.Icon != null && p.Icon != null) ShopUIStyle.Skin(c.Icon, p.Icon);

            // 룬 — 룬 선택 카드와 같은 말: 등급색 테두리·빛, 등급·칸 수 태그
            var rune = p.Rune;
            var rc   = rune != null ? ShopUIStyle.Rarity(rune.rarity) : Color.white;
            if (c.IconFrame != null) c.IconFrame.color = rune != null ? rc : Color.white;
            if (c.IconGlow  != null) c.IconGlow.color  = rune != null
                ? new Color(rc.r, rc.g, rc.b, rune.rarity == ItemRarity.Common ? 0.28f : 0.55f) : Color.clear;
            SetTag(c.Tag0Bg, c.Tag0, rune != null ? RewardPresentation.RarityLabel(rune.rarity) : null, rc);
            int cells = rune != null ? RuneCardKit.CellCount(rune) : 0;
            SetTag(c.Tag1Bg, c.Tag1, cells > 0 ? $"{cells}칸" : null, new Color(0.96f, 0.90f, 0.78f, 1f));

            // 고른 카드는 들린다 — 룬 선택 카드의 「고르면 들림」과 같은 말
            if (c.Root != null && !_dealing)
                ((RectTransform)c.Root.transform).anchoredPosition = c.Base + (selected ? new Vector2(0f, CardLift) : Vector2.zero);

            // 품절 — 바탕을 바랜 양피지로 갈고 도장을 겹친다(전용 아트 없으면 틴트로 대체).
            if (c.Bg != null)
            {
                var art = _skin?.CardBackground(p.Sold);
                if (art != null)
                {
                    ShopUIStyle.Skin(c.Bg, art, sliced: true);
                    FitCardArt(c.Bg);
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

    /// <summary>태그 글자 — 16이 기본, 칸을 넘을 때만 14까지 줄인다(「★ Legendary」).</summary>
    private static void FitTagLabel(TMP_Text label)
    {
        label.enableAutoSizing = true;
        label.fontSizeMin      = 14f;
        label.fontSizeMax      = 16f;
    }

    /// <summary>구운 프리팹의 태그를 지금 자리(<see cref="TagX"/>…)로 옮긴다 — 다시 굽지 않아도 된다.</summary>
    private void FitCardTags()
    {
        foreach (var c in _cards)
        {
            if (c.Root == null) continue;
            var card = c.Root.transform;
            if (c.Tag0Bg != null) PlaceIn(card, c.Tag0Bg, TagX, Tag0Y, Tag0W, 20f, CardW, CardH);
            if (c.Tag1Bg != null) PlaceIn(card, c.Tag1Bg, TagX, Tag1Y, Tag1W, 20f, CardW, CardH);
            if (c.Tag0 != null) FitTagLabel(c.Tag0);
            if (c.Tag1 != null) FitTagLabel(c.Tag1);
        }
    }

    /// <summary>
    /// 카드 아트를 목업처럼 <b>통째로 줄인 두께</b>로 그린다. 아트(628×600)는 임포트 때 256으로 줄고 스프라이트 ppu도 함께
    /// 줄어드는데, 공용 SliceScale은 픽셀 수로 배율을 재서 1.02가 나왔다 → 테두리·오른쪽 아래 그림자 띠가 목업의 약 2.8배로 그려져
    /// 종이가 좁아지고, 목업 좌표로 놓은 이름·효과·태그·배지가 종이 밖으로 넘쳤다(09-28 사용자 캡처).
    /// 원본 폭(단위) ÷ 칸 폭으로 재면 임포트 크기와 상관없이 목업과 같은 두께다.
    /// </summary>
    private static void FitCardArt(Image bg)
    {
        var sp = bg != null ? bg.sprite : null;
        float w = sp != null ? bg.rectTransform.rect.width : 0f;
        if (w <= 1f) return;
        float refPpu = bg.canvas != null ? bg.canvas.referencePixelsPerUnit : 100f;
        float artW   = sp.rect.width / sp.pixelsPerUnit * refPpu;
        bg.pixelsPerUnitMultiplier = Mathf.Max(1f, artW / w);
    }

    private static void SetTag(Image bg, TMP_Text label, string text, Color ink)
    {
        bool on = !string.IsNullOrEmpty(text);
        if (bg != null) bg.gameObject.SetActive(on);
        if (!on || label == null) return;
        label.text  = text;
        label.color = ink;
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

        if (_buyBtn != null)
        {
            _buyBtn.interactable = has && p.Purchasable && _controller.PlayerGold >= p.Price;
            UIAffordGlow.Set(_buyBtn, _buyBtn.interactable);   // 살 수 있을 때만 은은한 불(09-29)
        }
        if (_buyLabel != null) _buyLabel.text = has && p.Sold ? "품절" : BuyLabel;

        if (_selNamePlate != null) _selNamePlate.gameObject.SetActive(has);
        if (_selPreview != null)   _selPreview.gameObject.SetActive(has);
        if (_selGlow != null)      _selGlow.color = Color.clear;

        if (_selShape != null)
        {
            RuneCardKit.BuildShape(_selShape, null, 0f, 0f);   // 지운다(룬이면 아래에서 다시 그린다)
            if (_selShape.TryGetComponent<Image>(out var plate)) plate.enabled = has && p.Rune != null;
        }
        LayoutSelArt(has && p.Rune != null);

        if (!has)
        {
            SetText(_selName, "");    SetText(_selCat, "");
            SetText(_selEffect, "");
            SetText(_selFlavor, "마음에 드는 걸 골라 봐.");
            return;
        }

        int cat = Mathf.Clamp((int)p.Category, 0, CatColor.Length - 1);
        int gold = _controller.PlayerGold;
        var rune = p.Rune;
        SetText(_selName, p.DisplayName);

        var sb = new System.Text.StringBuilder();
        if (rune != null)
        {
            // 룬 선택 카드와 같은 내용 — 등급·칸 수·속성 / 효과 전부 / 놓을 자리
            var rc = ShopUIStyle.Rarity(rune.rarity);
            if (_selCat != null)
            {
                var elem = ElementDef.GetById(rune.element);
                _selCat.text  = RuneCardKit.RarityChipText(rune) + (elem != null ? $" · {elem.Name}" : "");
                // 양피지 위 잉크 — 밝은 등급색은 종이에 묻힌다. 평범(흰색)은 눌러도 회색이라 짙은 갈색 잉크로.
                _selCat.color = rune.rarity == ItemRarity.Common ? new Color(0.30f, 0.22f, 0.14f, 1f)
                                                                 : new Color(rc.r * 0.55f, rc.g * 0.55f, rc.b * 0.55f, 1f);
            }
            if (_selGlow != null) _selGlow.color = new Color(rc.r, rc.g, rc.b, rune.rarity == ItemRarity.Common ? 0.25f : 0.5f);

            if (rune.effects != null)
                foreach (var slot in rune.effects)
                    if (!string.IsNullOrEmpty(slot.effectType))
                        sb.Append("▲ ").Append(EffectDescriptionFormatter.Describe(slot).Combined).Append('\n');

            bool fit = RuneCardKit.CanPlace(rune);
            // 블록 모양 — 판 위 블록과 같은 속성 타일. 놓을 자리가 없으면 어둡게(룬 선택 카드와 같은 규칙).
            if (_selShape != null) RuneCardKit.BuildShape(_selShape, rune, 30f, 3f, dim: !fit);
            sb.Append(fit ? "<color=#2F7A48><b>놓을 자리 있음</b></color>" : "<color=#9C2A1C><b>놓을 자리 없음</b></color>")
              .Append("  <color=#6B5842>사면 판이 열린다</color>\n");
        }
        else
        {
            // 카테고리 색은 어두운 배지 바탕용이다 — 양피지 위 글자로 그대로 쓰면 종이에 묻힌다. 절반 밝기로 눌러 잉크처럼 쓴다.
            if (_selCat != null)
            {
                var ink = CatColor[cat];
                _selCat.text  = p.CategoryLabel;
                _selCat.color = new Color(ink.r * 0.5f, ink.g * 0.5f, ink.b * 0.5f, 1f);
            }
            sb.Append("▲ ").Append(p.EffectText).Append('\n');
            if (!string.IsNullOrEmpty(p.DetailText)) sb.Append("<color=#6B5842>").Append(p.DetailText).Append("</color>\n");
        }

        // 값과 모자람은 두 줄 — 한 줄에 붙이면 「값 81   54 골드 모자람」이 숫자 둘로 읽혔다(09-29).
        sb.Append("값 <b>").Append(p.Price).Append(" 골드</b>");
        if (gold < p.Price) sb.Append("\n<color=#9C2A1C><b>").Append(p.Price - gold).Append(" 골드 모자람</b></color>");
        else                sb.Append("   <color=#6B5842>(잔액 ").Append(gold - p.Price).Append(")</color>");

        var deal = _controller.SpecialDeal;
        if (deal != null && deal != p && gold >= p.Price && gold - p.Price < deal.Price)
            sb.Append("\n<color=#8A4A1C>사면 특가 ").Append(deal.DisplayName).Append("(").Append(deal.Price).Append(")는 못 산다</color>");

        SetText(_selEffect, sb.ToString());
        SetText(_selFlavor, gold < p.Price ? "골드가 모자라는군." : "힘을 살 텐가, 특가를 챙길 텐가.");   // 16px에 한 줄(예전 문장은 말줄임됐다)

        if (_selPreview != null && p.Icon != null) ShopUIStyle.Skin(_selPreview, p.Icon);
    }

    // ── 핸들러 ──

    private void OnBuyClicked()
    {
        if (_controller == null || _buying) return;

        bool special = _selectedIndex == SpecialIndex;
        int  index   = _selectedIndex;
        // 연출에 쓸 것은 <b>사기 전에</b> 잡는다 — 특가는 사는 즉시 컨트롤러에서 사라진다.
        var product = Selected();
        var source  = special ? (_dealRoot != null ? (RectTransform)_dealRoot.transform : null)
                              : (index >= 0 && index < _cards.Count && _cards[index].Root != null
                                 ? (RectTransform)_cards[index].Root.transform : null);

        var result = special ? _controller.TryBuySpecial() : _controller.TryBuyProduct(index);
        if (result != ShopPurchaseResult.Success)
        {
            ShopUIStyle.PlaySfx("shop_reject");
            RefreshAll();
            return;
        }

        ShopUIStyle.PlaySfx(special ? "shop_deal" : "shop_buy");   // 특가는 종소리로 구분(기획: 소리 구분)
        if (special) _selectedIndex = -1;
        PurchaseFxAsync(product, special, source, index).Forget();
        RefreshAll();
    }

    private void OnRerollClicked()
    {
        if (_controller == null) return;
        bool ok = _controller.TryReroll();
        if (ok) { ShopUIStyle.PlaySfx("shop_reroll"); _selectedIndex = -1; }
        else ShopUIStyle.PlaySfx("shop_reject");
        RefreshAll();
        if (!ok) return;
        // 새로고침도 골드를 쓴다 — 동전이 빠지고 새 물건이 좌판에 다시 깔린다(예전엔 소리 · 숫자뿐이라 진열이 순간 바뀌었다).
        // 10-01 사용자 「소모할 수 있는 재화가 있는 버튼은 연출이 있어야」.
        SpendCoinsAsync().Forget();
        PlayOpenFxAsync().Forget();
    }

    // ── 구매 연출 (기획_상점개편 「거래의 손맛」) ──────────────────────────────
    //
    // 기획: 구매 시 골드 코인 빠지는 연출 + 상인 반응 + 물건 → 인벤 이동 / 특가는 딱지 떼어짐 → 품절 / 소리 구분.
    // 전부 표시층이다 — 골드 차감·지급·품절은 컨트롤러가 이미 확정했고, 여기서는 그 사실을 눈에 보이게만 한다.
    // 팝업은 시간이 멈춰 있어 모든 시간은 unscaled다.

    private static readonly Color DealStockInk = new(0.55f, 0.24f, 0.10f, 1f);
    private static readonly Color SoldInk      = new(0.45f, 0.38f, 0.30f, 1f);

    /// <summary>구매 직후 한 벌 — 동전이 떨어지고, 상인이 반응하고, 산 물건이 받는 곳으로 날아간다.</summary>
    private async UniTaskVoid PurchaseFxAsync(ShopProduct p, bool special, RectTransform source, int cardIndex)
    {
        if (p == null) return;
        _buying = true;

        SpendCoinsAsync().Forget();
        SetMerchantReaction(p);
        if (special) DealSoldAsync().Forget();
        else if (cardIndex >= 0 && cardIndex < _cards.Count) PopStampAsync(_cards[cardIndex].SoldStamp).Forget();

        await FlyItemAsync(p, source);
        _buying = false;
    }

    /// <summary>
    /// 골드 칸에서 동전 몇 닢이 튀어 떨어진다. 숫자만 줄면(CurrencyCounter) "빠져나간 느낌"이 없다 —
    /// 기획이 말하는 "코인 빠지는 연출"이 이 몫이다.
    /// </summary>
    private async UniTaskVoid SpendCoinsAsync()
    {
        EnsureCoins();
        if (_coins == null || _goldText == null || _fxRoot == null) return;

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
            float t = 0f;
            while (t < CoinFallDur)
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

        for (int i = 0; i < _coins.Length; i++)
            if (_coins[i] != null) _coins[i].gameObject.SetActive(false);
    }

    /// <summary>산 물건이 카드에서 떠올라 받는 곳으로 날아가 빨려든다.</summary>
    private async UniTask FlyItemAsync(ShopProduct p, RectTransform source)
    {
        EnsureFlyIcon();
        if (_flyIcon == null || _fxRoot == null || source == null) return;

        Vector2 from = LocalInFx(source.position);
        Vector2 to   = DestinationInFx(p);
        int cat = Mathf.Clamp((int)p.Category, 0, CatColor.Length - 1);

        var rt = _flyIcon.rectTransform;
        if (p.Icon != null) ShopUIStyle.Skin(_flyIcon, p.Icon);
        else { _flyIcon.sprite = null; _flyIcon.color = CatColor[cat]; }   // 아이콘 없는 상품은 카테고리 색 조각으로
        rt.sizeDelta        = new Vector2(96f, 96f);
        rt.anchoredPosition = from;
        rt.localScale       = Vector3.one;
        _flyIcon.gameObject.SetActive(true);

        try
        {
            float t = 0f;
            while (t < ItemFlyDur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / ItemFlyDur);
                float e = 1f - (1f - k) * (1f - k);   // 끝에서 느려지며 빨려든다
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

    /// <summary>
    /// 받는 곳 — HUD에서 실제 칸을 찾아 그쪽으로 보낸다(버프창 · 포션 칸 · 재화 칸).
    /// 룬 보관함은 HUD에 칸이 없어 화면 오른쪽 아래로 보내고, 어디로 갔는지는 상인이 말로 알린다.
    /// </summary>
    private Vector2 DestinationInFx(ShopProduct p)
    {
        string hudName = p.Category switch
        {
            ShopProductCategory.Buff     => "BuffGridRoot",
            ShopProductCategory.Potion   => "HUD_Active_01",
            ShopProductCategory.Material => p.DisplayName != null && p.DisplayName.StartsWith("원석")
                                            ? "Pill_RuneOre" : "Pill_EnhanceMat",
            _                            => null,
        };
        var hud = hudName != null ? FindHudSlot(hudName) : null;
        if (hud != null) return LocalInFx(hud.position);

        // 폴백 — 카테고리별 화면 가장자리 방향(HUD 배치가 바뀌어도 방향은 남는다).
        var half = _fxRoot.rect.size * 0.5f;
        return p.Category switch
        {
            ShopProductCategory.Buff     => new Vector2(-half.x, -half.y),   // 좌하단 버프창
            ShopProductCategory.Material => new Vector2( half.x,  half.y),   // 우상단 재화 칸
            _                            => new Vector2( half.x, -half.y),   // 우하단 포션 칸·보관함
        };
    }

    /// <summary>HUD에서 이름으로 칸을 찾는다(비활성 포함). 없으면 null.</summary>
    private static Transform FindHudSlot(string name)
    {
        var hud = UnityEngine.Object.FindFirstObjectByType<HudView>(FindObjectsInactive.Include);
        if (hud == null) return null;
        var all = hud.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
            if (all[i].name == name) return all[i];
        return null;
    }

    /// <summary>월드 좌표를 연출 층의 로컬 좌표로 옮긴다(창 배율이 달라도 자리가 맞는다).</summary>
    private Vector2 LocalInFx(Vector3 world)
    {
        var cam = GetComponentInParent<Canvas>()?.worldCamera;
        var screen = RectTransformUtility.WorldToScreenPoint(cam, world);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_fxRoot, screen, cam, out var local);
        return local;
    }

    /// <summary>
    /// 상인 반응 — 무엇을 어디에 넣었는지 말로 알린다. 받는 칸(HUD)은 암막 뒤라 도착지가 안 보인다.
    /// 잠깐 뒤 인사말로 돌아간다.
    /// </summary>
    // 행상 = 웃는 버섯(10-01 NPC 교체) — 밝은 반말 장사꾼. 예전 「어서 오게, 빛이여 · ~네」는 사람 상인 몸이었을 때 말투.
    private const string Greeting = "어서 와! 오늘도 살아 있네?";
    private const string BuyLabel = "산다";   // 재련소 「벼린다」 · 쉼터 「쉰다」와 같은 결

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
        _dialogText.text = Greeting;
    }

    /// <summary>특가 — 「1개 남음」이 「품절」로 바뀌고 도장이 찍힌 뒤 좌판에서 사라진다.</summary>
    private async UniTaskVoid DealSoldAsync()
    {
        _dealHold = true;
        if (_dealStock != null) { _dealStock.text = "품절"; _dealStock.color = SoldInk; }
        PopStampAsync(_dealStamp).Forget();

        try { await UniTask.Delay((int)(DealHoldDur * 1000f), ignoreTimeScale: true, cancellationToken: destroyCancellationToken); }
        catch (OperationCanceledException) { return; }

        _dealHold = false;
        RefreshDeal();
    }

    /// <summary>품절 도장이 내려찍힌다(크게 → 제자리).</summary>
    private async UniTaskVoid PopStampAsync(Image stamp)
    {
        if (stamp == null || _skin?.soldStamp == null) return;
        stamp.gameObject.SetActive(true);
        var rt = stamp.rectTransform;
        try
        {
            float t = 0f;
            while (t < StampPopDur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / StampPopDur);
                float s = Mathf.Lerp(1.8f, 1f, 1f - (1f - k) * (1f - k));
                rt.localScale = new Vector3(s, s, 1f);
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { return; }
        rt.localScale = Vector3.one;
    }

    /// <summary>동전·물건 그림은 코드가 런타임에 만든다 — 구운 프리팹에 굳혀 둘 이유가 없다.</summary>
    private void EnsureCoins()
    {
        if (_coins != null || _fxRoot == null) return;
        _coins = new Image[CoinCount];
        for (int i = 0; i < CoinCount; i++)
        {
            var img = ShopUIStyle.MakeImage(_fxRoot, "Coin", ShopUIStyle.Gold);
            img.rectTransform.sizeDelta = new Vector2(22f, 22f);
            img.preserveAspect = true;
            ShopUIStyle.Skin(img, _skin?.goldCoin);
            img.gameObject.SetActive(false);
            _coins[i] = img;
        }
    }

    private void EnsureFlyIcon()
    {
        if (_flyIcon != null || _fxRoot == null) return;
        _flyIcon = ShopUIStyle.MakeImage(_fxRoot, "FlyItem", Color.white);
        _flyIcon.preserveAspect = true;
        _flyIcon.gameObject.SetActive(false);
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
