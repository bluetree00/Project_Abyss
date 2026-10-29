using UnityEngine;

/// <summary>
/// 유물 진화(10-14 빌드 시너지 C) — 빌드의 완성. 보스 처치 기억 카드 두 장 중 둘째 자리에 「진화」가 열린다(런당 1회 고름 · 10-18 창 2장).
/// <para>10-22 E1 단순화(D1 · D2 · D3 · D4) — 계열 각인 문턱 · 「각인 +2」는 걷었다.
/// 10-29 조각 조건(2개)도 걷었다 — 조각 = 지나온 보스 수라 진화가 Ch3 보스에서만 처음 나왔다(사용자 「모든 챕터 동일하게 좋은 유물 진화」).
/// 이제 해방기부터 <b>보스마다</b>(Ch1 · Ch2 · Ch3) 둘째 자리가 진화 — 고르지 않으면 다음 보스에 다시 나온다.
/// 봉인기엔 진화가 없다(<see cref="Unlocked"/> — 해방기부터). 방향은 저절로(<see cref="Direction"/>) · 카드엔 계열 이름 대신 동작 말(<see cref="DirectionWord"/>).</para>
/// <para>효과는 조각 효과 틀(<see cref="RelicPartEffect"/>)로 붙는다.</para>
/// </summary>
public static class RelicEvolution
{
    private static readonly BuildFamily[] s_directions = { BuildFamily.Combo, BuildFamily.Crit, BuildFamily.Ranged };

    /// <summary>
    /// 진화가 열린 시기인가 — 해방기부터(봉인기엔 카드 · 안내 · 진행 줄 어디에도 없다, 10-22 E1 D2).
    /// 시기 흐름(A10 · 18)이 바뀌면 여기 한 곳만 고친다.
    /// </summary>
    public static bool Unlocked => StoryProgress.IsLiberated;

    public sealed class Def
    {
        public int Index;
        public string Id, RelicId, Name, Desc;
        public BuildFamily Family;
    }

    // 10-23 U7 — 본문은 하는 일 · 누르는 키로(심판 막타 → Q 마지막 일격 · 강타는 무엇인지 · 정오 화상은 맞힌 적이 불탄다)
    public static readonly Def[] All =
    {
        new Def { Index = 1, Id = "evo_gawain_combo",    RelicId = "gawain",   Family = BuildFamily.Combo,  Name = "타오르는 정오",
                  Desc = "정오에 맞힌 적은 불타고 태양흔이 1개씩 쌓여 가득 차면 터진다(같은 적 3초에 한 번). 공격속도가 최대치를 넘친 만큼 불이 세진다" },
        new Def { Index = 2, Id = "evo_gawain_crit",     RelicId = "gawain",   Family = BuildFamily.Crit,   Name = "천정의 일격",
                  Desc = "정오에 강타(대검 · 석궁처럼 묵직한 공격)나 치명타를 맞히면 그 적에게 작은 해가 떨어진다(1초에 한 번)" },
        new Def { Index = 3, Id = "evo_gawain_ranged",   RelicId = "gawain",   Family = BuildFamily.Ranged, Name = "태양의 화살",
                  Desc = "정오에 원거리로 5번 맞힐 때마다 마지막에 맞은 적에게 작은 해가 떨어진다" },
        new Def { Index = 4, Id = "evo_lancelot_combo",  RelicId = "lancelot", Family = BuildFamily.Combo,  Name = "끝없는 광란",
                  Desc = "공격으로 얻는 광기가 1.5배가 되고, 광란 중엔 적을 10번 맞힐 때마다 광란이 1초 늘어난다" },
        new Def { Index = 5, Id = "evo_lancelot_crit",   RelicId = "lancelot", Family = BuildFamily.Crit,   Name = "심판자",
                  Desc = "강타(대검 · 석궁처럼 묵직한 공격)는 낙인을 2개 쌓는다. Q 마지막 일격 때 내 둘레 3m 적이 그 피해의 50%를 치명타로 더 받는다" },
        new Def { Index = 6, Id = "evo_lancelot_ranged", RelicId = "lancelot", Family = BuildFamily.Ranged, Name = "배신의 화살",
                  Desc = "원거리로 맞힌 적은 낙인이 1개 쌓이고, 낙인이 가득 찬 적을 원거리로 맞히면 낙인이 모두 터진다(같은 적 2초에 한 번)" },
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
    /// 지금 열리는 진화(없으면 null) — 해방기부터 · 아직 진화하지 않았을 때(10-29 조각 조건 걷음 — 보스마다). 방향은 <see cref="Direction"/>.
    /// 드래프트를 <b>굴리는 순간</b> 정한다(RelicDraftComposer) — 빌드가 덜 섰을 땐 든 무기 계열을 따른다.
    /// </summary>
    public static Def Ready(PlayerLoadout l, string relicId)
    {
        if (!Unlocked || l == null || !string.IsNullOrEmpty(l.RelicEvolutionId) || string.IsNullOrEmpty(relicId)) return null;
        var dir = Direction();
        foreach (var d in All) if (d.RelicId == relicId && d.Family == dir) return d;
        return null;
    }

    /// <summary>
    /// 진화 방향 — 빌드에서 가장 많은 연격 · 필살 · 사격(룬 · 서약 · 무기가 세는 각인 수) → 같으면 지금 든 무기의 계열 → 셋 다 없으면 연격.
    /// 플레이어는 고르지 않는다(10-22 E1 D3).
    /// </summary>
    public static BuildFamily Direction()
    {
        var wm = GameRunBootstrapper.Instance?.Run?.Player != null ? GameRunBootstrapper.Instance.Run.Player.WeaponManager : null;
        var weaponFam = wm != null ? BuildFamilyRules.OfWeapon(wm.CurrentWeaponData) : BuildFamily.None;   // 지금 든 무기(예전엔 슬롯 0만 — 활을 들어도 근접 계열, 10-15 점검)
        BuildFamily best = BuildFamily.Combo;
        int bestCount = -1;
        foreach (var f in s_directions)
        {
            int c = BuildImprint.Count(f);
            if (c > bestCount || (c == bestCount && f == weaponFam)) { best = f; bestCount = c; }
        }
        return best;
    }

    /// <summary>방향을 동작 말로 — 카드 · 알림에 계열 이름(연격 · 필살 · 사격) 대신 쓴다(10-22 E1 D3 · D4 「유물 창에 계열 낱말 0」).</summary>
    public static string DirectionWord(BuildFamily f) => f switch
    {
        BuildFamily.Crit   => "치명타를 노리는",
        BuildFamily.Ranged => "원거리로 치는",
        _                  => "몰아치는",
    };

    /// <summary>카드 꼬리말 — 「보스를 쓰러뜨려 열렸다」(✓ 기호는 글리프 밖이라 쓰지 않는다 · 10-29 조각 수는 뺐다).</summary>
    public static string RecipeLine(PlayerLoadout l, Def d) => "보스를 쓰러뜨려 열렸다";

    /// <summary>
    /// 진행 한 줄 — 기억 창 머리 · 일시정지 「되찾은 기억」 맨 위(10-22 E1 B6). 봉인기 · 로드아웃 없음이면 빈 글(진화를 말하지 않는다).
    /// 「보스마다 진화를 고를 수 있다」 · 「유물 진화 완료」(10-29 조각 조건 걷음).
    /// </summary>
    public static string ProgressLine(PlayerLoadout l)
    {
        if (!Unlocked || l == null) return string.Empty;
        if (!string.IsNullOrEmpty(l.RelicEvolutionId)) return "유물 진화 완료";
        return "보스마다 진화를 고를 수 있다";
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
