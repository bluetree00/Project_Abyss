using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 제단 판 — 「서약서 쓰기」(10-02 「한 장의 서약서」 설계서 §3). 문장이 이미 있을 때 연다.
/// <list type="bullet">
/// <item>왼쪽(원인 열 + 가운데 자리) = <b>지금 문장</b> 한 줄씩. 줄마다 [고쳐].</item>
/// <item>오른쪽 = 이을 절 3장(이어 쓰기) 또는 그 줄에 들어갈 말 3장(고쳐 쓰기). 교체는 판당 2.</item>
/// <item>고른 카드가 문장에 금빛으로 미리 들어간다. 「새긴다」 = 새 문장 id를 돌려준다.</item>
/// <item>판은 제단에 묶인다(<see cref="CovenantWriteBoard"/>) — 닫았다 열어도 같은 카드 · 남은 교체 그대로.</item>
/// </list>
/// 아트 · 판 크기 · 카드는 조립 판 그대로(같은 프리팹) — 이 모드는 원인 열 · 가운데 미리보기를 접고 문장 지면을 세운다.
/// </summary>
public partial class UI_CovenantAssemble
{
    // 문장 지면 — 판(Book) 비율 좌표. 원인 열 왼끝 ~ 가운데 열 오른끝(프리팹 0.163 ~ 0.643)
    private static readonly Vector2 PageMin = new(0.165f, 0.175f);
    private static readonly Vector2 PageMax = new(0.640f, 0.815f);
    private const int   PageRows      = 8;      // 조건 + 결과 5 + 새 줄 + 빈도
    private const float FixButtonW    = 0.13f;  // 줄 폭 대비 [고쳐] 폭
    private const string PendingHex   = "#9A6A12";   // 금빛 미리보기(양피지 위 진한 금)
    private const string LineDimHex   = "#6E5A42";

    private bool _writeMode;
    private CovenantSentence _sentence;
    private CovenantWriteBoard _board;
    private int _editIndex;            // -1 = 이어 쓰기 · 0 = 조건 고쳐 쓰기 · k ≥ 1 = 결과 k-1 고쳐 쓰기 · -2 = 가득 참(줄을 골라야)
    private int _selWrite;
    private RectTransform _page;
    private readonly List<TMP_Text> _rowTexts = new();
    private readonly List<Button>   _rowFix   = new();

    /// <summary>서약서 쓰기 판을 세운다. 결과 = 새 문장 id(취소 null) — <see cref="WaitForResultAsync"/>.</summary>
    public void SetupWrite(CovenantSentence sentence, CovenantWriteBoard board)
    {
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

        _editIndex = IsFull ? -2 : -1;
        _selWrite  = 0;

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
        for (int r = 0; r < PageRows; r++)
        {
            float top = 1f - r / (float)PageRows, bottom = 1f - (r + 1) / (float)PageRows;
            var row = new GameObject($"Row{r}", typeof(RectTransform));
            var rrt = (RectTransform)row.transform;
            rrt.SetParent(_page, false);
            rrt.anchorMin = new Vector2(0f, bottom); rrt.anchorMax = new Vector2(1f, top);
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;

            var text = style != null ? Instantiate(style, rrt) : new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.name = "Text";
            var trt = text.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = new Vector2(1f - FixButtonW - 0.02f, 1f);
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero; trt.pivot = new Vector2(0f, 0.5f);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.enableAutoSizing = true;
            text.fontSizeMax = 24f; text.fontSizeMin = 14f;
            text.color = InkOnBook;
            text.raycastTarget = false;
            _rowTexts.Add(text);

            _rowFix.Add(MakeFixButton(rrt, r));
        }
    }

    /// <summary>[고쳐] — 옅은 잉크 테두리 판 + 글자. 누르면 그 줄 고쳐 쓰기(다시 누르면 이어 쓰기로).</summary>
    private Button MakeFixButton(RectTransform row, int r)
    {
        var go = new GameObject("Fix", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        var rt = (RectTransform)go.transform;
        rt.SetParent(row, false);
        rt.anchorMin = new Vector2(1f - FixButtonW, 0.18f); rt.anchorMax = new Vector2(1f, 0.82f);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.color = new Color(InkOnBook.r, InkOnBook.g, InkOnBook.b, 0.10f);
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        int line = r;
        btn.onClick.AddListener(() => OnFix(line));

        var style = _rerollCountText;
        var label = style != null ? Instantiate(style, rt) : new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        label.name = "Label";
        var lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        label.text = "고쳐";
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true; label.fontSizeMax = 18f; label.fontSizeMin = 11f;
        label.color = InkOnBookDim;
        label.raycastTarget = false;
        return btn;
    }

    // ── 갱신 ─────────────────────────────────────────────
    private void RefreshWrite()
    {
        var cards = CurrentCards(out var causeCards);
        int n = causeCards != null ? causeCards.Count : cards.Count;
        if (n > 0) _selWrite = Mathf.Clamp(_selWrite, 0, n - 1);

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
                if (CovenantPalette.TryGetEffect(c.effectId, out var e))
                    card.Bind($"{CovenantGrammar.LinkTag(c.link)} · {e.name}", p.valid ? p.EffectAmountCompact() : e.desc, c.tier, TierColor(c.tier),
                              EffectTaxonomy.Badge(e.axis, e.status));
            }
            card.SetSelected(i == _selWrite);
            card.SetSynergy(false);
        }

        RefreshPage(cards, causeCards);

        bool canForge = n > 0 && _editIndex != -2;
        if (_forgeButton)
        {
            _forgeButton.interactable = canForge;
            UIAffordGlow.Set(_forgeButton, canForge);
        }
        SetText(_forgeSummary, _editIndex switch
        {
            -2 => "서약서가 가득 찼다 — 고쳐 쓸 줄을 고르세요",
            -1 => n > 0 ? "이어 쓰기 — 문장 끝에 한 절을 새긴다" : "이을 절이 없다 — 고쳐 쓸 줄을 고르세요",
            0  => "고쳐 쓰기 — 조건을 바꾼다(길이는 늘지 않는다)",
            _  => $"고쳐 쓰기 — {_editIndex}번째 절을 바꾼다(길이는 늘지 않는다)",
        });
        SetText(_rerollCountText, $"교체 {_board.RerollsLeft}");
        GateReroll(_effectCards, _board.RerollsLeft > 0 && _editIndex != -2);
    }

    /// <summary>지금 문장 — 조건 · 결과 줄(고치는 줄은 금빛으로 바뀐 말) · 이어 쓸 줄(금빛) · 빈도.</summary>
    private void RefreshPage(List<SentenceCard> cards, List<CovenantDraftCard> causeCards)
    {
        if (_page == null) return;
        int row = 0;
        bool hasPick = (causeCards != null ? causeCards.Count : cards.Count) > _selWrite;

        // 조건
        string causeLine = CovenantPalette.TryGetCause(_sentence.CauseId, out var cd) ? cd.desc : _sentence.CauseId;
        if (_editIndex == 0 && causeCards != null && hasPick && CovenantPalette.TryGetCause(causeCards[_selWrite].id, out var nc))
            causeLine = $"<color={PendingHex}>{nc.desc}</color>";
        SetRow(row++, causeLine, fix: true, active: _editIndex == 0);

        // 결과
        for (int i = 0; i < _sentence.ResultCount; i++)
        {
            string prev = i > 0 ? _sentence.ResultId(i - 1) : null;
            string line = "→ " + CovenantGrammar.Line(_sentence.ResultId(i), _sentence.ResultLink(i), prev)
                          + $"  <color={LineDimHex}><size=80%>{_sentence.ResultTier(i).DisplayName()}</size></color>";
            if (_editIndex == i + 1 && causeCards == null && hasPick)
                line = $"<color={PendingHex}>→ {CovenantGrammar.Line(cards[_selWrite].effectId, _sentence.ResultLink(i), prev)}</color>";
            SetRow(row++, line, fix: true, active: _editIndex == i + 1);
        }

        // 이어 쓸 줄
        if (_editIndex == -1 && hasPick && causeCards == null)
        {
            var c = cards[_selWrite];
            string prev = _sentence.ResultId(_sentence.ResultCount - 1);
            SetRow(row++, $"<color={PendingHex}>→ {CovenantGrammar.Line(c.effectId, c.link, prev)}  ▏</color>", fix: false, active: false);
        }

        // 빈도 — 조건 기준(뒤 절은 「앞 절이 일어날 때」)
        if (row < PageRows - 1) SetRow(row++, string.Empty, fix: false, active: false);
        SetRow(row++, $"<color={LineDimHex}><size=80%>이 문장이 터지는 때 — {cd.desc}</size></color>", fix: false, active: false);
        for (; row < PageRows; row++) SetRow(row, string.Empty, fix: false, active: false);
    }

    private void SetRow(int r, string text, bool fix, bool active)
    {
        if (r < 0 || r >= _rowTexts.Count) return;
        _rowTexts[r].text = text;
        var btn = _rowFix[r];
        btn.gameObject.SetActive(fix);
        if (fix && btn.targetGraphic is Image img)
            img.color = new Color(InkOnBook.r, InkOnBook.g, InkOnBook.b, active ? 0.28f : 0.10f);
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
        Managers.Sound.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();
        _selWrite = idx;
        RefreshWrite();
    }

    private void OnFix(int line)
    {
        Managers.Sound.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();
        _editIndex = _editIndex == line ? (IsFull ? -2 : -1) : line;
        _selWrite = 0;
        RefreshWrite();
    }

    /// <summary>카드 한 장 교체 — 판에 묶인 목록의 그 칸만 바꾼다(남은 교체는 판 전체 공용).</summary>
    private void RerollWrite(int idx)
    {
        if (_board.RerollsLeft <= 0 || _editIndex == -2) return;
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
        Managers.Sound.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();
        _board.RerollsLeft--;
        _selWrite = idx;
        RefreshWrite();
    }

    /// <summary>고른 카드로 새 문장 id를 만들어 돌려준다.</summary>
    private void OnForgeWrite()
    {
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

        Managers.Sound.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();
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
