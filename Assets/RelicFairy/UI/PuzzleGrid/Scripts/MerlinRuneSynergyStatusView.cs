using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 속성(존)별 시너지 상태 뷰.
///
/// ⚠️ 판정 기준은 <b>속성별 점유 셀 '개수'</b>다 — 셀이 서로 붙어 있는지(연결/클러스터)는 보지 않는다.
///    과거 '연결 클러스터' 방식에서 개수 방식으로 바뀌었으니 이름·문구에 '연결'을 다시 쓰지 말 것.
///
/// 09-25: 모든 존(6속성 + 중앙)을 <b>항상</b> 한 줄씩 보여 준다. 0칸인 존도 다음 문턱(1단계 6칸·중앙 4칸)을
/// 미리 알린다 — 예전엔 룬을 놓기 전까지 이 칸이 통째로 비어 무엇을 채우는지 알 수 없었다.
///
/// Refresh(zoneCounts) : MerlinRuneBridge.OnZoneCellsUpdated에서 호출.
/// </summary>
public sealed class MerlinRuneSynergyStatusView : MonoBehaviour
{
    // ── Constants ──
    // 존 순서/이름/아이콘/색은 ElementDef에서 빌드 (단일 소스)
    private static readonly string[] ZONE_ORDER;
    private static readonly string[] ZONE_NAMES;
    private static readonly string[] ZONE_ICONS;
    private static readonly Color[]  ZONE_COLORS;

    static MerlinRuneSynergyStatusView()
    {
        var order = ElementDef.Order;
        int n = order.Count;

        // 속성 6종 + 중앙(CENTER). 중앙은 자체 효과가 아니라 '활성 속성 시너지 증폭'이라
        // 목록 맨 아래에 따로 붙인다(ElementDef.Order에는 포함되지 않는다).
        ZONE_ORDER  = new string[n + 1];
        ZONE_NAMES  = new string[n + 1];
        ZONE_ICONS  = new string[n + 1];
        ZONE_COLORS = new Color[n + 1];
        for (int i = 0; i < n; i++)
        {
            var e = ElementDef.GetById(order[i]);
            ZONE_ORDER[i]  = e.Id;
            ZONE_NAMES[i]  = e.Name;
            ZONE_ICONS[i]  = e.Icon;
            ZONE_COLORS[i] = e.Color;
        }
        ZONE_ORDER[n]  = ElementDef.CenterId;
        ZONE_NAMES[n]  = "중앙";
        ZONE_ICONS[n]  = "◆";
        ZONE_COLORS[n] = ElementDef.CenterColor;
    }

    // 위 캐릭터 정보 판과 같은 바탕 — 두 판의 색이 달라(0.05 vs 0.12) 좌측 열이 이어 붙인 조각처럼 보였다(09-28).
    private static readonly Color COLOR_BG_PANEL      = new(0.05f, 0.06f, 0.09f, 0.98f);
    private const float Gutter = CharacterInfoPanelView.Gutter;   // 좌측 열 공통 좌우 여백
    private static readonly Color COLOR_ROW_BG_ACTIVE = new(0.17f, 0.19f, 0.26f, 0.92f);
    private static readonly Color COLOR_BADGE_OFF     = new(0.18f, 0.20f, 0.28f, 0.85f);

    // ── Per-row helper ──
    private class ZoneRow
    {
        public GameObject go;
        public Image      accentStrip;
        public TMP_Text   nameText;
        public TMP_Text   countText;
        public Image[]    tierBGs    = new Image[4];
        public TMP_Text[] tierLabels = new TMP_Text[4];
        public int        zoneIdx;
    }

    private const float RowH = 42f;   // 7행이 영역(≈321)에 들어가는 높이 — 44면 13px 넘쳐 중앙 행이 잘렸다(09-27)
    private static readonly Color CountInk = new(0.92f, 0.96f, 1.00f, 1f);
    private static readonly Color AmpInk   = new(1.00f, 0.80f, 0.35f, 1f);   // 정제소 핵이 키우는 존의 칸 수
    private IReadOnlyDictionary<string, float> _amps;   // 존별 핵 증폭 배수(SetZoneAmplifiers)
    // 납품 행 아트(시너지 바탕·테두리 248×104~106) = 왼쪽 육각 + 알약. 경계가 없어 44px 행에 늘리면 육각이 납작해진다 —
    // 왼쪽 육각(≈104px)을 경계로 준 9-slice 사본을 쓴다. 배율은 원본 높이/행 높이(육각이 행 높이만 한 정사각이 되게).
    private static readonly Vector4 RowArtBorder = new(104f, 10f, 26f, 10f);
    // 행 아트 안쪽 치수(원본 px, 높이 104) — 육각 폭 91 · 몸통 안쪽 선 위 25 / 아래 80 · 오른쪽 둥근 끝 ≈ 36.
    // 칸은 안쪽 선 사이 띠에만, 둥근 끝 앞에서 끝낸다(09-29).
    private const float ArtH = 104f, ArtHexW = 91f, ArtInnerTop = 25f, ArtInnerBottom = 80f, ArtRightInset = 36f;
    private const float NameW = 64f;   // 「◆ 중앙」 17px 굵게 ≈ 60
    private static Sprite _rowBgSliced, _rowBorderSliced;

    // ── Private fields ──
    private readonly Dictionary<string, ZoneRow> _rows  = new();
    private readonly List<string> _activeOrder = new();   // 룬이 놓인 존(처음 놓은 순서). 임계 미달도 포함.
    private Transform   _rowContainer;
    private GameObject  _emptyLabelGO;
    private TMP_Text    _reactionText;   // 활성 속성 반응 배너(행 목록 최상단)

    // ── Tooltip ──
    private GameObject _tooltipGO;
    private TMP_Text   _tooltipText;
    private string     _hoveredZoneId;

    // ── Lifecycle ──

    private void Awake()
    {
        gameObject.AddComponent<Image>().color = COLOR_BG_PANEL;
        BuildGuideHeader();
        BuildRowContainer();
        BuildTooltip();
    }

    // ── Public API ──

    public void Refresh(IReadOnlyDictionary<string, int> zoneCounts)
    {
        // 시너지 데이터가 있는 존은 <b>전부</b> 고정 순서로 띄운다(09-25). 0칸이어도 행이 있어야
        // "어느 속성을 몇 칸 채우면 무엇이 열리는가"가 룬을 놓기 전에 보인다.
        _activeOrder.Clear();
        for (int i = 0; i < ZONE_ORDER.Length; i++)
        {
            var zone = ZONE_ORDER[i];
            var syn = Managers.RuneData?.GetZoneSynergies(zone);
            if (syn != null && syn.Count > 0) _activeOrder.Add(zone);
        }

        // 목록에서 빠진(비활성) 행 파괴
        var toRemove = new List<string>();
        foreach (var key in _rows.Keys)
            if (!_activeOrder.Contains(key)) toRemove.Add(key);
        foreach (var key in toRemove)
        {
            if (_rows[key].go != null) Destroy(_rows[key].go);
            _rows.Remove(key);
        }

        // 활성 순서대로 행 추가/갱신 + 형제 순서를 활성 순서에 맞춘다
        for (int i = 0; i < _activeOrder.Count; i++)
        {
            var zoneId = _activeOrder[i];
            int idx    = System.Array.IndexOf(ZONE_ORDER, zoneId);
            int count  = 0;
            zoneCounts?.TryGetValue(zoneId, out count);

            if (!_rows.TryGetValue(zoneId, out var row))
            {
                row = BuildRow(zoneId, idx);
                _rows[zoneId] = row;
            }
            RefreshRow(row, zoneId, count);
            if (row.go != null) row.go.transform.SetSiblingIndex(i);
        }

        // 활성 시너지가 하나도 없으면 안내 레이블
        _emptyLabelGO?.SetActive(_activeOrder.Count == 0);

        // 속성 반응 배너 — 브릿지가 계산한 활성 반응을 표시(둘 다 1단계 이상인 인접 쌍)
        RefreshReactionBanner();

        // 툴팁 갱신
        if (!string.IsNullOrEmpty(_hoveredZoneId))
        {
            if (_activeOrder.Contains(_hoveredZoneId) && _rows.TryGetValue(_hoveredZoneId, out var tr))
            {
                int count = 0;
                zoneCounts?.TryGetValue(_hoveredZoneId, out count);
                UpdateTooltipContent(_hoveredZoneId, count);
                PositionTooltipNear(tr.go.GetComponent<RectTransform>());
            }
            else
            {
                _tooltipGO?.SetActive(false);
                _hoveredZoneId = null;
            }
        }
    }

    // ── Build UI ──

    private void BuildGuideHeader()
    {
        var go = new GameObject("GuideHeader", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = go.GetComponent<RectTransform>();
        // 위에서 고정 높이 — 활성 효과 머리띠와 같은 띠(예전 비율 0.12 ≈ 44px라 위 띠의 두 배였다).
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = new Vector2(Gutter, -(1f + CharacterInfoPanelView.SectionBandH));
        rt.offsetMax = new Vector2(-Gutter, -1f);
        go.AddComponent<Image>().color = CharacterInfoPanelView.SectionBand;

        var titleTxt = new GameObject("Title", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        titleTxt.transform.SetParent(go.transform, false);
        var trt = titleTxt.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0.04f, 0f);
        trt.anchorMax = new Vector2(0.45f, 1f);
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        titleTxt.text          = "◆ 속성 시너지";
        titleTxt.fontSize      = 17f;
        titleTxt.fontStyle     = FontStyles.Bold;
        titleTxt.color         = CharacterInfoPanelView.SectionInk;
        titleTxt.alignment     = TextAlignmentOptions.MidlineLeft;
        titleTxt.raycastTarget = false;

        var descTxt = new GameObject("Desc", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        descTxt.transform.SetParent(go.transform, false);
        var drt = descTxt.GetComponent<RectTransform>();
        drt.anchorMin = new Vector2(0.45f, 0f);
        drt.anchorMax = new Vector2(1f, 1f);
        drt.offsetMin = Vector2.zero;
        drt.offsetMax = new Vector2(-8f, 0f);
        descTxt.text               = "칸을 채우면 열린다";
        descTxt.fontSize           = 16f;
        descTxt.color              = new Color(0.48f, 0.54f, 0.72f, 0.85f);
        descTxt.alignment          = TextAlignmentOptions.MidlineRight;
        // 띠가 한 줄 높이(32)라 줄바꿈하면 잘린다 — 칸을 0.45부터로 넓혀 한 줄에 넣는다(09-28).
        descTxt.textWrappingMode = TextWrappingModes.NoWrap;
        descTxt.raycastTarget      = false;
    }

    /// <summary>브릿지의 활성 반응 목록을 한 줄 배너로 표시. 반응 없으면 숨긴다.</summary>
    private void RefreshReactionBanner()
    {
        if (_reactionText == null) return;

        var reactions = MerlinRuneBridge.Instance?.ActiveReactions;
        if (reactions == null || reactions.Count == 0)
        {
            _reactionText.gameObject.SetActive(false);
            return;
        }

        var sb = new System.Text.StringBuilder("◆ 반응  ");
        for (int i = 0; i < reactions.Count; i++)
        {
            var d = reactions[i];
            var a = ElementDef.GetById(d.ZoneA);
            var b = ElementDef.GetById(d.ZoneB);
            if (i > 0) sb.Append("   ");
            sb.Append(a != null ? a.Icon : "?");
            sb.Append(b != null ? b.Icon : "?");
            sb.Append(' ');
            sb.Append(d.DisplayName);
        }
        _reactionText.text = sb.ToString();
        _reactionText.transform.SetAsFirstSibling();   // 항상 목록 맨 위
        _reactionText.gameObject.SetActive(true);
    }

    private void BuildRowContainer()
    {
        // ScrollRect viewport — 헤더 아래 전체 영역
        var viewGO = new GameObject("SynergyScroll", typeof(RectTransform));
        viewGO.transform.SetParent(transform, false);
        var viewRT = viewGO.GetComponent<RectTransform>();
        viewRT.anchorMin = new Vector2(0f, 0f);
        viewRT.anchorMax = new Vector2(1f, 1f);
        viewRT.offsetMin = new Vector2(0f, 2f);
        viewRT.offsetMax = new Vector2(0f, -(CharacterInfoPanelView.SectionBandH + 4f));
        viewGO.AddComponent<RectMask2D>();

        var scrollRect = viewGO.AddComponent<ScrollRect>();
        scrollRect.horizontal        = false;
        scrollRect.vertical          = true;
        scrollRect.scrollSensitivity = 30f;
        scrollRect.movementType      = ScrollRect.MovementType.Clamped;
        scrollRect.inertia           = false;

        // Content — 실제 행이 추가되는 컨테이너
        var contentGO = new GameObject("RowContainer", typeof(RectTransform));
        contentGO.transform.SetParent(viewGO.transform, false);
        var contentRT = contentGO.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0f, 1f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot     = new Vector2(0.5f, 1f);
        contentRT.offsetMin = contentRT.offsetMax = Vector2.zero;

        var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment       = TextAnchor.UpperLeft;
        vlg.spacing              = 3f;
        vlg.padding              = new RectOffset((int)Gutter, (int)Gutter, 4, 4);   // 머리띠·위 판과 같은 좌우 선
        vlg.childControlWidth    = true;
        vlg.childControlHeight   = false;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.content  = contentRT;
        scrollRect.viewport = viewRT;
        _rowContainer = contentGO.transform;

        // 속성 반응 배너 — 행 목록 최상단에 상시 자리. 반응 없으면 숨긴다.
        var rxGO = new GameObject("ReactionBanner", typeof(RectTransform));
        rxGO.transform.SetParent(_rowContainer, false);
        _reactionText = rxGO.AddComponent<TextMeshProUGUI>();
        _reactionText.fontSize      = 16f;
        _reactionText.fontStyle     = FontStyles.Bold;
        _reactionText.color         = new Color(1f, 0.82f, 0.45f, 1f);   // 반응 = 금빛
        _reactionText.alignment     = TextAlignmentOptions.Left;
        _reactionText.raycastTarget = false;
        var rxLe = rxGO.AddComponent<UnityEngine.UI.LayoutElement>();
        rxLe.minHeight = 24f;
        rxGO.SetActive(false);

        // 빈 상태 안내 (존이 하나도 없을 때)
        _emptyLabelGO = new GameObject("EmptyLabel", typeof(RectTransform));
        _emptyLabelGO.transform.SetParent(transform, false);
        var ert = _emptyLabelGO.GetComponent<RectTransform>();
        ert.anchorMin = Vector2.zero;
        ert.anchorMax = Vector2.one;
        ert.offsetMin = Vector2.zero;
        ert.offsetMax = new Vector2(0f, -(CharacterInfoPanelView.SectionBandH + 4f));
        var eTxt = _emptyLabelGO.AddComponent<TextMeshProUGUI>();
        eTxt.text          = "셀을 배치하면\n시너지가 표시됩니다";
        eTxt.fontSize      = 16f;
        eTxt.color         = new Color(0.55f, 0.58f, 0.72f, 0.85f);
        eTxt.alignment     = TextAlignmentOptions.Center;
        eTxt.raycastTarget = false;
        _emptyLabelGO.SetActive(true);
    }

    // 시너지 엔트리 스킨(시너지 바탕/테두리) — UI_GridPanel이 주입.
    private Sprite _rowBgSkin, _rowBorderSkin;
    public void SetSkin(Sprite bg, Sprite border) { _rowBgSkin = bg; _rowBorderSkin = border; }

    /// <summary>존별 핵 증폭 배수(1.2 = +20%). 다음 <see cref="Refresh"/>에 반영된다. null이면 없음.</summary>
    public void SetZoneAmplifiers(IReadOnlyDictionary<string, float> amps) => _amps = amps;

    private ZoneRow BuildRow(string zoneId, int idx)
    {
        var row = new ZoneRow { zoneIdx = idx };

        row.go = new GameObject($"Row_{zoneId}", typeof(RectTransform));
        row.go.transform.SetParent(_rowContainer, false);
        // 좌측 패널 뷰포트는 약 879px인데 6행 × 34px = 251px(29%)만 쓰고 나머지가 비어 있었다.
        // 폭(384px)은 고정이라 남는 건 세로뿐이므로, 행을 키워 글자를 읽히게 만든다.
        // 7존(6속성 + 중앙)이 스크롤 없이 한눈에 들어가게 한 줄 행(44px). 예전 72px 두 줄 행은 놓은 존만 떠서 가능했다.
        row.go.AddComponent<LayoutElement>().preferredHeight = RowH;
        // VLG가 높이를 잡지 않으므로(childControlHeight=false) 높이는 직접 준다 — 안 주면 RectTransform 기본값 100으로 선다.
        ((RectTransform)row.go.transform).sizeDelta = new Vector2(0f, RowH);
        var rowBg = row.go.AddComponent<Image>();
        rowBg.color = COLOR_ROW_BG_ACTIVE;
        _rowBgSliced     ??= ShopUIStyle.SlicedCopy(_rowBgSkin,     RowArtBorder);
        _rowBorderSliced ??= ShopUIStyle.SlicedCopy(_rowBorderSkin, RowArtBorder);
        bool rowArt = _rowBgSliced != null;
        if (rowArt)   // 시너지 바탕
        {
            rowBg.sprite = _rowBgSliced; rowBg.type = Image.Type.Sliced; rowBg.color = Color.white;
            rowBg.pixelsPerUnitMultiplier = _rowBgSkin.rect.height / RowH;
        }
        if (_rowBorderSliced != null)   // 시너지 테두리 — 위에 얹는 프레임
        {
            var bd = new GameObject("Border", typeof(RectTransform), typeof(Image));
            bd.transform.SetParent(row.go.transform, false);
            var brt = (RectTransform)bd.transform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
            var bi = bd.GetComponent<Image>();
            bi.sprite = _rowBorderSliced; bi.type = Image.Type.Sliced; bi.raycastTarget = false;
            bi.pixelsPerUnitMultiplier = _rowBorderSkin.rect.height / RowH;
            brt.SetAsLastSibling();
        }

        Color zoneColor = GetZoneColor(idx);

        // 좌측 악센트 스트립
        var accent = new GameObject("Accent", typeof(RectTransform));
        accent.transform.SetParent(row.go.transform, false);
        var art = accent.GetComponent<RectTransform>();
        art.anchorMin = Vector2.zero;
        art.anchorMax = new Vector2(0f, 1f);
        art.sizeDelta = new Vector2(4f, 0f);
        art.anchoredPosition = new Vector2(2f, 0f);
        row.accentStrip = accent.AddComponent<Image>();
        row.accentStrip.color         = zoneColor;
        row.accentStrip.raycastTarget = false;
        if (rowArt) accent.SetActive(false);   // 육각 아트가 행의 머리 — 색 띠는 폴백 전용

        // 육각 칸(아트가 있으면 왼쪽 ≈11%)과 나머지 — 아트가 없으면 예전 비율
        float hexR  = rowArt ? 0.11f : 0.022f;

        // 아이콘+이름 (4~22%)
        var nameGO = new GameObject("Name", typeof(RectTransform));
        nameGO.transform.SetParent(row.go.transform, false);
        var nrt = nameGO.GetComponent<RectTransform>();
        nrt.anchorMin = new Vector2(rowArt ? 0.125f : 0.022f, 0.08f);
        nrt.anchorMax = new Vector2(rowArt ? 0.275f : 0.22f, 0.92f);
        nrt.offsetMin = new Vector2(4f, 0f);
        nrt.offsetMax = Vector2.zero;
        var nameTxt = nameGO.AddComponent<TextMeshProUGUI>();
        nameTxt.text               = $"{ZONE_ICONS[idx]} {ZONE_NAMES[idx]}";
        nameTxt.fontSize           = 17f;
        nameTxt.fontStyle          = FontStyles.Bold;
        nameTxt.color              = zoneColor;
        nameTxt.alignment          = TextAlignmentOptions.MidlineLeft;
        nameTxt.textWrappingMode = TextWrappingModes.NoWrap;
        nameTxt.raycastTarget      = false;
        row.nameText = nameTxt;

        // 클러스터 수 (22~30%)
        var countGO = new GameObject("Count", typeof(RectTransform));
        countGO.transform.SetParent(row.go.transform, false);
        var crt = countGO.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(rowArt ? 0f : 0.22f, 0.08f);          // 아트가 있으면 육각 안에 칸 수
        crt.anchorMax = new Vector2(rowArt ? hexR : 0.30f, 0.92f);
        crt.offsetMin = crt.offsetMax = Vector2.zero;
        var countTxt = countGO.AddComponent<TextMeshProUGUI>();
        countTxt.text               = "0";
        countTxt.fontSize           = 20f;
        countTxt.fontStyle          = FontStyles.Bold;
        countTxt.color              = new Color(0.92f, 0.96f, 1.00f, 1f);
        countTxt.alignment          = TextAlignmentOptions.Midline;
        countTxt.textWrappingMode = TextWrappingModes.NoWrap;
        countTxt.raycastTarget      = false;
        row.countText = countTxt;

        // 4단계 트랙 (30~99%) — 각 단계 = 한 칸, 임계값 도달 시 점등
        float TRACK_L = rowArt ? 0.285f : 0.30f, TRACK_R = rowArt ? 0.965f : 0.99f;
        float segW = (TRACK_R - TRACK_L) / 4f;
        for (int b = 0; b < 4; b++)
        {
            float sL = TRACK_L + b * segW + 0.004f;
            float sR = TRACK_L + (b + 1) * segW - 0.004f;

            var segGO = new GameObject($"Tier{b}", typeof(RectTransform));
            segGO.transform.SetParent(row.go.transform, false);
            var segRT = segGO.GetComponent<RectTransform>();
            segRT.anchorMin = new Vector2(sL, 0.10f);
            segRT.anchorMax = new Vector2(sR, 0.90f);
            segRT.offsetMin = segRT.offsetMax = Vector2.zero;
            row.tierBGs[b] = segGO.AddComponent<Image>();
            row.tierBGs[b].color = COLOR_BADGE_OFF;
            row.tierBGs[b].raycastTarget = false;

            var lblGO = new GameObject("Lbl", typeof(RectTransform));
            lblGO.transform.SetParent(segGO.transform, false);
            var lrt = lblGO.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(2f, 0f);
            lrt.offsetMax = new Vector2(-2f, 0f);
            var lTxt = lblGO.AddComponent<TextMeshProUGUI>();
            lTxt.text               = $"{b + 1}단계";
            lTxt.fontSize           = 16f;   // 14 → 16(하한). 한 줄만 — 단계 이름(최대 6자)은 활성 효과 목록·툴팁에
            lTxt.color              = new Color(0.50f, 0.53f, 0.66f, 1f);
            lTxt.alignment          = TextAlignmentOptions.Center;
            lTxt.textWrappingMode = TextWrappingModes.NoWrap;
            lTxt.raycastTarget      = false;
            row.tierLabels[b] = lTxt;
        }

        if (rowArt) LayoutOnRowArt(row, crt, nrt);

        // 호버 → 툴팁
        var et = row.go.AddComponent<EventTrigger>();
        string capturedId = zoneId;

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ =>
        {
            _hoveredZoneId = capturedId;
            int c = 0;
            MerlinRuneBridge.Instance?.GetZoneOccupiedCounts()?.TryGetValue(capturedId, out c);
            UpdateTooltipContent(capturedId, c);
            PositionTooltipNear(row.go.GetComponent<RectTransform>());
            _tooltipGO?.SetActive(true);
        });
        et.triggers.Add(enter);

        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => { _hoveredZoneId = null; _tooltipGO?.SetActive(false); });
        et.triggers.Add(exit);

        return row;
    }

    /// <summary>
    /// 행 아트 위 배치 — 칸 수는 육각 한가운데, 이름은 육각 바로 옆, 단계 칸 넷은 알약 몸통의 <b>안쪽 선 사이 띠</b>에
    /// 둥근 끝 앞까지. 비율이 아니라 아트 치수(행 높이 / 원본 높이 배율)로 잡는다 — 비율은 행 폭이 바뀌면 아트와 어긋난다.
    /// </summary>
    private static void LayoutOnRowArt(ZoneRow row, RectTransform count, RectTransform name)
    {
        float s       = RowH / ArtH;
        float hexW    = ArtHexW * s;
        float bandTop = ArtInnerTop * s + 1.5f;               // 행 위에서
        float bandBot = (ArtH - ArtInnerBottom) * s + 1.5f;   // 행 아래에서

        count.anchorMin = new Vector2(0f, 0f); count.anchorMax = new Vector2(0f, 1f);
        count.offsetMin = new Vector2(0f, 0f); count.offsetMax = new Vector2(hexW, 0f);

        name.anchorMin = new Vector2(0f, 0f); name.anchorMax = new Vector2(0f, 1f);
        name.offsetMin = new Vector2(hexW + 6f, 0f); name.offsetMax = new Vector2(hexW + 6f + NameW, 0f);

        var track = new GameObject("Track", typeof(RectTransform)).GetComponent<RectTransform>();
        track.SetParent(row.go.transform, false);
        track.anchorMin = Vector2.zero; track.anchorMax = Vector2.one;
        track.offsetMin = new Vector2(hexW + 6f + NameW + 4f, bandBot);
        track.offsetMax = new Vector2(-ArtRightInset * s, -bandTop);

        for (int b = 0; b < row.tierBGs.Length; b++)
        {
            if (row.tierBGs[b] == null) continue;
            var rt = row.tierBGs[b].rectTransform;
            rt.SetParent(track, false);
            rt.anchorMin = new Vector2(b / 4f, 0f);
            rt.anchorMax = new Vector2((b + 1) / 4f, 1f);
            rt.offsetMin = new Vector2(b == 0 ? 0f : 1.5f, 0f);
            rt.offsetMax = new Vector2(b == 3 ? 0f : -1.5f, 0f);
        }
    }

    private void RefreshRow(ZoneRow row, string zoneId, int count)
    {
        Color zoneColor = GetZoneColor(row.zoneIdx);

        if (row.countText != null)
        {
            row.countText.SetText(count.ToString());
            // 정제소 핵이 이 존을 키우고 있으면 칸 수가 금빛 — 배율은 툴팁(09-27: 핵을 놓아도 표에 아무 변화가 없었다).
            row.countText.color = AmpOf(zoneId) > 0 ? AmpInk : CountInk;
        }

        var synergies = Managers.RuneData?.GetZoneSynergies(zoneId);
        var sorted    = new List<RuneSynergyEntry>();
        if (synergies != null) sorted.AddRange(synergies);
        sorted.Sort((a, b) => a.threshold.CompareTo(b.threshold));

        // 현재 도달한 최고 단계 인덱스
        int curTier = -1;
        for (int i = 0; i < sorted.Count && i < 4; i++)
            if (count >= sorted[i].threshold) curTier = i;

        for (int b = 0; b < 4; b++)
        {
            if (row.tierBGs[b] == null) continue;
            bool hasData = b < sorted.Count;
            bool met     = hasData && count >= sorted[b].threshold;
            bool isCur   = b == curTier;

            // 점등: 도달 시 존 색(현재 단계는 더 진하게), 미도달은 어둡게
            float mul = met ? (isCur ? 0.55f : 0.38f) : 0f;
            row.tierBGs[b].color = met
                ? new Color(zoneColor.r * mul, zoneColor.g * mul, zoneColor.b * mul, 0.95f)
                : COLOR_BADGE_OFF;

            if (row.tierLabels[b] != null)
            {
                // 도달: 「N단계」가 켜진다 / 미도달: 필요 칸 수. 한 줄(16px) — 단계 이름은 6자까지라 칸(≈60px)에 안 든다.
                row.tierLabels[b].text      = !hasData ? "—" : (met ? $"{b + 1}단계" : $"{sorted[b].threshold}칸");
                row.tierLabels[b].fontStyle = isCur ? FontStyles.Bold : FontStyles.Normal;
                row.tierLabels[b].color     = met
                    ? new Color(
                        Mathf.Clamp01(zoneColor.r * 1.55f),
                        Mathf.Clamp01(zoneColor.g * 1.55f),
                        Mathf.Clamp01(zoneColor.b * 1.55f), 1f)
                    : new Color(0.46f, 0.49f, 0.62f, 1f);
            }
        }
    }

    // ── Tooltip ──

    private void BuildTooltip()
    {
        _tooltipGO = new GameObject("SynergyTooltip", typeof(RectTransform));
        // 룬판 루트 아래 — 이 뷰(좌측 패널)의 자식이면 뒤에 그려지는 중앙 판(룬 칸)이 툴팁을 덮었다(09-28 사용자 캡처).
        _tooltipGO.transform.SetParent(TooltipRoot, false);
        var rt = _tooltipGO.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(TooltipMinWidth, 200f);

        // 공통 글래스 판 + 금 가는 선(UITheme) — 푸른 판 · 하늘색 선이 이 화면만 따로 놀았다(09-29).
        var bg = _tooltipGO.AddComponent<Image>();
        bg.raycastTarget = false;
        UITheme.StylePanel(bg, new Color(0.05f, 0.045f, 0.08f, 0.97f), UITheme.GoldLine, 8f);

        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(_tooltipGO.transform, false);
        var trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(TooltipPadX, TooltipPadY);
        trt.offsetMax = new Vector2(-TooltipPadX, -TooltipPadY);
        _tooltipText = textGO.AddComponent<TextMeshProUGUI>();
        _tooltipText.fontSize          = TooltipFont;
        _tooltipText.color             = UITheme.Ink;
        _tooltipText.alignment         = TextAlignmentOptions.TopLeft;
        _tooltipText.textWrappingMode  = TextWrappingModes.NoWrap;   // 문장 중간 줄바꿈 금지 — 폭을 내용에 맞춘다(09-29)
        _tooltipText.lineSpacing       = 6f;
        _tooltipText.raycastTarget     = false;
        TMPOutlineHelper.ApplySoftShadow(_tooltipText);

        _tooltipGO.SetActive(false);
    }

    // 툴팁 — 룬판 루트 좌표로 놓는다. 폭은 가장 긴 줄에 맞추되(줄바꿈 없음) 이 범위 안 — 넘으면 글자를 줄인다.
    private const float TooltipMinWidth = 360f;
    private const float TooltipMaxWidth = 760f;
    private const float TooltipPadX     = 16f;
    private const float TooltipPadY     = 12f;
    private const float TooltipFont     = 16f;
    private float _tooltipWidth = TooltipMinWidth;

    // 수치(10초 · 4% · +50% · ×2 · 3칸 …)만 골라 금빛 굵게 — 설명 속 숫자가 글에 묻혔다(09-29 사용자).
    private static readonly System.Text.RegularExpressions.Regex NumberRx =
        new(@"[+\-−×x]?\d+(?:\.\d+)?(?:%p|%|초|칸|m|회|배|단계)?", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex TierPrefixRx =
        new(@"^\s*\d+\s*단계\s*", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string EmphasizeNumbers(string s, string hex)
        => NumberRx.Replace(s, m => $"<color={hex}><b>{m.Value}</b></color>");

    /// <summary>툴팁의 부모 — 룬판 루트(모든 패널 위). 룬판이 아직 없으면 이 뷰.</summary>
    private Transform TooltipRoot => UI_GridPanel.Instance != null ? UI_GridPanel.Instance.transform : transform;

    /// <summary>
    /// 행 오른쪽 — 좌측 패널 바깥(판 위)에 띄운다. 행 위에 뜨면 보려던 단계 칩을 덮는다(2026-09-14).
    /// 높이는 내용에 맞추고, 화면 안으로 세로를 가둔다. 좌표는 룬판 루트 기준(부모가 루트라서).
    /// </summary>
    private void PositionTooltipNear(RectTransform rowRT)
    {
        if (_tooltipGO == null || rowRT == null || _tooltipText == null) return;
        var rt   = (RectTransform)_tooltipGO.transform;
        var root = rt.parent as RectTransform;
        if (root == null) return;

        var me = (RectTransform)transform;
        Vector2 rowC  = root.InverseTransformPoint(rowRT.TransformPoint(rowRT.rect.center));
        Vector2 edge  = root.InverseTransformPoint(me.TransformPoint(new Vector3(me.rect.xMax, 0f, 0f)));

        float h = _tooltipText.GetPreferredValues(_tooltipText.text, _tooltipWidth - TooltipPadX * 2f, 0f).y
                + TooltipPadY * 2f;
        var rr = root.rect;
        float y = Mathf.Clamp(rowC.y, rr.yMin + h * 0.5f + 8f, rr.yMax - h * 0.5f - 8f);

        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0f, 0.5f);
        rt.sizeDelta        = new Vector2(_tooltipWidth, h);
        rt.anchoredPosition = new Vector2(edge.x + 10f - rr.center.x, y - rr.center.y);
        rt.SetAsLastSibling();
    }

    private void UpdateTooltipContent(string zoneId, int count)
    {
        if (_tooltipText == null) return;
        int idx = System.Array.IndexOf(ZONE_ORDER, zoneId);
        if (idx < 0) return;

        var synergies = Managers.RuneData?.GetZoneSynergies(zoneId);
        var sb = new System.Text.StringBuilder();
        string zoneHex = ColorUtility.ToHtmlStringRGB(GetZoneColor(idx));

        List<RuneSynergyEntry> sorted = null;
        int nextThr = 0;
        if (synergies != null && synergies.Count > 0)
        {
            sorted = new List<RuneSynergyEntry>(synergies);
            sorted.Sort((a, b) => a.threshold.CompareTo(b.threshold));
            foreach (var s in sorted)
                if (count < s.threshold) { nextThr = s.threshold; break; }
        }

        // 머리 — 존 이름 · 지금 몇 칸 · 다음 단계까지(한 줄, 가장 먼저 읽히게)
        string next = nextThr > 0 ? $"   <color=#9A93A6>다음 단계까지</color> <color=#FFD98A><b>{nextThr - count}칸</b></color>" : "   <color=#6BE08A>모든 단계 달성</color>";
        sb.AppendLine($"<size=19><b><color=#{zoneHex}>{ZONE_ICONS[idx]} {ZONE_NAMES[idx]}</color></b></size>   <color=#9A93A6>채움</color> <b>{count}칸</b>{next}");
        int amp = AmpOf(zoneId);
        if (amp > 0) sb.AppendLine($"<color=#FFCB5A>◆ 핵 증폭 +{amp}%</color>  <color=#9A93A6>이 존의 시너지 효과가 커진다</color>");

        if (sorted != null)
        {
            for (int i = 0; i < sorted.Count; i++)
            {
                var s = sorted[i];
                bool met    = count >= s.threshold;
                bool isNext = s.threshold == nextThr;
                string desc = string.IsNullOrEmpty(s.description) ? s.effect_type : s.description;

                // 「1단계 독안개 살포: 효과, 효과」 → 이름 · 효과(쉼표 절을 「·」로 이어 한 줄)
                string name = null, effect = desc;
                int colon = desc.IndexOf(':');
                if (colon > 0) { name = TierPrefixRx.Replace(desc.Substring(0, colon), string.Empty).Trim(); effect = desc.Substring(colon + 1).Trim(); }
                effect = string.Join("  ·  ", effect.Split(new[] { ", ", "," }, System.StringSplitOptions.RemoveEmptyEntries));

                string mark   = met ? "<color=#6BE08A>◆</color>" : isNext ? "<color=#FFCB5A>◇</color>" : "<color=#6E6A78>◇</color>";
                string head   = met ? "#EDE6D8" : isNext ? "#FFD98A" : "#8E8A98";
                string numHex = met || isNext ? "#FFD98A" : "#C9B98A";
                string body   = met || isNext ? "#D9D3C4" : "#8E8A98";

                sb.AppendLine();
                sb.AppendLine($"{mark} <color={head}><b>{s.threshold}칸</b>{(string.IsNullOrEmpty(name) ? string.Empty : $"  {name}")}</color>");
                sb.AppendLine($"<indent=1.4em><color={body}>{EmphasizeNumbers(effect, numHex)}</color></indent>");
            }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("<color=#9A93A6>(시너지 데이터 없음)</color>");
        }

        string text = sb.ToString().TrimEnd();
        _tooltipText.enableAutoSizing = false;
        _tooltipText.fontSize = TooltipFont;
        _tooltipText.SetText(text);

        // 폭 = 가장 긴 줄(줄바꿈 없음). 최대 폭을 넘는 긴 설명만 글자를 줄여 판 안에 넣는다.
        float need = _tooltipText.GetPreferredValues(text, float.PositiveInfinity, 0f).x + TooltipPadX * 2f + 4f;
        _tooltipWidth = Mathf.Clamp(need, TooltipMinWidth, TooltipMaxWidth);
        if (need > TooltipMaxWidth)
        {
            _tooltipText.enableAutoSizing = true;
            _tooltipText.fontSizeMax = TooltipFont;
            _tooltipText.fontSizeMin = 12f;
        }
    }

    // ── Helpers ──

    /// <summary>이 존의 핵 증폭(%). 없으면 0.</summary>
    private int AmpOf(string zoneId)
        => _amps != null && _amps.TryGetValue(zoneId, out var m) ? Mathf.RoundToInt((m - 1f) * 100f) : 0;

    private static Color GetZoneColor(int idx) =>
        (idx >= 0 && idx < ZONE_COLORS.Length) ? ZONE_COLORS[idx] : Color.white;
}
