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
/// 점유 개수 > 0인 존만 행이 동적으로 추가되고, 0이 되면 행이 사라진다.
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
        ZONE_ICONS[n]  = "◈";
        ZONE_COLORS[n] = ElementDef.CenterColor;
    }

    private static readonly Color COLOR_BG_PANEL      = new(0.12f, 0.14f, 0.20f, 0.90f);
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

    // ── Private fields ──
    private readonly Dictionary<string, ZoneRow> _rows  = new();
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
        // 시너지 정의가 있는 전 존을 항상 표시한다 — 설계 레이아웃대로 6속성 구조를 늘 보여줘,
        // 빈 판에서도 어떤 시너지가 있고 몇 칸이 필요한지 파악하게 한다(과거: count>0 존만 노출 → 첫 판이 빈 플레이스홀더).
        var shown = new List<string>();
        for (int i = 0; i < ZONE_ORDER.Length; i++)
        {
            var syn = Managers.RuneData?.GetZoneSynergies(ZONE_ORDER[i]);
            if (syn != null && syn.Count > 0) shown.Add(ZONE_ORDER[i]);
        }

        // 시너지 정의가 없는(구성 변경 등) 존 행은 제거
        var toRemove = new List<string>();
        foreach (var key in _rows.Keys)
            if (!shown.Contains(key)) toRemove.Add(key);
        foreach (var key in toRemove)
        {
            if (_rows[key].go != null) Destroy(_rows[key].go);
            _rows.Remove(key);
        }

        // 전 존 행 추가/갱신 (count=0이면 임계 'N칸'만 어둡게 표시)
        foreach (var zoneId in shown)
        {
            int idx   = System.Array.IndexOf(ZONE_ORDER, zoneId);
            int count = 0;
            zoneCounts?.TryGetValue(zoneId, out count);

            if (!_rows.TryGetValue(zoneId, out var row))
            {
                row = BuildRow(zoneId, idx);
                _rows[zoneId] = row;
            }
            RefreshRow(row, zoneId, count);
        }

        // 빈 상태 레이블 — 시너지 정의 자체가 없을 때만(데이터 미로드 등)
        _emptyLabelGO?.SetActive(shown.Count == 0);

        // 속성 반응 배너 — 브릿지가 계산한 활성 반응을 표시(둘 다 1단계 이상인 인접 쌍)
        RefreshReactionBanner();

        // 툴팁 갱신
        if (!string.IsNullOrEmpty(_hoveredZoneId))
        {
            if (shown.Contains(_hoveredZoneId) && _rows.TryGetValue(_hoveredZoneId, out var tr))
            {
                int count = 0;
                zoneCounts?.TryGetValue(_hoveredZoneId, out count);
                UpdateTooltipContent(_hoveredZoneId, count);
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
        rt.anchorMin = new Vector2(0f, 0.88f);
        rt.anchorMax = new Vector2(1f, 1.00f);
        rt.offsetMin = new Vector2(2f, 1f);
        rt.offsetMax = new Vector2(-2f, -1f);
        go.AddComponent<Image>().color = new Color(0.14f, 0.18f, 0.26f, 0.88f);

        var titleTxt = new GameObject("Title", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        titleTxt.transform.SetParent(go.transform, false);
        var trt = titleTxt.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0.04f, 0f);
        trt.anchorMax = new Vector2(0.55f, 1f);
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        titleTxt.text          = "◆ 속성 시너지";
        titleTxt.fontSize      = 12f;
        titleTxt.fontStyle     = FontStyles.Bold;
        titleTxt.color         = new Color(0.75f, 0.90f, 1.00f, 1f);
        titleTxt.alignment     = TextAlignmentOptions.MidlineLeft;
        titleTxt.raycastTarget = false;

        var descTxt = new GameObject("Desc", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        descTxt.transform.SetParent(go.transform, false);
        var drt = descTxt.GetComponent<RectTransform>();
        drt.anchorMin = new Vector2(0.55f, 0f);
        drt.anchorMax = new Vector2(1f, 1f);
        drt.offsetMin = Vector2.zero;
        drt.offsetMax = new Vector2(-8f, 0f);
        descTxt.text               = "블록을 배치하면 시너지가 표시됩니다";
        descTxt.fontSize           = 9f;
        descTxt.color              = new Color(0.48f, 0.54f, 0.72f, 0.85f);
        descTxt.alignment          = TextAlignmentOptions.MidlineRight;
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

        var sb = new System.Text.StringBuilder("✦ 반응  ");
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
        viewRT.anchorMax = new Vector2(1f, 0.87f);
        viewRT.offsetMin = new Vector2(0f, 2f);
        viewRT.offsetMax = new Vector2(0f, -2f);
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
        vlg.padding              = new RectOffset(4, 4, 4, 4);
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
        _reactionText.fontSize      = 11f;
        _reactionText.fontStyle     = FontStyles.Bold;
        _reactionText.color         = new Color(1f, 0.82f, 0.45f, 1f);   // 반응 = 금빛
        _reactionText.alignment     = TextAlignmentOptions.Left;
        _reactionText.raycastTarget = false;
        var rxLe = rxGO.AddComponent<UnityEngine.UI.LayoutElement>();
        rxLe.minHeight = 18f;
        rxGO.SetActive(false);

        // 빈 상태 안내 (존이 하나도 없을 때)
        _emptyLabelGO = new GameObject("EmptyLabel", typeof(RectTransform));
        _emptyLabelGO.transform.SetParent(transform, false);
        var ert = _emptyLabelGO.GetComponent<RectTransform>();
        ert.anchorMin = Vector2.zero;
        ert.anchorMax = new Vector2(1f, 0.87f);
        ert.offsetMin = ert.offsetMax = Vector2.zero;
        var eTxt = _emptyLabelGO.AddComponent<TextMeshProUGUI>();
        eTxt.text          = "셀을 배치하면\n시너지가 표시됩니다";
        eTxt.fontSize      = 11f;
        eTxt.color         = new Color(0.40f, 0.43f, 0.56f, 0.65f);
        eTxt.alignment     = TextAlignmentOptions.Center;
        eTxt.raycastTarget = false;
        _emptyLabelGO.SetActive(true);
    }

    private ZoneRow BuildRow(string zoneId, int idx)
    {
        var row = new ZoneRow { zoneIdx = idx };

        row.go = new GameObject($"Row_{zoneId}", typeof(RectTransform));
        row.go.transform.SetParent(_rowContainer, false);
        row.go.AddComponent<LayoutElement>().preferredHeight = 34f;
        row.go.AddComponent<Image>().color = COLOR_ROW_BG_ACTIVE;

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

        // 아이콘+이름 (4~22%)
        var nameGO = new GameObject("Name", typeof(RectTransform));
        nameGO.transform.SetParent(row.go.transform, false);
        var nrt = nameGO.GetComponent<RectTransform>();
        nrt.anchorMin = new Vector2(0.022f, 0.08f);
        nrt.anchorMax = new Vector2(0.22f, 0.92f);
        nrt.offsetMin = new Vector2(4f, 0f);
        nrt.offsetMax = Vector2.zero;
        var nameTxt = nameGO.AddComponent<TextMeshProUGUI>();
        nameTxt.text               = $"{ZONE_ICONS[idx]} {ZONE_NAMES[idx]}";
        nameTxt.fontSize           = 12f;
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
        crt.anchorMin = new Vector2(0.22f, 0.08f);
        crt.anchorMax = new Vector2(0.30f, 0.92f);
        crt.offsetMin = crt.offsetMax = Vector2.zero;
        var countTxt = countGO.AddComponent<TextMeshProUGUI>();
        countTxt.text               = "0";
        countTxt.fontSize           = 14f;
        countTxt.fontStyle          = FontStyles.Bold;
        countTxt.color              = new Color(0.92f, 0.96f, 1.00f, 1f);
        countTxt.alignment          = TextAlignmentOptions.Midline;
        countTxt.textWrappingMode = TextWrappingModes.NoWrap;
        countTxt.raycastTarget      = false;
        row.countText = countTxt;

        // 4단계 트랙 (30~99%) — 각 단계 = 한 칸, 임계값 도달 시 점등
        const float TRACK_L = 0.30f, TRACK_R = 0.99f;
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
            lTxt.fontSize           = 8.5f;
            lTxt.color              = new Color(0.50f, 0.53f, 0.66f, 1f);
            lTxt.alignment          = TextAlignmentOptions.Center;
            lTxt.textWrappingMode = TextWrappingModes.NoWrap;
            lTxt.raycastTarget      = false;
            row.tierLabels[b] = lTxt;
        }

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

    private void RefreshRow(ZoneRow row, string zoneId, int count)
    {
        Color zoneColor = GetZoneColor(row.zoneIdx);

        if (row.countText != null)
            row.countText.SetText(count.ToString());

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
                // 도달: 단계명(예: 점화), 미도달: 필요 칸 수
                string line2 = !hasData ? "—" : (met ? TierEffectName(sorted[b]) : $"{sorted[b].threshold}칸");
                row.tierLabels[b].text      = $"{b + 1}단계\n{line2}";
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

    /// <summary>description "N단계 이름: 설명" 에서 "이름"만 추출. 실패 시 effect_type.</summary>
    private static string TierEffectName(RuneSynergyEntry e)
    {
        if (!string.IsNullOrEmpty(e.description))
        {
            int colon = e.description.IndexOf(':');
            string head = colon > 0 ? e.description.Substring(0, colon) : e.description;
            int sp = head.IndexOf(' ');
            if (sp > 0 && sp + 1 < head.Length) return head.Substring(sp + 1).Trim();
        }
        return e.effect_type;
    }

    // ── Tooltip ──

    private void BuildTooltip()
    {
        _tooltipGO = new GameObject("Tooltip", typeof(RectTransform));
        _tooltipGO.transform.SetParent(transform, false);
        var rt = _tooltipGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.40f);
        rt.anchorMax = new Vector2(0.65f, 0.86f);
        rt.offsetMin = new Vector2(4f, 4f);
        rt.offsetMax = new Vector2(-4f, -4f);

        var bg = _tooltipGO.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.09f, 0.14f, 0.97f);

        var border = new GameObject("Border", typeof(RectTransform));
        border.transform.SetParent(_tooltipGO.transform, false);
        var brt = border.GetComponent<RectTransform>();
        brt.anchorMin = Vector2.zero;
        brt.anchorMax = Vector2.one;
        brt.offsetMin = brt.offsetMax = Vector2.zero;
        border.AddComponent<Image>().color = new Color(0.25f, 0.38f, 0.65f, 0.40f);

        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(_tooltipGO.transform, false);
        var trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(8f, 6f);
        trt.offsetMax = new Vector2(-8f, -6f);
        _tooltipText = textGO.AddComponent<TextMeshProUGUI>();
        _tooltipText.fontSize          = 11f;
        _tooltipText.color             = new Color(0.88f, 0.93f, 1.00f, 1f);
        _tooltipText.alignment         = TextAlignmentOptions.TopLeft;
        _tooltipText.textWrappingMode = TextWrappingModes.Normal;
        _tooltipText.raycastTarget     = false;

        _tooltipGO.SetActive(false);
    }

    private void PositionTooltipNear(RectTransform rowRT)
    {
        if (_tooltipGO == null || rowRT == null) return;
        var rt = _tooltipGO.GetComponent<RectTransform>();

        // 행의 localY 중심을 구해 툴팁 Y 계산
        Vector2 localCenter = transform.InverseTransformPoint(
            rowRT.TransformPoint(rowRT.rect.center));
        float yCenter = (localCenter.y - ((RectTransform)transform).rect.yMin)
                      / ((RectTransform)transform).rect.height;

        bool showAbove = yCenter < 0.45f;
        if (showAbove)
        {
            rt.anchorMin = new Vector2(0f, Mathf.Clamp01(yCenter + 0.02f));
            rt.anchorMax = new Vector2(0.65f, Mathf.Clamp01(yCenter + 0.48f));
        }
        else
        {
            rt.anchorMin = new Vector2(0f, Mathf.Clamp01(yCenter - 0.48f));
            rt.anchorMax = new Vector2(0.65f, Mathf.Clamp01(yCenter - 0.02f));
        }
        rt.offsetMin = new Vector2(4f, 4f);
        rt.offsetMax = new Vector2(-4f, -4f);
        _tooltipGO.transform.SetAsLastSibling();
    }

    private void UpdateTooltipContent(string zoneId, int count)
    {
        if (_tooltipText == null) return;
        int idx = System.Array.IndexOf(ZONE_ORDER, zoneId);
        if (idx < 0) return;

        var synergies = Managers.RuneData?.GetZoneSynergies(zoneId);
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"<b><color=#B8E0FF>{ZONE_ICONS[idx]} {ZONE_NAMES[idx]} ({zoneId})</color></b>");
        sb.AppendLine($"현재 채움: <b>{count}칸</b>");
        sb.AppendLine();

        if (synergies != null && synergies.Count > 0)
        {
            var sorted = new List<RuneSynergyEntry>(synergies);
            sorted.Sort((a, b) => a.threshold.CompareTo(b.threshold));

            for (int i = 0; i < sorted.Count; i++)
            {
                var s = sorted[i];
                bool met   = count >= s.threshold;
                string chk = met ? "<color=#55FF88>●</color>" : "○";
                string desc = string.IsNullOrEmpty(s.description) ? s.effect_type : s.description;
                sb.AppendLine($"{chk} <b>({s.threshold}칸)</b> {desc}");
            }

            int maxThr = sorted[sorted.Count - 1].threshold;
            if (count < maxThr)
            {
                int nextThr = 0;
                foreach (var s in sorted)
                    if (count < s.threshold) { nextThr = s.threshold; break; }
                if (nextThr > 0)
                    sb.AppendLine($"\n다음 단계까지 <b>{nextThr - count}칸</b> 더 필요");
            }
        }
        else
        {
            sb.AppendLine("(시너지 데이터 없음)");
        }

        _tooltipText.SetText(sb.ToString().TrimEnd());
    }

    // ── Helpers ──

    private static Color GetZoneColor(int idx) =>
        (idx >= 0 && idx < ZONE_COLORS.Length) ? ZONE_COLORS[idx] : Color.white;
}
