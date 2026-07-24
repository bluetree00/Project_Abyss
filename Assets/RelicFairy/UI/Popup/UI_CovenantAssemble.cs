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
        _effects  = CovenantAssembleService.DraftEffects(DraftCount, _rng, _forceSilver);
        _selCause = 0;
        _selEffect = 0;

        WireColumn(_causeCards,  _causes,  true);
        WireColumn(_effectCards, _effects, false);

        if (_forgeButton)
        {
            _forgeButton.onClick.RemoveAllListeners();
            _forgeButton.onClick.AddListener(OnForge);
        }
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
            }
        }
    }

    private void BindCard(UI_AssembleCard card, CovenantDraftCard d, bool isCause)
    {
        card.SetSkin(_silverFrame, _goldFrame, _rubyFrame, _cardBgSprite);   // 등급 아트 주입(Bind 전)
        if (isCause && CovenantPalette.TryGetCause(d.id, out var c))
            card.Bind(c.name, c.desc, d.tier, TierColor(d.tier));
        else if (!isCause && CovenantPalette.TryGetEffect(d.id, out var e))
            card.Bind(e.name, e.desc, d.tier, TierColor(d.tier));
    }

    /// <summary>패널/중앙 결과카드에 아트 스프라이트 적용(지정된 것만 — 미지정 시 기존 외형 유지).</summary>
    private void ApplyPanelSkin()
    {
        SkinImage(_panelBg,     _panelBgSprite);
        SkinImage(_panelBorder, _panelBorderSprite);
        SkinImage(_prismBg,     _prismBgSprite);
        SkinImage(_prismBorder, _prismBorderSprite);
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
        if (isCause) _selCause = idx;
        else         _selEffect = idx;
        RefreshSelection();
    }

    private void Reroll(bool isCause, int idx)
    {
        if (_rerollsLeft <= 0) return;

        var pool = isCause ? CovenantPalette.CauseIds : CovenantPalette.EffectIds;
        var data = isCause ? _causes : _effects;

        var exclude = new HashSet<string>();
        for (int i = 0; i < data.Count; i++) exclude.Add(data[i].id);

        var rolled = CovenantAssembleService.RerollCard(pool, exclude, _rng, _forceSilver);
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
        UpdatePreview();
        UpdateConnectors();
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
        SetText(_previewEffect,    $"효과  {p.EffectAmountLabel()}");
        SetText(_forgeSummary,     $"{p.causeName} × {p.effectName}  →  {p.effectDesc}");
    }

    private void RefreshRerollText() => SetText(_rerollCountText, $"리롤 {_rerollsLeft}");

    private void OnForge()
    {
        var cause  = _causes[_selCause];
        var effect = _effects[_selEffect];
        string id = AssembledCovenant.MakeId(cause.id, cause.tier, effect.id, effect.tier);
        _tcs?.TrySetResult(id);
        ClosePopupUI();
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
    private static void FixRerollLabel(Button btn)
    {
        var lbl = btn.GetComponentInChildren<TMP_Text>(true);
        if (lbl == null) return;
        lbl.text     = "리롤";
        lbl.fontSize = 14f;
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
        FitSingleLine(_previewEffect);
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
