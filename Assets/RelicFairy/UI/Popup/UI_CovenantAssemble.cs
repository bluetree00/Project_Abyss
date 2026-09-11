using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 조립 서약 팝업 — 원인(Ⅰ) × 효과(Ⅲ)를 골라 서약을 벼려낸다. 중앙(Ⅱ)은 완성 미리보기.
/// 카드 티어(실버/골드/루비)가 조건·계수·효과를 함께 내장 — 슬라이더 없이 ↻ 리롤로 굴린다.
/// 첫 서약(forceSilver)은 모든 카드가 실버로 고정. 결과 = "asm:cause@tier|effect@tier" id.
///
/// 사용법:
///   var popup = await Managers.UI.ShowPopupUIAndGetAsync&lt;UI_CovenantAssemble&gt;();
///   popup.Setup(forceSilver, rng);
///   string id = await popup.WaitForResultAsync();   // null = 취소
/// </summary>
public class UI_CovenantAssemble : UI_Popup
{
    // ── Constants ────────────────────────────────────────
    private const int DraftCount     = 3;
    private const int DefaultRerolls = 2;

    // ── Static ───────────────────────────────────────────
    private static readonly Color SilverColor = new(0.80f, 0.82f, 0.88f);
    private static readonly Color GoldColor   = new(0.95f, 0.74f, 0.28f);
    private static readonly Color RubyColor   = new(0.92f, 0.28f, 0.40f);   // 루비 = 진홍
    private static readonly Color SynergyColor = new(0.35f, 0.90f, 0.80f);  // 시너지 = 청록(등급색과 안 겹치게)

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
    private List<CovenantDraftCard> _causes;
    private List<CovenantDraftCard> _effects;
    private int _selCause, _selEffect;
    private UniTaskCompletionSource<string> _tcs;

    // 시너지 힌트 — 프리팹에 자리가 없어 효과 줄을 복제해 그 아래 한 줄을 만든다.
    private TMP_Text _synergyText;

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
    }

    // ── Public Methods ───────────────────────────────────
    /// <summary>드래프트를 굴려 팝업을 구성한다. forceSilver=true면 첫 서약(실버 고정).</summary>
    public void Setup(bool forceSilver, System.Random rng)
    {
        _forceSilver = forceSilver;
        _rng         = rng;
        _rerollsLeft = DefaultRerolls;
        _tcs         = new UniTaskCompletionSource<string>();
        SetText(_titleText, "봉인된 예언자의 서약서");
        if (_titleText) { _titleText.fontSize = 28f; FitSingleLine(_titleText); }

        ConfigureTextFitting();
        ApplyPanelSkin();

        _causes   = CovenantAssembleService.DraftCauses(DraftCount, _rng, _forceSilver);
        // 보유 서약을 넘겨 페어링을 건다(C4) — 걸어 줄 서약이 없는데 먹는 서약만 손에 쥐면
        // 벼린 서약이 한 번도 터지지 않는 런이 된다.
        _effects  = CovenantAssembleService.DraftEffects(DraftCount, _rng, _forceSilver, HeldCovenants);
        _selCause = 0;
        _selEffect = 0;

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
                PlaceRerollAtTop(card.RerollButton);
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
            SkinImage(_prismBg, skin.resultScroll);
            if (_prismBorder != null) _prismBorder.enabled = false;
        }

        // 제목은 양피지 두루마리의 <b>말린 나무 봉 위</b>에 얹힌다 — authoring 금색(0.83,0.68,0.3)은
        // 봉의 갈색과 명도가 겹쳐 글자가 뭉개진다. 밝은 상아색으로 올려 대비를 준다.
        if (_titleText != null) _titleText.color = TitleOnScroll;

        ApplyHeaderChips(skin);
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
    private static readonly Color TitleOnScroll = new(0.97f, 0.93f, 0.80f, 1f);
    private static readonly Color ForgeFill     = new(0.36f, 0.26f, 0.13f, 1f);   // 청동
    private static readonly Color BtnLabel      = new(0.97f, 0.93f, 0.80f, 1f);
    private static readonly Color InkOnBook     = new(0.20f, 0.14f, 0.08f, 1f);
    private static readonly Color InkOnBookDim  = new(0.36f, 0.28f, 0.19f, 1f);
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
    /// </summary>
    private void ApplyBottomBarSkin()
    {
        ApplyForgeBanner();

        if (_forgeSummary    != null) _forgeSummary.color    = InkOnBook;
        if (_rerollCountText != null) _rerollCountText.color = InkOnBookDim;
        if (_synergyText     != null) _synergyText.color     = InkOnBookDim;
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

        foreach (var lbl in _forgeButton.GetComponentsInChildren<TMP_Text>(true))
            lbl.color = BtnLabel;
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

    /// <summary>열 머리표(원인·결과·효과) 교체. 아트에 글자가 구워져 있어 코드 라벨은 끈다.</summary>
    private void ApplyHeaderChips(CovenantSkinSO skin)
    {
        SkinHeader(_causeHeaderChip,  _causeHeaderText,  skin.headerCause);
        SkinHeader(_resultHeaderChip, _resultHeaderText, skin.headerResult);
        SkinHeader(_effectHeaderChip, _effectHeaderText, skin.headerEffect);
    }

    private static void SkinHeader(Image chip, TMP_Text label, Sprite art)
    {
        if (chip == null || art == null) return;
        chip.sprite = art;
        chip.type   = Image.Type.Simple;   // 글자가 구워져 있어 9-slice로 늘리면 깨진다
        chip.color  = Color.white;
        if (label != null) label.gameObject.SetActive(false);
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
        Managers.Sound.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();
        if (isCause) _selCause = idx;
        else         _selEffect = idx;
        RefreshSelection();
    }

    private void Reroll(bool isCause, int idx)
    {
        if (_rerollsLeft <= 0) return;
        Managers.Sound.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();

        var data = isCause ? _causes : _effects;

        CovenantDraftCard? rolled;
        if (isCause)
        {
            var exclude = new HashSet<string>();
            for (int i = 0; i < data.Count; i++) exclude.Add(data[i].id);
            rolled = CovenantAssembleService.RerollCard(CovenantPalette.CauseIds, exclude, _rng, _forceSilver);
        }
        else
        {
            // 효과는 방어축 보장·페어링을 리롤로 우회할 수 없다(axisLock + C4) — 서비스가 판정한다.
            rolled = CovenantAssembleService.RerollEffectCard(data, idx, _rng, _forceSilver, HeldCovenants);
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
        if (_bookRect == null || _prismRect == null) return;

        if (_connectorLeft && _causeCards != null && _selCause < _causeCards.Length && _causeCards[_selCause])
            DrawLine(_connectorLeft,
                     EdgeInBook((RectTransform)_causeCards[_selCause].transform, +1f),  // 카드 우측 중앙
                     EdgeInBook(_prismRect, -1f));                                       // 프리즘 좌측 중앙

        if (_connectorRight && _effectCards != null && _selEffect < _effectCards.Length && _effectCards[_selEffect])
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

    private void UpdatePreview()
    {
        var cause  = _causes[_selCause];
        var effect = _effects[_selEffect];
        var p = CovenantAssemblePreview.Build(cause.id, cause.tier, effect.id, effect.tier);
        if (!p.valid) return;

        SetText(_previewTitle,
            $"{p.causeName}[{p.causeTier.DisplayName()}] × {p.effectName}[{p.effectTier.DisplayName()}]");
        SetText(_previewSentence, $"\"{p.ResultSentence}\"");
        SetText(_previewCondition, $"발동 조건  {p.causeDesc}");
        SetText(_previewCoef,      $"봉인 계수  ×{p.coefficient:0.0}");
        SetText(_previewEffect,    $"효과  {p.EffectAmountLabel()}  ({p.Badge})");
        SetText(_forgeSummary,     $"{p.causeName} × {p.effectName}  →  {p.effectDesc}");

        UpdateSynergyLine(p);

        // 이미 가진 조합은 TryAdd가 조용히 거절해 "벼렸는데 아무 일도 없는" 상태가 된다.
        // 금지 조합은 TryAdd 자체는 통과하지만 밸런스가 무너지는 짝이라 팔레트가 막는다.
        // 둘 다 누르기 전에 잠가서 헛손질과 사고를 없앤다.
        if (_forgeButton)
        {
            bool owned  = IsOwned(SelectedId());
            bool banned = CovenantPalette.IsBannedPair(cause.id, effect.id);
            _forgeButton.interactable = !owned && !banned;

            if (banned)     SetText(_forgeSummary, "봉인된 조합 — 이 원인으로는 이 효과를 벼릴 수 없다");
            else if (owned) SetText(_forgeSummary, "이미 보유한 조합 — 원인이나 효과를 바꿔야 벼릴 수 있다");
        }
    }

    /// <summary>선택된 조합이 보유 서약과 물리면 한 줄로 알려준다. 없으면 줄을 감춘다.</summary>
    private void UpdateSynergyLine(CovenantAssemblePreview p)
    {
        EnsureSynergyText();
        if (_synergyText == null) return;

        string hint = p.SynergyHint(HeldCovenants);
        _synergyText.gameObject.SetActive(!string.IsNullOrEmpty(hint));
        if (!string.IsNullOrEmpty(hint)) _synergyText.text = hint;
    }

    /// <summary>
    /// 시너지 줄을 효과 줄 바로 아래에 만든다(프리팹 무수술).
    /// 효과 줄을 복제하는 이유: 폰트·정렬·자동축소 설정이 이미 이 팝업에 맞게 저작돼 있다.
    /// </summary>
    private void EnsureSynergyText()
    {
        if (_synergyText != null || _previewEffect == null) return;

        var src   = (RectTransform)_previewEffect.transform;
        var clone = Instantiate(_previewEffect, src.parent);
        clone.name  = "SynergyText(Runtime)";
        clone.color = SynergyColor;

        // 자리는 앵커로만 잡는다 — 팝업이 막 생성된 시점엔 rect.height가 아직 0이라
        // 픽셀로 내리면 효과 줄과 완전히 겹친다.
        var rt = (RectTransform)clone.transform;
        float h = src.anchorMax.y - src.anchorMin.y;
        rt.anchorMin        = src.anchorMin - new Vector2(0f, h);
        rt.anchorMax        = src.anchorMax - new Vector2(0f, h);
        rt.pivot            = src.pivot;
        rt.sizeDelta        = src.sizeDelta;
        rt.localScale       = src.localScale;
        rt.anchoredPosition = src.anchoredPosition;

        FitSingleLine(clone);
        _synergyText = clone;
    }

    private string SelectedId()
    {
        var cause  = _causes[_selCause];
        var effect = _effects[_selEffect];
        return AssembledCovenant.MakeId(cause.id, cause.tier, effect.id, effect.tier);
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
        string id = SelectedId();
        if (IsOwned(id)) return;   // 잠금이 뚫린 경로(키보드 등) 대비 최종 방어

        Managers.Sound.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();

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
    /// </summary>
    /// <summary>
    /// 교체 버튼을 카드 <b>상단 우측</b>으로 올린다.
    ///
    /// <para>프리팹에서는 카드 오른쪽 <b>아래</b> 구석(196,72)에 있었다 — 기획은 선택지 위에
    /// 작게 얹힌 「돌리는」 버튼이었는데, 아래에 있으면 카드 본문(이름·설명)을 다 읽고 나서야
    /// 눈에 들어와 "다시 뽑을까"를 고민하는 시점과 어긋난다.</para>
    ///
    /// <para>비율 앵커로 잡아 카드가 커지든 작아지든 같은 자리에 붙는다.</para>
    /// </summary>
    private static void PlaceRerollAtTop(Button btn)
    {
        if (btn == null) return;
        var rt = (RectTransform)btn.transform;

        // 카드 287×102 기준 — 우측 상단에 48×21이 여백 6을 두고 앉는다.
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(1f, 1f);
        rt.sizeDelta        = new Vector2(48f, 21f);
        rt.anchoredPosition = new Vector2(-6f, -6f);
    }

    private static void FixRerollLabel(Button btn)
    {
        var lbl = btn.GetComponentInChildren<TMP_Text>(true);
        if (lbl == null) return;
        lbl.text     = "리롤";
        lbl.fontSize = 16f;

        // 버튼이 카드 비율을 따라가면서 카드가 작을 땐 22px까지 좁아진다 — 14pt 두 글자가 넘친다.
        // 다른 라벨과 같은 규칙으로 박스 안에 가둔다(키우지는 않는다).
        lbl.textWrappingMode = TextWrappingModes.NoWrap;
        lbl.overflowMode     = TextOverflowModes.Ellipsis;
        lbl.enableAutoSizing = true;
        lbl.fontSizeMax      = 16f;   // 가독성 하한 — 14였을 땐 위의 16이 여기서 다시 눌렸다
        lbl.fontSizeMin      = 8f;
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
    /// </summary>
    private void ConfigureTextFitting()
    {
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
