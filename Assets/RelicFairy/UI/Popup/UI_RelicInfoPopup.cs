using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 유물 정보 팝업 — 제단에서 F를 누르면 뜬다.
///
/// <b>읽는 UI가 아니라 스캔하는 UI다.</b> 제단 앞에서 몇 초 안에 "이게 뭐 하는 놈인가"를 판단해야 한다.
/// 그래서 산문을 버리고 <b>능력 카드</b>로 쪼갠다 — 이름 · 배지 · 한 줄 요약 · 강조된 수치.
///
/// 배치 원칙(선택 화면 리서치):
///  • <b>크기가 곧 위계</b> — 결정에 가장 중요한 Q스킬을 맨 위에 두고 금테로 격을 올린다.
///  • <b>세로 스캔</b> — 카드를 오른쪽 좁은 열에 넣어 시선 이동 거리를 줄인다(가로로 길면 '읽기'가 된다).
///  • <b>2단 고정</b> — 일러스트가 아직 없어도 좌측 칸을 접지 않는다. 접으면 전체가 다시
///    폭 넓은 텍스트 스택이 되어 "설명서"로 되돌아간다. 없을 땐 플레이스홀더가 자리를 지킨다.
///  • 로어는 정보 흐름을 끊지 않도록 <b>하단 각주</b>로 뺀다.
///
/// 레이아웃은 전부 절차 생성한다 — 프리팹은 루트 하나뿐이다.
/// </summary>
public class UI_RelicInfoPopup : UI_Popup
{
    // ── Constants ─────────────────────────────────────────────
    private const float PanelWidth     = 1280f;
    private const float PortraitWidth  = 340f;
    // 일러 칸이 오른쪽 정보 열보다 훨씬 길면 Q카드 아래에 죽은 공간이 남는다 → 비슷하게 맞춘다.
    private const float PortraitHeight = 400f;
    private const float AccentBar      = 4f;
    private const float StatLabelWidth = 132f;  // 수치 표의 라벨 열 폭(고정해야 값이 세로로 정렬된다)

    private static readonly Color PanelBg   = new(0.07f, 0.06f, 0.10f, 0.97f);
    private static readonly Color PanelLine = new(0.85f, 0.72f, 0.35f, 1f);
    private static readonly Color CardBg    = new(0.12f, 0.11f, 0.15f, 1f);
    private static readonly Color HeroCardBg = new(0.16f, 0.13f, 0.10f, 1f);   // Q스킬 카드
    private static readonly Color ChipBg    = new(0.20f, 0.18f, 0.24f, 1f);
    private static readonly Color HolderBg  = new(0.13f, 0.12f, 0.17f, 1f);

    private static readonly Color TitleColor = new(0.96f, 0.84f, 0.45f, 1f);
    private static readonly Color TagLineCol = new(0.78f, 0.74f, 0.66f, 1f);
    private static readonly Color LoreColor  = new(0.55f, 0.52f, 0.48f, 1f);
    private static readonly Color BodyColor  = new(0.86f, 0.84f, 0.79f, 1f);
    private static readonly Color SummaryCol   = new(0.80f, 0.78f, 0.74f, 1f);   // 너무 죽이면 안 읽힌다
    private static readonly Color StatLabelCol = new(0.58f, 0.56f, 0.54f, 1f);   // 표의 라벨 열
    private static readonly Color BadgeColor = new(0.58f, 0.56f, 0.53f, 1f);
    private static readonly Color ChipText   = new(0.82f, 0.80f, 0.76f, 1f);
    private static readonly Color SectionCol = new(0.55f, 0.60f, 0.72f, 1f);   // 존 제목

    private const string NumberHex = "#FFD37A";   // 수치만 여기로 튄다

    private static readonly Color AccentPassive = new(0.62f, 0.66f, 0.76f, 1f);
    private static readonly Color AccentState   = new(0.98f, 0.52f, 0.30f, 1f);
    private static readonly Color AccentSkill   = new(0.96f, 0.84f, 0.45f, 1f);

    private const float CardIntroDelay    = 0.14f;
    private const float CardIntroStagger  = 0.06f;
    private const float CardIntroDuration = 0.22f;

    // ── Private ───────────────────────────────────────────────
    private RelicClassSO _relic;
    private Action       _onConfirm;

    private Image         _portrait;
    private RectTransform _placeholder;
    private TMP_Text      _placeholderInitial;
    private TMP_Text      _title;
    private TMP_Text      _tagline;
    private TMP_Text      _lore;
    private RectTransform _tagRow;
    private RectTransform _skillZone;     // 고유 스킬(Q) — 일러스트 옆, 세로 1열
    private RectTransform _passiveZone;   // 상시 능력 — 패널 하단, 가로 2열

    // 테마색으로 다시 칠할 요소들(유물마다 톤이 다르다). Init에서 만들고 Bind에서 재도색.
    private Outline _panelOutline;
    private Image   _confirmBg;
    private Outline _confirmLine;
    private TMP_Text _confirmLabel;
    private Color   _accentSkill = AccentSkill;   // Q카드 강조색(테마색으로 덮인다)

    private readonly List<GameObject> _spawned = new();
    private readonly StringBuilder _sb = new();
    private int _introGen;

    // 팝업 전 텍스트가 공유하는 가독성 머티리얼(두께 보정 + 얇은 테두리)
    private static Material s_textMat;

    public override bool BlocksGameplay => true;

    // ── Init ──────────────────────────────────────────────────
    public override void Init()
    {
        base.Init();
        BuildLayout();
    }

    // ── Public Methods ────────────────────────────────────────
    public void Setup(RelicClassSO relic, Action onConfirm)
    {
        _relic     = relic;
        _onConfirm = onConfirm;
        Bind();
    }

    // ── Private Methods ───────────────────────────────────────
    private void Bind()
    {
        if (_relic == null) return;

        for (int i = 0; i < _spawned.Count; i++) if (_spawned[i] != null) Destroy(_spawned[i]);
        _spawned.Clear();

        ApplyTheme(_relic.ThemeColor(TitleColor));   // 유물 톤 반영(카드 생성 전에 강조색을 정해둔다)

        _title.text = _relic.DisplayName;
        SetOrHide(_tagline, _relic.Tagline);
        SetOrHide(_lore,    _relic.LoreDesc);

        BindPortrait();
        BuildTags();
        BuildCards();

        PlayIntroAsync(++_introGen).Forget();
    }

    /// <summary>
    /// 유물 톤 색을 팝업 전반에 칠한다 — 제목·패널 테두리·확정 버튼·Q카드 강조.
    /// 유물마다 색이 달라야 "다른 유물"로 읽힌다(가웨인=태양금, 랜슬롯=핏빛 등).
    /// 레이아웃은 Init에서 이미 만들어졌으므로 여기서는 색만 다시 입힌다.
    /// </summary>
    private void ApplyTheme(Color theme)
    {
        _accentSkill = theme;   // 이후 BuildCards가 Q카드에 이 색을 쓴다

        if (_title != null)        _title.color = theme;
        if (_panelOutline != null) _panelOutline.effectColor = theme;

        if (_confirmBg != null)
        {
            // 버튼 배경은 톤을 어둡게 깐 색(글자와 대비). 톤을 0.28배 정도로 눌러 쓴다.
            _confirmBg.color = new Color(theme.r * 0.32f, theme.g * 0.28f, theme.b * 0.14f, 1f);
        }
        if (_confirmLine != null)  _confirmLine.effectColor = new Color(theme.r, theme.g, theme.b, 0.55f);
        if (_confirmLabel != null) _confirmLabel.color = theme;
    }

    /// <summary>
    /// 일러스트가 없으면 플레이스홀더가 자리를 지킨다 — 칸을 접으면 2단 구도가 무너져
    /// 전체가 다시 폭 넓은 텍스트 스택이 된다.
    /// </summary>
    private void BindPortrait()
    {
        var sprite = _relic.Portrait != null ? _relic.Portrait : _relic.RosterIllust;

        _portrait.gameObject.SetActive(sprite != null);
        _placeholder.gameObject.SetActive(sprite == null);

        if (sprite != null) { _portrait.sprite = sprite; return; }

        string name = _relic.DisplayName;
        _placeholderInitial.text = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1);
    }

    private static void SetOrHide(TMP_Text t, string body)
    {
        bool has = !string.IsNullOrWhiteSpace(body);
        t.gameObject.SetActive(has);
        if (has) t.text = body;
    }

    private void BuildTags()
    {
        var tags = _relic.Tags;
        bool has = tags != null && tags.Length > 0;
        _tagRow.gameObject.SetActive(has);
        if (!has) return;

        for (int i = 0; i < tags.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(tags[i])) continue;

            var chip = NewImage("Tag", _tagRow, ChipBg);

            // 칩은 글자 폭만큼만 오그라들어야 한다. 안 그러면 균등 분할돼 거대한 '버튼'처럼 보이고,
            // 누를 수 있는 것으로 오인된다(affordance 오류).
            var h = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(11, 11, 3, 3);
            h.childControlWidth = true;  h.childForceExpandWidth  = false;
            h.childControlHeight = true; h.childForceExpandHeight = false;

            var fit = chip.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit   = ContentSizeFitter.FitMode.Unconstrained;

            var le = chip.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 24f;
            le.flexibleWidth   = 0f;

            var t = NewText("T", chip.rectTransform, 15f, ChipText, FontStyles.Normal, TextAlignmentOptions.Center);
            t.text = tags[i];
            t.textWrappingMode = TextWrappingModes.NoWrap;

            _spawned.Add(chip.gameObject);
        }
    }

    /// <summary>
    /// 능력을 <b>두 존으로 나눠</b> 배치한다.
    ///
    /// 카드를 한 열에 죽 쌓으면 칸들이 붙어 보여서 눈이 미끄러진다 — 어디까지가 스킬이고
    /// 어디부터가 상시 능력인지 구분이 안 된다. 그래서 공간 자체를 갈라놓는다:
    ///  • 고유 스킬(Q) — 일러스트 옆, 크게 한 장. 결정의 근거이자 유물의 정체성이다.
    ///  • 상시 능력    — 패널 하단, 가로 2열. 서로 비교하며 훑는 정보라 나란히 두는 게 맞다.
    /// </summary>
    private void BuildCards()
    {
        var abilities = _relic.Abilities;
        if (abilities == null) return;

        int passiveCount = 0;
        for (int i = 0; i < abilities.Length; i++)
        {
            var a = abilities[i];
            if (a == null || string.IsNullOrWhiteSpace(a.name)) continue;

            bool isSkill = a.kind == RelicAbilityKind.Skill;
            _spawned.Add(BuildCard(a, isSkill ? _skillZone : _passiveZone));
            if (!isSkill) passiveCount++;
        }

        _passiveZone.parent.gameObject.SetActive(passiveCount > 0);
    }

    private GameObject BuildCard(RelicAbilityInfo a, RectTransform parent)
    {
        bool hero = a.kind == RelicAbilityKind.Skill;

        Color accent = a.kind switch
        {
            RelicAbilityKind.State => AccentState,
            RelicAbilityKind.Skill => _accentSkill,   // 유물 테마색
            _                      => AccentPassive,
        };

        var card = NewImage("Card", parent, hero ? HeroCardBg : CardBg);

        if (hero)   // 금테 — 이 카드가 이 유물의 정체성임을 한눈에
        {
            var line = card.gameObject.AddComponent<Outline>();
            line.effectColor    = new Color(accent.r, accent.g, accent.b, 0.55f);
            line.effectDistance = new Vector2(1.5f, -1.5f);
        }
        else        // 상시 능력은 2열로 나란히 — 폭을 균등 분할한다
        {
            var le = card.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minWidth      = 200f;
        }

        var h = card.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(0, 20, hero ? 20 : 16, hero ? 20 : 16);
        h.spacing = 16f;
        h.childControlWidth = true;  h.childForceExpandWidth  = false;
        h.childControlHeight = true; h.childForceExpandHeight = true;

        var bar = NewImage("Accent", card.rectTransform, accent);
        var barLe = bar.gameObject.AddComponent<LayoutElement>();
        barLe.preferredWidth = AccentBar;
        barLe.minWidth       = AccentBar;

        var col = NewRect("Col", card.rectTransform);
        var cv = col.gameObject.AddComponent<VerticalLayoutGroup>();
        cv.spacing = 3f;
        cv.childControlWidth = true;  cv.childForceExpandWidth  = true;
        cv.childControlHeight = true; cv.childForceExpandHeight = false;
        col.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        // 이름 ─────────── 배지
        var head = NewRect("Head", col);
        var hh = head.gameObject.AddComponent<HorizontalLayoutGroup>();
        hh.childControlWidth = true;  hh.childForceExpandWidth  = true;
        hh.childControlHeight = true; hh.childForceExpandHeight = false;

        var name = NewText("Name", head, hero ? 28f : 23f, accent, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        name.text = a.name;
        name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var badge = NewText("Badge", head, 15f, BadgeColor, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
        badge.text = string.IsNullOrWhiteSpace(a.badge) ? DefaultBadge(a.kind) : a.badge;
        badge.textWrappingMode = TextWrappingModes.NoWrap;
        badge.gameObject.AddComponent<LayoutElement>().flexibleWidth = 0f;

        if (!string.IsNullOrWhiteSpace(a.summary))
        {
            var sum = NewText("Summary", col, 17f, SummaryCol, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            sum.text = a.summary;
            sum.lineSpacing = -12f;
        }

        BuildStatRows(col, a.stats, hero);

        return card.gameObject;
    }

    /// <summary>
    /// 수치를 <b>표</b>로 뽑는다 — 라벨 열 / 값 열.
    ///
    /// "착탄 3.5배  화상 6초  작열 지대 4초" 처럼 한 줄에 몰아넣으면, 라벨과 값이 뒤엉켜
    /// 어느 숫자가 무엇의 숫자인지 눈이 못 짝지어 준다. 라벨 열의 폭을 고정하면 값이 세로로
    /// 정렬돼 <b>한 번에 훑힌다</b>.
    ///
    /// 데이터 형식은 "라벨|값". 세로줄이 없으면 그냥 한 줄로 낸다(회귀 0).
    /// </summary>
    private void BuildStatRows(RectTransform col, string[] stats, bool hero)
    {
        if (stats == null || stats.Length == 0) return;

        var table = NewRect("Stats", col);
        var tv = table.gameObject.AddComponent<VerticalLayoutGroup>();
        tv.spacing = 8f;
        tv.padding = new RectOffset(0, 0, 6, 0);
        tv.childControlWidth = true;  tv.childForceExpandWidth  = true;
        tv.childControlHeight = true; tv.childForceExpandHeight = false;

        foreach (var raw in stats)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            int bar = raw.IndexOf('|');
            string label = bar >= 0 ? raw.Substring(0, bar).Trim() : string.Empty;
            string value = bar >= 0 ? raw.Substring(bar + 1).Trim() : raw.Trim();

            var row = NewRect("Row", table);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childAlignment = TextAnchor.UpperLeft;
            h.childControlWidth = true;  h.childForceExpandWidth  = false;
            h.childControlHeight = true; h.childForceExpandHeight = false;

            var l = NewText("L", row, 16f, StatLabelCol, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            l.text = label;
            l.textWrappingMode = TextWrappingModes.NoWrap;
            var lle = l.gameObject.AddComponent<LayoutElement>();
            lle.preferredWidth = StatLabelWidth;   // 폭 고정 → 값이 세로로 정렬된다
            lle.minWidth       = StatLabelWidth;
            lle.flexibleWidth  = 0f;

            var v = NewText("V", row, hero ? 19f : 18f, BodyColor, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            v.text = Highlight(value);
            v.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }
    }

    private static string DefaultBadge(RelicAbilityKind kind) => kind switch
    {
        RelicAbilityKind.State => "상태",
        RelicAbilityKind.Skill => "Q",
        _                      => "패시브",
    };

    /// <summary>숫자가 든 토큰만 굵은 금색으로 — 라벨과 같은 색이면 눈이 수치를 못 집는다.</summary>
    private string Highlight(string value)
    {
        _sb.Clear();
        bool first = true;
        foreach (var token in value.Split(' '))
        {
            if (token.Length == 0) continue;
            if (!first) _sb.Append(' ');
            first = false;

            if (HasDigit(token)) _sb.Append("<color=").Append(NumberHex).Append('>').Append(token).Append("</color>");
            else                 _sb.Append(token);
        }
        return _sb.ToString();
    }

    private static bool HasDigit(string s)
    {
        for (int i = 0; i < s.Length; i++) if (char.IsDigit(s[i])) return true;
        return false;
    }

    private void Confirm()
    {
        var cb = _onConfirm;
        Managers.UI.ClosePopupUI(this);
        cb?.Invoke();
    }

    private void Cancel() => Managers.UI.ClosePopupUI(this);

    // ── 인트로 연출 ────────────────────────────────────────────
    private async UniTaskVoid PlayIntroAsync(int gen)
    {
        var cards = new List<CanvasGroup>(_spawned.Count);
        for (int i = 0; i < _spawned.Count; i++)
        {
            var go = _spawned[i];
            if (go == null || go.name != "Card") continue;

            // ⚠️ `??` 금지 — C# null 병합은 Unity가 오버로드한 ==(fake-null)을 건너뛴다.
            //    컴포넌트가 없는데도 '있다'고 판단해 AddComponent를 스킵하고 NRE가 난다.
            if (!go.TryGetComponent<CanvasGroup>(out var cg))
                cg = go.AddComponent<CanvasGroup>();

            cg.alpha = 0f;
            go.transform.localScale = new Vector3(1f, 0.92f, 1f);
            cards.Add(cg);
        }
        if (cards.Count == 0) return;

        await UniTask.Delay(TimeSpan.FromSeconds(CardIntroDelay), DelayType.UnscaledDeltaTime,
                            cancellationToken: destroyCancellationToken);
        if (gen != _introGen) return;

        for (int i = 0; i < cards.Count; i++)
        {
            RevealCardAsync(cards[i], gen).Forget();
            await UniTask.Delay(TimeSpan.FromSeconds(CardIntroStagger), DelayType.UnscaledDeltaTime,
                                cancellationToken: destroyCancellationToken);
            if (gen != _introGen) return;
        }
    }

    private async UniTaskVoid RevealCardAsync(CanvasGroup cg, int gen)
    {
        float t = 0f;
        while (t < 1f)
        {
            if (cg == null || gen != _introGen) return;

            t += Time.unscaledDeltaTime / CardIntroDuration;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);

            cg.alpha = e;
            cg.transform.localScale = new Vector3(1f, Mathf.Lerp(0.92f, 1f, e), 1f);

            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
        }
    }

    // ── 절차 생성 레이아웃 ─────────────────────────────────────
    private void BuildLayout()
    {
        var root = (RectTransform)transform;

        var dim = NewImage("Dim", root, new Color(0f, 0f, 0f, 0.65f));
        Stretch(dim.rectTransform);
        dim.raycastTarget = true;

        var panel = NewImage("Panel", root, PanelBg);
        var prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot     = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(PanelWidth, 300f);

        _panelOutline = panel.gameObject.AddComponent<Outline>();
        _panelOutline.effectColor    = PanelLine;
        _panelOutline.effectDistance = new Vector2(2f, -2f);

        var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(38, 38, 32, 30);
        v.spacing = 20f;
        v.childControlWidth = true;  v.childForceExpandWidth  = true;
        v.childControlHeight = true; v.childForceExpandHeight = false;

        var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fit.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        BuildBody(prt);
        BuildPassiveSection(prt);
        BuildDivider(prt);

        _lore = NewText("Lore", prt, 16f, LoreColor, FontStyles.Italic, TextAlignmentOptions.TopLeft);

        BuildButtons(prt);
    }

    /// <summary>상시 능력 존 — 패널 하단 전체 폭. 가로 2열로 나란히 둬 세로 단조로움을 깬다.</summary>
    private void BuildPassiveSection(RectTransform parent)
    {
        var section = NewRect("PassiveSection", parent);
        var sv = section.gameObject.AddComponent<VerticalLayoutGroup>();
        sv.spacing = 8f;
        sv.childControlWidth = true;  sv.childForceExpandWidth  = true;
        sv.childControlHeight = true; sv.childForceExpandHeight = false;

        BuildSectionLabel(section, "상시 능력");

        _passiveZone = NewRect("Cards", section);
        var h = _passiveZone.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 14f;
        h.childAlignment = TextAnchor.UpperLeft;
        h.childControlWidth = true;  h.childForceExpandWidth  = true;
        h.childControlHeight = true; h.childForceExpandHeight = true;
    }

    /// <summary>존 제목 — 얇은 대문자 라벨 + 선. 공간이 갈렸다는 신호를 준다.</summary>
    private void BuildSectionLabel(RectTransform parent, string text)
    {
        var row = NewRect("SectionLabel", parent);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;  h.childForceExpandWidth  = false;
        h.childControlHeight = true; h.childForceExpandHeight = false;

        var t = NewText("T", row, 14f, SectionCol, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        t.text = text;
        t.characterSpacing = 6f;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 0f;

        var line = NewImage("Line", row, new Color(1f, 1f, 1f, 0.09f));
        var le = line.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth   = 1f;
        le.preferredHeight = 1f;
        le.minHeight       = 1f;
    }

    /// <summary>좌: 일러스트(또는 플레이스홀더) / 우: 이름 · 컨셉 · 태그 · 능력 카드</summary>
    private void BuildBody(RectTransform parent)
    {
        var body = NewRect("Body", parent);
        var h = body.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 28f;
        h.childAlignment = TextAnchor.UpperLeft;
        h.childControlWidth = true;  h.childForceExpandWidth  = false;
        h.childControlHeight = true; h.childForceExpandHeight = false;

        BuildPortraitColumn(body);
        BuildInfoColumn(body);
    }

    private void BuildPortraitColumn(RectTransform parent)
    {
        var box = NewRect("PortraitBox", parent);
        var le = box.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth  = PortraitWidth;
        le.minWidth        = PortraitWidth;
        le.preferredHeight = PortraitHeight;
        le.flexibleWidth   = 0f;

        // 실제 일러스트
        _portrait = NewImage("Portrait", box, Color.white);
        Stretch(_portrait.rectTransform);
        _portrait.preserveAspect = true;

        // 플레이스홀더 — 일러스트가 들어오면 이 자리를 그대로 넘겨준다.
        _placeholder = NewRect("Placeholder", box);
        Stretch(_placeholder);

        var bg = NewImage("Bg", _placeholder, HolderBg);
        Stretch(bg.rectTransform);
        var bgLine = bg.gameObject.AddComponent<Outline>();
        bgLine.effectColor    = new Color(PanelLine.r, PanelLine.g, PanelLine.b, 0.30f);
        bgLine.effectDistance = new Vector2(1.5f, -1.5f);

        _placeholderInitial = NewText("Initial", _placeholder, 150f,
                                      new Color(TitleColor.r, TitleColor.g, TitleColor.b, 0.30f),
                                      FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(_placeholderInitial.rectTransform);
    }

    private void BuildInfoColumn(RectTransform parent)
    {
        var col = NewRect("Info", parent);
        var cv = col.gameObject.AddComponent<VerticalLayoutGroup>();
        cv.spacing = 7f;
        cv.childControlWidth = true;  cv.childForceExpandWidth  = true;
        cv.childControlHeight = true; cv.childForceExpandHeight = false;
        col.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        _title   = NewText("Title",   col, 44f, TitleColor, FontStyles.Bold,   TextAlignmentOptions.TopLeft);
        _tagline = NewText("Tagline", col, 19f, TagLineCol, FontStyles.Normal, TextAlignmentOptions.TopLeft);

        _tagRow = NewRect("Tags", col);
        var th = _tagRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        th.spacing = 6f;
        th.childAlignment = TextAnchor.MiddleLeft;
        th.childControlWidth = true;  th.childForceExpandWidth  = false;
        th.childControlHeight = true; th.childForceExpandHeight = false;
        _tagRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

        // 고유 스킬 존 — 일러스트 옆. 결정의 근거라 가장 눈에 띄는 자리에 크게 둔다.
        var skillSection = NewRect("SkillSection", col);
        var sv = skillSection.gameObject.AddComponent<VerticalLayoutGroup>();
        sv.spacing = 8f;
        sv.padding = new RectOffset(0, 0, 10, 0);
        sv.childControlWidth = true;  sv.childForceExpandWidth  = true;
        sv.childControlHeight = true; sv.childForceExpandHeight = false;

        BuildSectionLabel(skillSection, "고유 스킬");

        _skillZone = NewRect("Cards", skillSection);
        var lv = _skillZone.gameObject.AddComponent<VerticalLayoutGroup>();
        lv.spacing = 8f;
        lv.childControlWidth = true;  lv.childForceExpandWidth  = true;
        lv.childControlHeight = true; lv.childForceExpandHeight = false;
    }

    private void BuildDivider(RectTransform parent)
    {
        var d = NewImage("Divider", parent, new Color(1f, 1f, 1f, 0.10f));
        var le = d.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 1f;
        le.minHeight       = 1f;
    }

    private void BuildButtons(RectTransform parent)
    {
        var row = NewRect("Buttons", parent);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 62f;

        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 14f;
        h.childControlWidth = true;  h.childForceExpandWidth  = true;
        h.childControlHeight = true; h.childForceExpandHeight = true;

        // 확정 버튼은 참조를 잡아 둔다 — ApplyTheme가 유물 톤으로 다시 칠한다.
        MakeButton(row, "선택", new Color(0.34f, 0.27f, 0.10f, 1f), TitleColor, Confirm,
                   out _confirmBg, out _confirmLine, out _confirmLabel);
        MakeButton(row, "취소", new Color(0.16f, 0.16f, 0.19f, 1f), BodyColor,  Cancel,
                   out _, out _, out _);
    }

    private void MakeButton(RectTransform parent, string label, Color bg, Color fg, Action onClick,
                            out Image bgImg, out Outline outline, out TMP_Text labelText)
    {
        var img = NewImage("Btn_" + label, parent, bg);
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());

        var line = img.gameObject.AddComponent<Outline>();
        line.effectColor    = new Color(fg.r, fg.g, fg.b, 0.5f);
        line.effectDistance = new Vector2(1.5f, -1.5f);

        var t = NewText("Label", img.rectTransform, 25f, fg, FontStyles.Bold, TextAlignmentOptions.Center);
        t.text = label;
        Stretch(t.rectTransform);

        bgImg = img; outline = line; labelText = t;
    }

    // ── 생성 헬퍼 ─────────────────────────────────────────────
    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    private static TMP_Text NewText(string name, Transform parent, float size, Color color,
                                    FontStyles style, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);

        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize      = size;
        t.color         = color;
        t.fontStyle     = style;
        t.alignment     = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;

        ApplyReadableMaterial(t);
        return t;
    }

    /// <summary>
    /// 프로젝트 기본 한글 폰트가 <b>Thin 마스터로 구워져 있어 얇고 흐리다</b>.
    /// HUD는 TMPOutlineHelper로 테두리를 넣어 보정하는데, 이 팝업은 그게 없어서 글자가 뭉갰다.
    ///
    /// TMPOutlineHelper는 fontMaterial(인스턴스)을 건드려 텍스트마다 머티리얼이 하나씩 생긴다 —
    /// 이 팝업은 텍스트가 30개가 넘어 배칭이 통째로 깨진다. 그래서 <b>공유 머티리얼 1개</b>를 만들어
    /// 전 텍스트가 나눠 쓴다. 글자 두께(FaceDilate)까지 함께 올려 얇은 폰트를 메운다.
    /// </summary>
    private static void ApplyReadableMaterial(TMP_Text t)
    {
        if (t.font == null) return;

        if (s_textMat == null)   // Unity-null(파괴됨) 포함 → 재생성
        {
            var src = t.fontSharedMaterial;
            if (src == null) return;

            s_textMat = new Material(src) { name = src.name + " (RelicPopup)" };
            s_textMat.EnableKeyword("OUTLINE_ON");
            s_textMat.SetFloat(ShaderUtilities.ID_OutlineWidth,    0.07f);
            s_textMat.SetColor(ShaderUtilities.ID_OutlineColor,    new Color(0.03f, 0.02f, 0.04f, 1f));
            s_textMat.SetFloat(ShaderUtilities.ID_OutlineSoftness, 0.04f);
            s_textMat.SetFloat(ShaderUtilities.ID_FaceDilate,      0.05f);   // 얇은 폰트 최소 보정만
        }

        t.fontSharedMaterial = s_textMat;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
