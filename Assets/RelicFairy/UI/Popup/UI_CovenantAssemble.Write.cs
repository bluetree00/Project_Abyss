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
///
/// 10-18 빌드 피드백: ① 결과 줄은 동사구(「적이 피를 흘린다」) 대신 카드와 같은 수치(「출혈 공격력×37% · 4초」 — <see cref="CovenantGrammar.NumericLine"/>).
/// ② 모드가 보이지 않는 토글이었다(고친 줄을 한 번 더 눌러야 이어 쓰기로 돌아갔다) — 지면 머리에 탭 [이어 쓰기] · [고쳐 쓰기],
/// 고치는 줄의 라벨은 [취소], 안내 줄은 모드마다 바뀌고, ESC는 고쳐 쓰기를 먼저 푼 뒤 창을 닫는다.
/// </summary>
public partial class UI_CovenantAssemble
{
    // 문장 지면 — 판(Book) 비율 좌표. 원인 열 왼끝 ~ 가운데 열 오른끝(프리팹 0.163 ~ 0.643)
    private static readonly Vector2 PageMin = new(0.165f, 0.175f);
    private static readonly Vector2 PageMax = new(0.640f, 0.815f);
    private const int   PageRows   = 1 + CovenantGrammar.MaxResults + 1;   // 조건 + 결과 5 + 이어 쓸 줄
    private const float RowH       = 64f / 650f;   // 줄 높이 64(지면 650 기준 비율 — 판이 줄면 같이 준다, 설계서 2장 2) — 가득 찼을 때의 최소
    private const float RowHMax    = 92f / 650f;   // 10-22 줄이 적으면 이만큼까지 키운다(1~3절이면 지면 55~60%가 빈 양피지였다)
    private const float RowsSpan   = 0.74f;        // 줄 묶음이 쓰는 지면 높이(RowsTop 0.82 → 0.08)
    private const float RowsTop    = 0.82f;        // 줄 묶음은 지면 위 18% 지점부터(설계서 2장 3)
    private const float TabsMin    = 0.915f;       // 모드 탭 [이어 쓰기] · [고쳐 쓰기] — 지면 맨 위(10-18)
    private const float TabsMax    = 0.975f;
    private const float TabW       = 0.17f;        // 탭 하나 폭(지면 폭 비율)
    private const float TabGap     = 0.015f;
    private const float GuideMin   = 0.840f;       // 단계 안내 — 탭 바로 아래(모드마다 바뀐다)
    private const float GuideMax   = 0.905f;
    private const float FreqH      = 0.055f;       // 「이 문장이 터지는 때」 — 줄 묶음 바로 아래 한 줄
    private const float FixChipW   = 0.13f;        // 줄 폭 대비 [고쳐] 라벨 폭
    private const float TextInset  = 12f;          // 줄 판 안쪽 글 여백(px)
    private const float RowSize    = 26f;          // 문장 줄 Noto 400(10-22 22 → 26 — 화면 꽉 찬 책에 비해 작았다 · 사용자 「다시 쓸 때 가시성」)
    private const float RowMinSize = 20f;
    private const float GuideSize  = 22f;          // 단계 안내 · 「이 문장이 터지는 때」(10-22 20 → 22)
    private const float RowHoverFade = 0.15f;      // 호버하면 얇은 금선이 0.15초에 그려진다(설계서 2장 2)
    private const string TailPct     = "77%";      // 줄 끝 등급 = 26의 77% ≈ 20
    private const string OldPct      = "85%";      // 고쳐 쓰기의 지운 옛 말 ≈ 22(10-22 — 77%는 흐렸다)
    private const string NewWordHex  = "#5E1A0A";  // 바뀐 말만 이 색(리서치 P1 — 예전 금빛 #9A6A12는 1.9:1로 원래 글보다 흐렸다)
    private const string LineLabelHex = "#4A3820"; // 줄 끝 등급 이름 · 옛 말 · 안내 4.7:1(예전 #6E5A42 2.8:1)
    // 줄 판 — 호버 · 고친 줄의 금선은 종이 위에서 보이는 진한 금, 고친 줄 바탕은 옅은 금빛.
    private static readonly Color RowLineInk  = new(0.604f, 0.416f, 0.071f, 0.85f);   // #9A6A12 85%
    private static readonly Color RowFillOn   = new(0.788f, 0.627f, 0.263f, 0.24f);   // 금빛 바탕
    private static readonly Color FixLabelInk = new(0.227f, 0.165f, 0.094f, 1f);      // #3A2A18 5.5:1(예전 #5C4730 3.5:1)

    private bool _writeMode;
    private CovenantSentence _sentence;
    private CovenantWriteBoard _board;
    private int _editIndex;            // -1 = 이어 쓰기 · 0 = 조건 고쳐 쓰기 · k ≥ 1 = 결과 k-1 고쳐 쓰기 · -2 = 고쳐 쓰기 줄 고르기 전(가득 차면 여기서 연다)
    private int _selWrite;             // 고른 카드(-1 = 아직 안 고름 — [새긴다] 잠금)
    private RectTransform _page;
    private TMP_Text _guideText, _freqText, _emptyText;
    private Image    _emptySlot;       // 오른쪽 열이 비었을 때 자리표시(점선 판)
    private readonly List<Button>   _rowButtons = new();
    private readonly List<Image>    _rowFills   = new();
    private readonly List<TMP_Text> _rowTexts   = new();
    private readonly List<Image>    _fixFills   = new();
    private readonly List<Image>    _fixEdges   = new();
    private readonly List<TMP_Text> _fixLabels  = new();   // 「고치기」 · 고치는 줄은 「취소」(10-18)
    private readonly Button[]   _tabs      = new Button[2];   // [이어 쓰기] · [고쳐 쓰기](10-18)
    private readonly Image[]    _tabFills  = new Image[2];
    private readonly Image[]    _tabEdges  = new Image[2];
    private readonly TMP_Text[] _tabLabels = new TMP_Text[2];

    /// <summary>서약서 쓰기 판을 세운다. 결과 = 새 문장 id(취소 null) — <see cref="WaitForResultAsync"/>.</summary>
    public void SetupWrite(CovenantSentence sentence, CovenantWriteBoard board)
    {
        Managers.Sound?.PlayUiEvent(SoundEvent.UiOpen);
        _writeMode = true;
        _sentence  = sentence;
        _board     = board;
        _tcs       = new UniTaskCompletionSource<string>();
        SetText(_titleText, $"봉인된 예언자의 서약서 · {sentence.ResultCount}/{CovenantGrammar.MaxResultsNow}절");   // 10-22 몇 절까지 썼나(설계 §0)
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

        BuildTabs(style);

        // 단계 안내 — 탭 바로 아래(20 · 500 진한 잉크). 할 일을 줄 묶음 위에 둔다(MT 머리 띠 · 위 → 아래 경로).
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

            // 원인 줄(0)은 한 단계 굵게 — 원인과 결과가 같아 보여 위계가 없었다(10-22)
            var text = MakePageText(style, rrt, "Text", r == 0 ? UIWeight.Medium : UIWeight.Regular, RowSize, InkOnBook);
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
    /// 모드 탭 [이어 쓰기] · [고쳐 쓰기](10-18 사용자 「고쳐 쓰기를 누른 뒤 이어 쓰기로 돌아가려면 고쳐 쓰기를 한 번 더 눌러야 하는데
    /// 설명이 없어 한 번에 빠져나오기 힘들다」) — 지금 모드가 보이지 않았다. 색은 <see cref="RefreshTabs"/>가 모드로 칠한다.
    /// </summary>
    private void BuildTabs(TMP_Text style)
    {
        string[] names = { "이어 쓰기", "고쳐 쓰기" };
        for (int i = 0; i < _tabs.Length; i++)
        {
            float x = i * (TabW + TabGap);
            var go = new GameObject($"Tab{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_page, false);
            Place(rt, new Vector2(x, TabsMin), new Vector2(x + TabW, TabsMax));
            _tabFills[i] = go.GetComponent<Image>();   // 투명이어도 누름을 받는다 · 지금 모드는 옅은 금 바탕
            _tabFills[i].color = Color.clear;

            var edge = new GameObject("Edge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            edge.rectTransform.SetParent(rt, false);
            Place(edge.rectTransform, Vector2.zero, Vector2.one);
            StyleBox(edge, ThinBox, HeadRuleInk);
            _tabEdges[i] = edge;

            var label = MakePageText(style, rt, "Label", UIWeight.Medium, LabelSize, LabelInk);
            label.alignment = TextAlignmentOptions.Center;
            label.text      = names[i];
            _tabLabels[i]   = label;

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            bool append = i == 0;
            btn.onClick.AddListener(() => OnTab(append));
            _tabs[i] = btn;
        }
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
        _fixLabels.Add(label);
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
        RefreshTabs();

        bool hasPick = _selWrite >= 0 && _selWrite < n;
        SetForgeReady(hasPick && _editIndex != -2);   // 바꿀 말 · 이을 말을 고르기 전엔 잠근다(설계서 2장 6)

        // 단계 안내 — 할 일 순서를 다 적는다(10-14 사용자 「어떻게 해야 하는지 모르겠다」 — 예전 「이을 말을 고른다」 한 마디).
        // 10-18: 모드마다 바꾸고, 고쳐 쓰기엔 그만두는 길([취소])까지 적는다.
        SetText(_guideText, _editIndex switch
        {
            -2 => IsFull ? "① 고칠 줄을 누른다 — 서약서가 가득 찼다" : "① 고칠 줄을 누른다  →  ② 오른쪽 바꿀 말  →  ③ [새긴다]",
            -1 => n > 0 ? "① 오른쪽 이을 말 한 장  →  ② 아래 [새긴다]" : "이을 말이 없다 — 위 [고쳐 쓰기]로 한 줄을 바꾼다",
            _  => "② 오른쪽에서 바꿀 말 한 장  →  ③ 아래 [새긴다]  · 그만두려면 [취소]",
        });
        SetText(_forgeSummary, string.Empty);   // 안내는 지면 머리로 옮겼다 — 아래 띠는 「교체 n」 + [새긴다]만
        SetText(_rerollCountText, $"교체 {_board.RerollsLeft}회");
        GateReroll(_effectCards, _board.RerollsLeft > 0 && _editIndex != -2);
    }

    /// <summary>탭 색 — 지금 모드 = 금선 + 옅은 금 바탕 + 진한 먹, 다른 쪽 = 옅은 먹 테. 가득 차면 [이어 쓰기]는 흐리게 잠근다.</summary>
    private void RefreshTabs()
    {
        bool rewrite = _editIndex != -1;
        for (int i = 0; i < _tabs.Length; i++)
        {
            if (_tabs[i] == null) continue;
            bool on   = (i == 1) == rewrite;
            bool open = i == 1 || !IsFull;
            _tabs[i].interactable = open;
            _tabFills[i].color  = on ? RowFillOn : Color.clear;
            _tabEdges[i].color  = on ? RowLineInk : HeadRuleInk;
            _tabLabels[i].color = on ? InkOnBook : LabelInk;
            _tabLabels[i].alpha = open ? 1f : 0.45f;
        }
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
        // 결과 수치는 원인 계수를 탄다 — 원인을 바꾸는 중이면 새 원인으로 셈하고, 수치가 달라진 줄은 바뀐 말 색(10-18)
        string causeId   = _sentence.CauseId;
        var    causeTier = _sentence.CauseTier;
        if (_editIndex == 0 && causeCards != null && hasPick && CovenantPalette.TryGetCause(causeCards[_selWrite].id, out var nc))
        {
            causeLine = Changed(nc.desc, causeCards[_selWrite].tier, oldCause);
            causeId   = causeCards[_selWrite].id;
            causeTier = causeCards[_selWrite].tier;
        }
        SetRow(row++, causeLine, fix: true, active: _editIndex == 0);

        // 결과 — 동사구 대신 카드와 같은 수치(10-18 사용자 「『적이 피를 흘린다』보다 『출혈 피해 몇』이 직관적」)
        for (int i = 0; i < _sentence.ResultCount; i++)
        {
            string prev = i > 0 ? _sentence.ResultId(i - 1) : null;
            var    link = _sentence.ResultLink(i);
            var    tier = _sentence.ResultTier(i);
            string old  = "→ " + CovenantGrammar.NumericLine(_sentence.CauseId, _sentence.CauseTier, _sentence.ResultId(i), tier, link, prev, i);
            string now  = "→ " + CovenantGrammar.NumericLine(causeId, causeTier, _sentence.ResultId(i), tier, link, prev, i);
            string line = (now == old ? old : $"<color={NewWordHex}>{now}</color>") + Tail(tier);
            if (_editIndex == i + 1 && causeCards == null && hasPick)
                line = Changed("→ " + CovenantGrammar.NumericLine(causeId, causeTier, cards[_selWrite].effectId, cards[_selWrite].tier, link, prev, i),
                               cards[_selWrite].tier, old);
            SetRow(row++, line, fix: true, active: _editIndex == i + 1);
        }

        // 이어 쓸 줄
        if (_editIndex == -1 && hasPick && causeCards == null)
        {
            var c = cards[_selWrite];
            string prev = _sentence.ResultId(_sentence.ResultCount - 1);
            string now  = CovenantGrammar.NumericLine(causeId, causeTier, c.effectId, c.tier, c.link, prev, _sentence.ResultCount);
            SetRow(row++, $"<color={NewWordHex}>→ {now}</color>" + Tail(c.tier), fix: false, active: false);
        }
        else if (_editIndex == -1 && causeCards == null && n > 0 && !IsFull)
        {
            // 이어 쓸 자리 — 카드를 고르기 전에도 늘 보인다(설계 §3 「░ 이어 쓸 자리」 · 10-22 — 고른 뒤에야 줄이 생겨 어디에 붙는지 몰랐다)
            SetRow(row++, $"<color={LineLabelHex}>＋ 이어 쓸 자리 — 오른쪽에서 이을 말을 한 장 고른다</color>", fix: false, active: false);
            _rowFills[row - 1].color = new Color(InkOnBook.r, InkOnBook.g, InkOnBook.b, 0.07f);
        }

        int used = row;
        for (; row < PageRows; row++) SetRow(row, null, fix: false, active: false);

        // 10-22 줄 높이 — 줄이 적으면 키워 지면을 채운다(최대 92 · 가득 차면 64)
        float rh = Mathf.Clamp(RowsSpan / Mathf.Max(4, used), RowH, RowHMax);
        for (int r = 0; r < used; r++)
        {
            float t = RowsTop - r * rh;
            Place((RectTransform)_rowButtons[r].transform, new Vector2(0f, t - rh), new Vector2(1f, t));
        }

        // 빈도 — 조건 기준(뒤 절은 「앞 절이 일어날 때」). 줄 묶음 바로 아래에 붙인다.
        if (_freqText != null)
        {
            float top = RowsTop - used * rh - 0.012f;
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
        => $"<color={NewWordHex}>{now}</color>{Tail(tier)}\n<size={OldPct}><color={LineLabelHex}><s>{old}</s></color></size>";

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
        // 고치는 줄의 라벨 = 그만두기(10-18 — 같은 줄을 다시 누르면 풀린다는 게 보이지 않았다)
        _fixLabels[r].text = active ? "취소" : "고치기";
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
        if (_editIndex == line) { LeaveRewrite(); return; }   // 고치는 줄을 다시 누름 = [취소](라벨이 「취소」다)
        Managers.Sound?.PlayUiEvent(SoundEvent.CovenantFix);
        _editIndex = line;
        _selWrite = -1;   // 줄을 바꾸면 카드는 새로 고른다 — 고르기 전엔 [새긴다]가 잠겨 있다
        RefreshWrite();
    }

    /// <summary>모드 탭 — [이어 쓰기]는 고쳐 쓰기를 그만두고, [고쳐 쓰기]는 줄 고르기부터(바꿀 말은 줄을 고른 뒤에 나온다).</summary>
    private void OnTab(bool append)
    {
        if (append == (_editIndex == -1)) return;   // 이미 그 모드
        if (append && IsFull) { Managers.Sound?.PlayUiEvent(SoundEvent.UiReject); return; }   // 가득 차 이을 수 없다(탭도 잠겨 있다)
        Managers.Sound?.PlayUiEvent(SoundEvent.UiSelect);
        _editIndex = append ? -1 : -2;
        _selWrite  = -1;
        RefreshWrite();
    }

    /// <summary>
    /// 고쳐 쓰기를 그만둔다 — 이어 쓰기로(가득 찼으면 줄 고르기 전으로). [취소] · ESC가 부른다(10-18).
    /// 풀 것이 있었으면 true — ESC는 false일 때 창을 닫는다(<see cref="ClosePopupUI"/>).
    /// </summary>
    private bool LeaveRewrite()
    {
        if (!_writeMode) return false;   // 첫 쓰기 판(조립)엔 고쳐 쓰기가 없다 — ESC는 바로 닫는다
        int to = IsFull ? -2 : -1;
        if (_editIndex == to) return false;
        Managers.Sound?.PlayUiEvent(SoundEvent.UiClose);
        _editIndex = to;
        _selWrite  = -1;
        RefreshWrite();
        return true;
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
