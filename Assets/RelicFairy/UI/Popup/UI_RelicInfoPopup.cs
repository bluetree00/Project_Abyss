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
    // ── 목업 좌표계 (납품 아트의 원본 픽셀 = 1018×646) ─────────
    // 풀샷에서 템플릿 매칭으로 딴 값이고, 조각의 원본 크기와 정확히 일치한다(좌우 여백 91/92 대칭).
    // 여기 숫자를 고치면 화면이 그대로 따라간다 — 판 크기는 UIWindowFitter가 화면에 맞춰 키운다.
    private const float MockW = 1018f, MockH = 646f;

    // ── 좌표 정본 = 「유물 선택 팝업 ui/풀샷.png」. 조각 아트를 템플릿 매칭해 얻은 값이다.
    //    (판 674×428로 축소 합성돼 있어 축척 0.6621을 되돌려 설계 좌표계로 환산했다.)
    private const float PortraitX = 85f,  PortraitY = 82f,  PortraitW = 278f, PortraitH = 275f;
    private const float NameX     = 88f,  NameY     = 376f, NameW     = 270f, NameH     = 63f;
    private const float TagY      = 145f, TagW      = 156f, TagH      = 45f;
    private const float Tag0X     = 400f, TagStepX  = 186f;
    private const float CardX     = 397f, CardY     = 231f, CardW     = 533f, CardH     = 207f;
    // 구분선은 완성본에서 <b>버튼 바로 위</b>(≈470)를 가로지르는 장식이다.
    // 한때 615로 내려놨었는데(설명 줄과 겹친다는 이유), 완성본 「풀샷」을 잘라 확인하니
    // 560~646 구간엔 버튼 끝과 판 테두리뿐이고 구분선은 470에 있다. 615는 오판이었다.
    private const float DivX      = 228f, DivY      = 470f, DivW      = 621f, DivH      = 28f;
    private const float OkX       = 479f, OkY       = 518f, OkW       = 219f, OkH       = 83f;
    private const float NoX       = 716f, NoY       = 517f, NoW       = 214f, NoH       = 85f;
    // 태그라인은 능력 카드 열에 맞추고, 태그 칩(145~190) 위에서 끝나게 한다(96+54=150이면 겹쳤다).
    private const float LineX     = 397f, LineY     = 86f,  LineW     = 533f, LineH     = 54f;
    // 설명 줄은 완성본에 없는 요소다 — 그래서 자리를 스스로 찾아야 한다.
    // 폭 836으로 판을 가로지르면 어디에 놓든 구분선(470~498)이나 버튼(517~602)과 부딪힌다.
    // 완성본에서 <b>구분선 아래·버튼 왼쪽</b>이 통째로 비어 있으므로 그 좌측 열에 세로로 앉힌다.
    private const float LoreX     = 91f,  LoreY     = 512f, LoreW     = 372f, LoreH     = 100f;

    private const float PanelWidth     = 1280f;   // [레거시] 아트가 없을 때의 폴백 폭
    private const float PortraitWidth  = 340f;
    // 일러 칸이 오른쪽 정보 열보다 훨씬 길면 Q카드 아래에 죽은 공간이 남는다 → 비슷하게 맞춘다.
    // 목업의 액자는 230×297(세로/가로 1.29)이고 초상화 아트도 697:907(=1.29)로 같다.
    // 폭 340에 맞추면 세로는 440이 되어야 액자가 안 찌그러진다.
    // ※ 예전에 "패널이 너무 길다"고 330으로 줄인 적이 있는데, 그건 액자 비율을 깨서
    //    오차를 키운 잘못된 처방이었다. 실제로 440이면 내용 총 높이가 프레임 아트 비율에도 더 가깝다.
    private const float PortraitHeight = 440f;   // 아트가 없을 때만 쓰는 폴백
    private const float AccentBar      = 4f;
    private const float ButtonRowHeight = 62f;
    private const float StatLabelWidth = 132f;  // 수치 표의 라벨 열 폭(고정해야 값이 세로로 정렬된다)

    // 판은 완전 불투명이어야 한다 — 0.97이면 뒤의 월드 조명·바닥이 옅게 배어 글자와 경쟁한다.
    private static readonly Color PanelBg   = new(0.07f, 0.06f, 0.10f, 1f);
    private static readonly Color PanelLine = new(0.85f, 0.72f, 0.35f, 1f);
    private static readonly Color CardBg    = new(0.12f, 0.11f, 0.15f, 1f);
    private static readonly Color HeroCardBg = new(0.16f, 0.13f, 0.10f, 1f);   // Q스킬 카드
    private static readonly Color ChipBg    = new(0.20f, 0.18f, 0.24f, 1f);
    private static readonly Color HolderBg  = new(0.13f, 0.12f, 0.17f, 1f);

    private static readonly Color TitleColor = UIPalette.Gold;
    private static readonly Color TagLineCol = new(0.78f, 0.74f, 0.66f, 1f);
    private static readonly Color LoreColor  = new(0.55f, 0.52f, 0.48f, 1f);
    private static readonly Color BodyColor  = new(0.86f, 0.84f, 0.79f, 1f);
    private static readonly Color SummaryCol   = new(0.80f, 0.78f, 0.74f, 1f);   // 너무 죽이면 안 읽힌다
    private static readonly Color StatLabelCol = new(0.58f, 0.56f, 0.54f, 1f);   // 표의 라벨 열
    // Q스킬 카드는 유물 톤(가웨인=주황금) 판이 깔린다 — 회색 라벨은 그 위에서 읽히지 않는다.
    private static readonly Color StatLabelHeroCol = new(0.96f, 0.90f, 0.78f, 0.92f);
    private static readonly Color BadgeColor = new(0.58f, 0.56f, 0.53f, 1f);
    private static readonly Color ChipText   = new(0.82f, 0.80f, 0.76f, 1f);
    private static readonly Color SectionCol = new(0.55f, 0.60f, 0.72f, 1f);   // 존 제목

    private const string NumberHex = "#FFD37A";   // 수치만 여기로 튄다

    private static readonly Color AccentPassive = new(0.62f, 0.66f, 0.76f, 1f);
    private static readonly Color AccentState   = new(0.98f, 0.52f, 0.30f, 1f);
    private static readonly Color AccentSkill   = new(0.96f, 0.84f, 0.45f, 1f);

    /// <summary>패널이 차지할 수 있는 화면 비율 상한. 넘으면 통째로 축소한다.</summary>
    private const float ScreenFill = 0.94f;
    /// <summary>이보다 작은 화면 rect는 "아직 레이아웃 전"으로 보고 클램프를 건너뛴다.</summary>
    private const float MinClampExtent = 400f;

    // 테두리.png(2589×1728) 실측 — 테두리 선 안쪽이 세로 0.1024~0.9508이고, 그 위·아래는
    // 나침반과 ▽ 장식이 사는 <b>투명 여백</b>이다. 아트를 이만큼 rect 밖으로 내민다.
    private const float PanelArtMarginTop    = 0.1024f;
    private const float PanelArtMarginBottom = 0.0492f;

    private const float CardIntroDelay    = 0.14f;
    private const float CardIntroStagger  = 0.06f;
    private const float CardIntroDuration = 0.22f;

    // ── Private ───────────────────────────────────────────────
    private RelicClassSO _relic;
    private Action       _onConfirm;

    [SerializeField] private RectTransform _panel;
    [SerializeField] private Image         _portrait;
    [SerializeField] private RectTransform _placeholder;
    [SerializeField] private TMP_Text      _placeholderInitial;
    [SerializeField] private TMP_Text      _title;
    [SerializeField] private TMP_Text      _tagline;
    [SerializeField] private TMP_Text      _lore;
    [SerializeField] private RectTransform _tagRow;
    [SerializeField] private RectTransform _skillZone;     // 고유 스킬(Q) — 일러스트 옆, 세로 1열
    [SerializeField] private RectTransform _passiveZone;   // 상시 능력 — 패널 하단, 가로 2열

    // 테마색으로 다시 칠할 요소들(유물마다 톤이 다르다). Init에서 만들고 Bind에서 재도색.
    private RelicInfoSkinSO _skin;
    [SerializeField] private Outline _panelOutline;
    [SerializeField] private Image   _confirmBg;
    [SerializeField] private Outline _confirmLine;
    [SerializeField] private TMP_Text _confirmLabel;
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

        ClampPanelToScreen();
        PlayIntroAsync(++_introGen).Forget();
    }

    /// <summary>
    /// 패널이 화면을 넘지 않게 가둔다.
    ///
    /// 패널 높이는 <see cref="ContentSizeFitter"/>가 내용에서 뽑는데 상한이 없다 — 16:9에서 이미
    /// 화면 높이의 98%를 쓰고, 21:9(2560×1080)처럼 논리 높이가 935로 줄면 <b>위·아래가 잘려나간다</b>
    /// (스킬 줄이 많은 유물일수록 먼저 잘린다). 넘칠 때만 통째로 축소해 비율을 유지한다.
    /// </summary>
    private void ClampPanelToScreen()
    {
        if (_panel == null) return;

        _panel.localScale = Vector3.one;                       // 이전 배율 위에 또 곱하지 않는다
        LayoutRebuilder.ForceRebuildLayoutImmediate(_panel);   // Fitter 결과를 지금 확정시킨다

        var screen = (RectTransform)transform;
        float availW = screen.rect.width  * ScreenFill;
        float availH = screen.rect.height * ScreenFill;

        // 화면 rect가 아직 안 잡혔으면 축소하지 않는다. 여기서 작은 값을 믿으면 판이 7%로
        // 쪼그라들어 버튼도 못 누른다 — 클램프는 넘칠 때만 도는 안전장치지 레이아웃이 아니다.
        if (availW < MinClampExtent || availH < MinClampExtent)
        {
            _panel.localScale = Vector3.one;
            return;
        }

        float k = Mathf.Min(1f, availW / Mathf.Max(1f, _panel.rect.width),
                                availH / Mathf.Max(1f, _panel.rect.height));
        _panel.localScale = new Vector3(k, k, 1f);
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

        // 아트 버튼은 톤을 곱하지 않는다 — 회색 금속 명판이 유물 색으로 물들어버린다.
        if (_confirmBg != null && _skin?.selectButton == null)
        {
            // 버튼 배경은 톤을 어둡게 깐 색(글자와 대비). 톤을 0.28배 정도로 눌러 쓴다.
            _confirmBg.color = new Color(theme.r * 0.32f, theme.g * 0.28f, theme.b * 0.14f, 1f);
        }
        if (_confirmLine != null)  _confirmLine.effectColor = new Color(theme.r, theme.g, theme.b, 0.55f);
        // 아트 판 위의 글자까지 유물 톤으로 칠하면 대비가 무너진다(랜슬롯 핏빛 × 주황 판).
        // 아트가 있을 때는 판이 톤을 담당하므로 글자는 읽히는 색으로 고정한다.
        if (_confirmLabel != null)
            _confirmLabel.color = _skin?.selectButton != null ? TitleColor : theme;
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

            // 완성 칩 아트(아이콘+글자가 구워진 156×45)가 있는 태그는 그 한 장으로 끝낸다 —
            // 바탕·글자를 얹으면 두 겹이 된다. 크기는 아트 비율로 고정(행 높이 45에 맞춤).
            // 납품이 가웨인 3종뿐이라 다른 유물 태그는 아래 글자 칩으로 폴백한다.
            var tagArt = _skin?.TagSprite(tags[i]);
            if (tagArt != null)
            {
                var artChip = NewImage("Tag", _tagRow, Color.white);
                artChip.sprite = tagArt;
                artChip.preserveAspect = true;
                var artLe = artChip.gameObject.AddComponent<LayoutElement>();
                artLe.preferredHeight = 45f;
                artLe.preferredWidth  = 45f * tagArt.rect.width / Mathf.Max(1f, tagArt.rect.height);
                artLe.flexibleWidth   = 0f;
                _spawned.Add(artChip.gameObject);
                continue;
            }

            var chip = NewImage("Tag", _tagRow, ChipBg);
            // 칩 폭은 글자 길이에 따라 변한다 — 9-slice로 늘리고, 아트는 자식으로 깐다.
            // (칩은 ContentSizeFitter로 글자 폭을 재는데, 스프라이트를 직접 넣으면
            //  아트 원본 폭 222px가 preferredWidth로 보고돼 칩이 전부 같은 크기로 뚱뚱해진다.)
            AddBackdrop(chip, _skin?.tagChip, sliced: true);

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
            le.preferredHeight = 28f;   // 16px 줄높이(≈21) + 위아래 3 — 24였을 땐 글자가 13.9까지 줄었다
            le.flexibleWidth   = 0f;

            var t = NewText("T", chip.rectTransform, 16f, ChipText, FontStyles.Normal, TextAlignmentOptions.Center);
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
    /// <summary>새 배치에선 능력 칸이 아트를 가져, 안의 항목은 아트를 쓰지 않는다.</summary>
    private bool _heroCardUsesOwnArt = true;

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

        // 새 배치에선 고유·상시가 <b>같은 스크롤</b>에 들어간다 — 그때 부모를 끄면
        // 뷰포트째 사라져 고유 능력까지 안 보인다. 존이 분리돼 있을 때만 끈다.
        if (_passiveZone != _skillZone)
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

        // 아트가 있으면 색 박스 대신 그것을 쓴다. 고유(스킬)는 큰 강조 칸, 상시는 하단 2열 칸.
        // 칸이 이미 카드 아트를 갖고 있으면 항목엔 깔지 않는다 — 액자가 두 겹이 된다.
        var cardArt = !_heroCardUsesOwnArt ? null
                    : hero ? _skin?.uniqueCard
                           : _skin?.PassivePlate(parent.childCount - 1);
        AddBackdrop(card, cardArt, sliced: true);

        if (hero)
        {
            // 금테 — 아트가 없을 때만. 아트엔 이미 테두리가 있어 겹치면 이중선이 된다.
            if (cardArt == null)
            {
                var line = card.gameObject.AddComponent<Outline>();
                line.effectColor    = new Color(accent.r, accent.g, accent.b, 0.55f);
                line.effectDistance = new Vector2(1.5f, -1.5f);
            }
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

        var badge = NewText("Badge", head, 16f, BadgeColor, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
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

            var l = NewText("L", row, 16f, hero ? StatLabelHeroCol : StatLabelCol, FontStyles.Normal, TextAlignmentOptions.TopLeft);
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
        // 프리팹이 구워져 있으면 <b>짓지 않고 잇기만 한다</b> — 다시 지으면 UI가 두 벌 겹친다.
        if (transform.childCount > 0) { BindBakedHierarchy(); return; }

        var root = (RectTransform)transform;

        // 프리팹 루트는 화면 한가운데 100×100으로 authoring 돼 있다 — 암막이 그만큼만 덮어
        // 월드가 한 번도 어두워지지 않았다. 루트부터 화면 전체로 편다.
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        var dim = NewImage("Dim", root, new Color(0f, 0f, 0f, 0.88f));
        Stretch(dim.rectTransform);
        dim.raycastTarget = true;

        _skin = UISkin.RelicInfo;

        // ── 판 — 목업 크기 그대로 두고, 화면 맞춤은 UIWindowFitter에 맡긴다.
        var panel = NewImage("Panel", root, _skin?.panelFrame != null ? Color.white : PanelBg);
        var prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(MockW, MockH);
        _panel = prt;

        if (_skin?.panelFrame != null)
        {
            // 프레임 아트 중앙 알파가 70%라 그것만 깔면 월드가 비쳐 글자가 안 읽힌다.
            AddSolidFill(panel, PanelBg);
            panel.sprite = _skin.panelFrame;
            panel.type   = Image.Type.Simple;
        }
        else
        {
            _panelOutline = panel.gameObject.AddComponent<Outline>();
            _panelOutline.effectColor    = PanelLine;
            _panelOutline.effectDistance = new Vector2(2f, -2f);
        }

        panel.gameObject.AddComponent<UIWindowFitter>().Configure(maxScale: UIWindowFitter.ContentScreen);

        BuildPortrait(prt);
        BuildNamePlate(prt);
        BuildTagline(prt);
        BuildTagSlots(prt);
        BuildAbilityCard(prt);
        BuildDividerArt(prt);
        BuildActionButtons(prt);

        _lore = NewText("Lore", prt, 15f, LoreColor, FontStyles.Italic, TextAlignmentOptions.Top);
        Place(_lore.rectTransform, LoreX, LoreY, LoreW, LoreH);
    }

    /// <summary>목업 좌상단 기준 사각형을 판 기준 <b>비율 앵커</b>로 굳힌다(판이 커지면 같이 커진다).</summary>
    private static void Place(RectTransform rt, float x, float y, float w, float h)
        => UIProportional.Place(rt, x, y, w, h, MockW, MockH);

    /// <summary>초상화 액자 + 그 안의 일러스트. 액자가 위에 얹혀야 테두리가 그림을 덮는다.</summary>
    private void BuildPortrait(RectTransform panel)
    {
        var box = NewImage("PortraitFrame", panel, Color.white);
        Place(box.rectTransform, PortraitX, PortraitY, PortraitW, PortraitH);
        box.raycastTarget = false;

        _portrait = NewImage("Portrait", box.transform, Color.white);
        Stretch(_portrait.rectTransform);
        _portrait.preserveAspect = true;
        _portrait.raycastTarget  = false;

        // 플레이스홀더 — 일러스트가 없을 때 이름 첫 글자를 띄운다. Bind()가 무조건 참조하므로
        // 여기서 반드시 만들어야 한다(옛 빌더에만 있던 시절엔 새 배치에서 null 참조가 났다).
        _placeholder = NewRect("Placeholder", box.transform);
        Stretch(_placeholder);

        var holderBg = NewImage("Bg", _placeholder, HolderBg);
        Stretch(holderBg.rectTransform);
        holderBg.raycastTarget = false;

        _placeholderInitial = NewText("Initial", _placeholder, 96f,
                                      new Color(TitleColor.r, TitleColor.g, TitleColor.b, 0.30f),
                                      FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(_placeholderInitial.rectTransform);

        // 그림·플레이스홀더가 뒤, 액자가 그 위 — 순서를 지켜야 테두리가 그림을 덮는다.
        _portrait.transform.SetAsFirstSibling();

        if (_skin?.portraitFrame != null) box.sprite = _skin.portraitFrame;
        else                              box.color  = new Color(1f, 1f, 1f, 0f);
    }

    /// <summary>이름판 — 목업에서 유물 이름이 우측 열이 아니라 초상화 아래로 내려왔다.</summary>
    private void BuildNamePlate(RectTransform panel)
    {
        var plate = NewImage("NamePlate", panel, new Color(1f, 1f, 1f, 0f));
        Place(plate.rectTransform, NameX, NameY, NameW, NameH);
        plate.raycastTarget = false;
        if (_skin?.namePlate != null) { plate.sprite = _skin.namePlate; plate.color = Color.white; }

        _title = NewText("Title", plate.transform, 26f, TitleColor, FontStyles.Bold,
                         TextAlignmentOptions.Center);
        Stretch(_title.rectTransform);
    }

    /// <summary>태그라인 — 태그칩 위 빈 띠. 목업에 판이 없어 글자만 얹는다.</summary>
    private void BuildTagline(RectTransform panel)
    {
        _tagline = NewText("Tagline", panel, 18f, TagLineCol, FontStyles.Normal,
                           TextAlignmentOptions.BottomLeft);
        Place(_tagline.rectTransform, LineX, LineY, LineW, LineH);
    }

    /// <summary>
    /// 태그칩 3자리. 목업이 <b>고정 3칸</b>이라 가변 폭 가로 배치가 아니라 자리를 미리 잡는다 —
    /// 유물마다 태그가 정확히 3개다(가웨인·랜슬롯 모두 3개).
    /// </summary>
    private void BuildTagSlots(RectTransform panel)
    {
        _tagRow = NewRect("Tags", panel);
        Place(_tagRow, Tag0X, TagY, TagStepX * 2f + TagW, TagH);

        // 칩은 ContentSizeFitter로 <b>제 폭만</b> 정한다 — 줄을 세우는 건 부모 몫이라
        // 이 그룹이 없으면 태그 3개가 같은 자리에 겹쳐 한 장으로 보인다.
        var th = _tagRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        th.spacing          = TagStepX - TagW;
        th.childAlignment   = TextAnchor.MiddleLeft;
        th.childControlWidth = true;  th.childForceExpandWidth  = false;
        th.childControlHeight = true; th.childForceExpandHeight = false;
    }

    /// <summary>
    /// 능력 칸 — 목업의 큰 카드 하나. <b>고유·상시·수치를 전부 여기에</b> 담는다.
    ///
    /// <para>목업에는 상시 능력을 놓을 자리가 따로 없다. 빼면 정보가 사라지므로 이 칸을 스크롤로
    /// 만들어 고유 다음에 이어 붙인다. 카드 아트는 <b>칸이</b> 갖고 안의 항목은 아트 없이 글만
    /// 쌓는다 — 항목마다 아트를 깔면 액자가 두 겹이 된다.</para>
    /// </summary>
    private void BuildAbilityCard(RectTransform panel)
    {
        var box = NewImage("AbilityCard", panel, Color.white);
        Place(box.rectTransform, CardX, CardY, CardW, CardH);
        box.raycastTarget = true;
        if (_skin?.uniqueCard != null)
        {
            box.sprite = _skin.uniqueCard;
            box.type   = Image.Type.Sliced;
        }
        else box.color = CardBg;

        // 모서리 장식이 글자를 먹지 않게 아트 테두리 안쪽만 쓴다.
        var viewport = NewRect("Viewport", box.transform);
        viewport.anchorMin = new Vector2(0.045f, 0.08f);
        viewport.anchorMax = new Vector2(0.955f, 0.92f);
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = NewRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot     = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        // 좌우로 늘린 앵커에서는 sizeDelta.x가 <b>뷰포트에 더해지는 값</b>이다.
        // 새 RectTransform의 기본값 100을 그대로 두면 내용이 뷰포트보다 100px 넓어져
        // 좌우 50px씩 마스크에 잘린다. y는 ContentSizeFitter가 다시 쓴다.
        content.sizeDelta = Vector2.zero;

        var cv = content.gameObject.AddComponent<VerticalLayoutGroup>();
        cv.spacing = 6f;
        cv.childControlWidth = true;  cv.childForceExpandWidth  = true;
        cv.childControlHeight = true; cv.childForceExpandHeight = false;

        var fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fit.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = box.gameObject.AddComponent<ScrollRect>();
        scroll.viewport        = viewport;
        scroll.content         = content;
        scroll.horizontal      = false;
        scroll.movementType    = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;

        _heroCardUsesOwnArt = false;
        _skillZone   = content;
        _passiveZone = content;
    }

    private void BuildDividerArt(RectTransform panel)
    {
        var d = NewImage("Divider", panel, new Color(1f, 1f, 1f, 0.10f));
        Place(d.rectTransform, DivX, DivY, DivW, DivH);
        d.raycastTarget = false;
        if (_skin?.divider != null) { d.sprite = _skin.divider; d.color = Color.white; }
    }

    /// <summary>선택 / 취소.</summary>
    private void BuildActionButtons(RectTransform panel)
    {
        MakeActionButton(panel, "SelectBtn", _skin?.selectButton, OkX, OkY, OkW, OkH,
                         "선택", Confirm);
        MakeActionButton(panel, "CancelBtn", _skin?.cancelButton, NoX, NoY, NoW, NoH,
                         "취소", Cancel);
    }

    private void MakeActionButton(RectTransform panel, string name, Sprite art,
                                  float x, float y, float w, float h,
                                  string fallbackLabel, UnityEngine.Events.UnityAction onClick)
    {
        var img = NewImage(name, panel, art != null ? Color.white : CardBg);
        Place(img.rectTransform, x, y, w, h);
        img.raycastTarget = true;

        if (art != null) img.sprite = art;

        // 아트가 있어도 <b>글자는 코드가 그린다</b>. 이 화면의 납품본
        // (선택 버튼.png 219×83 · 취소.png 214×85)은 글자가 없는 빈 판이라,
        // 라벨을 생략하면 주황·회색 덩어리 두 개만 남아 무슨 버튼인지 알 수 없다.
        var lbl = NewText("Label", img.transform, 20f, TitleColor, FontStyles.Bold,
                          TextAlignmentOptions.Center);
        Stretch(lbl.rectTransform);
        lbl.text = fallbackLabel;
        if (name == "SelectBtn") _confirmLabel = lbl;

        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
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
        h.padding = new RectOffset(12, 12, 8, 12);   // 상시 카드도 같은 글로우 여백 규칙
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

    /// <summary>액자 아트 비율에 맞춘 초상화 칸 높이. 아트가 없으면 <see cref="PortraitHeight"/>.</summary>
    private float PortraitFrameHeight()
    {
        var art = _skin?.portraitFrame;
        if (art == null || art.rect.width <= 1f) return PortraitHeight;
        return PortraitWidth * (art.rect.height / art.rect.width);
    }

    private void BuildPortraitColumn(RectTransform parent)
    {
        var box = NewRect("PortraitBox", parent);
        var le = box.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth  = PortraitWidth;
        le.minWidth        = PortraitWidth;
        // 높이는 <b>액자 아트의 비율에서 뽑는다</b> — 상수로 박아 두면 아트가 바뀔 때마다
        // 액자가 찌그러진다(실제로 세로 액자 697:907 → 정사각 278:275로 갱신되며 겪었다).
        // 아트가 없으면 기존 상수로 돌아간다.
        le.preferredHeight = PortraitFrameHeight();
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

        // 액자는 일러스트·플레이스홀더 위로 지나가야 한다(마지막 자식).
        if (_skin?.portraitFrame != null)
        {
            var frame = NewImage("Frame", box, Color.white);
            Stretch(frame.rectTransform);
            frame.raycastTarget = false;
            ShopUIStyle.Skin(frame, _skin.portraitFrame);
        }
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
        // 카드 아트(고유.png)는 <b>바깥 25px이 네온 글로우</b>다 — 여백 없이 붙이면 그 빛이
        // 왼쪽 액자와 아래 「상시 능력」 라벨을 파고든다. 글로우가 앉을 자리를 비워 둔다.
        lv.padding = new RectOffset(12, 12, 8, 12);
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
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = ButtonRowHeight;

        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 14f;
        h.childAlignment = TextAnchor.MiddleCenter;
        // 아트 버튼은 "선택"·"취소" 글자가 구워져 있다 — 폭을 강제로 늘리면 글자가 2.4배 가로로
        // 늘어나 뭉개진다(패널 폭 1204를 반씩 나눠 595×62가 되던 문제). 아트 비율대로 고정한다.
        bool artButtons = _skin?.selectButton != null || _skin?.cancelButton != null;
        h.childControlWidth = true;  h.childForceExpandWidth  = !artButtons;
        h.childControlHeight = true; h.childForceExpandHeight = true;

        // 확정 버튼은 참조를 잡아 둔다 — ApplyTheme가 유물 톤으로 다시 칠한다.
        MakeButton(row, "선택", new Color(0.34f, 0.27f, 0.10f, 1f), TitleColor, Confirm,
                   _skin?.selectButton, out _confirmBg, out _confirmLine, out _confirmLabel);
        MakeButton(row, "취소", new Color(0.16f, 0.16f, 0.19f, 1f), BodyColor,  Cancel,
                   _skin?.cancelButton, out _, out _, out _);
    }

    /// <summary>
    /// 아트가 있으면 색·테두리는 아트에 넘기되, <b>글자는 코드가 그린다</b>.
    ///
    /// <para>예전엔 "납품본에 글자가 구워져 있다"고 보고 라벨을 껐는데, 이 화면의 납품본
    /// (<c>선택 버튼.png</c> 219×83 · <c>취소.png</c> 214×85)은 <b>글자 없는 빈 판</b>이다.
    /// 그래서 확정·취소 버튼이 주황·회색 덩어리로만 보였다. 룬 선택 화면의
    /// <c>선택 버튼@2x.png</c>에는 글자가 구워져 있어 그쪽은 라벨을 끄는 것이 맞다 —
    /// 전제가 화면마다 다르므로 아트를 실제로 열어 보고 정할 것.</para>
    /// </summary>
    private void MakeButton(RectTransform parent, string label, Color bg, Color fg, Action onClick,
                            Sprite art, out Image bgImg, out Outline outline, out TMP_Text labelText)
    {
        var img = NewImage("Btn_" + label, parent, art != null ? Color.white : bg);
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());

        Outline line = null;
        if (art != null)
        {
            // 버튼도 자식으로 깐다 — 아트 원본(466×117)이 행 높이 62를 밀어내 버튼이 뚱뚱해진다.
            AddBackdrop(img, art, sliced: false);

            // 아트 비율(466:117)대로 폭을 못 박는다. 안 그러면 행이 폭을 반씩 나눠줘 가로로 늘어난다.
            var le = img.gameObject.GetComponent<LayoutElement>() ?? img.gameObject.AddComponent<LayoutElement>();
            float w = ButtonRowHeight * (art.rect.width / Mathf.Max(1f, art.rect.height));
            le.preferredWidth = w;
            le.minWidth       = w;
            le.flexibleWidth  = 0f;
        }
        else
        {
            line = img.gameObject.AddComponent<Outline>();
            line.effectColor    = new Color(fg.r, fg.g, fg.b, 0.5f);
            line.effectDistance = new Vector2(1.5f, -1.5f);
        }

        var t = NewText("Label", img.rectTransform, 25f, fg, FontStyles.Bold, TextAlignmentOptions.Center);
        t.text = label;
        Stretch(t.rectTransform);

        bgImg = img; outline = line; labelText = t;
    }

    /// <summary>레이아웃에 영향을 주지 않는 불투명 바닥판. 프레임 아트가 반투명일 때 뒤를 막는다.</summary>
    private static void AddSolidFill(Image host, Color color)
    {
        if (host == null) return;

        var go = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer));
        var rt = (RectTransform)go.transform;
        rt.SetParent(host.rectTransform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        go.AddComponent<LayoutElement>().ignoreLayout = true;

        var img = go.AddComponent<Image>();
        img.color         = color;
        img.raycastTarget = false;
        rt.SetAsFirstSibling();
    }

    /// <summary>
    /// 아트를 <b>자식으로</b> 깐다. 절대 대상 오브젝트의 Image에 직접 넣지 말 것 —
    /// <see cref="Image"/>는 <c>ILayoutElement</c>라 스프라이트 <b>원본 픽셀 크기</b>를 preferredSize로
    /// 보고한다. 레이아웃 그룹/ContentSizeFitter가 그 값을 채택하면 칸이 아트 해상도만큼 부푼다
    /// (패널이 1728px 높이로, 상시 카드가 1128px 폭으로 터졌던 원인).
    /// 자식 + <c>ignoreLayout</c>이면 크기 계산에서 완전히 빠진다.
    /// </summary>
    /// <param name="marginTop">아트에서 <b>테두리 선 위쪽</b>이 차지하는 비율(장식이 사는 여백).</param>
    /// <param name="marginBottom">아트에서 테두리 선 아래쪽이 차지하는 비율.</param>
    private static void AddBackdrop(Image host, Sprite art, bool sliced,
                                    float marginTop = 0f, float marginBottom = 0f)
    {
        if (host == null || art == null) return;

        host.color = new Color(1f, 1f, 1f, 0f);   // 색 폴백은 감춘다 — 아트가 그 자리를 대신한다

        var go = new GameObject("Bg", typeof(RectTransform), typeof(CanvasRenderer));
        var rt = (RectTransform)go.transform;
        rt.SetParent(host.rectTransform, false);

        // 여백이 있는 아트는 <b>rect 밖으로 내밀어</b> 테두리 선 안쪽이 rect와 정확히 겹치게 한다.
        // 0~1로 그냥 늘리면 장식 여백만큼 선이 안으로 밀려, 그 띠에 깔린 불투명 바닥판이
        // 테두리 바깥으로 삐져나온 판때기처럼 보인다(그게 "뒤에 레거시 배경").
        float inner = 1f - marginTop - marginBottom;
        float over  = inner > 0.01f ? 1f / inner : 1f;
        rt.anchorMin = new Vector2(0f, -marginBottom * over);
        rt.anchorMax = new Vector2(1f, 1f + marginTop * over);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        go.AddComponent<LayoutElement>().ignoreLayout = true;

        var img = go.AddComponent<Image>();
        img.sprite        = art;
        img.type          = sliced ? Image.Type.Sliced : Image.Type.Simple;
        img.raycastTarget = false;
        rt.SetAsFirstSibling();   // 내용보다 뒤에 깔린다
    }

    /// <summary>
    /// 구워진 프리팹을 잇는다 — 계층·좌표·아트는 프리팹이 갖고, 코드는 배선만 한다.
    /// 능력 카드·태그 칩은 <see cref="Bind"/>가 데이터마다 새로 만드므로 여기서 다룰 것이 없다.
    /// </summary>
    private void BindBakedHierarchy()
    {
        _skin  = UISkin.RelicInfo;
        _panel = transform.Find("Panel") as RectTransform;

        // 능력 칸이 카드 아트를 갖는 새 배치다 — 항목까지 아트를 깔면 액자가 두 겹이 된다.
        _heroCardUsesOwnArt = false;

        BindClick("SelectBtn", Confirm);
        BindClick("CancelBtn", Cancel);
    }

    /// <summary>
    /// 이름으로 <b>깊이 탐색</b>해 클릭을 잇는다. 고정 경로를 박으면 계층이 한 겹만 달라져도
    /// 조용히 <c>null</c>이 돌아오고, 버튼이 리스너 없이 떠서 "눌러도 아무 일 없는" 상태가 된다.
    /// </summary>
    private void BindClick(string name, Action onClick)
    {
        var t = ShopUIStyle.FindDeep(transform, name);
        if (t == null) { Debug.LogWarning($"[UI_RelicInfoPopup] 배선 실패 — 「{name}」 없음"); return; }

        var btn = t.GetComponent<Button>();
        if (btn == null) return;

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => onClick());
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

        // 글자가 판 밖으로 나가지 않게 하는 안전망 — TMP 기본 넘침은 잘라내지 않고 <b>바깥에 그린다</b>.
        // 최대를 설계 크기로 묶으므로 커지지는 않고, 안 들어갈 때만 줄어든다.
        t.enableAutoSizing = true;
        t.fontSizeMax      = size;
        t.fontSizeMin      = Mathf.Max(9f, size * 0.55f);
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
