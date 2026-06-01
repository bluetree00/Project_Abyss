using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// TFT 스타일 연결 클러스터 시너지 상태 뷰.
///
/// 블록이 배치되어 클러스터(cluster > 0)가 생기면 해당 존 행이 동적으로 추가되고,
/// 제거되면 행이 사라진다. 비활성 존은 표시하지 않는다.
///
/// Refresh(clusterSizes) : MerlinRuneBridge.OnZoneCellsUpdated에서 호출.
/// </summary>
public sealed class MerlinRuneSynergyStatusView : MonoBehaviour
{
    // ── Constants ──
    private static readonly string[] ZONE_ORDER = { "ATK", "DEF", "MAG", "HP", "SPD", "LUCK" };
    private static readonly string[] ZONE_NAMES = { "공격", "방어", "마력", "체력", "속도", "행운" };
    private static readonly string[] ZONE_ICONS = { "⚔", "🛡", "✦", "❤", "》", "★" };

    private static readonly Color COLOR_ATK  = new(1.0f, 0.35f, 0.30f, 1f);
    private static readonly Color COLOR_DEF  = new(0.3f, 0.55f, 1.00f, 1f);
    private static readonly Color COLOR_MAG  = new(0.7f, 0.30f, 1.00f, 1f);
    private static readonly Color COLOR_HP   = new(0.3f, 0.85f, 0.45f, 1f);
    private static readonly Color COLOR_SPD  = new(1.0f, 0.85f, 0.20f, 1f);
    private static readonly Color COLOR_LUCK = new(1.0f, 0.75f, 0.20f, 1f);

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
        public Image      barFill;
        public Image[]    badgeBGs    = new Image[2];
        public TMP_Text[] badgeLabels = new TMP_Text[2];
        public int        zoneIdx;
    }

    // ── Private fields ──
    private readonly Dictionary<string, ZoneRow> _rows  = new();
    private Transform   _rowContainer;
    private GameObject  _emptyLabelGO;

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

    public void Refresh(IReadOnlyDictionary<string, int> clusterSizes)
    {
        // 활성 존 판별
        var active = new HashSet<string>();
        for (int i = 0; i < ZONE_ORDER.Length; i++)
        {
            int cluster = 0;
            clusterSizes?.TryGetValue(ZONE_ORDER[i], out cluster);
            if (cluster > 0) active.Add(ZONE_ORDER[i]);
        }

        // 비활성 → 행 제거
        var toRemove = new List<string>();
        foreach (var key in _rows.Keys)
            if (!active.Contains(key)) toRemove.Add(key);
        foreach (var key in toRemove)
        {
            if (_rows[key].go != null) Destroy(_rows[key].go);
            _rows.Remove(key);
        }

        // 활성 → 행 추가/갱신
        foreach (var zoneId in active)
        {
            int idx     = System.Array.IndexOf(ZONE_ORDER, zoneId);
            int cluster = 0;
            clusterSizes?.TryGetValue(zoneId, out cluster);

            if (!_rows.TryGetValue(zoneId, out var row))
            {
                row = BuildRow(zoneId, idx);
                _rows[zoneId] = row;
            }
            RefreshRow(row, zoneId, cluster);
        }

        // 빈 상태 레이블
        _emptyLabelGO?.SetActive(active.Count == 0);

        // 툴팁 갱신
        if (!string.IsNullOrEmpty(_hoveredZoneId))
        {
            if (active.Contains(_hoveredZoneId) && _rows.TryGetValue(_hoveredZoneId, out var tr))
            {
                int cluster = 0;
                clusterSizes?.TryGetValue(_hoveredZoneId, out cluster);
                UpdateTooltipContent(_hoveredZoneId, cluster);
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
        titleTxt.text          = "◆ 연결 시너지";
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
        descTxt.enableWordWrapping = false;
        descTxt.raycastTarget      = false;
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
        nameTxt.enableWordWrapping = false;
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
        countTxt.enableWordWrapping = false;
        countTxt.raycastTarget      = false;
        row.countText = countTxt;

        // 프로그레스 바 (30~64%)
        var barBG = new GameObject("BarBG", typeof(RectTransform));
        barBG.transform.SetParent(row.go.transform, false);
        var brt = barBG.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0.30f, 0.28f);
        brt.anchorMax = new Vector2(0.64f, 0.72f);
        brt.offsetMin = new Vector2(2f, 0f);
        brt.offsetMax = new Vector2(-2f, 0f);
        barBG.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.16f, 0.85f);

        var barFillGO = new GameObject("Fill", typeof(RectTransform));
        barFillGO.transform.SetParent(barBG.transform, false);
        var frrt = barFillGO.GetComponent<RectTransform>();
        frrt.anchorMin = Vector2.zero;
        frrt.anchorMax = Vector2.one;
        frrt.offsetMin = new Vector2(1f, 1f);
        frrt.offsetMax = new Vector2(-1f, -1f);
        var fillImg = barFillGO.AddComponent<Image>();
        fillImg.type       = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillAmount = 0f;
        fillImg.color      = new Color(0.4f, 0.4f, 0.5f, 0.5f);
        fillImg.raycastTarget = false;
        row.barFill = fillImg;

        // 임계값 배지 x2 (64~99%)
        for (int b = 0; b < 2; b++)
        {
            float bL = 0.64f + b * 0.178f + 0.004f;
            float bR = 0.64f + (b + 1) * 0.178f - 0.004f;

            var badgeGO = new GameObject($"Badge{b}", typeof(RectTransform));
            badgeGO.transform.SetParent(row.go.transform, false);
            var badgeRT = badgeGO.GetComponent<RectTransform>();
            badgeRT.anchorMin = new Vector2(bL, 0.10f);
            badgeRT.anchorMax = new Vector2(bR, 0.90f);
            badgeRT.offsetMin = badgeRT.offsetMax = Vector2.zero;
            row.badgeBGs[b] = badgeGO.AddComponent<Image>();
            row.badgeBGs[b].color = COLOR_BADGE_OFF;
            row.badgeBGs[b].raycastTarget = false;

            var lblGO = new GameObject("Lbl", typeof(RectTransform));
            lblGO.transform.SetParent(badgeGO.transform, false);
            var lrt = lblGO.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(2f, 0f);
            lrt.offsetMax = new Vector2(-2f, 0f);
            var lTxt = lblGO.AddComponent<TextMeshProUGUI>();
            lTxt.text               = b == 0 ? "각성 —" : "완성 —";
            lTxt.fontSize           = 9.5f;
            lTxt.color              = new Color(0.50f, 0.53f, 0.66f, 1f);
            lTxt.alignment          = TextAlignmentOptions.Center;
            lTxt.enableWordWrapping = false;
            lTxt.raycastTarget      = false;
            row.badgeLabels[b] = lTxt;
        }

        // 호버 → 툴팁
        var et = row.go.AddComponent<EventTrigger>();
        string capturedId = zoneId;

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ =>
        {
            _hoveredZoneId = capturedId;
            int c = 0;
            MerlinRuneBridge.Instance?.GetLastClusterSizes()?.TryGetValue(capturedId, out c);
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

    private void RefreshRow(ZoneRow row, string zoneId, int cluster)
    {
        Color zoneColor = GetZoneColor(row.zoneIdx);

        if (row.countText != null)
            row.countText.SetText(cluster.ToString());

        var synergies = Managers.RuneData?.GetZoneSynergies(zoneId);
        var sorted    = new List<RuneSynergyEntry>();
        if (synergies != null) sorted.AddRange(synergies);
        sorted.Sort((a, b) => a.threshold.CompareTo(b.threshold));

        // 프로그레스 바
        if (row.barFill != null)
        {
            float fill = 0f;
            bool  anyMet = false;
            if (sorted.Count > 0)
            {
                anyMet = cluster >= sorted[0].threshold;
                bool allMet = cluster >= sorted[sorted.Count - 1].threshold;
                if (allMet) { fill = 1f; }
                else
                {
                    int prev = 0, next = sorted[sorted.Count - 1].threshold;
                    foreach (var e in sorted)
                    {
                        if (cluster < e.threshold) { next = e.threshold; break; }
                        prev = e.threshold;
                    }
                    fill = next > prev ? (float)(cluster - prev) / (next - prev) : 0f;
                }
            }
            row.barFill.fillAmount = Mathf.Clamp01(fill);
            row.barFill.color = anyMet
                ? new Color(zoneColor.r, zoneColor.g, zoneColor.b, 0.88f)
                : new Color(zoneColor.r * 0.5f, zoneColor.g * 0.5f, zoneColor.b * 0.5f, 0.45f);
        }

        // 배지
        for (int b = 0; b < 2; b++)
        {
            if (row.badgeBGs[b] == null) continue;
            bool  hasData = b < sorted.Count;
            bool  met     = hasData && cluster >= sorted[b].threshold;
            string tier   = b == 0 ? "각성" : "완성";
            string label  = met && hasData
                ? FormatEffectShort(sorted[b])
                : (hasData ? $"{tier} {sorted[b].threshold}" : "—");

            row.badgeBGs[b].color = met
                ? new Color(zoneColor.r * 0.40f, zoneColor.g * 0.40f, zoneColor.b * 0.40f, 0.95f)
                : COLOR_BADGE_OFF;

            if (row.badgeLabels[b] != null)
            {
                row.badgeLabels[b].text     = label;
                row.badgeLabels[b].fontSize  = met ? 9.0f : 9.5f;
                row.badgeLabels[b].color     = met
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
        _tooltipText.enableWordWrapping = true;
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

    private void UpdateTooltipContent(string zoneId, int cluster)
    {
        if (_tooltipText == null) return;
        int idx = System.Array.IndexOf(ZONE_ORDER, zoneId);
        if (idx < 0) return;

        var synergies = Managers.RuneData?.GetZoneSynergies(zoneId);
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"<b><color=#B8E0FF>{ZONE_ICONS[idx]} {ZONE_NAMES[idx]} ({zoneId})</color></b>");
        sb.AppendLine($"현재 클러스터: <b>{cluster}</b>");
        sb.AppendLine();

        if (synergies != null && synergies.Count > 0)
        {
            var sorted = new List<RuneSynergyEntry>(synergies);
            sorted.Sort((a, b) => a.threshold.CompareTo(b.threshold));

            foreach (var s in sorted)
            {
                bool met   = cluster >= s.threshold;
                string chk = met ? "<color=#55FF88>●</color>" : "○";
                string tier = s.threshold == sorted[0].threshold ? "각성" : "완성";
                float pct  = s.value * 100f;
                sb.AppendLine($"{chk} {tier} ({s.threshold}): {FormatEffectShort(s)}");
            }

            int maxThr = sorted[sorted.Count - 1].threshold;
            if (cluster < maxThr)
            {
                int nextThr = 0;
                foreach (var s in sorted)
                    if (cluster < s.threshold) { nextThr = s.threshold; break; }
                if (nextThr > 0)
                    sb.AppendLine($"\n다음 효과까지 <b>{nextThr - cluster}개</b> 더 필요");
            }
        }
        else
        {
            sb.AppendLine("(시너지 데이터 없음)");
        }

        _tooltipText.SetText(sb.ToString().TrimEnd());
    }

    // ── Helpers ──

    private static string FormatEffectShort(RuneSynergyEntry e)
    {
        float  pct      = e.value * 100f;
        string sign     = pct >= 0f ? "+" : "";
        string abbr = e.effect_type switch
        {
            "AttackPowerUp"     => "공격",
            "DefenseUp"         => "방어",
            "MagicPowerUp"      => "마력",
            "SpeedUp"           => "속도",
            "LuckUp"            => "행운",
            "ShieldAccumulate"  => "방패",
            "HpRecovery"        => "회복",
            "MaxHpUp"           => "MaxHP",
            "SkillCooldownDown" => "CDR",
            _                   => e.effect_type is { Length: > 5 }
                                       ? e.effect_type[..5]
                                       : (e.effect_type ?? "?"),
        };
        string trigIcon = e.trigger switch
        {
            "Always"  => "",
            "OnHit"   => "피격·",
            "OnLowHp" => "체력↓·",
            "OnKill"  => "처치·",
            "OnUse"   => "사용·",
            _         => "",
        };
        return $"✓{trigIcon}{abbr} {sign}{pct:F0}%";
    }

    private static Color GetZoneColor(int idx) => idx switch
    {
        0 => COLOR_ATK,  1 => COLOR_DEF,  2 => COLOR_MAG,
        3 => COLOR_HP,   4 => COLOR_SPD,  5 => COLOR_LUCK,
        _ => Color.white,
    };
}
