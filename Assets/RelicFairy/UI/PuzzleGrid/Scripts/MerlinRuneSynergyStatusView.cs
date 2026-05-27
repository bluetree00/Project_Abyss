using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 존별 시너지 달성 상태 뷰.
///
/// ATK / DEF / MAG / HP / SPD / LUCK 6개 존을 가로 컬럼으로 나열하고,
/// 각 존의 채운 셀 수, 도트 게이지, threshold별 효과 달성 여부를 표시한다.
///
/// Refresh(Dictionary) 호출로 실시간 갱신된다.
/// BuildColumns() 은 Awake에서 호출해 UI를 먼저 생성한다.
/// </summary>
public sealed class MerlinRuneSynergyStatusView : MonoBehaviour
{
    // ── Constants ──
    private static readonly string[] ZONE_ORDER = { "ATK", "DEF", "MAG", "HP", "SPD", "LUCK" };

    private static readonly string[] ZONE_ICONS = { "⚔", "🛡", "✨", "❤", "👣", "🍀" };

    private static readonly Color COLOR_ATK    = new(1.0f, 0.35f, 0.30f, 1f);
    private static readonly Color COLOR_DEF    = new(0.3f, 0.55f, 1.00f, 1f);
    private static readonly Color COLOR_MAG    = new(0.7f, 0.30f, 1.00f, 1f);
    private static readonly Color COLOR_HP     = new(0.3f, 0.85f, 0.45f, 1f);
    private static readonly Color COLOR_SPD    = new(1.0f, 0.85f, 0.20f, 1f);
    private static readonly Color COLOR_LUCK   = new(1.0f, 0.75f, 0.20f, 1f);

    private static readonly Color COLOR_BG_PANEL  = new(0.06f, 0.07f, 0.10f, 0.90f);
    private static readonly Color COLOR_DOT_ON     = new(0.95f, 0.95f, 1.00f, 1.00f);
    private static readonly Color COLOR_DOT_OFF    = new(0.25f, 0.25f, 0.32f, 0.80f);
    private static readonly Color COLOR_EFFECT_OK  = new(0.30f, 1.00f, 0.55f, 1.00f);
    private static readonly Color COLOR_EFFECT_YET = new(0.55f, 0.55f, 0.65f, 0.85f);

    private const int DOT_MAX   = 5;
    private const float DOT_SIZE = 8f;
    private const float DOT_GAP  = 3f;

    // ── Private fields ──

    // 존 인덱스별 UI 참조
    private TMP_Text[]   _countTexts;   // "N/M"
    private GameObject[] _dotRoots;     // 도트 5개 부모
    private Image[][]    _dotImages;    // [zoneIdx][dotIdx]
    private TMP_Text[]   _effectTexts;  // "+10% ✓" or "N개 남"

    // 존별 총 셀 수 (존맵에서 계산)
    private int[] _zoneTotalCells;

    // ── Lifecycle ──

    private void Awake()
    {
        gameObject.AddComponent<Image>().color = COLOR_BG_PANEL;
        BuildColumns();
    }

    // ── Public API ──

    /// <summary>
    /// 존별 배치 셀 수를 받아 시너지 상태를 갱신한다.
    /// UI_GridPanel 또는 외부 컨트롤러에서 호출.
    /// </summary>
    public void Refresh(Dictionary<string, int> cellCountByZone)
    {
        if (_countTexts == null) return;

        for (int i = 0; i < ZONE_ORDER.Length; i++)
        {
            string zoneId   = ZONE_ORDER[i];
            int    count    = (cellCountByZone != null && cellCountByZone.TryGetValue(zoneId, out var c)) ? c : 0;
            int    total    = _zoneTotalCells != null ? _zoneTotalCells[i] : 0;

            // "N/M" 카운트
            if (_countTexts[i] != null)
                _countTexts[i].SetText($"{count}/{total}");

            // 도트 표시 (최대 DOT_MAX)
            if (_dotImages != null && _dotImages[i] != null)
            {
                int dotFilled = total > 0 ? Mathf.RoundToInt((float)count / total * DOT_MAX) : 0;
                dotFilled = Mathf.Clamp(dotFilled, 0, DOT_MAX);
                for (int d = 0; d < DOT_MAX; d++)
                {
                    if (_dotImages[i][d] != null)
                        _dotImages[i][d].color = d < dotFilled ? COLOR_DOT_ON : COLOR_DOT_OFF;
                }
            }

            // threshold 효과 텍스트
            if (_effectTexts[i] != null)
            {
                _effectTexts[i].text  = BuildEffectText(zoneId, count);
                _effectTexts[i].color = IsThresholdMet(zoneId, count) ? COLOR_EFFECT_OK : COLOR_EFFECT_YET;
            }
        }
    }

    // ── Build UI ──

    private void BuildColumns()
    {
        int count = ZONE_ORDER.Length;
        _countTexts  = new TMP_Text[count];
        _dotRoots    = new GameObject[count];
        _dotImages   = new Image[count][];
        _effectTexts = new TMP_Text[count];

        for (int i = 0; i < count; i++)
        {
            float anchorL = (float)i / count;
            float anchorR = (float)(i + 1) / count;

            var colGO = new GameObject($"Col_{ZONE_ORDER[i]}", typeof(RectTransform));
            colGO.transform.SetParent(transform, false);
            var colRT = colGO.GetComponent<RectTransform>();
            colRT.anchorMin        = new Vector2(anchorL + 0.005f, 0f);
            colRT.anchorMax        = new Vector2(anchorR - 0.005f, 1f);
            colRT.offsetMin        = Vector2.zero;
            colRT.offsetMax        = Vector2.zero;

            var colBG = colGO.AddComponent<Image>();
            colBG.color = new Color(0.08f, 0.09f, 0.13f, 0.60f);

            Color zoneColor = GetZoneColor(i);

            // 상단 악센트 라인
            var accentGO = new GameObject("Accent", typeof(RectTransform));
            accentGO.transform.SetParent(colGO.transform, false);
            var accentRT = accentGO.GetComponent<RectTransform>();
            accentRT.anchorMin        = new Vector2(0f, 1f);
            accentRT.anchorMax        = new Vector2(1f, 1f);
            accentRT.sizeDelta        = new Vector2(0f, 3f);
            accentRT.anchoredPosition = Vector2.zero;
            accentGO.AddComponent<Image>().color = zoneColor;

            // 아이콘 + 이름 헤더
            var headerGO = MakeTxt(colGO.transform, "Header",
                $"{ZONE_ICONS[i]} {ZONE_ORDER[i]}", 10f, zoneColor);
            var headerRT = headerGO.GetComponent<RectTransform>();
            headerRT.anchorMin = new Vector2(0f, 0.72f);
            headerRT.anchorMax = new Vector2(1f, 1.00f);
            headerRT.offsetMin = new Vector2(4f, 0f);
            headerRT.offsetMax = Vector2.zero;

            // 셀 카운트 "N/M"
            var countGO = MakeTxt(colGO.transform, "Count", "0/0", 9f,
                new Color(0.75f, 0.80f, 0.95f, 1f));
            var countRT = countGO.GetComponent<RectTransform>();
            countRT.anchorMin = new Vector2(0f, 0.52f);
            countRT.anchorMax = new Vector2(1f, 0.70f);
            countRT.offsetMin = new Vector2(4f, 0f);
            countRT.offsetMax = Vector2.zero;
            _countTexts[i] = countGO.GetComponent<TMP_Text>();

            // 도트 게이지 영역
            var dotRootGO = new GameObject("DotRoot", typeof(RectTransform));
            dotRootGO.transform.SetParent(colGO.transform, false);
            var dotRootRT = dotRootGO.GetComponent<RectTransform>();
            dotRootRT.anchorMin = new Vector2(0f, 0.34f);
            dotRootRT.anchorMax = new Vector2(1f, 0.50f);
            dotRootRT.offsetMin = new Vector2(4f, 0f);
            dotRootRT.offsetMax = new Vector2(-4f, 0f);
            _dotRoots[i]   = dotRootGO;
            _dotImages[i]  = BuildDots(dotRootGO.transform, zoneColor);

            // 효과 텍스트 "+10% ✓" or "N개 남"
            var effectGO = MakeTxt(colGO.transform, "Effect", "—", 8.5f, COLOR_EFFECT_YET);
            var effectRT = effectGO.GetComponent<RectTransform>();
            effectRT.anchorMin = new Vector2(0f, 0.02f);
            effectRT.anchorMax = new Vector2(1f, 0.32f);
            effectRT.offsetMin = new Vector2(4f, 0f);
            effectRT.offsetMax = new Vector2(-4f, 0f);
            var effectTxt = effectGO.GetComponent<TMP_Text>();
            effectTxt.enableWordWrapping = true;
            effectTxt.alignment = TextAlignmentOptions.TopLeft;
            _effectTexts[i] = effectTxt;
        }
    }

    private Image[] BuildDots(Transform parent, Color zoneColor)
    {
        var images = new Image[DOT_MAX];
        float totalW = DOT_MAX * DOT_SIZE + (DOT_MAX - 1) * DOT_GAP;
        float startX = -totalW * 0.5f + DOT_SIZE * 0.5f;

        for (int d = 0; d < DOT_MAX; d++)
        {
            var dotGO = new GameObject($"Dot{d}", typeof(RectTransform));
            dotGO.transform.SetParent(parent, false);
            var rt = dotGO.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.sizeDelta        = new Vector2(DOT_SIZE, DOT_SIZE);
            rt.anchoredPosition = new Vector2(startX + d * (DOT_SIZE + DOT_GAP), 0f);

            var img = dotGO.AddComponent<Image>();
            img.color         = COLOR_DOT_OFF;
            img.raycastTarget = false;
            images[d] = img;
        }
        return images;
    }

    // ── Zone total cells from zone map ──

    /// <summary>RuneDataManager 로드 완료 후 1회 호출. UI_GridPanel.OpenPanel()에서 보장된다.</summary>
    public void CacheZoneTotals()
    {
        _zoneTotalCells = new int[ZONE_ORDER.Length];
        var runeData = Managers.RuneData;
        if (runeData == null) return;

        var rows = runeData.GetZoneMapRows();
        if (rows == null) return;

        // 존 코드 → 인덱스 매핑
        var codeToIdx = new Dictionary<char, int>
        {
            { 'A', 0 }, { 'D', 1 }, { 'M', 2 },
            { 'H', 3 }, { 'S', 4 }, { 'L', 5 },
        };

        foreach (var row in rows)
        {
            if (row.pattern == null) continue;
            foreach (char ch in row.pattern)
            {
                if (codeToIdx.TryGetValue(ch, out int idx))
                    _zoneTotalCells[idx]++;
            }
        }
    }

    // ── Effect text helpers ──

    private static string BuildEffectText(string zoneId, int count)
    {
        var synergies = Managers.RuneData?.GetZoneSynergies(zoneId);
        if (synergies == null || synergies.Count == 0) return "—";

        // threshold 기준으로 정렬
        var sorted = new List<RuneSynergyEntry>(synergies);
        sorted.Sort((a, b) => a.threshold.CompareTo(b.threshold));

        var lines = new System.Text.StringBuilder();
        foreach (var s in sorted)
        {
            if (s.threshold <= 0) continue;
            bool met = count >= s.threshold;
            float pct = s.value * 100f;
            string pctStr = $"{(pct >= 0f ? "+" : "")}{pct:F0}%";
            if (met)
                lines.Append($"{pctStr} ✓\n");
            else
            {
                int remaining = s.threshold - count;
                lines.Append($"{pctStr} ({remaining}개 남)\n");
            }
        }

        string result = lines.ToString().TrimEnd('\n');
        return string.IsNullOrEmpty(result) ? "—" : result;
    }

    private static bool IsThresholdMet(string zoneId, int count)
    {
        var synergies = Managers.RuneData?.GetZoneSynergies(zoneId);
        if (synergies == null) return false;
        foreach (var s in synergies)
            if (s.threshold > 0 && count >= s.threshold) return true;
        return false;
    }

    private static Color GetZoneColor(int idx) => idx switch
    {
        0 => COLOR_ATK,
        1 => COLOR_DEF,
        2 => COLOR_MAG,
        3 => COLOR_HP,
        4 => COLOR_SPD,
        5 => COLOR_LUCK,
        _ => Color.white,
    };

    // ── Helpers ──

    private static GameObject MakeTxt(Transform parent, string name,
        string text, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text          = text;
        t.fontSize      = size;
        t.color         = color;
        t.raycastTarget = false;
        t.enableWordWrapping = false;
        t.alignment     = TextAlignmentOptions.MidlineLeft;
        return go;
    }
}
