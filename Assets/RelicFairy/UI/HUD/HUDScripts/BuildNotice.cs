using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 빌드 완성 신호 한 줄(10-14 빌드 시너지 · 83 UI 요청 3) — 각인 단계 오름 · 유물 진화 · 속성 존 단계 오름.
/// 알림 길은 그대로(<see cref="ItemEffectVfxHelper"/> → HUD 왼쪽 쌓이는 줄)이고, 줄 앞에 계열 · 속성 색 칩 + 단계 눈금을 단다.
/// 글은 코드가 실제로 하는 것만 — 각인 = <see cref="BuildFamilyRules.StageBonusText"/>, 존 = 시너지 차트 설명의 단계 이름, 진화 = <see cref="RelicEvolution.Def"/>.
/// </summary>
public readonly struct BuildNotice
{
    /// <summary>같은 프레임에 들어온 신호는 이 순서로 보인다 — 진화가 각인 +2를 낳으므로 진화가 먼저.</summary>
    public enum Kind { Evolution = 0, Imprint = 1, Zone = 2 }

    // ── Properties ───────────────────────────────────────
    public Kind        What        { get; }
    public string      Key         { get; }   // 보류 중 같은 열쇠는 하나로 합친다(imprint.Combo · zone.FIRE · evo)
    public string      Chip        { get; }   // 칩 글 — 계열 · 속성 이름 · 「유물 진화」
    public Color       Tint        { get; }   // 칩 · 눈금 · 강조선 색
    public int         Tier        { get; }
    public int         MaxTier     { get; }   // 0이면 눈금 없음
    public string      Head        { get; }   // 첫 줄 — 칩 · 눈금 뒤(색 태그 가능)
    public string      Detail      { get; }   // 둘째 줄 — 하는 일(없으면 한 줄)
    public BuildFamily Family      { get; }
    public string      ZoneId      { get; }
    public string      EvolutionId { get; }

    private BuildNotice(Kind what, string key, string chip, Color tint, int tier, int maxTier, string head, string detail,
                        BuildFamily family, string zoneId, string evolutionId)
    {
        What = what; Key = key; Chip = chip; Tint = tint; Tier = tier; MaxTier = maxTier; Head = head; Detail = detail;
        Family = family; ZoneId = zoneId; EvolutionId = evolutionId;
    }

    // ── Public Methods ───────────────────────────────────

    /// <summary>각인 단계 오름(각인 2 · 4 · 6 = 1 · 2 · 3단계) — 「연격 ◆◆◇ 각인 2단계」 / 「룬 효과 +40% · …」.</summary>
    public static BuildNotice Imprint(BuildFamily f, int stage)
    {
        string label = BuildFamilyRules.Label(f);
        string bonus = BuildFamilyRules.StageBonusText(f, stage);
        if (bonus.StartsWith(label + " ")) bonus = bonus.Substring(label.Length + 1);   // 칩이 계열 이름을 이미 말한다
        return new BuildNotice(Kind.Imprint, "imprint." + f, label, BuildFamilyRules.ColorOf(f),
            stage, BuildFamilyRules.Thresholds.Length, $"각인 {stage}단계", bonus, f, null, null);
    }

    /// <summary>유물 진화 — 「유물 진화 「타오르는 정오」」 / 「연격 각인 +2」(문장 그림은 뷰가 유물에서 읽는다).</summary>
    public static BuildNotice Evolution(RelicEvolution.Def d)
    {
        if (d == null) return default;
        return new BuildNotice(Kind.Evolution, "evo", "유물 진화", BuildFamilyRules.ColorOf(d.Family), 0, 0,
            $"<color={BuildFamilyRules.Hex(d.Family)}>「{d.Name}」</color>", $"{BuildFamilyRules.Label(d.Family)} 각인 +{RelicEvolution.ImprintBonus}",
            d.Family, null, d.Id);
    }

    /// <summary>속성 존 단계 오름(1~4) — 「불 ◆◆◇◇ 존 2단계 — 점화」. 단계 이름은 시너지 차트 설명 앞머리(「2단계 점화: …」).</summary>
    public static BuildNotice Zone(string zoneId, int tier)
    {
        var el   = ElementDef.GetById(zoneId);
        string name = TierName(zoneId, tier);
        return new BuildNotice(Kind.Zone, "zone." + zoneId, el != null ? el.Name : zoneId,
            ElementDef.IdColor(zoneId, UITheme.Thin.Ink), tier, MaxZoneTier,
            string.IsNullOrEmpty(name) ? $"존 {tier}단계" : $"존 {tier}단계 — {name}", null, BuildFamily.None, zoneId, null);
    }

    /// <summary>보류했다 보일 때 아직 참인가 — 룬판에서 놓았다 뺀 단계는 내지 않는다.</summary>
    public bool StillHolds() => What switch
    {
        Kind.Imprint   => BuildImprint.Stage(Family) >= Tier,
        Kind.Zone      => MerlinRuneBridge.Instance != null && MerlinRuneBridge.Instance.GetZoneTier(ZoneId) >= Tier,
        Kind.Evolution => !string.IsNullOrEmpty(EvolutionId),
        _              => false,
    };

    // ── Private ──────────────────────────────────────────
    private const int MaxZoneTier = 4;   // 존 시너지 4단계(BlockSynergyBridge — 4단계 = 존 전부)

    /// <summary>존 <paramref name="tier"/>단계 이름 — 차트 행을 임계값 순으로 세어 그 행 설명의 「N단계 이름:」에서 이름만.</summary>
    private static string TierName(string zoneId, int tier)
    {
        var rows = Managers.RuneData?.GetZoneSynergies(zoneId);
        if (rows == null || tier <= 0 || tier > rows.Count) return null;
        var sorted = new List<RuneSynergyEntry>(rows);
        sorted.RemoveAll(e => e == null);
        sorted.Sort((a, b) => a.threshold.CompareTo(b.threshold));
        if (tier > sorted.Count) return null;
        string desc = sorted[tier - 1].description;
        if (string.IsNullOrEmpty(desc)) return null;
        int colon = desc.IndexOf(':');
        string head = (colon > 0 ? desc.Substring(0, colon) : desc).Trim();
        int sp = head.IndexOf("단계", System.StringComparison.Ordinal);
        if (sp >= 0) head = head.Substring(sp + 2).Trim();
        return head.Length > 0 ? head : null;
    }
}
