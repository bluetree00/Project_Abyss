using UnityEngine;

/// <summary>
/// 전설 룬 각성 진척(10-24 전설 룬 개편 · 기획/RelicFairy_전설룬_개편_설계_20261024.md §4) — 전설은 그 속성 존이 4단계면
/// 「각성」해 규칙이 하나 더 붙는다. 룬판 상세 카드 한 줄과 각성 순간 띠가 같은 셈을 쓴다.
/// 각성 여부 · 단계의 정본은 <see cref="LegendaryTuning"/>(효과 쪽)이고, 여기는 칸 수로 「각성까지 n칸」만 보탠다.
/// </summary>
public static class LegendaryAwaken
{
    // ── Constants ──
    private const string GoldHex = "#FFCC4D";   // UITheme.Thin.Gold

    // ── Public Methods ──
    /// <summary>존 4단계 문턱(칸) — 그 존 시너지 표의 가장 높은 문턱. 표가 없으면 0.</summary>
    public static int AwakenCells(string zoneId)
    {
        var entries = Managers.RuneData?.GetZoneSynergies(zoneId);
        int max = 0;
        if (entries != null)
            foreach (var e in entries) if (e.threshold > max) max = e.threshold;
        return max;
    }

    /// <summary>그 존에 지금 찬 칸.</summary>
    public static int Cells(string zoneId)
    {
        var counts = MerlinRuneBridge.Instance?.GetZoneOccupiedCounts();
        return counts != null && counts.TryGetValue(zoneId, out int c) ? c : 0;
    }

    /// <summary>전설 룬 카드 한 줄 — 「불 존 2/4 · 각성까지 9칸」, 각성이면 「각성 — 불 존 4단계」. 전설이 아니면 false.</summary>
    public static bool TryLine(RuntimeItemData item, out string line)
    {
        line = null;
        if (item == null || item.rarity != ItemRarity.Legendary || string.IsNullOrEmpty(item.element)) return false;
        string zone = item.element;
        var elem = ElementDef.GetById(zone);
        string name = elem != null ? $"<color={ElementDef.IdHex(zone)}>{elem.Name}</color>" : zone;
        if (LegendaryTuning.Awakened(zone))
        {
            line = $"<color={GoldHex}>각성</color> — {name} 존 4단계";
            return true;
        }
        int need = Mathf.Max(0, AwakenCells(zone) - Cells(zone));
        line = need > 0
            ? $"{name} 존 {LegendaryTuning.Tier(zone)}/4 · 각성까지 {need}칸"
            : $"{name} 존 {LegendaryTuning.Tier(zone)}/4";
        return true;
    }

    /// <summary>카드 설명에서 각성 줄(「각성」부터 끝까지) — 각성 띠의 둘째 줄. 없으면 빈 글.</summary>
    public static string AwakenClause(RuntimeItemData item)
    {
        if (item?.effects == null) return string.Empty;
        foreach (var slot in item.effects)
        {
            string d = slot?.description;
            if (string.IsNullOrEmpty(d)) continue;
            int i = d.IndexOf("각성", System.StringComparison.Ordinal);
            if (i >= 0) return d.Substring(i);
        }
        return string.Empty;
    }
}
