using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 조립 서약 팝업 — 원인(Ⅰ) × 효과(Ⅲ)를 골라 서약을 벼려낸다. 중앙(Ⅱ)은 완성 미리보기.
/// 카드 티어(실버/골드/프리즘)가 조건·계수·효과를 함께 내장 — 슬라이더 없이 ↻ 리롤로 굴린다.
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
    private static readonly Color SilverColor = new(0.75f, 0.76f, 0.80f);
    private static readonly Color GoldColor   = new(0.86f, 0.68f, 0.24f);
    private static readonly Color PrismColor  = new(0.70f, 0.40f, 1.00f);

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

    // ── Public Methods ───────────────────────────────────
    /// <summary>드래프트를 굴려 팝업을 구성한다. forceSilver=true면 첫 서약(실버 고정).</summary>
    public void Setup(bool forceSilver, System.Random rng)
    {
        _forceSilver = forceSilver;
        _rng         = rng;
        _rerollsLeft = DefaultRerolls;
        _tcs         = new UniTaskCompletionSource<string>();
        SetText(_titleText, "봉인된 예언자의 서약서");

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
            }
        }
    }

    private void BindCard(UI_AssembleCard card, CovenantDraftCard d, bool isCause)
    {
        if (isCause && CovenantPalette.TryGetCause(d.id, out var c))
            card.Bind(c.name, c.desc, d.tier, TierColor(d.tier));
        else if (!isCause && CovenantPalette.TryGetEffect(d.id, out var e))
            card.Bind(e.name, e.desc, d.tier, TierColor(d.tier));
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
        SetText(_forgeSummary,     $"{p.causeName} × {p.effectName}  ➜  {p.effectDesc}");
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
        CovenantTier.Prism => PrismColor,
        _                  => SilverColor,
    };

    private static void SetText(TMP_Text t, string v) { if (t) t.text = v; }
}
