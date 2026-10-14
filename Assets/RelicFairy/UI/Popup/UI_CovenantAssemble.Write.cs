using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 제단 판 — 「서약서 쓰기」(10-02 「한 장의 서약서」 설계서 §3). 문장이 이미 있을 때 연다.
/// <list type="bullet">
/// <item>왼쪽(원인 열 + 가운데 자리) = <b>지금 문장</b> 한 줄씩. 줄 전체가 버튼이고 오른쪽 [고쳐]는 그 줄의 라벨이다.</item>
/// <item>오른쪽 = 이을 절 3장(이어 쓰기) 또는 그 줄에 들어갈 말 3장(고쳐 쓰기). 교체는 판당 2.</item>
/// <item>고른 카드가 그 줄에 수치색으로 미리 들어가고, 옛 말은 줄 아래 취소선으로 남는다. 「새긴다」 = 새 문장 id를 돌려준다.</item>
/// <item>판은 제단에 묶인다(<see cref="CovenantWriteBoard"/>) — 닫았다 열어도 같은 카드 · 남은 교체 그대로.</item>
/// </list>
/// 아트 · 판 크기 · 카드는 조립 판 그대로(같은 프리팹) — 이 모드는 원인 열 · 가운데 미리보기를 접고 문장 지면을 세운다.
///
/// 10-11 개편(설계서 UI4종 2장 · 리서치 B §5-4): 「무엇을 할지」를 지면 머리에 단계 안내로(20 · 500) — 예전엔 아래 띠의 흐린 14px 한 줄이었다.
/// 줄은 지면 위 18% 지점부터 64px로 모으고, 오른쪽 열이 비면 떠 있던 「효과」 칩 대신 점선 자리표시를 둔다.
/// 카드를 고르기 전엔 [새긴다]를 잠근다(새겨지는 결과 · 규칙은 그대로).
/// </summary>
public partial class UI_CovenantAssemble
{
    // 문장 지면 — 판(Book) 비율 좌표. 원인 열 왼끝 ~ 가운데 열 오른끝(프리팹 0.163 ~ 0.643)
    private static readonly Vector2 PageMin = new(0.165f, 0.175f);
    private static readonly Vector2 PageMax = new(0.640f, 0.815f);
    private const int   PageRows   = 1 + CovenantGrammar.MaxResults + 1;   // 조건 + 결과 5 + 이어 쓸 줄
    private const float RowH       = 64f / 650f;   // 줄 높이 64(지면 650 기준 비율 — 판이 줄면 같이 준다, 설계서 2장 2)
    private const float RowsTop    = 0.82f;        // 줄 묶음은 지면 위 18% 지점부터(설계서 2장 3)
    private const float GuideMin   = 0.900f;       // 단계 안내 — 지면 맨 위
    private const float GuideMax   = 0.975f;
    private const float FreqH      = 0.055f;       // 「이 문장이 터지는 때」 — 줄 묶음 바로 아래 한 줄
    private const float FixChipW   = 0.13f;        // 줄 폭 대비 [고쳐] 라벨 폭
    private const float TextInset  = 12f;          // 줄 판 안쪽 글 여백(px)
    private const float RowSize    = 22f;          // 문장 줄 Noto 400 22(리서치 B §5-4 P2 · 설계서 2장 2)
    private const float RowMinSize = 17f;
    private const float GuideSize  = 20f;          // 단계 안내 · 「이 문장이 터지는 때」(리서치 P1 「80% → 90%」 ≈ 20)
    private const float RowHoverFade = 0.15f;      // 호버하면 얇은 금선이 0.15초에 그려진다(설계서 2장 2)
    private const string TailPct     = "77%";      // 줄 끝 등급 · 옛 말 = 22의 77% ≈ 17(v2-0 등급 이름 17)
    private const string NewWordHex  = "#5E1A0A";  // 바뀐 말만 이 색(리서치 P1 — 예전 금빛 #9A6A12는 1.9:1로 원래 글보다 흐렸다)
    private const string LineLabelHex = "#4A3820"; // 줄 끝 등급 이름 · 옛 말 · 안내 4.7:1(예전 #6E5A42 2.8:1)
    // 줄 판 — 호버 · 고친 줄의 금선은 종이 위에서 보이는 진한 금, 고친 줄 바탕은 옅은 금빛.
    private static readonly Color RowLineInk  = new(0.604f, 0.416f, 0.071f, 0.85f);   // #9A6A12 85%
    private static readonly Color RowFillOn   = new(0.788f, 0.627f, 0.263f, 0.24f);   // 금빛 바탕
    private static readonly Color FixLabelInk = new(0.227f, 0.165f, 0.094f, 1f);      // #3A2A18 5.5:1(예전 #5C4730 3.5:1)

    private bool _writeMode;
    private CovenantSentence _sentence;
    private CovenantWriteBoard _board;
    private int _editIndex;            // -1 = 이어 쓰기 · 0 = 조건 고쳐 쓰기 · k ≥ 1 = 결과 k-1 고쳐 쓰기 · -2 = 가득 참(줄을 골라야)
    private int _selWrite;             // 고른 카드(-1 = 아직 안 고름 — [새긴다] 잠금)
    private RectTransform _page;
    private TMP_Text _guideText, _freqText, _emptyText;
    private Image    _emptySlot;       // 오른쪽 열이 비었을 때 자리표시(점선 판)
    private readonly List<Button>   _rowButtons = new();
    private readonly List<Image>    _rowFills   = new();
    private readonly List<TMP_Text> _rowTexts   = new();
    private readonly List<Image>    _fixFills   = new();
    private readonly List<Image>    _fixEdges   = new();

    /// <summary>서약서 쓰기 판을 세운다. 결과 = 새 문장 id(취소 null) — <see cref="WaitForResultAsync"/>.</summary>
    public void SetupWrite(CovenantSentence sentence, CovenantWriteBoard board)
    {
        Managers.Sound?.PlayUiEvent(SoundEvent.UiOpen);
        _writeMode = true;
        _sentence  = sentence;
        _board     = board;
        _tcs       = new UniTaskCompletionSource<string>();
        SetText(_titleText, "봉인된 예언자의 서약서");
        if (_titleText)
        {
            _titleText.fontSize = 28f; FitSingleLine(_titleText);
            if (!_titleDropped) { _titleDropped = true; ((RectTransform)_titleText.transform).anchoredPosition += new Vector2(0f, -TitleDrop); }
        }
        ConfigureTextFitting();
        ApplyPanelSkin();
        ApplyBottomBarSkin();

        // 원인 열 · 가운데 미리보기를 접고 문장 지면을 세운다
        if (_causeCards != null && _causeCards.Length > 0 && _causeCards[0] != null) _causeCards[0].transform.parent.gameObject.SetActive(false);
        if (_prismRect != null && _prismRect.parent != null) _prismRect.parent.gameObject.SetActive(false);
        BuildPage();
        BuildEmptyColumn();

        _editIndex = IsFull ? -2 : -1;
        _selWrite  = -1;

        for (int i = 0; i < _effectCards.Length; i++)
        {
            var card = _effectCards[i];
            if (card == null) continue;
            int idx = i;
            if (card.SelectButton) { card.SelectButton.onClick.RemoveAllListeners(); card.SelectButton.onClick.AddListener(() => SelectWrite(idx)); }
            if (card.RerollButton)
            {
                card.RerollButton.onClick.RemoveAllListeners();
                card.RerollButton.onClick.AddListener(() => RerollWrite(idx));
                FixRerollLabel(card.RerollButton);
                var rl = card.RerollButton.GetComponentInChildren<TMP_Text>(true);
                if (rl != null) rl.text = "교체";   // 아래 줄 「교체 n」과 같은 말(설계서 §3)
                card.DockReroll(outerLeft: false);
            }
        }
        if (_forgeButton) { _forgeButton.onClick.RemoveAllListeners(); _forgeButton.onClick.AddListener(OnForgeWrite); }
        foreach (var lbl in _forgeButton.GetComponentsInChildren<TMP_Text>(true)) lbl.text = "새긴다";

        RefreshWrite();
    }

    private bool IsFull => _sentence.ResultCount >= CovenantGrammar.MaxResultsNow
                           || CovenantGrammar.IsTerminal(_sentence.ResultId(_sentence.ResultCount - 1));

    // ── 지면 ─────────────────────────────────────────────
    private void BuildPage()
    {
        if (_page != null || _bookRect == null) return;
        var go = new GameObject("SentencePage(Runtime)", typeof(RectTransform));
        _page = (RectTransform)go.transform;
        _page.SetParent(_bookRect, false);
        _page.anchorMin = PageMin; _page.anchorMax = PageMax;
        _page.offsetMin = Vector2.zero; _page.offsetMax = Vector2.zero;

        var style = _previewCondition != null ? _previewCondition : _previewSentence;

        // 단계 안내 — 지면 머리(20 · 500 진한 잉크). 할 일을 맨 위에 둔다(MT 머리 띠 · 위 → 아래 경로).
        _guideText = MakePageText(style, _page, "Guide", UIWeight.Medium, GuideSize, InkOnBook);
        Place(_guideText.rectTransform, new Vector2(0f, GuideMin), new Vector2(1f, GuideMax));
        _guideText.rectTransform.offsetMin = new Vector2(TextInset, 0f);   // 줄 글과 같은 왼쪽 선

        for (int r = 0; r < PageRows; r++)
        {
            float top = RowsTop - r * RowH, bottom = top - RowH;
            var row = new GameObject($"Row{r}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var rrt = (RectTransform)row.transform;
            rrt.SetParent(_page, false);
            Place(rrt, new Vector2(0f, bottom), new Vector2(1f, top));

            // 줄 판 바탕 — 투명이어도 누름을 받는다. 고친 줄은 금빛 바탕.
            var fill = row.GetComponent<Image>();
            fill.color = Color.clear;
            _rowFills.Add(fill);

            // 줄 판 금선 — 버튼 색 전이의 대상. 평소 0 · 호버 1(0.15초) · 고친 줄은 평소에도 1.
            var line = new GameObject("Line", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            line.rectTransform.SetParent(rrt, false);
            Place(line.rectTransform, Vector2.zero, Vector2.one);
            StyleBox(line, ThinBox, RowLineInk);

            var btn = row.GetComponent<Button>();
            btn.targetGraphic = line;
            btn.transition    = Selectable.Transition.ColorTint;
            int lineIndex = r;
            btn.onClick.AddListener(() => OnFix(lineIndex));
            _rowButtons.Add(btn);

            var text = MakePageText(style, rrt, "Text", UIWeight.Regular, RowSize, InkOnBook);
            Place(text.rectTransform, Vector2.zero, new Vector2(1f - FixChipW - 0.02f, 1f));
            text.rectTransform.offsetMin = new Vector2(TextInset, 0f);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.fontSizeMin      = RowMinSize;
            _rowTexts.Add(text);

            MakeFixChip(style, rrt);
        }

        // 「이 문장이 터지는 때」 — 줄 묶음 바로 아래(자리는 RefreshPage가 쓴 줄 수에 맞춰 옮긴다)
        _freqText = MakePageText(style, _page, "Freq", UIWeight.Regular, GuideSize, LabelInk);
        _freqText.rectTransform.offsetMin = new Vector2(TextInset, 0f);
    }

    /// <summary>
    /// [고쳐] — 줄의 라벨(누르는 것은 줄 전체). 1px 잉크 45% 테 + 채움 18%, 라벨 #3A2A18(리서치 B §5-4 P1 —
    /// 예전 판은 잉크 10%라 바탕 밝기 164 / 177로 거의 안 보였다).
    /// </summary>
    private void MakeFixChip(TMP_Text style, RectTransform row)
    {
        var go = new GameObject("Fix", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(row, false);
        Place(rt, new Vector2(1f - FixChipW, 0.20f), new Vector2(0.99f, 0.80f));
        var fill = go.GetComponent<Image>();
        fill.raycastTarget = false;
        _fixFills.Add(fill);

        var edge = new GameObject("Edge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        edge.rectTransform.SetParent(rt, false);
        Place(edge.rectTransform, Vector2.zero, Vector2.one);
        StyleBox(edge, ThinBox, Color.clear);
        _fixEdges.Add(edge);

        var label = MakePageText(style, rt, "Label", UIWeight.Medium, ChipSize, FixLabelInk);
        Place(label.rectTransform, Vector2.zero, Vector2.one);
        label.text      = "고치기";
        label.alignment = TextAlignmentOptions.Center;
    }

    /// <summary>지면 글 한 칸 — 프리팹 글을 복제해(정렬 · 머티리얼 설정 그대로) 톤 v2 서체로 간다. 누름은 받지 않는다.</summary>
    private static TMP_Text MakePageText(TMP_Text style, RectTransform parent, string name, UIWeight weight, float size, Color color)
    {
        var t = style != null ? Instantiate(style, parent) : new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        t.name = name;
        var slot = t.transform.Find("Slot(Runtime)");   // 복제원(두루마리 글)에 붙은 자리표시는 떼어 낸다
        if (slot != null) Destroy(slot.gameObject);
        t.rectTransform.SetParent(parent, false);
        Place(t.rectTransform, Vector2.zero, Vector2.one);
        t.rectTransform.pivot = new Vector2(0f, 0.5f);
        t.enableAutoSizing = true;
        UITheme.StyleThinText(t, weight, size, color);
        t.fontSizeMax      = size;
        t.fontSizeMin      = Mathf.Min(size, 15f);
        t.alignment        = TextAlignmentOptions.MidlineLeft;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode     = TextOverflowModes.Ellipsis;
        t.margin           = Vector4.zero;
        t.alpha            = 1f;
        t.raycastTarget    = false;
        t.text             = string.Empty;
        return t;
    }

    /// <summary>
    /// 오른쪽 열이 비었을 때(가득 참 · 이을 말 없음)의 자리표시 — 얇은 점선 판 + 「← 고칠 줄을 고르면 …」(17).
    /// 카드 세 칸 자리를 그대로 덮는다(설계서 2장 4 — 떠 있던 「효과」 판 버튼 대신).
    /// </summary>
    private void BuildEmptyColumn()
    {
        if (_emptySlot != null || _effectCards == null || _effectCards.Length == 0 || _effectCards[0] == null) return;
        var column = _effectCards[0].transform.parent as RectTransform;
        if (column == null) return;

        var go = new GameObject("EmptyColumn(Runtime)", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(column, false);
        Place(rt, new Vector2(0f, 0.170f), new Vector2(1f, 0.728f));   // 카드 0 윗변 ~ 카드 2 아랫변(프리팹 앵커)
        _emptySlot = go.GetComponent<Image>();
        StyleBox(_emptySlot, DashBox, SlotInk);

        _emptyText = MakePageText(_previewCondition != null ? _previewCondition : _previewSentence, rt, "Hint", UIWeight.Regular, LabelSize, LabelInk);
        _emptyText.rectTransform.offsetMin = new Vector2(16f, 0f);
        _emptyText.rectTransform.offsetMax = new Vector2(-16f, 0f);
        _emptyText.alignment        = TextAlignmentOptions.Center;
        _emptyText.textWrappingMode = TextWrappingModes.Normal;
    }

    private static void Place(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min; rt.anchorMax = max;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    // ── 갱신 ─────────────────────────────────────────────
    private void RefreshWrite()
    {
        var cards = CurrentCards(out var causeCards);
        int n = causeCards != null ? causeCards.Count : cards.Count;
        if (_selWrite >= n) _selWrite = -1;

        // 오른쪽 카드
        for (int i = 0; i < _effectCards.Length; i++)
        {
            var card = _effectCards[i];
            if (card == null) continue;
            bool on = i < n;
            card.gameObject.SetActive(on);
            if (!on) continue;
            card.SetSkin(_silverFrame, _goldFrame, _rubyFrame, _cardBgSprite);
            card.SetGradeSkin(UISkin.Covenant);
            if (causeCards != null)
            {
                var c = causeCards[i];
                if (CovenantPalette.TryGetCause(c.id, out var cd)) card.Bind(cd.name, cd.desc, c.tier, TierColor(c.tier));
            }
            else
            {
                var c = cards[i];
                int pos = _editIndex == -1 ? _sentence.ResultCount : _editIndex - 1;
                var p = CovenantAssemblePreview.Build(_sentence.CauseId, _sentence.CauseTier, c.effectId, c.tier, CovenantGrammar.PositionMult(pos));
                // 이름 = 효과 이름만 · 잇는 말(「쓰러지면」 등)은 설명 줄 머리로 — 이름이 「잇는 말 · 이름」이면 배지와 한 줄을 나눠 13px까지 줄었다(10-14)
                if (CovenantPalette.TryGetEffect(c.effectId, out var e))
                    card.Bind(e.name, $"{CovenantGrammar.LinkTag(c.link)} — {(p.valid ? p.EffectAmountCompact() : e.desc)}", c.tier, TierColor(c.tier),
                              EffectTaxonomy.Badge(e.axis, e.status));
            }
            card.SetSelected(i == _selWrite);
            card.SetSynergy(false);
        }

        RefreshColumnHead(n, causeCards != null);
        RefreshPage(cards, causeCards);

        bool hasPick = _selWrite >= 0 && _selWrite < n;
        SetForgeReady(hasPick && _editIndex != -2);   // 바꿀 말 · 이을 말을 고르기 전엔 잠근다(설계서 2장 6)

        // 단계 안내 — 할 일 순서를 다 적는다(10-14 사용자 「어떻게 해야 하는지 모르겠다」 — 예전 「이을 말을 고른다」 한 마디)
        SetText(_guideText, _editIndex switch
        {
            -2 => "① 고칠 줄을 누른다 — 서약서가 가득 찼다",
            -1 => n > 0 ? "① 오른쪽 이을 말 한 장  →  ② 아래 [새긴다]" : "이을 말이 없다 — 줄의 [고치기]로 한 줄을 바꾼다",
            _  => "② 오른쪽에서 바꿀 말 한 장  →  ③ 아래 [새긴다]",
        });
        SetText(_forgeSummary, string.Empty);   // 안내는 지면 머리로 옮겼다 — 아래 띠는 「교체 n」 + [새긴다]만
        SetText(_rerollCountText, $"교체 {_board.RerollsLeft}회");
        GateReroll(_effectCards, _board.RerollsLeft > 0 && _editIndex != -2);
    }

    /// <summary>
    /// 오른쪽 열 머리 — 카드가 있으면 1장과 같은 글자 라벨(「바꿀 원인」 · 「바꿀 말」 · 「이을 말」),
    /// 없으면 머리를 숨기고 점선 자리표시를 켠다(리서치 B §5-4 P2 — 모드와 상관없이 「효과」가 떠 있었다).
    /// </summary>
    private void RefreshColumnHead(int cardCount, bool causeRewrite)
    {
        bool empty = cardCount == 0;
        if (_effectHeaderText != null)
        {
            _effectHeaderText.gameObject.SetActive(!empty);
            string head = causeRewrite ? "바꿀 원인" : _editIndex == -1 ? "이을 말" : "바꿀 말";
            if (!causeRewrite && _sentence != null)
            {
                // 이 자리의 효과 배율 — 뒤 절일수록 약해진다(×0.85 · 0.7 · 0.6 · 0.5). 카드 수치엔 이미 곱해져 있다(10-14)
                int pos = _editIndex == -1 ? _sentence.ResultCount : _editIndex - 1;
                float mult = CovenantGrammar.PositionMult(pos);
                if (mult < 0.999f) head += $"  · 이 자리 효과 ×{mult:0.##}";
            }
            _effectHeaderText.text = head;
        }
        if (_effectHeadRule != null) _effectHeadRule.enabled = !empty;
        if (_emptySlot != null) _emptySlot.gameObject.SetActive(empty);
        if (_emptyText != null)
            _emptyText.text = _editIndex == -2 ? "← 고칠 줄을 고르면\n바꿀 말 3장이 여기 나온다"
                                               : "이을 말이 없다\n← 고칠 줄을 고른다";
    }

    /// <summary>
    /// 지금 문장 — 조건 · 결과 줄(고치는 줄은 새 말 + 아래 취소선 옛 말) · 이어 쓸 줄 · 「이 문장이 터지는 때」.
    /// 바뀐 말만 수치색(#5E1A0A)이고 나머지는 먹 — 무엇이 바뀌는지가 보인다(설계서 2장 5 · 리서치 P1).
    /// </summary>
    private void RefreshPage(List<SentenceCard> cards, List<CovenantDraftCard> causeCards)
    {
        if (_page == null) return;
        int row = 0;
        int n = causeCards != null ? causeCards.Count : cards.Count;
        bool hasPick = _selWrite >= 0 && _selWrite < n;

        // 조건
        string oldCause  = CovenantPalette.TryGetCause(_sentence.CauseId, out var cd) ? cd.desc : _sentence.CauseId;
        string causeLine = oldCause + Tail(_sentence.CauseTier);
        if (_editIndex == 0 && causeCards != null && hasPick && CovenantPalette.TryGetCause(causeCards[_selWrite].id, out var nc))
            causeLine = Changed(nc.desc, causeCards[_selWrite].tier, oldCause);
        SetRow(row++, causeLine, fix: true, active: _editIndex == 0);

        // 결과
        for (int i = 0; i < _sentence.ResultCount; i++)
        {
            string prev = i > 0 ? _sentence.ResultId(i - 1) : null;
            string old  = "→ " + CovenantGrammar.Line(_sentence.ResultId(i), _sentence.ResultLink(i), prev);
            string line = old + Tail(_sentence.ResultTier(i));
            if (_editIndex == i + 1 && causeCards == null && hasPick)
                line = Changed("→ " + CovenantGrammar.Line(cards[_selWrite].effectId, _sentence.ResultLink(i), prev), cards[_selWrite].tier, old);
            SetRow(row++, line, fix: true, active: _editIndex == i + 1);
        }

        // 이어 쓸 줄
        if (_editIndex == -1 && hasPick && causeCards == null)
        {
            var c = cards[_selWrite];
            string prev = _sentence.ResultId(_sentence.ResultCount - 1);
            SetRow(row++, $"<color={NewWordHex}>→ {CovenantGrammar.Line(c.effectId, c.link, prev)}</color>" + Tail(c.tier), fix: false, active: false);
        }

        int used = row;
        for (; row < PageRows; row++) SetRow(row, null, fix: false, active: false);

        // 빈도 — 조건 기준(뒤 절은 「앞 절이 일어날 때」). 줄 묶음 바로 아래에 붙인다.
        if (_freqText != null)
        {
            float top = RowsTop - used * RowH - 0.012f;
            Place(_freqText.rectTransform, new Vector2(0f, top - FreqH), new Vector2(1f, top));
            _freqText.rectTransform.offsetMin = new Vector2(TextInset, 0f);
            _freqText.text = $"이 문장이 터지는 때 — {oldCause}";
        }
    }

    /// <summary>줄 끝 등급 — 등급색 ◆ + 등급 이름(17 · 라벨 잉크). 예전엔 「골드」 날글자가 2.8:1로 붙어 있었다.</summary>
    private static string Tail(CovenantTier tier)
        => $"  <size={TailPct}>{TierMark(tier)} <color={LineLabelHex}>{tier.DisplayName()}</color></size>";

    /// <summary>고치는 줄 미리보기 — 새 말(수치색) + 등급, 아랫줄에 옛 말 취소선(17 · 흐린 잉크).</summary>
    private static string Changed(string now, CovenantTier tier, string old)
        => $"<color={NewWordHex}>{now}</color>{Tail(tier)}\n<size={TailPct}><color={LineLabelHex}><s>{old}</s></color></size>";

    /// <summary>
    /// 줄 하나 — 글 · 누름 · 금선 · [고쳐] 라벨. text가 null이면 줄을 접는다.
    /// 고친 줄(active)은 금빛 바탕 + 금선 상시 · [고쳐] 채움 35% + 금 테(리서치 P1 「고치는 줄 α0.35 + 1px 금 85%」).
    /// </summary>
    private void SetRow(int r, string text, bool fix, bool active)
    {
        if (r < 0 || r >= _rowTexts.Count) return;
        var btn = _rowButtons[r];
        btn.gameObject.SetActive(text != null);
        if (text == null) return;
        _rowTexts[r].text = text;

        btn.interactable = fix;
        _rowFills[r].raycastTarget = fix;
        _rowFills[r].color = active ? RowFillOn : Color.clear;

        // 금선 = 버튼 색 전이. 평소 · 고름(Selected)은 같은 값이어야 누른 뒤에 선이 남거나 사라지지 않는다.
        var on  = Color.white;
        var off = new Color(1f, 1f, 1f, 0f);
        var cb  = btn.colors;
        cb.normalColor      = active ? on : off;
        cb.selectedColor    = cb.normalColor;
        cb.highlightedColor = on;
        cb.pressedColor     = on;
        cb.disabledColor    = off;
        cb.colorMultiplier  = 1f;
        cb.fadeDuration     = RowHoverFade;
        btn.colors = cb;

        var chip = _fixFills[r];
        chip.gameObject.SetActive(fix);
        if (!fix) return;
        chip.color = new Color(InkOnBook.r, InkOnBook.g, InkOnBook.b, active ? 0.35f : 0.18f);
        _fixEdges[r].color = active ? RowLineInk : new Color(InkOnBook.r, InkOnBook.g, InkOnBook.b, 0.45f);
    }

    /// <summary>지금 편집(이어 쓰기 · 고쳐 쓰기)의 카드 — 판에 묶여 있으면 그대로, 없으면 굴려서 묶는다.</summary>
    private List<SentenceCard> CurrentCards(out List<CovenantDraftCard> causeCards)
    {
        causeCards = null;
        var effects = Results(out var links);
        int step = MemoryAltarService.CovenantPartStep;
        switch (_editIndex)
        {
            case -2: return new List<SentenceCard>();
            case -1:
                return _board.Append ??= CovenantSentenceService.DraftAppend(_sentence.CauseId, effects, links, DraftCount, _board.Rng, BuildStatus, step);
            case 0:
                causeCards = _board.RewriteCause ??= CovenantSentenceService.DraftRewriteCause(_sentence.CauseId, effects, links, DraftCount, _board.Rng, BuildStatus, step);
                return new List<SentenceCard>();
            default:
                if (!_board.Rewrite.TryGetValue(_editIndex, out var list))
                    _board.Rewrite[_editIndex] = list = CovenantSentenceService.DraftRewrite(_sentence.CauseId, effects, links, _editIndex - 1, DraftCount, _board.Rng, BuildStatus, step);
                return list;
        }
    }

    private List<string> Results(out List<ClauseLink> links)
    {
        var effects = new List<string>(_sentence.ResultCount);
        links = new List<ClauseLink>(_sentence.ResultCount);
        for (int i = 0; i < _sentence.ResultCount; i++) { effects.Add(_sentence.ResultId(i)); links.Add(_sentence.ResultLink(i)); }
        return effects;
    }

    // ── 조작 ─────────────────────────────────────────────
    private void SelectWrite(int idx)
    {
        Managers.Sound?.PlayUiEvent(SoundEvent.CovenantPick);
        _selWrite = idx;
        RefreshWrite();
    }

    private void OnFix(int line)
    {
        Managers.Sound?.PlayUiEvent(SoundEvent.CovenantFix);
        _editIndex = _editIndex == line ? (IsFull ? -2 : -1) : line;
        _selWrite = -1;   // 줄을 바꾸면 카드는 새로 고른다 — 고르기 전엔 [새긴다]가 잠겨 있다
        RefreshWrite();
    }

    /// <summary>카드 한 장 교체 — 판에 묶인 목록의 그 칸만 바꾼다(남은 교체는 판 전체 공용).</summary>
    private void RerollWrite(int idx)
    {
        if (_board.RerollsLeft <= 0) { Managers.Sound?.PlayUiEvent(SoundEvent.UiReject); return; }   // 교체 소진
        if (_editIndex == -2) return;
        var cards = CurrentCards(out var causeCards);
        var effects = Results(out var links);
        int step = MemoryAltarService.CovenantPartStep;
        if (causeCards != null)
        {
            if (idx >= causeCards.Count) return;
            var ex = new HashSet<string> { _sentence.CauseId };
            foreach (var c in causeCards) ex.Add(c.id);
            var pool = CovenantSentenceService.DraftRewriteCause(_sentence.CauseId, effects, links, 16, _board.Rng, BuildStatus, step);
            pool.RemoveAll(c => ex.Contains(c.id));
            if (pool.Count == 0) return;
            causeCards[idx] = new CovenantDraftCard(pool[0].id, CovenantAssembleService.RollRerollTier(_board.Rng, false));
        }
        else
        {
            if (idx >= cards.Count) return;
            var ex = new HashSet<string>();
            foreach (var c in cards) ex.Add(c.effectId);
            var pool = _editIndex == -1
                ? CovenantSentenceService.DraftAppend(_sentence.CauseId, effects, links, 1, _board.Rng, BuildStatus, step, ex)
                : CovenantSentenceService.DraftRewrite(_sentence.CauseId, effects, links, _editIndex - 1, 1, _board.Rng, BuildStatus, step, ex);
            if (pool.Count == 0) return;
            cards[idx] = new SentenceCard(pool[0].effectId, CovenantAssembleService.RollRerollTier(_board.Rng, false), pool[0].link);
        }
        Managers.Sound?.PlayUiEvent(SoundEvent.CovenantReroll);
        _board.RerollsLeft--;
        _selWrite = idx;
        RefreshWrite();
    }

    /// <summary>고른 카드로 새 문장 id를 만들어 돌려준다.</summary>
    private void OnForgeWrite()
    {
        if (_selWrite < 0) return;   // 카드를 고르기 전(버튼 잠금이 뚫린 경로 대비)
        var cards = CurrentCards(out var causeCards);
        var parts = new List<(string, CovenantTier, ClauseLink)>(_sentence.ResultCount + 1);
        for (int i = 0; i < _sentence.ResultCount; i++) parts.Add((_sentence.ResultId(i), _sentence.ResultTier(i), _sentence.ResultLink(i)));
        string causeId = _sentence.CauseId;
        var causeTier = _sentence.CauseTier;

        switch (_editIndex)
        {
            case -1:
                if (_selWrite >= cards.Count) return;
                parts.Add((cards[_selWrite].effectId, cards[_selWrite].tier, cards[_selWrite].link));
                break;
            case 0:
                if (causeCards == null || _selWrite >= causeCards.Count) return;
                causeId = causeCards[_selWrite].id; causeTier = causeCards[_selWrite].tier;
                break;
            default:
                if (_editIndex < 1 || _selWrite >= cards.Count) return;
                parts[_editIndex - 1] = (cards[_selWrite].effectId, cards[_selWrite].tier, _sentence.ResultLink(_editIndex - 1));
                break;
        }

        Managers.Sound?.PlayUiEvent(SoundEvent.CovenantForge);
        _forged = true;
        string id = CovenantSentence.MakeId(causeId, causeTier, parts);
        ClosePopupUI();   // 닫기가 먼저 — 결과가 대기 측을 동기로 재개시킨다(OnForge 주석)
        _tcs?.TrySetResult(id);
    }
}

/// <summary>
/// 제단 하나의 서약서 판 — 이어 쓰기 카드 · 고쳐 쓰기 카드(줄마다) · 남은 교체. 제단이 들고 있다가 다시 열면 그대로 보여 준다(설계서 §3 「판은 제단에 묶인다」).
/// </summary>
public sealed class CovenantWriteBoard
{
    public const int DefaultRerolls = 2;

    public readonly System.Random Rng;
    public int RerollsLeft = DefaultRerolls;
    public List<SentenceCard> Append;
    public List<CovenantDraftCard> RewriteCause;
    public readonly Dictionary<int, List<SentenceCard>> Rewrite = new();

    public CovenantWriteBoard(int seed) { Rng = new System.Random(seed); }
}
