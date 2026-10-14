using UnityEngine;

/// <summary>
/// 유물 진화(10-14 빌드 시너지 C) — 빌드의 완성. 보스 처치 기억 카드에 「진화」 4번째 카드가 열린다(런당 1회 고름).
/// <para>레시피: 그 유물 조각 4개 이상 + 연격 · 필살 · 사격 중 한 계열 각인 4 이상. 방향 = 그 계열(가장 높은 것 · 동률이면 무기 계열).</para>
/// <para>진화는 그 계열 각인 +2를 준다(빌드를 한 단계 밀어 줌) · 효과는 조각 효과 틀(<see cref="RelicPartEffect"/>)로 붙는다.</para>
/// </summary>
public static class RelicEvolution
{
    public const int FragmentsNeeded = 4, ImprintNeeded = 4, ImprintBonus = 2;

    public sealed class Def
    {
        public int Index;
        public string Id, RelicId, Name, Desc;
        public BuildFamily Family;
    }

    public static readonly Def[] All =
    {
        new Def { Index = 1, Id = "evo_gawain_combo",    RelicId = "gawain",   Family = BuildFamily.Combo,  Name = "타오르는 정오",
                  Desc = "정오 동안 적중마다 태양흔(같은 적 간격 없음) · 넘친 공격속도가 태양 화상을 키운다" },
        new Def { Index = 2, Id = "evo_gawain_crit",     RelicId = "gawain",   Family = BuildFamily.Crit,   Name = "천정의 일격",
                  Desc = "정오에 강타 · 치명이 작은 낙일을 떨어뜨린다(1초에 한 번)" },
        new Def { Index = 3, Id = "evo_gawain_ranged",   RelicId = "gawain",   Family = BuildFamily.Ranged, Name = "태양의 화살",
                  Desc = "정오에 원거리 명중 5번마다 그 자리에 작은 해가 떨어진다" },
        new Def { Index = 4, Id = "evo_lancelot_combo",  RelicId = "lancelot", Family = BuildFamily.Combo,  Name = "끝없는 광란",
                  Desc = "광기를 1.5배로 얻는다 · 광란 중 10타마다 광란 +1초" },
        new Def { Index = 5, Id = "evo_lancelot_crit",   RelicId = "lancelot", Family = BuildFamily.Crit,   Name = "심판자",
                  Desc = "강타가 낙인 2개를 새긴다 · 심판 마무리 둘레 3m에 치명 몫(마무리 피해 50%)을 더 준다" },
        new Def { Index = 6, Id = "evo_lancelot_ranged", RelicId = "lancelot", Family = BuildFamily.Ranged, Name = "배신의 화살",
                  Desc = "원거리 적중이 낙인을 새긴다(같은 적 0.3초에 한 번)" },
    };

    public static Def ById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var d in All) if (d.Id == id) return d;
        return null;
    }

    public static Def ByIndex(int index)
    {
        foreach (var d in All) if (d.Index == index) return d;
        return null;
    }

    /// <summary>
    /// 지금 열리는 진화(없으면 null) — 아직 진화하지 않았고, 그 유물 조각이 4개 이상이고, 연격 · 필살 · 사격 중 각인 4 이상인 계열이 있을 때.
    /// 여럿이면 각인이 가장 높은 계열, 같으면 들고 있는 무기의 계열.
    /// </summary>
    public static Def Ready(PlayerLoadout l, string relicId)
    {
        if (l == null || !string.IsNullOrEmpty(l.RelicEvolutionId) || string.IsNullOrEmpty(relicId)) return null;
        if (l.RelicPartIds == null || l.RelicPartIds.Count < FragmentsNeeded) return null;

        BuildFamily best = BuildFamily.None;
        int bestCount = 0;
        var wm = GameRunBootstrapper.Instance?.Run?.Player != null ? GameRunBootstrapper.Instance.Run.Player.WeaponManager : null;
        var weaponFam = wm != null ? BuildFamilyRules.OfWeapon(wm.Weapon0Data) : BuildFamily.None;
        foreach (var f in new[] { BuildFamily.Combo, BuildFamily.Crit, BuildFamily.Ranged })
        {
            int c = BuildImprint.Count(f);
            if (c < ImprintNeeded) continue;
            if (c > bestCount || (c == bestCount && f == weaponFam)) { best = f; bestCount = c; }
        }
        if (best == BuildFamily.None) return null;
        foreach (var d in All) if (d.RelicId == relicId && d.Family == best) return d;
        return null;
    }

    /// <summary>카드 꼬리말 — 「조각 4 · 연격 각인 4 충족」(✓ 기호는 글리프 밖이라 쓰지 않는다).</summary>
    public static string RecipeLine(PlayerLoadout l, Def d)
    {
        int parts = l?.RelicPartIds?.Count ?? 0;
        return $"조각 {parts}/{FragmentsNeeded} · {BuildFamilyRules.Label(d.Family)} 각인 {BuildImprint.Count(d.Family)}/{ImprintNeeded} 충족 — 각인 +{ImprintBonus}";
    }

    /// <summary>진화 효과(조각 효과 틀) 만들기.</summary>
    public static IRelicPartEffect CreateEffect(string id) => id switch
    {
        "evo_gawain_combo"    => new GEvoBlazingNoonEffect(),
        "evo_gawain_crit"     => new GEvoZenithStrikeEffect(),
        "evo_gawain_ranged"   => new GEvoSunArrowEffect(),
        "evo_lancelot_combo"  => new LEvoEndlessFrenzyEffect(),
        "evo_lancelot_crit"   => new LEvoJudgeEffect(),
        "evo_lancelot_ranged" => new LEvoTraitorArrowEffect(),
        _                     => null,
    };

    /// <summary>타오르는 정오 — 넘친 공속이 태양 화상을 키운다(넘친 피해 % × 2).</summary>
    public static float BurnMul(PlayerController p)
    {
        var l = AppBootstrapper.Instance?.Loadout;
        if (p == null || l == null || l.RelicEvolutionId != "evo_gawain_combo") return 1f;
        return 1f + 2f * p.RuntimeStats.AttackSpeedOverflowDamage;
    }
}
