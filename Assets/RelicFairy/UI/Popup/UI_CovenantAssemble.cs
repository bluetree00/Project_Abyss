using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 조립 서약 팝업 — 원인(Ⅰ) × 효과(Ⅲ)를 골라 서약을 벼려낸다. 중앙(Ⅱ)은 완성 미리보기.
/// 카드 티어(실버/골드/루비)가 조건·계수·효과를 함께 내장 — 슬라이더 없이 ↻ 리롤로 굴린다.
/// 결과 = 서약서 첫 문장 id "sen:cause@tier&gt;effect@tier"(10-02 「한 장의 서약서」 — 조건 + 첫 결과).
/// 문장이 이미 있으면 같은 판을 「서약서 쓰기」로 연다(<c>UI_CovenantAssemble.Write.cs</c>).
///
/// 10-11 개편(설계서 UI4종 1장 · v2 보강 / 리서치 B §4-4): 아무것도 고르지 않은 채 열고, 가운데 두루마리가
/// 「원인 → 효과」 한 문장을 조립해 보인다(안 고른 쪽은 점선 칸). 글자는 톤 v2(Noto · 가짜 굵게 · 기울임 0, 제목만 DNF),
/// 양피지 위 잉크는 4.5:1 이상, 열 머리는 판 모양 칩 대신 글자 라벨 + 1px 선. 규칙 · 수치 · 선택 결과는 그대로다.
///
/// 사용법:
///   var popup = await Managers.UI.ShowPopupUIAndGetAsync&lt;UI_CovenantAssemble&gt;();
///   popup.Setup(forceSilver, rng);
///   string id = await popup.WaitForResultAsync();   // null = 취소
/// </summary>
public partial class UI_CovenantAssemble : UI_Popup
{
    // ── Constants ────────────────────────────────────────
    private const int DraftCount     = 3;
    private const int DefaultRerolls = 2;
    private const float TitleDrop    = 26f;   // 제목을 찢긴 종이 윗가장자리에서 종이 안으로(09-28)

    // ── Static ───────────────────────────────────────────
    private static readonly Color SilverColor = new(0.80f, 0.82f, 0.88f);
    private static readonly Color GoldColor   = new(0.95f, 0.74f, 0.28f);
    private static readonly Color RubyColor   = new(0.92f, 0.28f, 0.40f);   // 루비 = 진홍
    private static Sprite s_dashBox, s_thinBox;   // 코드로 굽는 1px 테 — 점선(자리표시) · 실선(줄 판)

    // ── [SerializeField] ─────────────────────────────────
    [Header("제목")]
    [SerializeField] private TMP_Text _titleText;

    [Header("Ⅰ 원인 / Ⅲ 효과 카드")]
    [SerializeField] private UI_AssembleCard[] _causeCards;
    [SerializeField] private UI_AssembleCard[] _effectCards;

    [Header("Ⅱ 완성 미리보기")]
    [SerializeField] private TMP_Text _previewTitle;
    [SerializeField] private TMP_Text _previewSentence;
    [SerializeField] private TMP_Text _previewCondition;
    [SerializeField] private TMP_Text _previewCoef;
    [SerializeField] private TMP_Text _previewEffect;

    [Header("하단")]
    [SerializeField] private TMP_Text _rerollCountText;
    [SerializeField] private TMP_Text _forgeSummary;
    [SerializeField] private Button   _forgeButton;


    [Header("스킨 — 등급 테두리 아트(카드에 주입)")]
    [SerializeField] private Sprite _silverFrame;   // 실버 테두리@2x
    [SerializeField] private Sprite _goldFrame;     // 골드 테두리@2x
    [SerializeField] private Sprite _rubyFrame;     // 루비 테두리@2x
    [SerializeField] private Sprite _cardBgSprite;  // 테두리 바탕@2x

    [Header("스킨 — 패널/중앙카드 아트(대상 Image)")]
    [SerializeField] private Image _panelBg;        // 서약 큰 바탕@2x
    [SerializeField] private Image _panelBorder;    // 서약 큰 테두리@2x
    [SerializeField] private Image _prismBg;        // 프리즘 바탕@2x (중앙 결과 카드)
    [SerializeField] private Image _prismBorder;    // 프리즘 테두리@2x
    [SerializeField] private Sprite _panelBgSprite, _panelBorderSprite, _prismBgSprite, _prismBorderSprite;

    [Header("열 머리표 — 개편본 아트(원인/결과/효과) 대상")]
    [SerializeField] private Image    _causeHeaderChip,  _resultHeaderChip,  _effectHeaderChip;
    [SerializeField] private TMP_Text _causeHeaderText,  _resultHeaderText,  _effectHeaderText;

    [Header("연결선 — 선택 카드 → 중앙 결과 카드")]
    [SerializeField] private RectTransform _connectorLeft;   // 선택된 원인 카드 → 프리즘 좌측
    [SerializeField] private RectTransform _connectorRight;  // 선택된 효과 카드 → 프리즘 우측
    [SerializeField] private RectTransform _prismRect;       // 중앙 결과 카드(끝점)
    [SerializeField] private RectTransform _bookRect;        // 좌표 변환 기준(공통 부모)

    // ── Private ──────────────────────────────────────────
    private System.Random _rng;
    private bool _forceSilver;
    private int  _rerollsLeft;
    private bool _forged;   // 맺기로 닫히는 중 — 닫기 소리를 내지 않는다(맺기 소리가 맡는다)
    private bool _titleDropped;
    private List<CovenantDraftCard> _causes;
    private List<CovenantDraftCard> _effects;
    private int _selCause, _selEffect;
    private UniTaskCompletionSource<string> _tcs;

    // 개편(양피지) 스킨에선 연결선을 긋지 않는다 — ApplyPanelSkin 주석 참조.
    private bool _connectorsOff;

    // 두루마리 자리표시(점선 칸) — 원인 · 효과를 아직 안 고른 쪽 절에 켠다. 효과 열 머리 밑줄은 쓰기 판이 껐다 켠다.
    private Image _causeSlot, _effectSlot;
    private Image _effectHeadRule;
    private CancellationTokenSource _fadeCts;   // 두루마리 글이 바뀌는 순간의 스밈(0.18초)

    // ── Public Properties ────────────────────────────────
    public override bool BlocksGameplay => true;

    /// <summary>ESC = 조립 취소. OnDestroy가 _tcs를 null로 완료시켜 ChooseAsync가 취소로 끝난다.</summary>
    public override bool CloseOnEscape => true;

    // ── Lifecycle ────────────────────────────────────────
    /// <summary>
    /// 벼리지 않고 팝업이 사라지는 모든 경로(씬 전환 CloseAllPopupUI 등)에서 대기를 끝낸다.
    /// 이게 없으면 _tcs가 영구 미완료 → ChooseAsync가 무한 대기 → 제단이 영구 잠긴다.
    /// OnForge가 이미 결과를 넣었으면 TrySetResult가 false를 반환하고 무시된다.
    /// </summary>
    protected override void OnDestroy()
    {
        base.OnDestroy();   // 차단 잠금 누수 방지(UI_Popup)
        _tcs?.TrySetResult(null);
        _fadeCts?.Cancel();
        _fadeCts?.Dispose();
        _fadeCts = null;
    }

    // ── Public Methods ───────────────────────────────────
    /// <summary>드래프트를 굴려 팝업을 구성한다. forceSilver=true면 첫 서약(실버 고정).</summary>
    public void Setup(bool forceSilver, System.Random rng)
    {
        Managers.Sound?.PlayUiEvent(SoundEvent.UiOpen);
        _forceSilver = forceSilver;
        _rng         = rng;
        _rerollsLeft = DefaultRerolls;
        _tcs         = new UniTaskCompletionSource<string>();
        SetText(_titleText, "봉인된 예언자의 서약서");
        if (_titleText)
        {
            _titleText.fontSize = 28f; FitSingleLine(_titleText);
            // 제목이 찢긴 종이 윗가장자리에 걸쳐 윗부분은 어두운 배경, 아랫부분은 밝은 종이 위였다 — 종이 안으로 한 번 내린다.
            if (!_titleDropped)
            {
                _titleDropped = true;
                ((RectTransform)_titleText.transform).anchoredPosition += new Vector2(0f, -TitleDrop);
            }
        }

        ConfigureTextFitting();
        ApplyPanelSkin();

        // 보유 서약을 넘겨 페어링을 건다(C4) — 걸어 줄 서약이 없는데 먹는 서약만 손에 쥐면
        // 벼린 서약이 한 번도 터지지 않는 런이 된다. 원인 × 효과 9칸은 전부 벼릴 수 있는 짝으로만 제시된다.
        CovenantAssembleService.DraftBoard(DraftCount, _rng, _forceSilver, HeldCovenants, out _causes, out _effects, BuildStatus);
        // 10-11: 아무것도 고르지 않은 채 연다 — 두루마리 점선 칸이 「무엇을 고를지」를 말하고,
        // 둘 다 골라야 [서약 새기기]가 켜진다(설계서 1장 3 · 4). 고를 수 있는 카드 · 새겨지는 결과는 그대로다.
        _selCause  = -1;
        _selEffect = -1;

        WireColumn(_causeCards,  _causes,  true);
        WireColumn(_effectCards, _effects, false);

        if (_forgeButton)
        {
            _forgeButton.onClick.RemoveAllListeners();
            _forgeButton.onClick.AddListener(OnForge);
        }


        ApplyBottomBarSkin();

        RefreshRerollText();
        RefreshSelection();
    }

    /// <summary>결과 대기. 반환 = 조립된 서약 id(취소 시 null).</summary>
    public UniTask<string> WaitForResultAsync() => _tcs.Task;

    /// <summary>
    /// ESC(취소)로 닫힐 때만 닫기 소리 — 맺기로 닫히면 맺기 소리가 이미 났다.
    /// 쓰기 판에서 ESC는 고쳐 쓰기를 먼저 푼다 — 그다음 ESC가 창을 닫는다(10-18). 그 프레임에 ESC가 눌린 닫기(EscKeyListener 길)만 가른다 —
    /// 새긴다 · 다른 닫기 길은 그대로 닫힌다.
    /// </summary>
    public override void ClosePopupUI()
    {
        if (!_forged && Input.GetKeyDown(KeyCode.Escape) && LeaveRewrite()) return;
        if (!_forged) Managers.Sound?.PlayUiEvent(SoundEvent.UiClose);
        base.ClosePopupUI();
    }

    // ── Private Methods ──────────────────────────────────
    private void WireColumn(UI_AssembleCard[] cards, List<CovenantDraftCard> data, bool isCause)
    {
        if (cards == null) return;
        for (int i = 0; i < cards.Length; i++)
        {
            var card = cards[i];
            if (card == null) continue;

            if (i >= data.Count) { card.gameObject.SetActive(false); continue; }
            card.gameObject.SetActive(true);
            BindCard(card, data[i], isCause);

            int idx = i;
            if (card.SelectButton)
            {
                card.SelectButton.onClick.RemoveAllListeners();
                card.SelectButton.onClick.AddListener(() => Select(isCause, idx));
            }
            if (card.RerollButton)
            {
                card.RerollButton.onClick.RemoveAllListeners();
                card.RerollButton.onClick.AddListener(() => Reroll(isCause, idx));
                FixRerollLabel(card.RerollButton);
                card.DockReroll(outerLeft: isCause);   // 원인 열은 왼쪽 바깥, 효과 열은 오른쪽 바깥
            }
        }
    }

    private void BindCard(UI_AssembleCard card, CovenantDraftCard d, bool isCause)
    {
        card.SetSkin(_silverFrame, _goldFrame, _rubyFrame, _cardBgSprite);   // 등급 아트 주입(Bind 전)
        card.SetGradeSkin(UISkin.Covenant);   // 개편본(조각 조립 테두리 + 양피지 카드) — 없으면 위 구 아트 유지
        if (isCause && CovenantPalette.TryGetCause(d.id, out var c))
            card.Bind(c.name, c.desc, d.tier, TierColor(d.tier));
        else if (!isCause && CovenantPalette.TryGetEffect(d.id, out var e))
            card.Bind(e.name, e.desc, d.tier, TierColor(d.tier), EffectTaxonomy.Badge(e.axis, e.status));
    }

    /// <summary>패널/중앙 결과카드에 아트 스프라이트 적용(지정된 것만 — 미지정 시 기존 외형 유지).</summary>
    private void ApplyPanelSkin()
    {
        SkinImage(_panelBg,     _panelBgSprite);
        SkinImage(_panelBorder, _panelBorderSprite);
        SkinImage(_prismBg,     _prismBgSprite);
        SkinImage(_prismBorder, _prismBorderSprite);

        // 개편본 스킨이 있으면 프리팹에 꽂힌 구 아트를 덮어쓴다.
        // 양피지 배경은 자체 테두리(말린 양끝)를 갖고 있어, 코드가 그리던 별도 테두리 판은 지운다.
        var skin = UISkin.Covenant;
        if (skin == null) return;

        if (skin.panelBg != null)
        {
            SkinImage(_panelBg, skin.panelBg);
            if (_panelBorder != null) _panelBorder.enabled = false;
        }
        if (skin.resultScroll != null)
        {
            // 가운데 열은 책 양피지 위에 바로 앉힌다 — 예전엔 책(두루마리) 안에 또 두루마리 그림 · 그 위에 흐린 크림 판을 덧대
            // 톤이 따로 놀았다(10-16 사용자 「중앙 텍스트를 보는 곳이 톤도 다르고 굉장히 어색해」). 쓰기 판과 같은 1px 선 틀(EnsurePlate).
            if (_prismBg != null) _prismBg.enabled = false;
            if (_prismBorder != null) _prismBorder.enabled = false;
            // 결과 두루마리는 밝은 종이다 — 구 아트(어두운 프리즘)에 맞춘 상아색 글자가 종이 위에서 사라졌다.
            _resultOnPaper = true;
            foreach (var t in new[] { _previewTitle, _previewSentence, _previewCondition, _previewCoef, _previewEffect })
                if (t != null) t.color = InkOnBook;
            ApplyScrollLayout();
        }

        // 제목은 종이 위에 먹으로 — 봉은 좌우 세로라 윗가장자리엔 봉이 없고, 상아색은 밝은 종이 위에서 대비가 없었다(09-28 UI 전수).
        if (_titleText != null) _titleText.color = InkOnBook;

        // 연결선(선택 카드 → 결과 두루마리)은 구 아트 시절 넓은 판(1600×840)에 맞춘 장식이다. 양피지 배치에선
        // 열과 두루마리 사이가 18px뿐이라, 위·아래 카드를 고르면 두루마리 옆에 170px짜리 거의 수직인 선이
        // 떠 있을 뿐 무엇도 잇지 않는다(목업에도 없다). 구 판 테두리·프리즘 테두리처럼 개편 스킨에선 끈다.
        _connectorsOff = true;
        if (_connectorLeft)  _connectorLeft.gameObject.SetActive(false);
        if (_connectorRight) _connectorRight.gameObject.SetActive(false);

        ApplyHeaderLabels();
        ApplyMockupLayout();
        ApplyBottomBarSkin();
    }

    // ── 완성본 목업(서약 풀샷.png 1306×948) 실측 비율 ──────────
    // 프리팹은 구 아트 기준(Book 1600×840, 비율 1.905)으로 authoring돼 있어
    // 양피지 아트(1292:919 = 1.406) 위에 얹으면 카드가 1.5배 크고 좌상으로 치우친다.
    // 프리팹을 수술하는 대신 런타임에 비율로 다시 앉힌다 — 되돌리기 쉽고 재납품에도 강하다.
    private const float BookAspect  = 1.406f;
    // 다른 컨텐츠 화면(UIWindowFitter)과 <b>같은 값</b>이어야 한다. 예전엔 목업 실측 946을
    // 상한으로 두고 여백도 0.96이라, 이 화면만 세로 87.6%로 열려 재련소·상점(94%)과 크기가 달랐다.
    private const float BookMargin  = 0.94f;
    private const float BookFallbackH = 946f;   // 부모 크기를 못 잴 때만 쓰는 값

    // ── 톤 ──────────────────────────────────────────────
    // 배치는 프리팹이 갖지만 <b>색은 코드가 칠한다</b> — 프리팹 authoring 색이 구 아트 기준(형광 보라 버튼 ·
    // 흰 글자)이라 양피지 위에서 튀거나 날아간다. 아트 재납품에 따라 바뀌는 값이라 한곳에 모아 둔다.
    private static readonly Color ForgeFill     = new(0.36f, 0.26f, 0.13f, 1f);   // 청동
    private static readonly Color BtnLabel      = new(0.97f, 0.93f, 0.80f, 1f);
    // 10-11: 양피지(밝기 170) 위 잉크는 가장 얼룩진 곳 기준 4.5:1 이상(v2-0) — 본문 #2A1C0E 7:1 · 라벨 #4A3820 4.6:1 · 수치 #5E1A0A 5.3:1.
    private static readonly Color InkOnBook     = new(0.165f, 0.110f, 0.055f, 1f);   // #2A1C0E
    private static readonly Color LabelInk      = new(0.290f, 0.220f, 0.125f, 1f);   // #4A3820
    private static readonly Color NumInk        = new(0.369f, 0.102f, 0.039f, 1f);   // #5E1A0A
    private bool _resultOnPaper;   // 결과 두루마리가 밝은 종이(개편 스킨)인가 — 글자 잉크를 가른다
    private const string PreviewLabelHex = "#A89A82";   // 중앙 두루마리(어두운 판) — 항목 이름
    private const string PreviewValueHex = "#F4E7C8";   //                              값
    private const string PaperLabelHex   = "#4A3820";   // 중앙 두루마리(밝은 종이 — 개편 스킨) — 항목 이름(예전 #7A6448 2.3:1)
    private const string PaperValueHex   = "#2A1C0E";   //                                        값
    private const float  SummaryLeft     = 0.105f;      // 하단 조합 줄 — 하단 바 안 비율(양피지 테두리 안쪽부터)
    private const float  SummaryRight    = 0.49f;       //                리롤 표시 앞까지

    // 글자 단계(1080 기준 px, v2-0 「화면당 5단계」) — 15 꼬리표 · 17 보조 · 20 본문 · 22 이름/문장 줄 · 28 제목(DNF).
    private const float LabelSize  = 17f;   // 열 머리 · 두루마리 수치 줄 · 리롤 표시 · 경고
    private const float ButtonSize = 20f;   // [서약 새기기] · [새긴다]
    private const float ChipSize   = 16f;   // 카드 옆 [리롤] · [교체] · 줄 [고치기] — 15는 글자 하한(16) 아래였다(10-14)
    /// <summary>
    /// <b>판 크기만</b> 정한다. 그 안의 배치는 <b>프리팹이 정본</b>이다.
    ///
    /// <para>예전엔 여기서 열·카드·머리표·미리보기 글자까지 전부 다시 앉혔다. 목업 비율을 코드가 쥐고 있어
    /// 인스펙터에서 옮겨도 플레이하면 원위치였고, 화면을 보며 고칠 방법이 상수를 만지고 재컴파일하는 것뿐이었다.
    /// 그 값들은 프리팹 앵커로 구워 넣었다 — 전부 판 대비 <b>비율</b>이라 판이 줄면 같이 줄어든다.</para>
    ///
    /// <para>판 크기만 코드에 남는 이유: 「목업 실측 946을 넘지 않되 화면 밖으로도 나가지 않는다」는
    /// 상한이 있는 규칙이라 앵커로는 표현되지 않는다. 바깥은 코드, 안쪽은 프리팹.</para>
    /// </summary>
    private void ApplyMockupLayout()
    {
        if (_panelBg != null) ResizeBook(_panelBg.rectTransform);
    }

    /// <summary>
    /// 판 크기는 목업 실측(1330×946)이되 화면을 넘지 않게 가둔다.
    /// 21:9 이상 초광폭에서는 캔버스 논리 높이가 946보다 작아져(2560×1080 → 935) 판 위·아래가 잘린다.
    /// </summary>
    private static void ResizeBook(RectTransform book)
    {
        float h = BookFallbackH;
        if (book.parent is RectTransform area && area.rect.width > 1f && area.rect.height > 1f)
        {
            // 목업 실측을 상한으로 걸지 않는다 — 다른 화면들도 목업 크기를 넘겨 화면에 맞춘다.
            // 세로를 먼저 채우고, 가로가 모자라면 가로에 맞춘다(아트 비율은 항상 지킨다).
            h = area.rect.height * BookMargin;
            h = Mathf.Min(h, area.rect.width * BookMargin / BookAspect);
        }
        book.sizeDelta = new Vector2(h * BookAspect, h);
    }

    /// <summary>
    /// 하단 바 <b>톤만</b> 맞춘다 — 위치는 프리팹 앵커(0.082~0.158)가 갖는다.
    /// 프리팹 버튼은 형광 보라라 양피지 위에서 혼자 튀고, 요약·리롤 글자는 흰색이라 날아간다.
    ///
    /// <para>10-11: 요약 문장(「연격 × 마지막 숨결 → …」)은 지웠다 — 가운데 두루마리와 같은 말이었다(설계서 1장 4).
    /// 이 칸은 이제 「왜 새길 수 없나」(봉인된 조합 · 이미 보유)만 수치색으로 쓴다. 띠에는 「리롤 n」과 버튼만 남는다.</para>
    /// </summary>
    private void ApplyBottomBarSkin()
    {
        ApplyForgeBanner();

        if (_forgeSummary    != null)
        {
            _forgeSummary.enableAutoSizing = true;
            UITheme.StyleThinText(_forgeSummary, UIWeight.Regular, LabelSize, NumInk);
            // 하단 바는 책 폭의 2~98%라 왼쪽 끝이 두루마리 말린 곳(양피지 밖)에 걸린다 — 테두리 안에서 시작해 리롤 앞에서 끝낸다(09-29).
            var srt = _forgeSummary.rectTransform;
            srt.anchorMin = new Vector2(SummaryLeft, srt.anchorMin.y);
            srt.anchorMax = new Vector2(SummaryRight, srt.anchorMax.y);
            srt.offsetMin = new Vector2(0f, srt.offsetMin.y);
            srt.offsetMax = new Vector2(0f, srt.offsetMax.y);
            _forgeSummary.fontSizeMax = LabelSize;
            _forgeSummary.fontSizeMin = 15f;
        }
        if (_rerollCountText != null) UITheme.StyleThinText(_rerollCountText, UIWeight.Medium, LabelSize, LabelInk);
    }

    /// <summary>
    /// [서약 새기기] · [새긴다] 켜고 끄기 + 후광(이 화면의 후광은 이것 하나 — v2-0).
    /// 누를 수 없을 땐 글자도 흐린다 — 배너만 회색이 되고 먹 글자는 그대로 진해 「눌러도 된다」로 읽혔다.
    /// </summary>
    private void SetForgeReady(bool ready)
    {
        if (_forgeButton == null) return;
        _forgeButton.interactable = ready;
        UIAffordGlow.Set(_forgeButton, ready);   // 벼릴 수 있으면 은은한 불(09-29)
        foreach (var lbl in _forgeButton.GetComponentsInChildren<TMP_Text>(true))
            lbl.alpha = ready ? 1f : 0.45f;
    }

    /// <summary>
    /// 조립 버튼 — 납품 양피지 배너를 입히고 <b>아트 비율(3.34:1)로 폭을 다시 잡는다</b>.
    ///
    /// <para>버튼이 299×51(5.86:1)이라 그대로 넣으면 양끝 나침반·밀랍인장이 세로로 짓눌린다.
    /// 높이는 그대로 두고 폭만 비율에 맞추면 장식이 제 모양을 지킨다.</para>
    ///
    /// <para>연마를 걷어내 하단 바에 이 버튼 하나만 남았다 — 넓게 앉아도 자리가 충분하고,
    /// 「체결」이라는 되돌릴 수 없는 행동에 무게도 맞는다.</para>
    ///
    /// <para>아트가 없으면 예전 청동 틴트로 돌아간다.</para>
    /// </summary>
    private void ApplyForgeBanner()
    {
        var art = UISkin.Covenant?.forgeBanner;
        if (art == null || _forgeButton == null) { TintButton(_forgeButton, ForgeFill); return; }

        if (_forgeButton.TryGetComponent<Image>(out var img))
        {
            img.sprite = art;
            img.type   = Image.Type.Sliced;   // 양끝 장식은 늘리지 않는다
            img.color  = Color.white;
        }

        var rt = (RectTransform)_forgeButton.transform;
        float h = rt.rect.height > 1f ? rt.rect.height : rt.sizeDelta.y;
        if (h > 1f)
        {
            float aspect = art.rect.width / Mathf.Max(1f, art.rect.height);
            // 이 버튼도 스트레치 앵커라 sizeDelta는 앵커폭(≈300)에 더해진다.
            // 절대 폭은 앵커와 무관하게 크기를 세우는 이 API로 준다.
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, h * aspect);
        }

        // 10-11: 배너 가운데(#C8A272) 위 크림 글자는 2.0:1이라 「누를 것」이 안 보였다 → 먹 #2A1C0E 7:1 · Noto 500(리서치 B §4-4 P1).
        foreach (var lbl in _forgeButton.GetComponentsInChildren<TMP_Text>(true))
            UITheme.StyleThinText(lbl, UIWeight.Medium, ButtonSize, InkOnBook);
    }

    private static void TintButton(Button btn, Color fill)
    {
        if (btn == null) return;

        if (btn.TryGetComponent<Image>(out var img))
        {
            img.sprite = null;   // 프리팹에 구워진 보라 판을 걷어낸다
            img.color  = fill;
        }
        foreach (var t in btn.GetComponentsInChildren<TMP_Text>(true))
            t.color = BtnLabel;
    }

    // ── 열 머리 ─────────────────────────────────────────
    // 칩 아트(글자가 구워진 판)는 눌리지 않는데 버튼처럼 보였고, 첫 카드와 77~94px 떨어져 카드 묶음의 머리로 안 읽혔다(리서치 B §4-3).
    // 글자 라벨(17 · 500 · 자간) + 아래 1px 선으로 바꾸고 첫 카드 윗변 바로 위로 내린다(설계서 1장 1 · 리서치 P2 「24px」).
    // 값은 열(Column) 세로 비율 — 열 높이 761(1080 기준)에서 첫 카드 윗변 0.728.
    private const float HeadTracking = 12f;      // 자간(em/100)
    private const float HeadLabelMin = 0.758f;   // 라벨 — 첫 카드 윗변에서 약 23~51px 위
    private const float HeadLabelMax = 0.795f;
    private const float HeadRuleY    = 0.752f;   // 밑줄 — 첫 카드 윗변에서 약 18px 위
    private static readonly Color HeadRuleInk = new(0.165f, 0.110f, 0.055f, 0.35f);

    /// <summary>열 머리(원인 · 결과 · 효과)를 글자 라벨 + 밑줄로. 칩 아트는 끈다(스킨 슬롯은 그대로 둔다).</summary>
    private void ApplyHeaderLabels()
    {
        StyleHeader(_causeHeaderChip,  _causeHeaderText,  "원인");
        StyleHeader(_resultHeaderChip, _resultHeaderText, "결과");
        _effectHeadRule = StyleHeader(_effectHeaderChip, _effectHeaderText, "효과");
    }

    /// <summary>머리 한 칸 — 칩을 끄고 라벨을 켠 뒤 밑줄을 단다. 밑줄 Image를 돌려준다(쓰기 판이 껐다 켠다).</summary>
    private static Image StyleHeader(Image chip, TMP_Text label, string text)
    {
        if (chip != null) chip.enabled = false;
        if (label == null) return null;
        label.gameObject.SetActive(true);
        label.text = text;
        label.enableAutoSizing = false;
        UITheme.StyleThinText(label, UIWeight.Medium, LabelSize, LabelInk);
        label.characterSpacing = HeadTracking;
        label.alignment        = TextAlignmentOptions.Bottom;
        label.raycastTarget    = false;
        var rt = label.rectTransform;
        rt.anchorMin = new Vector2(0f, HeadLabelMin);
        rt.anchorMax = new Vector2(1f, HeadLabelMax);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return EnsureLine(rt.parent as RectTransform, "HeadRule(Runtime)", HeadRuleY, 0.08f, 0.92f, HeadRuleInk);
    }

    /// <summary>가로 1px 선 한 줄(없으면 만든다 · 있으면 다시 앉힌다) — 열 머리 밑줄 · 두루마리 구분선.</summary>
    private static Image EnsureLine(RectTransform parent, string name, float y, float xMin, float xMax, Color color)
    {
        if (parent == null) return null;
        var found = parent.Find(name);
        Image img;
        if (found != null) img = found.GetComponent<Image>();
        else
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            img = go.GetComponent<Image>();
            img.raycastTarget = false;
        }
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(xMin, y);
        rt.anchorMax = new Vector2(xMax, y);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(0f, UITheme.Thin.Stroke);
        rt.anchoredPosition = Vector2.zero;
        img.color = color;
        return img;
    }

    // ── 가운데 두루마리 = 문장 조립 ───────────────────────
    // 종이 안쪽 장식선 ≈ 열 x 0.133~0.865 · y 0.195~0.625(28.png 실측). 글은 그 안에 좌우 대칭으로 앉힌다
    // (예전 0.159~0.788 — 열 가운데에서 10.7px 왼쪽으로 치우쳤다). 위에서부터
    // 등급 줄(17 · 500) → 문장 앞절 = 원인(20) → 「→ 뒷절」 = 효과(20) → 가는 선 → 봉인 계수(17) → 효과 수치 · 시너지(17).
    private const float ScrollL = 0.16f, ScrollR = 0.84f;
    private static readonly Vector2 PlateMin = new(0.14f, 0.200f);
    private static readonly Vector2 PlateMax = new(0.86f, 0.622f);
    private static readonly Vector2 TitleY   = new(0.566f, 0.602f);
    private static readonly Vector2 CauseY   = new(0.492f, 0.560f);   // 두 절 사이 약 8px — 점선 칸 둘이 겹치지 않게
    private static readonly Vector2 VerbY    = new(0.418f, 0.482f);
    private const float ScrollRuleY = 0.405f;
    private static readonly Vector2 CoefY    = new(0.360f, 0.396f);
    private static readonly Vector2 EffectY  = new(0.212f, 0.356f);
    private const float  SentenceSize = 20f;   // 문장(리서치 「Regular 18」 · 설계서 22 → v2 단계 20)
    private const float  SentenceMin  = 17f;
    private const float  RowMin       = 15f;
    private const float  LabelCol     = 90f;   // 항목 이름 열 폭(px) — 값은 그 오른쪽에서 시작하고 줄바꿈해도 같은 자리로
    private const string HintSizePct  = "85%"; // 자리표시 글자 = 20의 85% ≈ 17
    private const float  SlotPad      = 10f;   // 자리표시 글자의 왼쪽 여백(점선 안쪽)
    private Image _plateFrame;   // 가운데 문장 칸 1px 테 — 원인 · 효과를 다 고르면(맺을 준비) 짙은 금(RowLineInk), 아니면 옅은 먹
    private static readonly Color SlotInk  = new(0.165f, 0.110f, 0.055f, 0.45f);
    private static readonly Color RuleInk  = new(0.165f, 0.110f, 0.055f, 0.22f);

    /// <summary>
    /// 두루마리 글 자리 · 서체 · 밝은 판 · 자리표시 칸. 개편 스킨(밝은 종이)일 때 Setup마다 한 번 돈다 — 만드는 것은 이름으로 찾아 한 번만.
    /// 저작 프리팹의 rect를 런타임에 다시 쓴다(프리팹 무수술).
    /// </summary>
    private void ApplyScrollLayout()
    {
        var column = _previewTitle != null ? _previewTitle.rectTransform.parent as RectTransform : null;
        if (column == null) return;

        EnsurePlate(column);
        EnsureLine(column, "ScrollRule(Runtime)", ScrollRuleY, ScrollL, ScrollR, RuleInk);

        StyleScrollText(_previewTitle,     TitleY,  UIWeight.Medium,  LabelSize,    RowMin,      false, TextAlignmentOptions.MidlineLeft);
        StyleScrollText(_previewCondition, CauseY,  UIWeight.Regular, SentenceSize, SentenceMin, true,  TextAlignmentOptions.BottomLeft);
        StyleScrollText(_previewSentence,  VerbY,   UIWeight.Regular, SentenceSize, SentenceMin, true,  TextAlignmentOptions.TopLeft);
        StyleScrollText(_previewCoef,      CoefY,   UIWeight.Regular, LabelSize,    RowMin,      false, TextAlignmentOptions.MidlineLeft);
        StyleScrollText(_previewEffect,    EffectY, UIWeight.Regular, LabelSize,    RowMin,      true,  TextAlignmentOptions.TopLeft);

        _causeSlot  = EnsureSlot(_previewCondition);
        _effectSlot = EnsureSlot(_previewSentence);
    }

    /// <summary>두루마리 글 한 칸 — 자리 · Noto 굵기 · 크기(자동 크기는 줄이기만) · 기울임/가짜 굵게 0.</summary>
    private static void StyleScrollText(TMP_Text t, Vector2 y, UIWeight weight, float size, float min, bool wrap, TextAlignmentOptions align)
    {
        if (t == null) return;
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(ScrollL, y.x);
        rt.anchorMax = new Vector2(ScrollR, y.y);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        t.enableAutoSizing = true;
        UITheme.StyleThinText(t, weight, size, InkOnBook);
        t.fontSizeMax      = size;
        t.fontSizeMin      = min;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        t.overflowMode     = TextOverflowModes.Ellipsis;
        t.alignment        = align;
        t.characterSpacing = 0f;
        t.margin           = Vector4.zero;
    }

    /// <summary>가운데 문장 칸의 1px 선 틀 — 글 아래에 한 번 깐다(쓰기 판 줄과 같은 ThinBox · 10-16 크림 판 대신).</summary>
    private void EnsurePlate(RectTransform column)
    {
        const string name = "PaperFrame(Runtime)";
        var found = column.Find(name);
        if (found != null) { _plateFrame = found.GetComponent<Image>(); return; }
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(column, false);
        int above = -1;
        if (_prismBg     != null && _prismBg.transform.parent     == column) above = Mathf.Max(above, _prismBg.transform.GetSiblingIndex());
        if (_prismBorder != null && _prismBorder.transform.parent == column) above = Mathf.Max(above, _prismBorder.transform.GetSiblingIndex());
        rt.SetSiblingIndex(above + 1);
        rt.anchorMin = PlateMin;
        rt.anchorMax = PlateMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        _plateFrame = go.GetComponent<Image>();
        StyleBox(_plateFrame, ThinBox, HeadRuleInk);
    }

    /// <summary>자리표시 점선 칸 — 글 상자보다 사방으로 조금 크게. 처음엔 꺼 둔다.</summary>
    private static Image EnsureSlot(TMP_Text host)
    {
        if (host == null) return null;
        const string name = "Slot(Runtime)";
        var found = host.transform.Find(name);
        if (found != null) return found.GetComponent<Image>();
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(host.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-6f, -2f);
        rt.offsetMax = new Vector2(6f, 2f);
        var img = go.GetComponent<Image>();
        StyleBox(img, DashBox, SlotInk);
        img.enabled = false;
        return img;
    }

    /// <summary>1px 테 Image — 점선은 변을 되풀이(Tiled), 실선은 늘림(Sliced). 안쪽은 비운다.</summary>
    private static void StyleBox(Image img, Sprite box, Color color)
    {
        img.sprite        = box;
        img.type          = box == DashBox ? Image.Type.Tiled : Image.Type.Sliced;
        img.fillCenter    = false;
        img.color         = color;
        img.raycastTarget = false;
    }

    /// <summary>1px 점선 테 — 변 가운데 8px를 4px 선 · 4px 틈으로 되풀이한다(Tiled). 자리표시 칸.</summary>
    private static Sprite DashBox => s_dashBox != null ? s_dashBox : (s_dashBox = BakeBox("CovDashBox", dashed: true));

    /// <summary>1px 실선 테 — 9-slice라 늘려도 선은 1px로 남는다. 쓰기 판 줄 · [고쳐] 라벨.</summary>
    private static Sprite ThinBox => s_thinBox != null ? s_thinBox : (s_thinBox = BakeBox("CovThinBox", dashed: false));

    /// <summary>16×16 흰 테(바깥 1px) · 9-slice 경계 4. 점선이면 변 가운데 칸(4~11)의 뒤 절반을 비운다.</summary>
    private static Sprite BakeBox(string name, bool dashed)
    {
        const int n = 16, b = 4;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                bool edge  = x == 0 || y == 0 || x == n - 1 || y == n - 1;
                int  along = (x == 0 || x == n - 1) ? y : x;
                bool gap   = dashed && along >= b && along < n - b && ((along - b) / 4) % 2 == 1;
                px[y * n + x] = new Color32(255, 255, 255, (byte)(edge && !gap ? 255 : 0));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        var s = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                              SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        s.name      = name;
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    private static void SkinImage(Image img, Sprite sprite)
    {
        if (img == null || sprite == null) return;
        img.sprite = sprite;
        img.type   = Image.Type.Sliced;
        img.color  = Color.white;
    }

    private void Select(bool isCause, int idx)
    {
        Managers.Sound?.PlayUiEvent(SoundEvent.CovenantPick);
        if (isCause) _selCause = idx;
        else         _selEffect = idx;
        RefreshSelection();
    }

    private void Reroll(bool isCause, int idx)
    {
        if (_rerollsLeft <= 0) { Managers.Sound?.PlayUiEvent(SoundEvent.UiReject); return; }   // 리롤 소진
        Managers.Sound?.PlayUiEvent(SoundEvent.CovenantReroll);

        var data = isCause ? _causes : _effects;

        CovenantDraftCard? rolled;
        if (isCause)
        {
            var exclude = new HashSet<string>();
            for (int i = 0; i < data.Count; i++) exclude.Add(data[i].id);
            rolled = CovenantAssembleService.RerollCauseCard(exclude, _rng, _forceSilver, _effects, HeldCovenants);
        }
        else
        {
            // 효과는 방어축 보장·페어링·짝 규칙을 리롤로 우회할 수 없다(axisLock + C4) — 서비스가 판정한다.
            rolled = CovenantAssembleService.RerollEffectCard(data, idx, _rng, _forceSilver, HeldCovenants, _causes, BuildStatus);
        }
        if (rolled == null) return;

        data[idx] = rolled.Value;
        var cards = isCause ? _causeCards : _effectCards;
        BindCard(cards[idx], rolled.Value, isCause);

        _rerollsLeft--;
        RefreshRerollText();
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        for (int i = 0; i < _causeCards.Length; i++)
            if (_causeCards[i]) _causeCards[i].SetSelected(i == _selCause);
        for (int i = 0; i < _effectCards.Length; i++)
            if (_effectCards[i]) _effectCards[i].SetSelected(i == _selEffect);
        RefreshSynergyGlow();
        UpdatePreview();
        UpdateConnectors();
    }

    /// <summary>빌드(유물)가 거는 상태 — 소모형 카드의 먹이로 친다(서약서 설계서 §5).</summary>
    private static HashSet<StatusCurrency> BuildStatus
        => CovenantBuildStatus.Collect(GameRunBootstrapper.Instance?.Run);

    /// <summary>보유 서약(런 중 조립 서약만). 시너지 판정의 상대편.</summary>
    private static IReadOnlyList<CovenantBase> HeldCovenants
        => GameRunBootstrapper.Instance?.Run?.CovenantHandler?.Covenants;

    /// <summary>이미 가진 서약과 통화가 물리는 효과 카드를 켠다 — 그물은 고르는 순간 보여야 짤 수 있다.</summary>
    private void RefreshSynergyGlow()
    {
        var held = HeldCovenants;
        if (_effectCards == null) return;

        for (int i = 0; i < _effectCards.Length; i++)
        {
            if (_effectCards[i] == null || i >= _effects.Count) continue;
            _effectCards[i].SetSynergy(CovenantAssemblePreview.HasSynergy(_effects[i].id, held));
        }
    }

    /// <summary>선택된 원인/효과 카드에서 중앙 결과 카드로 이어지는 대각 연결선을 갱신.</summary>
    private void UpdateConnectors()
    {
        if (_connectorsOff || _bookRect == null || _prismRect == null) return;

        if (_connectorLeft && _causeCards != null && _selCause >= 0 && _selCause < _causeCards.Length && _causeCards[_selCause])
            DrawLine(_connectorLeft,
                     EdgeInBook((RectTransform)_causeCards[_selCause].transform, +1f),  // 카드 우측 중앙
                     EdgeInBook(_prismRect, -1f));                                       // 프리즘 좌측 중앙

        if (_connectorRight && _effectCards != null && _selEffect >= 0 && _selEffect < _effectCards.Length && _effectCards[_selEffect])
            DrawLine(_connectorRight,
                     EdgeInBook((RectTransform)_effectCards[_selEffect].transform, -1f), // 카드 좌측 중앙
                     EdgeInBook(_prismRect, +1f));                                        // 프리즘 우측 중앙
    }

    /// <summary>rt의 좌/우측 변 중앙점을 _bookRect 로컬 좌표로 변환. side +1=우측, -1=좌측.</summary>
    private Vector2 EdgeInBook(RectTransform rt, float side)
    {
        Vector3 edgeLocal = new(rt.rect.width * 0.5f * side, 0f, 0f);
        Vector3 world     = rt.TransformPoint(edgeLocal);
        return _bookRect.InverseTransformPoint(world);
    }

    private void DrawLine(RectTransform line, Vector2 a, Vector2 b)
    {
        Vector2 mid = (a + b) * 0.5f;
        float   len = Vector2.Distance(a, b);
        float   ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
        line.anchoredPosition = mid;
        line.sizeDelta        = new Vector2(len, line.sizeDelta.y);
        line.localEulerAngles = new Vector3(0f, 0f, ang);
        if (!line.gameObject.activeSelf) line.gameObject.SetActive(true);
    }

    /// <summary>
    /// 가운데 두루마리 = 문장 조립(설계서 1장 3). 등급 줄 → 「원인 조건 → 효과 동사구」 한 문장 → 수치 두 줄(봉인 계수 · 효과).
    /// 안 고른 쪽은 점선 칸 「원인을 고른다」 / 「효과를 고른다」. 따옴표 · 기울임 없이, 수치만 적갈 + Noto 500.
    /// 「발동 조건」 줄은 문장 앞절과 같은 말이라 없앴고, 아래 띠 요약도 같은 말이라 지웠다(경고만 남긴다).
    /// </summary>
    private void UpdatePreview()
    {
        bool hasCause  = _causes  != null && _selCause  >= 0 && _selCause  < _causes.Count;
        bool hasEffect = _effects != null && _selEffect >= 0 && _selEffect < _effects.Count;
        CovenantPalette.CauseDef  cd = default;
        CovenantPalette.EffectDef ed = default;
        hasCause  = hasCause  && CovenantPalette.TryGetCause(_causes[_selCause].id, out cd);
        hasEffect = hasEffect && CovenantPalette.TryGetEffect(_effects[_selEffect].id, out ed);
        if (_plateFrame != null) _plateFrame.color = hasCause && hasEffect ? RowLineInk : HeadRuleInk;   // 금 = 준비(맺을 수 있다)

        // 두루마리가 밝은 종이면(개편 스킨) 잉크 · 적갈, 어두운 판이면(구 아트) 상아 · 금.
        string num   = _resultOnPaper ? UI_AssembleCard.NumOnParchment : UIKeywordInk.OnDark;
        string label = _resultOnPaper ? PaperLabelHex : PreviewLabelHex;
        RestartFades();

        // 등급 줄 — 「◆ 연격 · ◆ 마지막 숨결」. 등급은 「[골드]」 날글자 대신 등급색 마름모(설계서 1장 3). 안 고른 쪽은 흐린 자리 이름.
        string causeHead  = hasCause  ? $"{TierMark(_causes[_selCause].tier)} {cd.name}"   : $"<color={label}>원인</color>";
        string effectHead = hasEffect ? $"{TierMark(_effects[_selEffect].tier)} {ed.name}" : $"<color={label}>효과</color>";
        SetText(_previewTitle, $"{causeHead}  <color={label}>·</color>  {effectHead}");

        // 문장 — 앞절(원인 조건) → 뒷절(첫 결과는 「곧바로」 이음이라 머리말 없는 동사구).
        SetClause(_previewCondition, _causeSlot, hasCause ? UI_AssembleCard.InkNumbers(cd.desc, num) : null,
                  string.Empty, "원인을 고른다", label, TextAlignmentOptions.BottomLeft);
        SetClause(_previewSentence, _effectSlot, hasEffect ? "→ " + CovenantGrammar.Line(ed.id, ClauseLink.Immediate, null) : null,
                  "→ ", "효과를 고른다", label, TextAlignmentOptions.TopLeft);

        // 수치 두 줄 — 항목 이름 왼쪽 열(90) + 값 오른쪽 열(리서치 B §4-4 P2 「하데스 계약서 2열」).
        string coef = hasCause
            ? UI_AssembleCard.InkNumbers($"×{CovenantMath.RawCoef(cd, _causes[_selCause].tier):0.0}", num)
            : $"<color={label}>—</color>";
        SetTextFading(_previewCoef, Row("봉인 계수", coef, label));

        var p = hasCause && hasEffect
            ? CovenantAssemblePreview.Build(_causes[_selCause].id, _causes[_selCause].tier, _effects[_selEffect].id, _effects[_selEffect].tier)
            : default;
        // 원인을 고르기 전엔 효과 크기가 정해지지 않는다(원인 계수가 곱해진다) — 무슨 효과인지만 흐리게 적는다.
        string amount = p.valid    ? UI_AssembleCard.InkNumbers(p.EffectAmountLabel(), num)
                      : hasEffect  ? $"<color={label}>{ed.desc}</color>"
                      :              $"<color={label}>—</color>";
        string rows = Row("효과", amount, label);
        // 보유 서약과 물리면 한 줄 더 — 시너지는 청록 대신 수치색 + 「시너지」 글자로(리서치 P3), 종이 안에 앉는다(예전엔 아래로 삐져나갔다).
        string hint = p.valid ? p.SynergyHint(HeldCovenants) : null;
        if (!string.IsNullOrEmpty(hint))
            rows += "\n" + Row("시너지", $"<color={num}>{SynergyBody(hint)}</color>", label);
        SetTextFading(_previewEffect, rows);

        RefreshEffectAmounts();

        // 이미 가진 조합은 TryAdd가 조용히 거절해 "벼렸는데 아무 일도 없는" 상태가 된다.
        // 금지 조합은 TryAdd 자체는 통과하지만 밸런스가 무너지는 짝이라 팔레트가 막는다.
        // 둘 다 누르기 전에 잠가서 헛손질과 사고를 없앤다. 원인 · 효과를 다 고르기 전에도 잠근다(10-11).
        bool both   = hasCause && hasEffect;
        bool owned  = both && IsOwned(SelectedId());
        bool banned = both && CovenantPalette.IsBannedPair(_causes[_selCause].id, _effects[_selEffect].id);
        SetForgeReady(both && !owned && !banned);
        SetText(_forgeSummary, banned ? "봉인된 조합 — 이 원인으로는 이 효과를 벼릴 수 없다"
                             : owned  ? "이미 보유한 조합 — 원인이나 효과를 바꿔야 벼릴 수 있다"
                             :          string.Empty);
    }

    /// <summary>문장 한 절 — 고른 말이면 그대로, 아니면 점선 칸 안에 흐린 안내(85% ≈ 17).</summary>
    private void SetClause(TMP_Text t, Image slot, string filled, string lead, string hint, string label, TextAlignmentOptions align)
    {
        if (t == null) return;
        bool empty = filled == null;
        t.alignment = empty ? TextAlignmentOptions.MidlineLeft : align;
        t.margin    = empty ? new Vector4(SlotPad, 0f, 0f, 0f) : Vector4.zero;
        if (slot != null) slot.enabled = empty;
        SetTextFading(t, empty ? $"{lead}<size={HintSizePct}><color={label}>{hint}</color></size>" : filled);
    }

    /// <summary>항목 이름(왼쪽 열) + 값(오른쪽 열). 값이 줄바꿈해도 같은 열에서 이어진다(&lt;indent&gt;).</summary>
    private static string Row(string name, string value, string label)
        => $"<color={label}>{name}</color><indent={LabelCol}>{value}</indent>";

    /// <summary>시너지 힌트 앞의 「시너지」 머리말을 뗀다 — 왼쪽 열 이름이 같은 말을 한다.</summary>
    private static string SynergyBody(string hint)
    {
        const string head = "시너지";
        return hint.StartsWith(head, System.StringComparison.Ordinal) ? hint.Substring(head.Length).TrimStart() : hint;
    }

    /// <summary>양피지 위 등급 마름모. 등급색은 등급 표기에만 쓴다(v2-0). 실버의 밝은 은색은 종이 위에서 사라져 강철빛으로 눌렀다.</summary>
    private static string TierMark(CovenantTier t) => $"<color={TierInkHex(t)}>◆</color>";

    private static string TierInkHex(CovenantTier t) => t switch
    {
        CovenantTier.Gold => "#8C6412",
        CovenantTier.Ruby => "#9C2238",
        _                 => "#5B6170",
    };

    // ── 바뀌는 순간 스밈 ─────────────────────────────────
    /// <summary>이번 갱신의 스밈을 새로 시작한다 — 앞 스밈이 덜 끝났으면 끊고 그 글은 다 보이게 둔다.</summary>
    private void RestartFades()
    {
        _fadeCts?.Cancel();
        _fadeCts?.Dispose();
        _fadeCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
    }

    /// <summary>글이 바뀌었을 때만 갈아 끼우고 0.18초 스며들게 한다(톤 v2 「상태가 바뀌는 순간은 실시간 연출」). 같으면 그대로.</summary>
    private void SetTextFading(TMP_Text t, string v)
    {
        if (t == null || t.text == v) return;
        t.text = v;
        if (_fadeCts != null && isActiveAndEnabled) FadeInAsync(t, _fadeCts.Token).Forget();
    }

    private static async UniTaskVoid FadeInAsync(TMP_Text t, CancellationToken ct)
    {
        try
        {
            await UIMotion.Tween(UIMotion.Swap, k => { if (t != null) t.alpha = k; }, ct);
        }
        catch (System.OperationCanceledException)
        {
            if (t != null) t.alpha = 1f;   // 끊긴 스밈은 다 보이게 — 반쯤 흐린 채 남지 않는다
        }
    }

    /// <summary>
    /// 효과 카드 설명 = 지금 고른 원인으로 벼렸을 때의 <b>실제 수치</b>(원인 계수가 곱해진다) — 서술형 문구로는 무슨 기능인지 안 읽혔다(09-29).
    /// 원인을 고르기 전엔 카드가 서술형 설명 그대로다(수치가 아직 정해지지 않는다).
    /// </summary>
    private void RefreshEffectAmounts()
    {
        if (_effectCards == null || _causes == null || _selCause < 0 || _selCause >= _causes.Count) return;
        var cause = _causes[_selCause];
        for (int i = 0; i < _effectCards.Length && i < _effects.Count; i++)
        {
            if (_effectCards[i] == null) continue;
            var p = CovenantAssemblePreview.Build(cause.id, cause.tier, _effects[i].id, _effects[i].tier);
            if (p.valid) _effectCards[i].SetSub(p.EffectAmountCompact());
        }
    }

    private string SelectedId()
    {
        var cause  = _causes[_selCause];
        var effect = _effects[_selEffect];
        return CovenantSentence.MakeId(cause.id, cause.tier,
            new List<(string, CovenantTier, ClauseLink)> { (effect.id, effect.tier, ClauseLink.Immediate) });
    }

    private static bool IsOwned(string id)
        => GameRunBootstrapper.Instance?.Run?.CovenantHandler?.Has(id) ?? false;

    /// <summary>
    /// 남은 리롤 표시 + <b>카드의 리롤 버튼 게이팅</b>. 소진 뒤에도 버튼이 멀쩡히 켜져 있으면
    /// 눌러도 아무 일이 없어(<see cref="Reroll"/>의 첫 줄에서 return) 고장으로 읽힌다.
    /// 고르는 버튼(SelectButton)은 건드리지 않는다 — 리롤이 떨어져도 서약은 골라야 한다.
    /// </summary>
    private void RefreshRerollText()
    {
        SetText(_rerollCountText, $"리롤 {_rerollsLeft}");

        bool canReroll = _rerollsLeft > 0;
        GateReroll(_causeCards,  canReroll);
        GateReroll(_effectCards, canReroll);
    }

    private static void GateReroll(UI_AssembleCard[] cards, bool on)
    {
        if (cards == null) return;
        for (int i = 0; i < cards.Length; i++)
            if (cards[i] && cards[i].RerollButton) cards[i].RerollButton.interactable = on;
    }

    // ── 체결 ─────────────────────────────────────────────
    /// <summary>고른 원인·효과 한 쌍을 확정하고 팝업을 닫는다.</summary>
    private void OnForge()
    {
        if (_selCause < 0 || _selEffect < 0) return;   // 둘 다 고르기 전(버튼 잠금이 뚫린 경로 대비)
        string id = SelectedId();
        if (IsOwned(id)) return;   // 잠금이 뚫린 경로(키보드 등) 대비 최종 방어

        Managers.Sound?.PlayUiEvent(SoundEvent.CovenantForge);
        _forged = true;

        // 닫기가 먼저다. TrySetResult가 대기 측(WorldCovenantAltar.OpenAsync) 후속을 동기로 재개시킬 수 있어,
        // 순서를 뒤집으면 팝업이 열린 채(=HUD 차단/timeScale 0) 획득 안내가 떠 안내가 화면에 눌어붙는다.
        ClosePopupUI();
        _tcs?.TrySetResult(id);
    }

    private static Color TierColor(CovenantTier t) => t switch
    {
        CovenantTier.Gold  => GoldColor,
        CovenantTier.Ruby  => RubyColor,
        _                  => SilverColor,
    };

    private static void SetText(TMP_Text t, string v) { if (t) t.text = v; }

    /// <summary>
    /// 리롤 버튼 라벨. 프리팹은 ↻(U+21BB)로 authoring 돼 있는데 본문 폰트(DNFForgedBlade)에
    /// 그 글리프가 없어 화면에는 빈 네모(□)만 나온다 — 폰트에 있는 글자로 바꿔 준다.
    /// 10-11: DNF Bold + 가짜 굵게 16 → Noto 500 15(리서치 B §4-4 P3). 칩은 56×32로 커졌다(<see cref="UI_AssembleCard.DockReroll"/>).
    /// </summary>
    private static void FixRerollLabel(Button btn)
    {
        var lbl = btn.GetComponentInChildren<TMP_Text>(true);
        if (lbl == null) return;
        lbl.text = "리롤";

        // 버튼이 카드 비율을 따라가면서 카드가 작을 땐 22px까지 좁아진다 — 14pt 두 글자가 넘친다.
        // 다른 라벨과 같은 규칙으로 박스 안에 가둔다(키우지는 않는다).
        lbl.textWrappingMode = TextWrappingModes.NoWrap;
        lbl.overflowMode     = TextOverflowModes.Ellipsis;
        lbl.enableAutoSizing = true;
        UITheme.StyleThinText(lbl, UIWeight.Medium, ChipSize, lbl.color);   // 색은 카드가 칠한 청동 위 잉크 그대로
        lbl.fontSizeMin      = 12f;
    }

    /// <summary>
    /// 미리보기 텍스트들의 넘침 처리를 코드에서 확정한다.
    ///
    /// 여기 들어가는 문자열은 전부 <b>길이가 가변</b>이다 — 원인/효과 이름과 티어가 조합될 때마다
    /// 길이가 달라지는데 프리팹은 고정 크기 박스에 자동 축소도 줄바꿈도 없이 authoring 돼 있었다.
    /// 그래서 조합에 따라 중앙 결과 문장이 박스를 뚫고 나가 옆 요소 위에 겹쳤다.
    ///
    /// 한 줄짜리 라벨은 줄바꿈 없이 말줄임(…), 중앙 결과 문장은 줄바꿈 + 자동 축소로 박스 안에 가둔다.
    /// 프리팹을 고쳐도 되지만, 값이 코드에서 오는 이상 제약도 코드가 쥐고 있어야 다시 깨지지 않는다.
    /// 개편 스킨(밝은 종이)이면 두루마리 글은 <see cref="ApplyScrollLayout"/>이 자리 · 서체째 다시 잡는다.
    /// </summary>
    private void ConfigureTextFitting()
    {
        // 제목은 DNF 한 줄만 남긴다(유물 콘셉트) — 프리팹은 DNF Bold 위에 가짜 굵게(Bold 스타일)를 또 걸어 두었다(v2-0 금지).
        if (_titleText) _titleText.fontStyle = FontStyles.Normal;

        // 한 줄 라벨 — 넘치면 말줄임(레이아웃을 밀지 않는다)
        FitSingleLine(_previewTitle);
        FitSingleLine(_previewCondition);
        FitSingleLine(_previewCoef);
        // 효과 줄은 "감전 1중첩 · 방사형 3체 · 반경 5.5m"처럼 길어 한 줄로는 반드시 잘린다.
        // 상자가 세로로 넉넉하니(종이면 아래 1/4) 줄바꿈을 허용해 끝까지 읽히게 한다.
        if (_previewEffect != null)
        {
            _previewEffect.textWrappingMode = TextWrappingModes.Normal;
            _previewEffect.overflowMode     = TextOverflowModes.Truncate;
            _previewEffect.enableAutoSizing = true;
            _previewEffect.fontSizeMin      = Mathf.Max(9f, _previewEffect.fontSize * 0.7f);
            _previewEffect.fontSizeMax      = _previewEffect.fontSize;
        }
        FitSingleLine(_forgeSummary);

        // 중앙 결과 문장 — 여러 줄 허용 + 박스에 맞게 자동 축소(최대 62%까지만 줄인다)
        FitWrapped(_previewSentence, minRatio: 0.62f);
    }

    private static void FitSingleLine(TMP_Text t)
    {
        if (!t) return;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode     = TextOverflowModes.Ellipsis;
    }

    /// <summary>
    /// 박스에 맞게 <b>줄이기만</b> 한다. 예전엔 fontSizeMax를 minSize*2로 올려버려서,
    /// authoring 값(17)보다 하한(18)이 크면 상한이 36으로 튀어 결과 문장이 원래보다 두 배로 커졌다
    /// — 화면에서 중앙 문장만 유독 거대해 보이던 원인이다. 자동 크기는 authoring 값을 넘지 않는다.
    /// </summary>
    private static void FitWrapped(TMP_Text t, float minRatio)
    {
        if (!t) return;
        float authored = t.fontSize;
        t.textWrappingMode  = TextWrappingModes.Normal;
        t.overflowMode      = TextOverflowModes.Truncate;
        t.enableAutoSizing  = true;
        t.fontSizeMax       = authored;
        t.fontSizeMin       = Mathf.Max(10f, authored * minRatio);
    }
}
