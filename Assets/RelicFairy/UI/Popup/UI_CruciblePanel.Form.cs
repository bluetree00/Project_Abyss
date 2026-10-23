using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 재련소 「형」 UI(10-08 · 장비 개선 Part B — c3 시안 01~04, 10-06 기본값 U1~U8) · 톤 v2(얇은 금선).
///  · 성장 길 — 머리 아래 한 줄: 무형검 → +5 형 깨어남 → 단련 Ⅱ → 단련 Ⅲ → 전설, 오른쪽 「다음」 = 다음에 열리는 것(형 · 스킬 단계 · 마스터리).
///    원거리 탭은 스킬 단계 길(파츠 합 2 · 10). 단계가 오르면 지금 칸 테가 다음 칸으로 미끄러진다.
///  · 형 칸 — 오른쪽 판 [강화하기] 아래: 잠긴 형(무형검) · 형이 깨어난다(+5, 진화 버튼) · 형 단련(변주 둘 미리 보기 + 보라 버튼) · 형 완성 요약.
///    강화(확률 · 수치)와 단련(확정 · 기능)을 같은 판에 위아래로 두어 재료 쓸 곳을 한눈에 견준다(설계서 §4-5).
///  · 단련 창 — 카드를 고르면 금 테가 그려지고 다른 카드는 옅어진다 → 확정 버튼이 벼린다 → 망치 · 도장 → 성장 길 칸이 채워진다.
/// 구운 프리팹(1600×1000)은 그대로 두고 Bind 때 무대 · 정보창을 62px 내려 길 자리를 낸다 — 요소 크기는 그대로, 사이 띠만 줄인다.
/// </summary>
public sealed partial class UI_CruciblePanel
{
    // ── 성장 길(시안 ①) ──
    private const float GtY = 104f, GtH = 58f, BodyShift = 62f;
    // 대사 띠 108 → 64(10-11 v2 — 띠 글자가 정보 줄보다 컸다). 줄인 44px은 무대 · 정보창이 아래로 받는다 — 무대 내용은 반만큼 내려 가운데를 지킨다
    private const float BandH = 64f, BandGrow = LogH - BandH, CMid = BandGrow * 0.5f;
    private const float GtW = InfoX + InfoW - StageX;          // 1552 — 무대 왼쪽 끝 ~ 정보창 오른쪽 끝
    private const float GtNodesX = 92f, GtNodesR = 930f;       // 칸들이 나눠 쓰는 폭
    private const float GtNextX = 958f;                        // 「다음」 칸
    private const float GtMinGap = 30f;
    private const int   GtNodes = 5;
    private const string NamelessTitle = "무형검";

    // ── 줄인 판(무대 960 · 정보 568 × 730 = 686 + 대사 띠가 내준 44) ──
    private const float CStageH = StagePanelH - BodyShift + BandGrow, CInfoH = InfoH - BodyShift + BandGrow;
    private const float CStageDy = -24f;                       // 무기 칸 · 모루 · 망치가 올라가는 만큼
    private const float CRowY0 = 164f, CRowH = 46f;
    private const float CEnhanceY = 336f;

    // ── 형 칸(시안 ③ · ④) — 정보창 좌표 ──
    private const float FmY = 472f, FmBtnY = 624f + BandGrow, FmBtnH = 52f;   // 버튼은 판 바닥을 따라 내려가고 형 칸이 그만큼 넓어진다(10-11)
    private const float FmCardW = 242f, FmCardGap = 8f;
    private const float FmPreviewH = 104f;   // 깨어남 · 단련 미리 보기 칸 — 82 → 104(10-11 · 글자 15로 키운 만큼, 판이 44 늘어난 안에서)

    // ── 단련 창(시안 ③) — 창 좌표 ──
    private const float TpCardW = 640f, TpCardH = 560f, TpCardY = 200f, TpCardGap = 64f;
    private const float TpDiaW = 592f, TpDiaH = 226f;

    private static readonly Color FormInk    = new(0.79f, 0.65f, 1f, 1f);     // #C9A6FF — 형 · 스킬 단계 글자
    private static readonly Color TrackFill  = new(0.045f, 0.035f, 0.03f, 0.90f);
    private static readonly Color EnemyDot   = new(0.86f, 0.38f, 0.34f, 0.95f);

    private enum FormUi { None, Locked, Ready, Temper, Complete }
    private enum NodeState { Past, Current, Next, Future, Out }

    private sealed class TrackNode
    {
        public RectTransform root;
        public TMP_Text glyph, name, sub;
        public float x, w;
    }

    private sealed class TrackLink
    {
        public RectTransform root;
        public Image solid;
        public Image[] dash;
        public TMP_Text arrow;
    }

    private sealed class FormCard
    {
        public RectTransform root;
        public CanvasGroup group;
        public Image glow, icon;
        public TMP_Text name, line, foot, mark;
    }

    private sealed class TemperCard
    {
        public RectTransform root, chipBox, diagram;
        public CanvasGroup group;
        public UIThinFrame frame;
        public TMP_Text chip, index, line, caption, changes, cost;
    }

    private bool _compact, _formUiReady, _holdTrack;

    // 성장 길
    private RectTransform _track, _trackCur;
    private CanvasGroup   _trackGroup;
    private Image         _trackGlow;
    private TMP_Text      _next1, _next2;
    private readonly TrackNode[] _nodes = new TrackNode[GtNodes];
    private readonly TrackLink[] _links = new TrackLink[GtNodes - 1];
    private int   _trackCurIdx = -1, _trackMode = -1, _trackGen;
    private float _curBoxX, _curBoxW;

    // 형 칸
    private RectTransform _formBlock;
    private CanvasGroup   _formGroup;
    private TMP_Text      _formHead, _formChip, _formDesc, _formSummary;
    private RectTransform _formChipBox;
    private readonly FormCard[] _formCards = new FormCard[2];
    private FormUi _formState = FormUi.None;
    private int    _formGen;

    // 단련 창 — 9c 실측(장비 형/7)이 _temperPanel · _temperName · OnTemperPicked 이름을 읽는다
    private GameObject _temperPanel;
    private TMP_Text   _temperTitle, _temperSub, _temperPips, _temperNow, _temperHint, _temperConfirmLbl;
    private readonly TMP_Text[]   _temperName  = new TMP_Text[2];
    private readonly TemperCard[] _temperCards = new TemperCard[2];
    private readonly List<WeaponFormSO.VariantDef> _temperOptions = new(2);
    private readonly List<WeaponFormSO.VariantDef> _previewOptions = new(2);   // 성장 길 · 형 칸 미리 보기(단련 창 목록과 따로)
    private Button      _temperConfirm;
    private UIThinFrame _temperConfirmFrame;
    private int _temperPick = -1, _temperTierShown, _temperCostShown, _temperTierNow, _temperCostNow;

    // ── 줄인 판 높이(무대 · 정보창) — 줄이기 전엔 구운 값 ──
    private float StageHNow  => _compact ? CStageH : StagePanelH;
    private float InfoHNow   => _compact ? CInfoH : InfoH;
    private float StrikeYNow => _compact ? StrikeY + CStageDy + CMid : StrikeY;

    // ── 짓기 ──

    /// <summary>Bind 때 한 번 — 판을 줄여 성장 길 자리를 내고, 길 · 형 칸을 짓는다. 구운 화면 · 코드 빌드 화면 모두 같은 길.</summary>
    private void EnsureFormUi()
    {
        if (_formUiReady) return;
        var info = _infoGroup != null ? (RectTransform)_infoGroup.transform : FindDeep("InfoPanel") as RectTransform;
        if (_window == null || _stageArea == null || info == null) return;
        _formUiReady = true;
        CompactBody(info);
        BuildGrowthTrack();
        BuildFormBlock(info);
    }

    /// <summary>
    /// 무대 · 정보창을 62px 내린다. 자식은 비율 앵커라 그대로 두면 8% 눌린다 — 크기는 지키고 자리만 옮긴다(구운 px 사각형을 다시 앉힌다).
    /// 무대: 이름 −6 · 무기 칸 · 모루 · 망치 −24 · 공격 줄 −36 · 막대 −52. 원거리: 머리 −10 · 본문 −12. 정보창은 격자를 새로 잡는다.
    /// </summary>
    private void CompactBody(RectTransform info)
    {
        if (_compact) return;
        _compact = true;
        Place(_stageArea, StageX, StageY + BodyShift, StagePanelW, CStageH);
        Place(info, InfoX, InfoY + BodyShift, InfoW, CInfoH);
        if (_meleeStage != null)  ShiftChildren(_meleeStage, StagePanelW, StagePanelH, CStageH, MeleeDy);
        if (_rangedStage != null) ShiftChildren(_rangedStage, StagePanelW, StagePanelH, CStageH, n => CMid + (n is "RangedTitle" or "SlotSummary" ? -10f : -12f));
        ShiftChildren(_stageArea, StagePanelW, StagePanelH, CStageH, _ => CStageDy + CMid);   // 판정 도장(StageResult)
        if (_hammer != null)
        {
            var pv = new Vector2(HammerPivotX / StagePanelW, 1f - StrikeYNow / CStageH);
            _hammer.anchorMin = _hammer.anchorMax = pv;
        }

        InfoPlace(info, "InfoTitle", 0f, 14f, InfoW, 34f);
        InfoPlace(info, "RateLabel", 0f, 48f, InfoW, 24f);
        InfoPlace(info, "RowLineTop", RowPad, CRowY0, RowW, 1f);
        for (int i = 0; i < InfoRows; i++)
        {
            float y = CRowY0 + i * CRowH;
            InfoPlace(info, $"RowLabel{i}", RowPad, y, RowLabelW, CRowH);
            InfoPlace(info, $"Row{i}", RowPad + RowLabelW, y, RowW - RowLabelW, CRowH);
            InfoPlace(info, $"RowLine{i}", RowPad, y + CRowH - 1f, RowW, 1f);
        }
        float sy = CRowY0 + InfoRows * CRowH + 6f;
        InfoPlace(info, "Streak", RowPad, sy, RowW, 26f);
        InfoPlace(info, "EventEffect", RowPad, sy, RowW, 26f);
        if (_enhanceBtn != null) PlaceIn((RectTransform)_enhanceBtn.transform, (InfoW - 470f) * 0.5f, CEnhanceY, 470f, 128f, InfoW, CInfoH);
        if (_evolveBtn != null)
        {
            PlaceIn((RectTransform)_evolveBtn.transform, RowPad, FmBtnY, RowW, FmBtnH, InfoW, CInfoH);
            // 보라 띠에 얇은 테 — 형 계열 = 보라(U3)는 그대로, 테만 톤 v2
            UIThinBox.Create((RectTransform)_evolveBtn.transform, "ThinEdge", Vector2.zero, Vector2.one, 0f,
                             UITheme.Thin.WithA(FormInk, 0.55f), Color.clear, Color.clear);   // 색 · 글자는 RefreshEvolveButton · ApplyUxChrome(10-11)
        }
    }

    private static float MeleeDy(string n) => CMid + n switch
    {
        "WeaponName" or "JackpotBanner" => -6f,
        "TypeTier"                      => -8f,
        "AtkPlate" or "Atk"             => -36f,
        "Gauge" or "ForgeGlow"          => -52f,
        _                               => CStageDy,
    };

    /// <summary>비율 앵커로 앉은 직계 자식을 옛 부모 높이 기준 px 사각형으로 읽어, 새 높이에 dy만큼 옮겨 다시 앉힌다. 늘림 층(0~1)과 점 앵커는 건드리지 않는다.</summary>
    private static void ShiftChildren(RectTransform parent, float pw, float oldH, float newH, Func<string, float> dy)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            if (parent.GetChild(i) is not RectTransform rt) continue;
            Vector2 a = rt.anchorMin, b = rt.anchorMax;
            if (Mathf.Approximately(a.x, b.x) || Mathf.Approximately(a.y, b.y)) continue;   // 점 앵커(망치 등)
            if (a == Vector2.zero && b == Vector2.one) continue;                             // 늘림 층(잉크 · 불씨 · 불꽃)
            if (rt.sizeDelta.sqrMagnitude > 0.01f) continue;                                 // 비율 배치가 아니다
            float x = a.x * pw, w = (b.x - a.x) * pw;
            float top = (1f - b.y) * oldH, h = (b.y - a.y) * oldH;
            UIProportional.Place(rt, x, top + dy(rt.name), w, h, pw, newH);
        }
    }

    private static void InfoPlace(RectTransform info, string name, float x, float y, float w, float h)
    {
        if (info.Find(name) is RectTransform rt) PlaceIn(rt, x, y, w, h, InfoW, CInfoH);
    }

    private void BuildGrowthTrack()
    {
        var bg = ShopUIStyle.MakeImage(_window, "GrowthTrack", TrackFill);
        _track = bg.rectTransform;
        Place(_track, StageX, GtY, GtW, GtH);
        _track.SetSiblingIndex(_stageArea.GetSiblingIndex());   // 덮개 창(진화 · 단련 · 각인 · 고르기)보다 아래
        _trackGroup = bg.gameObject.AddComponent<CanvasGroup>();
        UIThinBox.Create(_track, "Line", Vector2.zero, Vector2.one, 0f,
                         UITheme.Thin.WithA(UITheme.Thin.Gold, 0.28f), UITheme.Thin.WithA(UITheme.Thin.Gold, 0.65f), Color.clear);

        var label = ThinText(_track, "TrackLabel", 15f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.55f));
        label.text = "성장 길";
        PlaceIn(label.rectTransform, 18f, 0f, 70f, GtH, GtW, GtH);

        _trackGlow = ShopUIStyle.MakeImage(_track, "CurGlow", UITheme.Thin.WithA(UITheme.Thin.Gold, 0.16f));
        _trackGlow.sprite = UI_RuneSelectPopup.SoftDot;
        _trackCur = ShopUIStyle.MakeRect(_track, "TrackCurrent").GetComponent<RectTransform>();
        UIThinBox.Create(_trackCur, "Box", Vector2.zero, Vector2.one, 0f, UITheme.Thin.LineActive,
                         Color.clear, UITheme.Thin.WithA(UITheme.Thin.Gold, 0.07f));

        for (int i = 0; i < GtNodes - 1; i++)
        {
            var l = new TrackLink { root = ShopUIStyle.MakeRect(_track, $"Link{i}").GetComponent<RectTransform>() };
            l.solid = ShopUIStyle.MakeImage(l.root, "Solid", Color.white);
            ShopUIStyle.Stretch(l.solid.rectTransform);
            l.dash = new Image[6];
            for (int k = 0; k < l.dash.Length; k++)
            {
                l.dash[k] = ShopUIStyle.MakeImage(l.root, "Dash", Color.white);
                var rt = l.dash[k].rectTransform;
                rt.anchorMin = new Vector2(k / 6f, 0f);
                rt.anchorMax = new Vector2(k / 6f + 0.09f, 1f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            l.arrow = ThinText(l.root, "Arrow", 13f, UIWeight.Regular, TextAlignmentOptions.MidlineRight, UITheme.Thin.InkA(0.6f));
            l.arrow.text = "▶";
            var art = l.arrow.rectTransform;
            art.anchorMin = art.anchorMax = new Vector2(1f, 0.5f);
            art.pivot = new Vector2(1f, 0.5f);
            art.sizeDelta = new Vector2(14f, 16f);
            art.anchoredPosition = new Vector2(3f, 0f);
            _links[i] = l;
        }

        for (int i = 0; i < GtNodes; i++)
        {
            var n = new TrackNode { root = ShopUIStyle.MakeRect(_track, $"Node{i}").GetComponent<RectTransform>() };
            n.glyph = ThinText(n.root, "Glyph", 15f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
            n.name  = ThinText(n.root, "Name", 17f, UIWeight.Medium, TextAlignmentOptions.MidlineLeft, ShopUIStyle.TextPrimary);
            n.sub   = ThinText(n.root, "Sub", 15f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.6f));
            foreach (var t in new[] { n.glyph, n.name }) { t.textWrappingMode = TextWrappingModes.NoWrap; t.enableAutoSizing = false; }
            n.sub.enableAutoSizing = false;
            _nodes[i] = n;
        }

        var div = ShopUIStyle.MakeImage(_track, "NextDivider", UITheme.Thin.InkA(0.18f));
        PlaceIn(div.rectTransform, GtNextX - 16f, 10f, 1f, GtH - 20f, GtW, GtH);
        var head = ThinText(_track, "NextHead", 15f, UIWeight.Medium, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.6f));
        head.text = "다음";
        PlaceIn(head.rectTransform, GtNextX, 4f, 46f, 28f, GtW, GtH);
        _next1 = ThinText(_track, "Next1", 17f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, ShopUIStyle.TextPrimary);
        _next1.enableAutoSizing = true;
        _next1.fontSizeMax = 17f;
        _next1.fontSizeMin = 15f;
        _next1.textWrappingMode = TextWrappingModes.NoWrap;
        _next1.overflowMode = TextOverflowModes.Ellipsis;   // 길 칸 밖(창 바깥)으로 새지 않게
        _next1.richText = true;
        PlaceIn(_next1.rectTransform, GtNextX + 50f, 4f, GtW - GtNextX - 64f, 28f, GtW, GtH);
        _next2 = ThinText(_track, "Next2", 15f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.62f));
        _next2.enableAutoSizing = false;   // 15 아래로 줄이지 않는다(10-11) — 넘치면 말줄임
        _next2.overflowMode = TextOverflowModes.Ellipsis;
        PlaceIn(_next2.rectTransform, GtNextX, 31f, GtW - GtNextX - 14f, 24f, GtW, GtH);
    }

    private void BuildFormBlock(RectTransform info)
    {
        _formBlock = ShopUIStyle.MakeRect(info, "FormBlock").GetComponent<RectTransform>();
        PlaceIn(_formBlock, 0f, FmY, InfoW, CInfoH - FmY, InfoW, CInfoH);
        _formGroup = _formBlock.gameObject.AddComponent<CanvasGroup>();
        _formGroup.blocksRaycasts = false;   // 미리 보기 칸 — 누르는 곳은 아래 보라 버튼

        var rule = ShopUIStyle.MakeImage(_formBlock, "FormRule", UITheme.Thin.WithA(UITheme.Thin.Gold, 0.35f));
        FmPlace(rule.rectTransform, RowPad, FmY, RowW, 1f);

        _formHead = ThinText(_formBlock, "FormHead", 20f, UIWeight.Medium, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
        _formHead.textWrappingMode = TextWrappingModes.NoWrap;
        _formHead.richText = true;
        FmPlace(_formHead.rectTransform, RowPad, FmY + 8f, RowW - 150f, 30f);

        _formChipBox = ShopUIStyle.MakeRect(_formBlock, "FormChip").GetComponent<RectTransform>();
        _formChip = ThinText(_formChipBox, "Text", 15f, UIWeight.Medium, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.75f));
        ShopUIStyle.Stretch(_formChip.rectTransform);
        UIThinBox.Create(_formChipBox, "Box", Vector2.zero, Vector2.one, 0f, UITheme.Thin.InkA(0.3f), Color.clear,
                         UITheme.Thin.InkA(0.04f));

        _formDesc = ThinText(_formBlock, "FormDesc", 15f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.62f));
        _formDesc.overflowMode = TextOverflowModes.Ellipsis;
        FmPlace(_formDesc.rectTransform, RowPad, FmY + 38f, RowW, 22f);

        _formSummary = ThinText(_formBlock, "FormSummary", 15f, UIWeight.Regular, TextAlignmentOptions.TopLeft, UITheme.Thin.InkA(0.85f));
        _formSummary.textWrappingMode = TextWrappingModes.Normal;
        _formSummary.richText = true;
        FmPlace(_formSummary.rectTransform, RowPad, FmY + 44f, RowW, 150f);

        for (int k = 0; k < 2; k++)
        {
            var c = new FormCard { root = ShopUIStyle.MakeRect(_formBlock, k == 0 ? "FormCardA" : "FormCardB").GetComponent<RectTransform>() };
            c.group = c.root.gameObject.AddComponent<CanvasGroup>();
            UIThinBox.Create(c.root, "Box", Vector2.zero, Vector2.one, 0f, UITheme.Thin.InkA(0.22f),
                             Color.clear, new Color(1f, 1f, 1f, 0.03f));   // 미리 보기 칸 — 금 눈금은 누를 것에만(10-11)
            c.glow = ShopUIStyle.MakeImage(c.root, "Glow", Color.clear);
            c.glow.sprite = UI_RuneSelectPopup.SoftDot;
            c.icon = ShopUIStyle.MakeImage(c.root, "Icon", Color.white);
            c.icon.preserveAspect = true;
            c.name = ThinText(c.root, "Name", 17f, UIWeight.Medium, TextAlignmentOptions.MidlineLeft, ShopUIStyle.TextPrimary);
            c.name.richText = true;
            c.line = ThinText(c.root, "Line", 15f, UIWeight.Regular, TextAlignmentOptions.TopLeft, UITheme.Thin.InkA(0.78f));
            c.line.textWrappingMode = TextWrappingModes.Normal;
            c.line.overflowMode = TextOverflowModes.Ellipsis;   // 15로 키운 뒤 칸을 넘으면 말줄임(아래 칸을 침범하지 않게)
            c.foot = ThinText(c.root, "Foot", 15f, UIWeight.Medium, TextAlignmentOptions.MidlineLeft, MuteInk);
            c.mark = ThinText(c.root, "Mark", 15f, UIWeight.Regular, TextAlignmentOptions.MidlineRight, UITheme.Thin.InkA(0.45f));
            _formCards[k] = c;
        }
        _formBlock.gameObject.SetActive(false);
    }

    /// <summary>형 칸 안 배치 — 정보창 좌표(y)로 받아 칸 기준 비율로 앉힌다.</summary>
    private void FmPlace(RectTransform rt, float x, float y, float w, float h)
        => PlaceIn(rt, x, y - FmY, w, h, InfoW, CInfoH - FmY);

    /// <summary>톤 v2 글자(Noto · 굵게 없음) — 줄 높이가 DNF보다 커서(≈1.45em) 칸은 글자 크기의 1.5배 이상으로 잡는다.</summary>
    private static TMP_Text ThinText(Transform parent, string name, float size, UIWeight weight, TextAlignmentOptions align, Color color)
    {
        var t = ShopUIStyle.MakeText(parent, name, size, FontStyles.Normal, align, color);
        UITheme.StyleThinText(t, weight, size, color);
        t.enableAutoSizing = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    // ── 갱신 ──

    private void RefreshFormUi()
    {
        if (!_formUiReady || _controller == null) return;
        RefreshGrowthTrack();
        RefreshFormBlock();
    }

    /// <summary>근접 칸 무기의 진화 갈래 중 가장 이른 강화 조건(D6 +5). 갈래가 없으면 −1.</summary>
    private int EvolveAt()
    {
        int at = int.MaxValue;
        foreach (var b in CurrentBranches) if (b != null && b.IsValid) at = Mathf.Min(at, b.requiredEnhanceLevel);
        return at == int.MaxValue ? -1 : at;
    }

    private (WeaponFormSO a, WeaponFormSO b) BranchForms()
    {
        var br = CurrentBranches;
        WeaponFormSO A = br.Count > 0 && br[0]?.target != null ? br[0].target.Form : null;
        WeaponFormSO B = br.Count > 1 && br[1]?.target != null ? br[1].target.Form : null;
        return (A, B);
    }

    private static string VariantName(WeaponData w, int tier)
    {
        if (w?.form == null || string.IsNullOrEmpty(w.formVariants)) return null;
        foreach (var id in w.formVariants.Split(','))
            if (w.form.TryGetVariant(id, out var v) && v.tier == tier) return v.displayName;
        return null;
    }

    private void RefreshGrowthTrack()
    {
        if (_track == null || _holdTrack) return;
        bool ranged = _activeTab == 1;
        int count = ranged ? 3 : GtNodes;
        var states = new NodeState[GtNodes];
        int cur = ranged ? FillRangedTrack(states) : FillMeleeTrack(states);
        int mode = ranged ? 1 : 0;
        LayoutTrack(count, cur, states, animate: mode == _trackMode && _trackCurIdx >= 0 && cur != _trackCurIdx);
        _trackMode = mode;
        _trackCurIdx = cur;
    }

    /// <summary>근접 길 다섯 칸 + 「다음」. 돌려주는 값 = 지금 칸.</summary>
    private int FillMeleeTrack(NodeState[] st)
    {
        var w = _controller.GetSlot(PlayerWeaponManager.Slot0);
        if (w == null)
        {
            SetNode(0, "근접 무기 없음", "", NodeState.Current);
            for (int i = 1; i < GtNodes; i++) SetNode(i, "", "", NodeState.Future);
            _next1.text = _next2.text = "";
            return 0;
        }

        bool nameless = CurrentBranches.Count > 0;
        int  req      = Mathf.Max(0, EvolveAt());
        var (fa, fb)  = BranchForms();
        var form      = w.HasForm ? w.form : fa;
        int  tier     = w.HasForm ? w.formTier : 0;
        bool legend   = !string.IsNullOrEmpty(w.legendId);
        int  cur      = nameless ? 0 : legend ? 4 : Mathf.Clamp(tier, 1, 3);
        string fName  = w.HasForm ? w.form.DisplayName : "";
        string v2 = VariantName(w, 2), v3 = VariantName(w, 3);
        int c2 = form != null ? form.TemperCost(2) : 0, c3 = form != null ? form.TemperCost(3) : 0;
        bool canPromote = _controller.CanPromote(PlayerWeaponManager.Slot0);

        for (int i = 0; i < GtNodes; i++)
            st[i] = i < cur ? NodeState.Past : i == cur ? NodeState.Current
                  : i == 4 && !legend && !canPromote ? NodeState.Out
                  : i == cur + 1 ? NodeState.Next : NodeState.Future;

        SetNode(0, "무형검", cur == 0 ? $"지금 · 재련 {Reforges(w.enhanceLevel)}회" : NamelessTitle, st[0]);
        SetNode(1, req > 0 ? $"재련 {Reforges(req)}회 형" : "형 깨어남",
                nameless ? $"{fa?.DisplayName ?? "?"} / {fb?.DisplayName ?? "?"}" : cur == 1 ? $"지금 · {fName} Ⅰ" : fName, st[1]);
        SetNode(2, "단련 Ⅱ", v2 == null ? $"강화재료 {c2}" : cur == 2 ? $"지금 · {v2}" : v2, st[2]);
        SetNode(3, "단련 Ⅲ", v3 == null ? $"강화재료 {c3}" : cur == 3 ? $"지금 · {v3}" : v3, st[3]);
        SetNode(4, "전설", legend ? $"지금 · {LegendName(w.legendId)}" : canPromote ? "승급할 수 있다" : "최대 재련 뒤", st[4]);

        FillMeleeNext(w, nameless, req, fa, fb);
        return cur;
    }

    /// <summary>
    /// 「다음」 — 지금 할 수 있는 것(진화 · 형 단련)이 먼저, 그다음은 강화 단계가 여는 것(형 · 스킬 단계 · 마스터리)을 가까운 순서로.
    /// 숫자만이 아니라 무엇이 바뀌는지를 쓴다(시안 ①: 「풍절이 두 갈래로, 또는 되돌아와 두 번」).
    /// </summary>
    private void FillMeleeNext(WeaponData w, bool nameless, int req, WeaponFormSO fa, WeaponFormSO fb)
    {
        int lv = w.enhanceLevel, max = _controller.MaxAt(PlayerWeaponManager.Slot0);
        var ahead = new List<(int lv, string text, string brief)>(4);
        string pair = $"{fa?.DisplayName ?? "?"} 또는 {fb?.DisplayName ?? "?"}";
        if (nameless && lv < req) ahead.Add((req, $"재련 {Reforges(req)}회에서 형이 깨어난다 — {pair}", $"재련 {Reforges(req)}회에서 형이 깨어난다 — {pair}"));
        var tiers = SkillTierResolver.Milestones(w);
        for (int i = 0; i < tiers.Length; i++)
        {
            int t = tiers[i];
            if (t <= lv || t > max) continue;
            string body = TierChangeBody(w, i + 2);
            ahead.Add((t, body != null ? $"재련 {Reforges(t)}회 스킬 {i + 2}단계 — {body}" : $"재련 {Reforges(t)}회 스킬 {i + 2}단계", $"재련 {Reforges(t)}회 스킬 {i + 2}단계"));
        }
        var table = _controller.Table;
        if (!nameless && table != null)
        {
            int cap = WeaponEnhanceService.BaseEnhanceCap(w, table);
            if (lv <= cap && cap + 1 <= max)
                ahead.Add((cap + 1, $"재련 {Reforges(cap + 1)}회 마스터리 1단계 — {MasteryDelta(w, 0, 1)}",
                           $"재련 {Reforges(cap + 1)}회 마스터리 1단계"));
        }
        ahead.Sort((x, y) => x.lv.CompareTo(y.lv));
        string Then(int i) => i < ahead.Count ? $"그다음 {ahead[i].brief}" : "";

        if (nameless && _controller.CanEvolve(PlayerWeaponManager.Slot0))
        {
            _next1.text = $"형을 고른다 — {pair}";
            _next2.text = ahead.Count > 0 ? Then(0) : $"진화 비용 없음 · 재련 {Reforges(lv)}회와 증강 · 각인은 그대로 이어진다";
        }
        else if (!nameless && _controller.CanTemperForm(PlayerWeaponManager.Slot0, out int tier, out int cost))
        {
            w.form.GetVariants(tier, _previewOptions);
            _next1.text = _previewOptions.Count >= 2
                ? $"단련 {RomanTier(tier)} — {_previewOptions[0].displayName}<size=88%><color=#C9A6FF>({_previewOptions[0].traitTag})</color></size> 또는 {_previewOptions[1].displayName}<size=88%><color=#C9A6FF>({_previewOptions[1].traitTag})</color></size>"
                : $"단련 {RomanTier(tier)}";
            string costText = _controller.FuelAmount >= cost ? cost.ToString() : $"<color=#FF7A6A>{cost}</color>";
            _next2.text = $"형 단련 {RomanTier(tier)} · 확정 성공 · 강화재료 {costText}" + (ahead.Count > 0 ? $"  ·  {Then(0)}" : "");
        }
        else if (ahead.Count > 0)
        {
            _next1.text = ahead[0].text;
            _next2.text = ahead.Count > 1 ? Then(1)
                        : nameless ? $"재련 {Reforges(lv)}회 → {Reforges(req)}회 · {Mathf.Max(0, Reforges(req) - Reforges(lv))}번 더 재련하면 하나를 고른다" : "";
            if (nameless && ahead[0].lv == req)
                _next2.text = $"재련 {Reforges(lv)}회 → {Reforges(req)}회 · {Mathf.Max(0, Reforges(req) - Reforges(lv))}번 더 재련하면 하나를 고른다" + (ahead.Count > 1 ? $"  ·  {Then(1)}" : "");
        }
        else
        {
            _next1.text = "남은 길은 재련 — 수치를 올린다";
            string mastery = MasteryText(w);
            _next2.text = !string.IsNullOrEmpty(mastery) ? mastery : lv >= max ? $"최대 재련 {Reforges(max)}회" : $"최대 재련 {Reforges(max)}회까지 {Reforges(max) - Reforges(lv)}번";
        }
    }

    /// <summary>원거리 길 — 스킬 단계 = 파츠 레벨 합(기본 2 · 10). 돌려주는 값 = 지금 칸.</summary>
    private int FillRangedTrack(NodeState[] st)
    {
        var rw = _controller.GetSlot(PlayerWeaponManager.Slot1);
        for (int i = 3; i < GtNodes; i++) SetNode(i, "", "", NodeState.Future);
        if (rw == null || !SkillTierResolver.IsRanged(rw))
        {
            SetNode(0, "원거리 무기 없음", "", NodeState.Current);
            SetNode(1, "", "", NodeState.Future);
            SetNode(2, "", "", NodeState.Future);
            _next1.text = _next2.text = "";
            return 0;
        }

        var ms    = SkillTierResolver.Milestones(rw);
        var state = RangedPartsState.Current;
        int total = state?.TotalLevel ?? 0;
        int cur   = Mathf.Clamp(SkillTierResolver.Resolve(rw) - 1, 0, 2);
        for (int i = 0; i < 3; i++) st[i] = i < cur ? NodeState.Past : i == cur ? NodeState.Current : i == cur + 1 ? NodeState.Next : NodeState.Future;
        SetNode(0, "스킬 1단계", cur == 0 ? $"지금 · 파츠 합 {total}" : "시작", st[0]);
        SetNode(1, "스킬 2단계", cur == 1 ? $"지금 · 파츠 합 {total}" : ms.Length > 0 ? $"파츠 합 {ms[0]}" : "", st[1]);
        SetNode(2, "스킬 3단계", cur == 2 ? $"지금 · 파츠 합 {total}" : ms.Length > 1 ? $"파츠 합 {ms[1]}" : "", st[2]);

        if (SkillTierResolver.TryGetNext(rw, 0, out int nt, out int rem))
        {
            string body = TierChangeBody(rw, nt);
            _next1.text = $"파츠 합 +{rem} — 스킬 {nt}단계" + (body != null ? $" · {body}" : "");
        }
        else _next1.text = "스킬 3단계 — 남은 길은 파츠 강화";
        _next2.text = $"지금 파츠 합 {total} · 켠 파츠 {state?.ActiveCount ?? 0} · 실패 없음";
        return cur;
    }

    private void SetNode(int i, string name, string sub, NodeState s)
    {
        var n = _nodes[i];
        n.name.text  = name;
        n.sub.text   = sub;
        n.glyph.text = s is NodeState.Past or NodeState.Current ? "◆" : "◇";
        // 금은 「누를 것」에만(10-11) — 길은 잉크 단계로 읽고, 지금 칸은 테(금 55%)가 말한다
        n.glyph.color = s switch
        {
            NodeState.Past or NodeState.Current => UITheme.Thin.InkA(0.85f),
            NodeState.Next   => UITheme.Thin.InkA(0.7f),
            NodeState.Future => UITheme.Thin.InkA(0.45f),
            _ => UITheme.Thin.InkA(0.28f),
        };
        n.name.color = s switch
        {
            NodeState.Current => UITheme.Thin.Ink,
            NodeState.Next    => UITheme.Thin.InkA(0.85f),
            NodeState.Past    => UITheme.Thin.InkA(0.85f),
            NodeState.Future  => UITheme.Thin.InkA(0.55f),
            _                 => UITheme.Thin.InkA(0.32f),
        };
        n.sub.color = s switch
        {
            NodeState.Current => UITheme.Thin.Ink,
            NodeState.Next    => UITheme.Thin.InkA(0.75f),
            NodeState.Past    => UITheme.Thin.InkA(0.6f),
            NodeState.Future  => UITheme.Thin.InkA(0.42f),
            _                 => UITheme.Thin.InkA(0.3f),
        };
    }

    /// <summary>칸 폭 = 글자 폭, 남는 폭은 잇는 선이 나눠 쓴다. 지금 칸 테는 단계가 오르면 0.35초에 다음 칸으로 미끄러진다.</summary>
    private void LayoutTrack(int count, int cur, NodeState[] st, bool animate)
    {
        float total = 0f;
        for (int i = 0; i < count; i++)
        {
            var n = _nodes[i];
            float tw = Mathf.Max(n.name.GetPreferredValues(n.name.text).x, n.sub.GetPreferredValues(n.sub.text).x);
            n.w = 22f + Mathf.Max(40f, tw) + 4f;
            total += n.w;
        }
        float gap = count > 1 ? Mathf.Max(GtMinGap, (GtNodesR - GtNodesX - total) / (count - 1)) : 0f;
        float x = GtNodesX;
        for (int i = 0; i < GtNodes; i++)
        {
            var n = _nodes[i];
            bool on = i < count;
            n.root.gameObject.SetActive(on);
            if (!on) continue;
            n.x = x;
            PlaceIn(n.root, x, 0f, n.w, GtH, GtW, GtH);
            PlaceIn(n.glyph.rectTransform, 0f, 5f, 20f, 26f, n.w, GtH);
            PlaceIn(n.name.rectTransform, 22f, 5f, n.w - 22f, 26f, n.w, GtH);
            PlaceIn(n.sub.rectTransform, 22f, 31f, n.w - 22f, 22f, n.w, GtH);
            x += n.w + gap;
        }
        for (int i = 0; i < _links.Length; i++)
        {
            var l = _links[i];
            bool on = i < count - 1;
            l.root.gameObject.SetActive(on);
            if (!on) continue;
            var a = _nodes[i]; var b = _nodes[i + 1];
            float x0 = a.x + a.w + 6f, x1 = b.x - 8f;
            PlaceIn(l.root, x0, 18f, Mathf.Max(4f, x1 - x0), UITheme.Thin.Stroke, GtW, GtH);
            var to = st[i + 1];
            bool dashed = to == NodeState.Out;
            var c = to is NodeState.Past or NodeState.Current ? UITheme.Thin.InkA(0.45f)
                  : to == NodeState.Next ? UITheme.Thin.InkA(0.32f)
                  : dashed ? UITheme.Thin.InkA(0.25f) : UITheme.Thin.InkA(0.16f);
            l.solid.enabled = !dashed;
            l.solid.color = c;
            foreach (var d in l.dash) { d.enabled = dashed; d.color = c; }
            l.arrow.gameObject.SetActive(to == NodeState.Next);
        }

        bool show = cur >= 0 && cur < count && !string.IsNullOrEmpty(_nodes[cur].name.text);
        _trackCur.gameObject.SetActive(show);
        _trackGlow.gameObject.SetActive(show);
        if (!show) return;
        float toX = _nodes[cur].x - 9f, toW = _nodes[cur].w + 16f;
        _trackGen++;
        if (animate && isActiveAndEnabled) SlideTrackBoxAsync(_trackGen, _curBoxX, _curBoxW, toX, toW).Forget();
        else PlaceTrackBox(toX, toW, 0f);
    }

    private void PlaceTrackBox(float x, float w, float flash)
    {
        _curBoxX = x;
        _curBoxW = w;
        PlaceIn(_trackCur, x, 3f, w, GtH - 6f, GtW, GtH);
        PlaceIn(_trackGlow.rectTransform, x - 24f, -16f, w + 48f, GtH + 32f, GtW, GtH);
        _trackGlow.color = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.30f * flash);   // 단계가 오르는 순간에만 — 평소 후광은 한 화면 한 곳(10-11)
    }

    /// <summary>단계가 오르는 순간 — 지금 칸 테가 다음 칸으로 미끄러지고(0.35초 감속), 닿으면 빛이 한 번 부풀었다 가라앉는다.</summary>
    private async UniTaskVoid SlideTrackBoxAsync(int gen, float fromX, float fromW, float toX, float toW)
    {
        var ct = destroyCancellationToken;
        try
        {
            await UIMotion.Tween(UIMotion.Settle, k =>
            {
                if (gen != _trackGen) return;
                PlaceTrackBox(Mathf.Lerp(fromX, toX, k), Mathf.Lerp(fromW, toW, k), k);
            }, ct);
            await UIMotion.Tween(UIMotion.Settle, k =>
            {
                if (gen != _trackGen) return;
                PlaceTrackBox(toX, toW, 1f - k);
            }, ct);
        }
        catch (OperationCanceledException) { }
    }

    // ── 형 칸 ──

    private FormUi CurrentFormUi(WeaponData w)
    {
        if (w == null || _activeTab != 0) return FormUi.None;
        if (CurrentBranches.Count > 0) return _controller.CanEvolve(PlayerWeaponManager.Slot0) ? FormUi.Ready : FormUi.Locked;
        if (!w.HasForm) return FormUi.None;
        return _controller.CanTemperForm(PlayerWeaponManager.Slot0, out _, out _) ? FormUi.Temper : FormUi.Complete;
    }

    private void RefreshFormBlock()
    {
        if (_formBlock == null) return;
        var w  = _controller.GetSlot(PlayerWeaponManager.Slot0);
        var st = CurrentFormUi(w);
        _formBlock.gameObject.SetActive(st != FormUi.None);
        if (st == FormUi.None) { _formState = st; return; }

        switch (st)
        {
            case FormUi.Locked:   FillLocked(w);   break;
            case FormUi.Ready:    FillReady(w);    break;
            case FormUi.Temper:   FillTemper(w);   break;
            case FormUi.Complete: FillComplete(w); break;
        }
        // 상태가 바뀌는 순간(잠김 → 깨어남 → 단련 → 완성) — 칸이 0.18초에 다시 차오른다
        if (st != _formState && _formState != FormUi.None && isActiveAndEnabled) FadeFormAsync(++_formGen).Forget();
        _formState = st;
    }

    private async UniTaskVoid FadeFormAsync(int gen)
    {
        try
        {
            await UIMotion.Tween(UIMotion.Swap, k => { if (gen == _formGen) _formGroup.alpha = k; }, destroyCancellationToken);
        }
        catch (OperationCanceledException) { }
    }

    private void SetFormHead(string head, string sub, string chip, Color chipColor, bool ready = false)
    {
        _formHead.text = string.IsNullOrEmpty(sub) ? head : $"{head}  <size=85%><color=#9A98A0>{sub}</color></size>";
        _formHead.color = ready ? UITheme.Thin.Gold : UITheme.Thin.Ink;   // 금 = 지금 할 수 있다(10-11)
        bool chipOn = !string.IsNullOrEmpty(chip);
        _formChipBox.gameObject.SetActive(chipOn);
        if (!chipOn) return;
        _formChip.text  = chip;
        _formChip.color = chipColor;
        foreach (var img in _formChipBox.GetComponentsInChildren<Image>(true))
            img.color = img.name == "Fill" ? UITheme.Thin.WithA(chipColor, 0.04f) : UITheme.Thin.WithA(chipColor, 0.4f);
        float cw = _formChip.GetPreferredValues(chip).x + 22f;
        FmPlace(_formChipBox, RowPad + RowW - cw, FmY + 11f, cw, 24f);
    }

    /// <summary>무형검(+5 전) — 잠긴 형 두 장: 무기 실루엣 · 형 이름(형 색을 낮춘 톤) · 무기 이름 · 한 줄 · 「+5에서 깨어난다」(U8 이름은 보여 줌).</summary>
    private void FillLocked(WeaponData w)
    {
        int req = Mathf.Max(0, EvolveAt());
        SetFormHead("잠긴 형", "둘 중 하나가 깨어난다", $"재련 {Reforges(req)}회에서 깨어남", UITheme.Thin.InkA(0.7f));
        _formDesc.gameObject.SetActive(false);
        _formSummary.gameObject.SetActive(false);
        var br = CurrentBranches;
        for (int k = 0; k < 2; k++)
        {
            var c = _formCards[k];
            var b = k < br.Count ? br[k] : null;
            bool on = b != null && b.IsValid && b.target.Form != null;
            c.root.gameObject.SetActive(on);
            if (!on) continue;
            var f = b.target.Form;
            FmPlace(c.root, RowPad + k * (FmCardW + FmCardGap), FmY + 42f, FmCardW, 162f);
            c.group.alpha = 1f;
            CardArt(c, b.target.icon, f.Tint, lit: false, 12f, 10f, FmCardW - 24f, 62f, 162f);
            c.name.text = $"<color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(f.Tint, Color.gray, 0.35f))}>{f.DisplayName}</color>  <size=88%><color=#8C8497>{b.target.displayName}</color></size>";
            PlaceIn(c.name.rectTransform, 12f, 72f, FmCardW - 24f, 26f, FmCardW, 162f);
            c.line.gameObject.SetActive(true);
            c.line.text = UIKeywordInk.Values(f.Line);   // 수치만 Noto 500(10-15 설명 글 강조)
            PlaceIn(c.line.rectTransform, 12f, 98f, FmCardW - 24f, 44f, FmCardW, 162f);
            c.foot.gameObject.SetActive(true);
            c.foot.text = $"재련 {Reforges(b.requiredEnhanceLevel)}회에서 깨어난다";
            PlaceIn(c.foot.rectTransform, 12f, 140f, FmCardW - 24f, 22f, FmCardW, 162f);
            c.mark.gameObject.SetActive(true);
            c.mark.text = "잠김";
            PlaceIn(c.mark.rectTransform, FmCardW - 70f, 6f, 60f, 22f, FmCardW, 162f);
        }
    }

    /// <summary>+5 — 형이 깨어난다: 두 형이 밝아지고 아래에 보라 [◆ 진화 — 형을 고른다].</summary>
    private void FillReady(WeaponData w)
    {
        SetFormHead("형이 깨어난다", "둘 중 하나를 고른다", "진화 비용 없음", UITheme.Thin.InkA(0.75f), ready: true);
        _formDesc.gameObject.SetActive(true);
        _formDesc.text = $"되돌릴 수 없다 · 재련 {Reforges(w.enhanceLevel)}회와 증강 · 각인은 그대로 이어진다";
        _formSummary.gameObject.SetActive(false);
        var br = CurrentBranches;
        for (int k = 0; k < 2; k++)
        {
            var c = _formCards[k];
            var b = k < br.Count ? br[k] : null;
            bool on = b != null && b.IsValid && b.target.Form != null;
            c.root.gameObject.SetActive(on);
            if (!on) continue;
            var f = b.target.Form;
            FmPlace(c.root, RowPad + k * (FmCardW + FmCardGap), FmY + 64f, FmCardW, FmPreviewH);
            c.group.alpha = 1f;
            CardArt(c, b.target.icon, f.Tint, lit: true, 8f, (FmPreviewH - 56f) * 0.5f, 56f, 56f, FmPreviewH);
            c.name.text = $"<color=#{ColorUtility.ToHtmlStringRGB(f.Tint)}>{f.DisplayName}</color>  <size=88%><color=#8C8497>{b.target.displayName}</color></size>";
            PlaceIn(c.name.rectTransform, 72f, 8f, FmCardW - 80f, 26f, FmCardW, FmPreviewH);
            c.line.gameObject.SetActive(true);
            c.line.text = UIKeywordInk.Values(f.Line);   // 수치만 Noto 500(10-15 설명 글 강조)
            PlaceIn(c.line.rectTransform, 72f, 36f, FmCardW - 80f, 66f, FmCardW, FmPreviewH);
            c.foot.gameObject.SetActive(false);
            c.mark.gameObject.SetActive(false);
        }
    }

    /// <summary>형 Ⅰ · Ⅱ — 「형 단련 칼바람 Ⅱ」 + 확정 칩 · 한 줄 · 변주 두 칸 미리 보기 · 아래 보라 버튼(RefreshEvolveButton).</summary>
    private void FillTemper(WeaponData w)
    {
        _controller.CanTemperForm(PlayerWeaponManager.Slot0, out int tier, out int cost);
        SetFormHead("형 단련", $"{w.form.DisplayName} {RomanTier(tier)}", "확정 · 하락 없음", UITheme.Thin.InkA(0.75f),
                    ready: _controller.FuelAmount >= cost);
        _formDesc.gameObject.SetActive(true);
        _formDesc.text = "강화는 수치(확률), 단련은 기능(확정) — 변주 하나를 고른다";
        _formSummary.gameObject.SetActive(false);
        w.form.GetVariants(tier, _previewOptions);
        for (int k = 0; k < 2; k++)
        {
            var c = _formCards[k];
            bool on = k < _previewOptions.Count;
            c.root.gameObject.SetActive(on);
            if (!on) continue;
            var v = _previewOptions[k];
            const float h = FmPreviewH;
            FmPlace(c.root, RowPad + k * (FmCardW + FmCardGap), FmY + 64f, FmCardW, h);
            c.group.alpha = 1f;
            CardArt(c, null, Color.clear, lit: false, 0f, 0f, 0f, 0f, h);
            c.name.text = $"◇ {v.displayName}";
            PlaceIn(c.name.rectTransform, 10f, 8f, FmCardW - 20f, 26f, FmCardW, h);
            c.mark.gameObject.SetActive(true);
            c.mark.text  = v.traitTag;
            c.mark.color = FormInk;
            PlaceIn(c.mark.rectTransform, FmCardW - 120f, 8f, 110f, 26f, FmCardW, h);
            c.line.gameObject.SetActive(true);
            c.line.text = UIKeywordInk.Values(v.line);   // 수치만 Noto 500(10-15 설명 글 강조)
            PlaceIn(c.line.rectTransform, 10f, 38f, FmCardW - 20f, 64f, FmCardW, h);
            c.foot.gameObject.SetActive(false);
        }
    }

    /// <summary>Ⅲ까지 벼렸다 — 형 완성 요약(고른 변주가 이 런의 형 기록). 버튼은 승급이 열릴 때만.</summary>
    private void FillComplete(WeaponData w)
    {
        string v2 = VariantName(w, 2), v3 = VariantName(w, 3);
        SetFormHead("형 완성", $"{w.form.DisplayName} · {v2 ?? "—"} · {v3 ?? "—"}", null, Color.clear);
        _formDesc.gameObject.SetActive(false);
        foreach (var c in _formCards) c.root.gameObject.SetActive(false);
        _formSummary.gameObject.SetActive(true);
        var sb = new System.Text.StringBuilder();
        foreach (var id in (w.formVariants ?? "").Split(','))
            if (w.form.TryGetVariant(id, out var v))
                sb.Append("<color=#C9A6FF>").Append(RomanTier(v.tier)).Append("  ").Append(v.displayName).Append("</color> — ").Append(UIKeywordInk.Values(v.line)).Append('\n');   // 수치만 Noto 500(10-15)
        sb.Append("<color=#9A93A6>남은 길은 강화 — 수치를 올린다</color>");
        _formSummary.text = sb.ToString();
    }

    /// <summary>카드 그림 — 잠김이면 흐린 회색 톤 + 옅은 형 색 빛, 깨어나면 원래 색. 아이콘이 바탕 칸을 가진 그림이라 검은 실루엣은 검은 네모가 됐다(CR_1).</summary>
    private static void CardArt(FormCard c, Sprite icon, Color tint, bool lit, float x, float y, float w, float h, float ph)
    {
        bool on = icon != null && w > 0f;
        c.icon.gameObject.SetActive(on);
        c.glow.gameObject.SetActive(on);
        if (!on) return;
        c.icon.sprite = icon;
        c.icon.color  = lit ? Color.white : new Color(0.42f, 0.42f, 0.47f, 0.85f);
        PlaceIn(c.icon.rectTransform, x, y, w, h, FmCardW, ph);
        c.glow.color = Color.clear;   // 미리 보기 칸엔 빛을 두지 않는다 — 후광은 한 화면 한 곳(10-11)
        PlaceIn(c.glow.rectTransform, x - 10f, y - 8f, w + 20f, h + 16f, FmCardW, ph);
    }

    // ── 단련 창(시안 ③) ──

    /// <summary>형 단련 — 다음 단계 변주 두 장. 고르면 금 테 · 다른 카드 0.55, 확정 버튼이 「「쌍풍」으로 단련 · 12」로 켜진다. 취소하면 다음 방문에 다시.</summary>
    private void ShowTemperPanel()
    {
        if (_animating) return;   // 강화 · 진화 연출 중(결과 미리 보임 방지 · 검토 M2)
        int m = PlayerWeaponManager.Slot0;
        var w = _controller?.GetSlot(m);
        if (w == null || !_controller.CanTemperForm(m, out int tier, out int cost)) return;
        w.form.GetVariants(tier, _temperOptions);
        if (_temperOptions.Count < 2) return;
        EnsureTemperPanel();
        _temperTierNow = tier;
        _temperCostNow = cost;

        var f = w.form;
        _temperTitle.text = $"{w.displayName} · 형 「{f.DisplayName}」 — 단련 {RomanTier(tier)}";
        _temperSub.text   = "변주 둘 중 하나를 고른다 · 확정 성공(실패 · 하락 없음) · 고르면 되돌릴 수 없다";
        _temperPips.text  = $"<color=#{ColorUtility.ToHtmlStringRGB(f.Tint)}>{new string('◆', f.MaxStacks)}</color>";
        string v2 = VariantName(w, 2);
        _temperNow.text = UIKeywordInk.Values(FormNowLine(f)) + (v2 != null ? $"  <color=#C9A6FF>· Ⅱ {v2}</color>" : "");

        int fuel = _controller.FuelAmount;
        for (int k = 0; k < 2; k++)
        {
            var c = _temperCards[k];
            var v = _temperOptions[k];
            c.chip.text = v.traitTag;
            float cw = c.chip.GetPreferredValues(v.traitTag).x + 26f;
            PlaceIn(c.chipBox, 24f, 22f, cw, 30f, TpCardW, TpCardH);
            c.index.text = k == 0 ? "변주 ①" : "변주 ②";
            _temperName[k].text = v.displayName;
            c.line.text = UIKeywordInk.Values(v.line);   // 수치만 Noto 500(10-15 설명 글 강조)
            ClearChildren(c.diagram);
            var (caption, changes) = DrawVariant(c.diagram, f, v.id);
            c.caption.text = caption;
            c.changes.text = changes;
            c.cost.text = fuel >= cost ? $"강화재료 {cost}" : $"<color=#FF7A6A>강화재료 {cost}</color>";
        }
        int next = f.TemperCost(tier + 1);
        _temperHint.text = $"보유 강화재료 {fuel}" + (next > 0 ? $" · 다음 단련 {RomanTier(tier + 1)}은 강화재료 {next}" : "")
                         + " · 단련하면 망치 연출 뒤 바로 적용된다";   // 10-18 「벼리면」 → 「단련하면」(용어 통일)
        PickTemperCard(-1);

        FitOverlayToV3(_temperPanel);   // v3 — 판(1560×880) 안을 덮는다
        _temperPanel.transform.SetAsLastSibling();
        _temperPanel.SetActive(true);
        ShopUIStyle.PlaySfx("shop_open");
    }

    /// <summary>지금 형 한 줄 — 무엇이 바뀌는지 보려면 바탕이 먼저 보여야 한다(시안 ③ 「지금 형」 띠).</summary>
    private static string FormNowLine(WeaponFormSO f)
    {
        string act = f.FormId == "weight"
            ? $"반경 {f.TriggerRange:0.#} m 충격파 · 공격력 {f.TriggerAtkRatio * 100f:0}% · 경직"
            : $"정면 {f.TriggerRange:0.#} m 관통 검기 · 공격력 {f.TriggerAtkRatio * 100f:0}%";
        return $"{f.DisplayName} {f.MaxStacks}에서 콤보 마지막 타가 <color=#FFCC4D>「{f.TriggerName}」</color> — {act}";
    }

    private void PickTemperCard(int k)
    {
        _temperPick = k >= 0 && k < _temperOptions.Count ? k : -1;
        for (int i = 0; i < _temperCards.Length; i++)
        {
            var c = _temperCards[i];
            if (c == null) continue;
            bool picked = i == _temperPick;
            c.frame.SetState(picked ? ThinFrameState.Ready : ThinFrameState.Idle, animate: true);
            _temperName[i].color = picked ? UITheme.Thin.Gold : UITheme.Thin.Ink;
            float to = _temperPick < 0 || picked ? 1f : 0.55f;
            if (isActiveAndEnabled) FadeGroupAsync(c.group, to).Forget();
            else c.group.alpha = to;
        }
        bool afford = _controller != null && _controller.FuelAmount >= _temperCostNow;
        bool ok = _temperPick >= 0 && afford;
        _temperConfirm.interactable = ok;
        _temperConfirmFrame.SetState(ok ? ThinFrameState.Ready : ThinFrameState.Idle, animate: true);
        if (_temperPick < 0) { _temperConfirmLbl.text = "<color=#9A98A0>변주를 고른다</color>"; return; }
        string name = _temperOptions[_temperPick].displayName;
        string cost = afford ? _temperCostNow.ToString() : $"<color=#FF7A6A>{_temperCostNow}</color>";
        _temperConfirmLbl.text = $"「{name}」{Ro(name)} 단련  <size=85%>강화재료 {cost}</size>";   // 10-18 「벼린다」 → 「단련」
    }

    private async UniTaskVoid FadeGroupAsync(CanvasGroup g, float to)
    {
        float from = g.alpha;
        try { await UIMotion.Tween(UIMotion.Quick, k => g.alpha = Mathf.Lerp(from, to, k), destroyCancellationToken); }
        catch (OperationCanceledException) { }
    }

    /// <summary>확정 — 데이터는 바로(결과 줄 · 재료), 연출은 그 뒤: 망치 → 판정 빛 → 도장 「쌍풍」 → 성장 길 칸이 채워진다. 실패 연출은 없다.</summary>
    private void OnTemperPicked(int k)
    {
        if (_animating || k < 0 || k >= _temperOptions.Count) return;
        var def = _temperOptions[k];
        int tier = _temperTierNow;
        _holdTrack = true;   // 망치 뒤에 칸이 채워지게 — 확정 즉시 오는 갱신(OnCrucibleChanged)은 길을 건너뛴다
        bool ok = _controller != null && _controller.TryTemperForm(PlayerWeaponManager.Slot0, def.id);
        _temperPanel.SetActive(false);
        if (_resultText != null)
            _resultText.text = ok ? $"<color=#9D7EE6>형 단련 — {def.displayName}</color>" : "<color=#FF7A6A>강화재료가 모자란다</color>";
        if (!ok)
        {
            _holdTrack = false;
            ShopUIStyle.PlaySfx("shop_reject");
            RefreshAll();
            return;
        }
        TemperRevealAsync(def, tier).Forget();
    }

    private async UniTaskVoid TemperRevealAsync(WeaponFormSO.VariantDef def, int tier)
    {
        if (_activeTab != 0) SelectTab(0);   // 무대는 근접 탭에 있다
        _animating = true;
        HideStageResult();
        try
        {
            await ForgeStrikeAsync();
            ShopUIStyle.PlaySfx("enhance_success");
            FlashStage(StageFlashSuccess);
            BurstSparks(StrikeSparks, SparkGold, StrikeX, StrikeYNow);
            ShowStageResult($"<size=50%><color=#C9A6FF>형 단련 {RomanTier(tier)}</color></size>\n{def.displayName}", EvolveStampSize, SuccessPopScale);
            Say(_controller.GetDialogue(CrucibleMood.Temper));
            await FocusAsync(false);
            await Hold(SettleDur);
        }
        catch (OperationCanceledException) { return; }

        ApplyFocus(0f);
        HideHammer();
        _animating = false;
        _skipAnim  = false;
        _holdTrack = false;
        RefreshAll();   // 성장 길 칸이 지금 채워진다(지금 칸 테가 다음 칸으로)

        var next = _queued;
        _queued = null;
        next?.Invoke();
    }

    private void EnsureTemperPanel()
    {
        if (_temperPanel != null) return;
        var host = _window != null ? (Transform)_window : transform;
        var dim = ShopUIStyle.MakeImage(host, "TemperDim", ChooserVeil, raycast: true);
        ShopUIStyle.Stretch(dim.rectTransform);
        _temperPanel = dim.gameObject;
        var d = dim.transform;

        _temperTitle = MakeLabel(d, "TemperTitle", 32f, FontStyles.Normal, TextAlignmentOptions.Center, UITheme.Thin.Ink);
        _temperTitle.textWrappingMode = TextWrappingModes.NoWrap;
        Place(_temperTitle.rectTransform, 0f, 40f, MockW, 52f);
        _temperSub = ThinText(d, "TemperSub", 17f, UIWeight.Regular, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.75f));
        Place(_temperSub.rectTransform, 0f, 94f, MockW, 28f);

        // 지금 형 띠
        var strip = ShopUIStyle.MakeImage(d, "FormNow", UITheme.Thin.GlassFill);
        Place(strip.rectTransform, 128f, 134f, MockW - 256f, 46f);
        UIThinBox.Create(strip.rectTransform, "Line", Vector2.zero, Vector2.one, 0f, UITheme.Thin.InkA(0.22f), Color.clear, Color.clear);
        var chipRt = ShopUIStyle.MakeRect(strip.transform, "NowChip").GetComponent<RectTransform>();
        PlaceIn(chipRt, 12f, 9f, 78f, 28f, MockW - 256f, 46f);
        UIThinBox.Create(chipRt, "Box", Vector2.zero, Vector2.one, 0f, UITheme.Thin.WithA(FormInk, 0.6f), Color.clear, UITheme.Thin.WithA(FormInk, 0.08f));
        var chip = ThinText(chipRt, "Text", 15f, UIWeight.Medium, TextAlignmentOptions.Center, FormInk);
        chip.text = "지금 형";
        ShopUIStyle.Stretch(chip.rectTransform);
        _temperPips = MakeLabel(strip.transform, "Pips", 15f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, Color.white);
        _temperPips.richText = true;
        _temperPips.textWrappingMode = TextWrappingModes.NoWrap;
        PlaceIn(_temperPips.rectTransform, 104f, 0f, 110f, 46f, MockW - 256f, 46f);
        _temperNow = ThinText(strip.transform, "NowLine", 17f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
        _temperNow.richText = true;
        _temperNow.enableAutoSizing = true;
        _temperNow.fontSizeMin = 15f;
        _temperNow.fontSizeMax = 17f;
        PlaceIn(_temperNow.rectTransform, 216f, 0f, MockW - 256f - 230f, 46f, MockW - 256f, 46f);

        for (int k = 0; k < 2; k++) _temperCards[k] = BuildTemperCard(d, k);

        var cancel = ThinButton(d, "TemperCancel", "취소", primary: false, new Vector2(200f, 60f), out _, out _);
        Place((RectTransform)cancel.transform, 468f, 790f, 200f, 60f);
        cancel.onClick.AddListener(() => _temperPanel.SetActive(false));

        _temperConfirm = ThinButton(d, "TemperConfirm", "", primary: true, new Vector2(440f, 60f), out _temperConfirmLbl, out _temperConfirmFrame);
        Place((RectTransform)_temperConfirm.transform, 692f, 790f, 440f, 60f);
        _temperConfirm.onClick.AddListener(() => { if (_temperPick >= 0) OnTemperPicked(_temperPick); });

        _temperHint = ThinText(d, "TemperHint", 15f, UIWeight.Regular, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.55f));
        Place(_temperHint.rectTransform, 0f, 866f, MockW, 26f);
        _temperPanel.SetActive(false);
    }

    private TemperCard BuildTemperCard(Transform d, int k)
    {
        var c = new TemperCard();
        var bg = ShopUIStyle.MakeImage(d, k == 0 ? "TemperA" : "TemperB", CardSolid, raycast: true);
        c.root = bg.rectTransform;
        Place(c.root, 128f + k * (TpCardW + TpCardGap), TpCardY, TpCardW, TpCardH);
        c.group = bg.gameObject.AddComponent<CanvasGroup>();
        c.frame = UIThinFrame.Create(c.root, ThinShape.Square, new Vector2(TpCardW, TpCardH));

        c.chipBox = ShopUIStyle.MakeRect(c.root, "Chip").GetComponent<RectTransform>();
        UIThinBox.Create(c.chipBox, "Box", Vector2.zero, Vector2.one, 0f, UITheme.Thin.WithA(FormInk, 0.55f), Color.clear, UITheme.Thin.WithA(FormInk, 0.08f));
        c.chip = ThinText(c.chipBox, "Text", 15f, UIWeight.Medium, TextAlignmentOptions.Center, FormInk);
        ShopUIStyle.Stretch(c.chip.rectTransform);
        c.index = ThinText(c.root, "Index", 15f, UIWeight.Regular, TextAlignmentOptions.MidlineRight, UITheme.Thin.InkA(0.5f));
        PlaceIn(c.index.rectTransform, TpCardW - 160f, 24f, 136f, 26f, TpCardW, TpCardH);

        _temperName[k] = ThinText(c.root, "Name", 32f, UIWeight.Medium, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
        _temperName[k].textWrappingMode = TextWrappingModes.NoWrap;
        PlaceIn(_temperName[k].rectTransform, 24f, 60f, TpCardW - 48f, 54f, TpCardW, TpCardH);
        c.line = ThinText(c.root, "Line", 20f, UIWeight.Regular, TextAlignmentOptions.TopLeft, UITheme.Thin.Ink);
        c.line.textWrappingMode = TextWrappingModes.Normal;
        PlaceIn(c.line.rectTransform, 24f, 114f, TpCardW - 48f, 60f, TpCardW, TpCardH);

        var diaBg = ShopUIStyle.MakeImage(c.root, "DiagramBg", new Color(0f, 0f, 0f, 0.38f));
        PlaceIn(diaBg.rectTransform, 24f, 176f, TpDiaW, TpDiaH, TpCardW, TpCardH);
        UIThinBox.Create(diaBg.rectTransform, "Line", Vector2.zero, Vector2.one, 0f, UITheme.Thin.InkA(0.12f), Color.clear, Color.clear);
        // 도식은 목업 px(좌상단 기준)로 그린다 — 칸 크기를 고정하고 가운데에 둔다
        c.diagram = ShopUIStyle.MakeRect(diaBg.transform, "Diagram").GetComponent<RectTransform>();
        c.diagram.anchorMin = c.diagram.anchorMax = c.diagram.pivot = new Vector2(0.5f, 0.5f);
        c.diagram.sizeDelta = new Vector2(TpDiaW, TpDiaH);
        c.caption = ThinText(c.root, "Caption", 15f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.5f));
        PlaceIn(c.caption.rectTransform, 36f, 176f + TpDiaH - 26f, TpDiaW - 24f, 22f, TpCardW, TpCardH);

        var ch = ThinText(c.root, "ChangesHead", 15f, UIWeight.Medium, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Gold);
        ch.text = "바뀌는 것";
        PlaceIn(ch.rectTransform, 24f, 412f, 200f, 24f, TpCardW, TpCardH);
        c.changes = ThinText(c.root, "Changes", 17f, UIWeight.Regular, TextAlignmentOptions.TopLeft, UITheme.Thin.InkA(0.9f));
        c.changes.textWrappingMode = TextWrappingModes.Normal;
        PlaceIn(c.changes.rectTransform, 24f, 438f, TpCardW - 48f, 58f, TpCardW, TpCardH);

        c.cost = Chip(c.root, "CostChip", "", UITheme.Thin.InkA(0.85f), UITheme.Thin.InkA(0.3f), 24f, 150f);
        Chip(c.root, "SureChip", "확정 성공 · 하락 없음", UITheme.Thin.InkA(0.85f), UITheme.Thin.InkA(0.3f), 186f, 196f);   // 초록은 오르는 값에만(10-11)
        Chip(c.root, "NoUndoChip", "되돌릴 수 없음", UITheme.Thin.InkA(0.6f), UITheme.Thin.InkA(0.25f), 394f, 140f);

        var btn = bg.gameObject.AddComponent<Button>();
        ShopUIStyle.ApplyButtonColors(btn);
        int pick = k;
        btn.onClick.AddListener(() => { ShopUIStyle.PlaySfx(SoundEvent.UiSelect); PickTemperCard(pick); });
        return c;
    }

    private static TMP_Text Chip(RectTransform card, string name, string text, Color ink, Color line, float x, float w)
    {
        var rt = ShopUIStyle.MakeRect(card, name).GetComponent<RectTransform>();
        PlaceIn(rt, x, 510f, w, 30f, TpCardW, TpCardH);
        UIThinBox.Create(rt, "Box", Vector2.zero, Vector2.one, 0f, line, Color.clear, UITheme.Thin.WithA(line, 0.06f));
        var t = ThinText(rt, "Text", 15f, UIWeight.Regular, TextAlignmentOptions.Center, ink);
        t.richText = true;
        t.text = text;
        ShopUIStyle.Stretch(t.rectTransform);
        return t;
    }

    /// <summary>톤 v2 버튼 — 글래스 + 얇은 테(UIThinFrame). 주 버튼 = 금(Ready), 보조 = Active. 누르면 테가 눌린다.</summary>
    private static Button ThinButton(Transform parent, string name, string label, bool primary, Vector2 size,
                                     out TMP_Text lbl, out UIThinFrame frame)
    {
        var btn = MakeStyledButton(parent, name, label, out lbl);
        var img = btn.GetComponent<Image>();
        img.sprite = null;
        img.color  = UITheme.Thin.GlassFill;
        frame = UIThinFrame.Create((RectTransform)btn.transform, ThinShape.Square, size);
        frame.SetState(primary ? ThinFrameState.Ready : ThinFrameState.Active, animate: false);
        btn.onClick.AddListener(frame.PlayPress);
        UITheme.StyleThinText(lbl, UIWeight.Medium, 20f, primary ? UITheme.Thin.Gold : UITheme.Thin.Ink);
        lbl.richText = true;
        lbl.textWrappingMode = TextWrappingModes.NoWrap;
        return btn;
    }

    private static void ClearChildren(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--)
        {
            var go = t.GetChild(i).gameObject;
            go.SetActive(false);
            Destroy(go);
        }
    }

    // ── 변주 도식(위에서 본 그림 · 592×226) — 흐린 것 = 지금, 밝은 것 = 단련 뒤. 수치는 형 데이터 · 형 코드 상수와 같다 ──

    /// <summary>도식을 그리고 (그림 아래 설명, 「바뀌는 것」 두 줄)을 돌려준다.</summary>
    private static (string caption, string changes) DrawVariant(RectTransform p, WeaponFormSO f, string id)
    {
        var dim    = UITheme.Thin.InkA(0.28f);
        var bright = UITheme.Thin.WithA(f.Tint, 0.95f);
        var gold   = UITheme.Thin.Gold;
        bool weight = f.FormId == "weight";
        float m = weight ? 22f : 50f;            // px / m — 칼바람 8 m · 무게 3.5 m(여진 ×1.4)가 칸에 들어가게
        var me = weight ? new Vector2(150f, 106f) : new Vector2(56f, 106f);
        DDot(p, me, 12f, UITheme.Thin.Ink);
        DText(p, me + new Vector2(0f, 20f), "나", 12f, UITheme.Thin.InkA(0.6f));

        if (!weight)
        {
            float L = f.TriggerRange * m;
            var end = me + new Vector2(L, 0f);
            switch (id)
            {
                case "twin_wind":
                {
                    DDash(p, me, end, 1.5f, dim);
                    DCrescent(p, end, 34f, 0f, dim);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float a = 12f * s;
                        var e = me + new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), -Mathf.Sin(a * Mathf.Deg2Rad)) * L;
                        DLine(p, me, e, 2f, bright);
                        DCrescent(p, e, 34f, a, bright);
                        DDot(p, Vector2.Lerp(me, e, 0.45f), 10f, EnemyDot);
                        DDot(p, Vector2.Lerp(me, e, 0.70f), 10f, EnemyDot);
                        DText(p, e + new Vector2(40f, s > 0 ? -2f : 2f), s > 0 ? "+12°" : "−12°", 13f, bright);
                    }
                    return ($"흐린 것 = 지금 {f.TriggerName}(1발 · {f.TriggerRange:0} m 관통) · 위에서 본 그림",
                            $"· {f.TriggerName} 1발 → 2발, 부채꼴(±12°)로 벌어진다\n· 앞에 넓게 퍼진 무리를 한 번에 벤다");
                }
                case "return_wind":
                {
                    var o = me + new Vector2(0f, -16f);
                    var e = o + new Vector2(L, 0f);
                    DLine(p, o, e, 2f, bright);
                    DCrescent(p, e, 34f, 0f, bright);
                    DText(p, Vector2.Lerp(o, e, 0.5f) + new Vector2(0f, -16f), "나가며 1번", 13f, bright);
                    var back0 = e + new Vector2(-6f, 34f);
                    DDash(p, back0, me + new Vector2(18f, 18f), 1.5f, bright);
                    DText(p, me + new Vector2(14f, 18f), "◀", 12f, bright, TextAlignmentOptions.Center, 20f);
                    DText(p, Vector2.Lerp(o, e, 0.5f) + new Vector2(0f, 54f), $"{0.35f:0.##}초 뒤 돌아오며 1번 더", 13f, bright, TextAlignmentOptions.Center, 220f);
                    for (int i = 0; i < 3; i++)
                    {
                        var en = Vector2.Lerp(o, e, 0.32f + i * 0.2f) + new Vector2(0f, 17f);
                        DDot(p, en, 10f, EnemyDot);
                        DText(p, en + new Vector2(10f, -12f), "×2", 11f, gold, TextAlignmentOptions.Left, 30f);
                    }
                    DText(p, e + new Vector2(30f, -14f), $"{f.TriggerRange:0} m", 13f, UITheme.Thin.InkA(0.6f));
                    return ($"실선 = 지금 {f.TriggerName} · 점선 = 되돌아오는 길(새로) · 위에서 본 그림",
                            "· 같은 줄을 나가며 한 번, 돌아오며 한 번 벤다\n· 한 줄로 선 적 · 큰 적에게 두 번 맞힌다");
                }
                case "still_wind":
                {
                    var a = me + new Vector2(40f, 44f);
                    var b = a + new Vector2(300f, -96f);
                    DDot(p, a, 12f, UITheme.Thin.InkA(0.5f));
                    DDash(p, a, b, 2f, bright, 10f, 6f);
                    DDot(p, b, 12f, UITheme.Thin.Ink);
                    DText(p, Vector2.Lerp(a, b, 0.5f) + new Vector2(-46f, -28f), "회피", 13f, bright);
                    for (int i = 0; i < 3; i++)
                    {
                        var en = Vector2.Lerp(a, b, 0.28f + i * 0.22f) + new Vector2(0f, 6f);
                        DDot(p, en, 11f, EnemyDot);
                        DLine(p, en + new Vector2(-9f, 9f), en + new Vector2(9f, -9f), 2f, gold);
                    }
                    DText(p, a + new Vector2(10f, 30f), $"<color=#{ColorUtility.ToHtmlStringRGB(f.Tint)}>{new string('◆', f.MaxStacks)}</color> {f.DisplayName} {f.MaxStacks}인 채로", 13f, UITheme.Thin.InkA(0.75f), TextAlignmentOptions.Left, 220f);
                    return ("점선 = 회피 길 · 그 길에 선 적을 벤다 · 위에서 본 그림",
                            $"· {f.DisplayName} {f.MaxStacks}에서 회피하면 지나간 길의 적을 벤다(공격력 100%)\n· 피하는 동작이 그대로 공격이 된다");
                }
                case "storm_eye":
                {
                    DLine(p, me, end, 2f, UITheme.Thin.WithA(f.Tint, 0.5f));
                    DCrescent(p, end, 34f, 0f, UITheme.Thin.WithA(f.Tint, 0.5f));
                    for (int i = 0; i < 3; i++)
                    {
                        var en = Vector2.Lerp(me, end, 0.3f + i * 0.22f);
                        DDot(p, en, 11f, EnemyDot);
                        DText(p, en + new Vector2(0f, -22f), "+1", 14f, gold);
                        DDash(p, en + new Vector2(0f, 12f), me + new Vector2(10f + i * 12f, 40f), 1.2f, UITheme.Thin.WithA(UITheme.Thin.Gold, 0.55f), 6f, 5f);
                    }
                    DText(p, me + new Vector2(70f, 54f), $"<color=#{ColorUtility.ToHtmlStringRGB(f.Tint)}>◆◆◆</color> 칼바람 +3까지 돌려받음", 13f, UITheme.Thin.InkA(0.75f), TextAlignmentOptions.Left, 260f);
                    return ($"{f.TriggerName}에 맞은 적 하나당 {f.DisplayName} +1 · 위에서 본 그림",
                            $"· {f.TriggerName}에 맞은 적 하나당 {f.DisplayName} +1을 돌려받는다(최대 3)\n· 무리 속에서 {f.TriggerName}이 빨리 다시 찬다");
                }
            }
        }
        else
        {
            float r = f.TriggerRange * m;
            var c = me + new Vector2(1.5f * m, 0f);      // 내려찍기 = 앞 1.5 m 자리
            switch (id)
            {
                case "fissure":
                {
                    DRing(p, c, r * 2f, dim, 1.5f);
                    DDisc(p, c, r * 2f - 6f, UITheme.Thin.WithA(f.Tint, 0.16f));
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * 72f * Mathf.Deg2Rad + 0.3f;
                        var q0 = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r * 0.2f);
                        var q1 = c + new Vector2(Mathf.Cos(a + 0.25f), Mathf.Sin(a + 0.25f)) * (r * 0.55f);
                        var q2 = c + new Vector2(Mathf.Cos(a - 0.1f), Mathf.Sin(a - 0.1f)) * (r * 0.85f);
                        DLine(p, q0, q1, 1.5f, bright);
                        DLine(p, q1, q2, 1.5f, bright);
                    }
                    DDot(p, c + new Vector2(30f, -24f), 10f, EnemyDot);
                    DDot(p, c + new Vector2(-18f, 36f), 10f, EnemyDot);
                    DText(p, c + new Vector2(r + 120f, -18f), "갈라진 땅 2초", 14f, bright, TextAlignmentOptions.Center, 200f);
                    DText(p, c + new Vector2(r + 120f, 6f), "0.5초마다 공격력 30%", 13f, UITheme.Thin.InkA(0.7f), TextAlignmentOptions.Center, 200f);
                    return ($"흐린 원 = 지금 {f.TriggerName}(반경 {f.TriggerRange:0.#} m) · 위에서 본 그림",
                            "· 충격파 자리에 2초 동안 갈라진 땅이 남는다\n· 그 위의 적은 0.5초마다 공격력 30% 피해를 입는다");
                }
                case "aftershock":
                {
                    DRing(p, c, r * 2f, UITheme.Thin.WithA(f.Tint, 0.75f), 2f);
                    DText(p, c + new Vector2(0f, -r + 12f), "1", 13f, UITheme.Thin.InkA(0.7f));
                    float r2 = r * 1.4f;
                    for (int i = 0; i < 24; i++)   // 점선 원 = 여진
                    {
                        if (i % 2 == 1) continue;
                        float a0 = i * 15f * Mathf.Deg2Rad, a1 = (i + 1) * 15f * Mathf.Deg2Rad;
                        DLine(p, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r2, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r2, 2f, bright);
                    }
                    DText(p, c + new Vector2(0f, -r2 + 12f), "2", 13f, bright);
                    DText(p, c + new Vector2(r2 + 110f, -12f), "0.4초 뒤 · 반경 ×1.4", 14f, bright, TextAlignmentOptions.Center, 200f);
                    DText(p, c + new Vector2(r2 + 110f, 12f), "피해 절반", 13f, UITheme.Thin.InkA(0.7f), TextAlignmentOptions.Center, 200f);
                    return ($"실선 원 = 지금 {f.TriggerName} · 점선 원 = 여진(새로) · 위에서 본 그림",
                            $"· 0.4초 뒤 더 큰 충격파(반경 ×1.4 · 피해 절반)가 한 번 더 퍼진다\n· 처음 원 밖으로 흩어진 적까지 닿는다");
                }
                case "heavy_hand":
                {
                    DRing(p, c, r * 2f, dim, 1.5f);
                    var en = c + new Vector2(r + 150f, 0f);
                    DDot(p, en, 14f, EnemyDot);
                    for (int i = 0; i < 2; i++)
                    {
                        var key = c + new Vector2(r + 30f, i == 0 ? -46f : 46f);
                        var box = ShopUIStyle.MakeRect(p, "Key").GetComponent<RectTransform>();
                        box.anchorMin = box.anchorMax = new Vector2(0f, 1f);
                        box.pivot = new Vector2(0.5f, 0.5f);
                        box.sizeDelta = new Vector2(30f, 30f);
                        box.anchoredPosition = new Vector2(key.x, -key.y);
                        UIThinBox.Create(box, "Box", Vector2.zero, Vector2.one, 0f, bright, Color.clear, UITheme.Thin.WithA(f.Tint, 0.12f));
                        DText(p, key, i == 0 ? "E" : "R", 15f, bright);
                        DLine(p, key + new Vector2(18f, 0f), en + new Vector2(-10f, i == 0 ? -6f : 6f), 1.5f, bright);
                    }
                    DText(p, en + new Vector2(0f, -24f), "치명", 15f, gold);
                    DText(p, c + new Vector2(0f, r + 2f), $"<color=#{ColorUtility.ToHtmlStringRGB(f.Tint)}>{new string('◆', f.MaxStacks)}</color> {f.DisplayName} {f.MaxStacks}", 13f, UITheme.Thin.InkA(0.75f), TextAlignmentOptions.Center, 160f);
                    return ($"{f.DisplayName}가 가득일 때 스킬 적중 · 위에서 본 그림",
                            $"· {f.DisplayName} {f.MaxStacks}일 때 E · R 적중이 치명타가 된다\n· 가득 찬 {f.DisplayName}를 스킬로도 쓴다");
                }
                case "crush":
                {
                    DRing(p, c, r * 2f, UITheme.Thin.WithA(f.Tint, 0.75f), 2f);
                    for (int i = 0; i < 3; i++)
                    {
                        float a = (i * 120f + 30f) * Mathf.Deg2Rad;
                        var en = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r * 0.55f);
                        DDot(p, en, 11f, EnemyDot);
                        DText(p, en + new Vector2(0f, -16f), "◆", 12f, gold);
                    }
                    DText(p, c + new Vector2(r + 120f, -12f), "경직된 적 · 2초", 14f, bright, TextAlignmentOptions.Center, 200f);
                    DText(p, c + new Vector2(r + 120f, 12f), "받는 피해 +6%", 14f, gold, TextAlignmentOptions.Center, 200f);
                    return ($"원 = {f.TriggerName} 범위 · ◆ = 경직(0.8초) · 위에서 본 그림",
                            "· 내려찍기로 경직된 적은 2초 동안 받는 피해 +6%\n· 무리를 묶어 두고 몰아 잡는다");
                }
            }
        }
        DText(p, new Vector2(TpDiaW * 0.5f, TpDiaH * 0.5f), "—", 20f, dim);
        return ("", "");
    }

    private static RectTransform DRect(RectTransform p, string name, Vector2 c, Vector2 size)
    {
        var rt = ShopUIStyle.MakeRect(p, name).GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = new Vector2(c.x, -c.y);
        return rt;
    }

    /// <summary>선 a → b(목업 px · 아래가 +y).</summary>
    private static void DLine(RectTransform p, Vector2 a, Vector2 b, float th, Color col)
    {
        var img = ShopUIStyle.MakeImage(p, "L", col);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 0.5f);
        var d = b - a;
        rt.sizeDelta = new Vector2(d.magnitude, th);
        rt.anchoredPosition = new Vector2(a.x, -a.y);
        rt.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
    }

    private static void DDash(RectTransform p, Vector2 a, Vector2 b, float th, Color col, float seg = 8f, float gap = 6f)
    {
        float len = Vector2.Distance(a, b);
        if (len < 1f) return;
        var dir = (b - a) / len;
        for (float s = 0f; s < len; s += seg + gap) DLine(p, a + dir * s, a + dir * Mathf.Min(len, s + seg), th, col);
    }

    private static void DDot(RectTransform p, Vector2 c, float d, Color col)
    {
        var img = ShopUIStyle.MakeImage(DRect(p, "D", c, new Vector2(d, d)), "Img", col);
        img.sprite = UIThinShapes.Disc(d);
        ShopUIStyle.Stretch(img.rectTransform);
    }

    private static void DDisc(RectTransform p, Vector2 c, float d, Color col) => DDot(p, c, d, col);

    private static Image DRing(RectTransform p, Vector2 c, float d, Color col, float stroke, float fill = 1f, float rotDeg = 0f)
    {
        var rt = DRect(p, "R", c, new Vector2(d, d));
        rt.localRotation = Quaternion.Euler(0f, 0f, rotDeg);
        var img = ShopUIStyle.MakeImage(rt, "Img", col);
        img.sprite = UIThinShapes.Ring(d, stroke);
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Radial360;
        img.fillOrigin = (int)Image.Origin360.Top;
        img.fillClockwise = true;
        img.fillAmount = fill;
        ShopUIStyle.Stretch(img.rectTransform);
        return img;
    }

    /// <summary>검기(초승달) — 원 테 37%를 진행 방향으로 돌려 세운다. mathDeg = 화면 기준 수학 각(오른쪽 0 · 위가 +).</summary>
    private static void DCrescent(RectTransform p, Vector2 c, float d, float mathDeg, Color col)
    {
        const float fill = 0.37f;
        float arcMid = 90f - fill * 180f;   // 위(90°)에서 시계 방향으로 채운 호의 가운데(수학 각)
        DRing(p, c, d, col, 3f, fill, mathDeg - arcMid);
    }

    private static void DText(RectTransform p, Vector2 c, string s, float size, Color col,
                              TextAlignmentOptions align = TextAlignmentOptions.Center, float w = 120f)
    {
        var rt = DRect(p, "T", c, new Vector2(w, size * 1.6f));
        if (align == TextAlignmentOptions.Left) rt.pivot = new Vector2(0f, 0.5f);
        var t = ThinText(rt, "Text", size, UIWeight.Regular, align == TextAlignmentOptions.Left ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center, col);
        t.richText = true;
        t.text = s;
        ShopUIStyle.Stretch(t.rectTransform);
    }
}
