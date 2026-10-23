using System.Collections.Generic;
using System.Text;

/// <summary>
/// 「되살아난 기억」 카드의 쉬운 말(10-18 빌드 피드백 ③ 사용자 「유물 진화의 설명이 거의 알아듣기 힘든 내용이야 — 직관적으로 가이드가 필요해」).
/// <list type="bullet">
/// <item><b>한 줄 요약</b> — 조각마다 「언제 → 무엇이」. 조각 글(line1~3)은 CDN 표(RELIC_PARTS_DATA)라 표에 열을 늘리지 않고 여기 사전으로 둔다.
///       표에 새 조각이 늘어 사전에 없으면 자리(시간대 · 광기 칸)로 「언제」만 말한다.</item>
/// <item><b>단계 짧은 말</b> — 같은 시간대 2 · 4개(가웨인) · 광기 칸 조각 2 · 3 · 4개(랜슬롯)가 여는 것. 정본 긴 글은 <see cref="RelicResonance"/>.</item>
/// <item><b>용어 한 줄</b> — 카드에 실제로 보이는 낱말만 골라 풀이한다(태양흔 · 개화 · 광기 · 낙인 …). 상한 · 시간은 지금 규칙에서 읽는다.</item>
/// <item><b>처음 안내</b> — 무엇을 고르나 · 등급 = 효과 줄 수 · 모으면 강해진다 · (해방기부터) 진화 조건(지금 조각 n/2).</item>
/// </list>
/// 규칙은 코드와 맞춘다 — 태양흔 · 개화(<see cref="GawainMemoryHub"/>), 낙인 · 원한 · 찢기(<see cref="LancelotMemoryHub"/>),
/// 진화 조건(<see cref="RelicEvolution.Ready"/> — 해방기부터 · 드래프트를 굴리는 순간 조각 2개).
/// </summary>
public static class RelicPartPlainText
{
    private const string TermHex = "#ECE6D8";   // 용어 · 안내 머리말 = 잉크(UITheme.Thin.Ink) — 금은 「지금 할 수 있는 것」에만
    private const string TermSep = "    ·    ";
    private const int    MadnessMax = 40;       // 광기 상한 = 맨 위 계단(r40)

    // ── 한 줄 요약(언제 → 무엇이) — line1을 줄인 것. line1이 바뀌면 같이 고친다 ──
    private static readonly Dictionary<string, string> s_summary = new()
    {
        // 가웨인 — 여명
        ["g_dawn_sowing"]      = "여명에 대시 → 스친 적에게 태양흔 1개",
        ["g_daybreak_mark"]    = "게이지 80% → 5m 안 적을 끌어당긴다",
        ["g_dawn_oath"]        = "여명에 받은 피해 → 정오에 발밑 폭발",
        ["g_morning_hunt"]     = "여명에 태양흔 3개 적 처치 → 정오 2초 일찍",
        // 정오
        ["g_zenith"]           = "정오가 열릴 때 → 태양흔 적 표시",
        ["g_second_sun"]       = "정오에 낙일 → 정오 끝에 작은 해 한 번 더",
        ["g_sundial"]          = "낙일로 3마리 이상 → 정오 2초 연장",
        ["g_noon_bloom"]       = "정오가 열릴 때 → 발밑 화상 지대",
        ["g_sun_arrow"]        = "정오에 원거리 적중 → 태양흔 1개",
        // 황혼
        ["g_ember_path"]       = "황혼에 적중 → 그 자리에 불씨",
        ["g_setting_sun"]      = "황혼에 → 작은 낙일을 한 번 더 쓴다",
        ["g_ember_carry"]      = "화상 · 태양흔 적 처치 → 옆 적에게 옮는다",
        ["g_dusk_judgment"]    = "황혼에 체력 15% 이하 → 처형",
        ["g_dusk_hunter"]      = "황혼에 원거리 처치 → 그 자리에 불씨",
        // 두 시간대
        ["g_dawn_to_noon"]     = "정오 개화 → 태양흔 1개가 남는다",
        ["g_noon_to_dusk"]     = "정오가 끝날 때 → 처치 수만큼 불씨",
        // 반응
        ["g_rx_thaw"]          = "서리 2개 적에 태양흔 → 두 배로 개화",
        ["g_rx_overheat"]      = "정전기 10개일 때 개화 → 폭발이 옆으로",
        ["g_rx_wildfire"]      = "개화가 독안개에 닿으면 → 불안개",
        ["g_rx_twin_sun"]      = "어느 시간대든 태양흔 적 적중 → 1개 더",
        ["g_rx_corona"]        = "치명타로 태양흔 적 → 1개 바로 개화",
        ["g_rx_eclipse"]       = "잠식 50 이상 → 흑점이 상한 없이",

        // 랜슬롯 — 광기 칸
        ["l_split_oath"]       = "광기 10부터 대시 → 적을 꿰뚫어 벤다",
        ["l_blood_scent"]      = "광기 10부터 체력 30% 이하 처치 → 광기 5초 유지",
        ["l_quarry_brand"]     = "광기 10부터 같은 적 원거리 3연속 → 낙인 1개",
        ["l_black_afterimage"] = "광기 20부터 평타 3번 → 잔상이 한 번 더",
        ["l_betrayer_step"]    = "광기 20부터 긴급 회피 → 광기 +8 · 낙인",
        ["l_torn_oath_blade"]  = "광기 30부터 평타 4번 → 작은 심판",
        ["l_madness_eye"]      = "광기 30부터 → 낙인 상한 3에서 5로",
        ["l_last_threshold"]   = "광기 40에서 계속 치면 → 광란이 길어진다",
        ["l_madness_crown"]    = "광란 시작 → 6m 안 적에게 낙인 2개",
        // 광란
        ["l_endless_frenzy"]   = "광란이 끝날 때 → 절반 길이 한 번 더",
        ["l_blood_frenzy"]     = "광란 중 공격 → 출혈",
        ["l_frenzy_step"]      = "광란 중 대시 → 공격 · 재사용 대기 없음",
        ["l_betrayal_feast"]   = "광란 중 처치 → 낙인이 2마리에게 옮는다",
        // 심판
        ["l_tearing_judgment"] = "심판이 낙인을 찢으면 → 찢긴 적끼리 실로 벤다",
        ["l_grudge_blade"]     = "원한 상한 5 → 8 · 가득 차면 막타가 화면 전체",
        ["l_betrayer_brand"]   = "심판에 체력 20% 이하 → 처형",
        ["l_second_judgment"]  = "광란 중 낙인 6개 이상 찢기 → 심판 한 방 더",
        ["l_distant_judgment"] = "심판 막타 뒤 → 화살 줄기 12m",
        // 반응
        ["l_rx_transfer"]      = "정전기 10개일 때 낙인 → 2마리에게 튄다",
        ["l_rx_shatter"]       = "광기 20부터 서리 적 → 서리가 낙인으로",
        ["l_rx_decay"]         = "독안개 속 낙인 적 → 출혈",
        ["l_rx_brand_iron"]    = "광기 20 미만에도 낙인 적 적중 → 낙인 1개",
        ["l_rx_expose"]        = "치명타로 낙인 적 → 낙인 상한 +2(8초)",
        ["l_rx_corrupt"]       = "잠식 50 이상 → 원한 상한 +3",

        // 진화(RelicEvolution.Def.Id)
        ["evo_gawain_combo"]    = "정오 적중마다 → 태양흔 · 가득 차면 그 자리에서 터진다",
        ["evo_gawain_crit"]     = "정오에 강타 · 치명타 → 작은 낙일",
        ["evo_gawain_ranged"]   = "정오에 원거리 5번 → 작은 해",
        ["evo_lancelot_combo"]  = "광기 1.5배 · 광란 중 10타마다 1초 연장",
        ["evo_lancelot_crit"]   = "강타 → 낙인 2개 · 심판 막타 둘레 치명 피해",
        ["evo_lancelot_ranged"] = "원거리 적중 → 낙인 · 가득 차면 찢는다",
    };

    // ── Public Methods ───────────────────────────────────

    /// <summary>조각 · 진화 한 줄 요약(언제 → 무엇이). 사전에 없으면 자리로 「언제」만.</summary>
    public static string Summary(string id, RelicPartEntry e = null)
    {
        if (!string.IsNullOrEmpty(id) && s_summary.TryGetValue(id, out var s)) return s;
        if (e == null) return string.Empty;
        return e.IsReaction ? $"{RuneTier1Label(e.rune_element)} 룬이 켜져 있을 때 작동한다" : $"{When(e.anchor)} 작동한다";
    }

    /// <summary>룬 속성 → 그 속성 1단계 효과 이름(반응 조각 조건 — <see cref="RelicDraftComposer.RuneTier1Key"/>와 같은 짝).</summary>
    public static string RuneTier1Label(string element) => element switch
    {
        "fire"     => "잔불",
        "ice"      => "서리",
        "electric" => "정전기",
        "grass"    => "독안개",
        "light"    => "광채",
        "dark"     => "잠식",
        _          => element ?? string.Empty,
    };

    /// <summary>가웨인 시간대 단계가 여는 것 — 짧은 말(정본 <see cref="RelicResonance.GawainTierText"/>).</summary>
    public static string PhaseStep(string phase, int tier) => (phase, tier) switch
    {
        (RelicPartAnchor.Dawn, 2) => $"태양흔 상한이 {GawainMemoryHub.BaseCap}에서 {GawainMemoryHub.DawnTier2Cap}로",
        (RelicPartAnchor.Dawn, 4) => "여명 끝에 해가 멈추고 낙일로 정오를 연다",
        (RelicPartAnchor.Noon, 2) => $"개화가 {GawainMemoryHub.ChainRadius:0.#}m 안 태양흔 적에게 1개씩 번진다",
        (RelicPartAnchor.Noon, 4) => "정오에도 태양흔이 쌓이고, 가득 차면 바로 개화한다",
        (RelicPartAnchor.Dusk, 2) => "황혼 화상이 여명 첫 1초까지 이어진다",
        (RelicPartAnchor.Dusk, 4) => $"황혼 끝에 {GawainMemoryHub.ShortNoonSeconds:0}초짜리 정오가 한 번 더 온다",
        _ => string.Empty,
    };

    /// <summary>랜슬롯 광기 칸 조각 수가 여는 것 — 짧은 말(정본 <see cref="RelicResonance.LadderText"/>).</summary>
    public static string LadderStep(int count) => count switch
    {
        2 => $"광기가 {MadnessMax / 2} 아래로 떨어지지 않는다",
        3 => "광란이 끝나면 광기 30에서 다시 시작한다",
        4 => "광란 중 광기 40에 또 닿으면 광란이 이어지고 심판 1회 더",
        _ => string.Empty,
    };

    /// <summary>
    /// 용어 한 줄 — <paramref name="seen"/>(카드에 보이는 글)에 나온 낱말만, 중요한 순서로 <paramref name="max"/>개.
    /// 없으면 빈 글(줄을 숨긴다).
    /// </summary>
    public static string Glossary(string relicId, string seen, PlayerController player, int max)
    {
        if (string.IsNullOrEmpty(seen) || max <= 0) return string.Empty;
        var sb = new StringBuilder();
        int n = 0;
        foreach (var (term, keys, desc) in Terms(relicId, player))
        {
            if (n >= max) break;
            if (!ContainsAny(seen, keys)) continue;
            if (sb.Length > 0) sb.Append(TermSep);
            sb.Append("<color=").Append(TermHex).Append('>').Append(term).Append("</color>  ").Append(desc);
            n++;
        }
        return sb.ToString();
    }

    /// <summary>
    /// 처음 볼 때 안내 — 단계마다 굵은 한 줄(무엇을) + 풀이 한 줄(왜 · 어떻게). 봉인기 세 단계 · 해방기부터 네 단계(진화 — 지금 조각 n/2).
    /// 10-22 사용자 「처음 설명 팝업이 한 번에 알아보기 힘들다 — 가독성 · 내용」 — 머리말 + 긴 한 문단(「= 효과 1 · 2 · 3줄」 · 「공명」 · 「보스 기억은 한 바퀴에 3번」)이었다.
    /// 10-22 E1 — 진화는 「조각 2개를 가진 채 보스」 하나 · 봉인기엔 진화를 말하지 않는다(옛 「두 번째 바퀴부터」 약속을 걷었다).
    /// </summary>
    public static string Guide(string relicId, string relicName, PlayerLoadout l)
    {
        int parts = l?.RelicPartIds?.Count ?? 0;
        int need  = RelicEvolution.FragmentsNeeded;
        bool evolved = l != null && !string.IsNullOrEmpty(l.RelicEvolutionId);
        string name = string.IsNullOrEmpty(relicName) ? "유물" : relicName;
        var sb = new StringBuilder();
        GuideStep(sb, 1, "카드 한 장 = 조각 하나",
            $"이번 런 동안 {name}에 붙는다.");
        GuideStep(sb, 2, "밝을수록 효과가 많다",
            "흐릿 1줄 · 선명 2줄 · 찬란 3줄. 같은 조각이 다시 나오면 더 밝아진다.");
        if (relicId == "lancelot")
            GuideStep(sb, 3, "광기 칸 조각을 모으면 세진다",
                $"광기 10 · 20 · 30 · {MadnessMax} 칸에 붙는 조각이 2개 · 3개 · 4개가 될 때마다 한 단계씩 세진다(칸 순서는 상관없다).");
        else
            GuideStep(sb, 3, "같은 시간대 조각을 모으면 그 시간대가 세진다",
                "여명(쳐서 쌓기) · 정오(터짐 · Q) · 황혼(Q 잠김) — 한 시간대의 조각이 2개 · 4개째일 때 한 단계씩.");
        if (!RelicEvolution.Unlocked) return sb.ToString();   // 봉인기 — 진화는 안내에도 없다(D2)
        if (evolved)
            GuideStep(sb, 4, "진화", "이번 런은 이미 진화했다.");
        else
            GuideStep(sb, 4, $"조각 {need}개면 진화  <color={UIPalette.GoldHex}>지금 {System.Math.Min(parts, need)}/{need}</color>",
                $"조각 {need}개를 가진 채 보스를 쓰러뜨리면 진화 카드가 나온다. 진화 방향은 빌드에 맞춰 저절로 정해진다.");
        return sb.ToString();
    }

    // ── Private Methods ──────────────────────────────────

    /// <summary>자리 → 「언제」(사전에 없는 조각의 요약 대신).</summary>
    private static string When(string anchor) => anchor switch
    {
        RelicPartAnchor.Dawn     => "여명 동안",
        RelicPartAnchor.Noon     => "정오 동안",
        RelicPartAnchor.Dusk     => "황혼 동안",
        RelicPartAnchor.DawnNoon => "여명 · 정오 동안",
        RelicPartAnchor.NoonDusk => "정오 · 황혼 동안",
        RelicPartAnchor.R10      => "광기 10부터",
        RelicPartAnchor.R20      => "광기 20부터",
        RelicPartAnchor.R30      => "광기 30부터",
        RelicPartAnchor.R40      => "광기 40일 때",
        RelicPartAnchor.Frenzy   => "광란 중",
        RelicPartAnchor.Judgment => "심판할 때",
        _                        => "언제나",
    };

    /// <summary>용어 목록(중요한 순서) — 낱말 · 찾을 글 · 풀이. 상한 · 시간은 지금 규칙에서 읽는다.</summary>
    private static IEnumerable<(string term, string[] keys, string desc)> Terms(string relicId, PlayerController player)
    {
        if (relicId == "lancelot")
        {
            yield return ("광기",  new[] { "광기" }, $"칠 때마다 오른다(최대 {MadnessMax})");
            yield return ("낙인",  new[] { "낙인" }, $"광기 20부터 친 적에게 새기는 표식(상한 {LancelotMemoryHub.BrandCapOf(player)})");
            yield return ("광란",  new[] { "광란" }, $"광기 {MadnessMax}에 닿으면 들어간다 — 심판(Q) 1회");
            yield return ("심판",  new[] { "심판" }, "광란 중 쓰는 Q — 막타가 낙인을 찢어 피해를 준다");
            yield return ("원한",  new[] { "원한" }, $"넘친 낙인이 쌓인 것(최대 {LancelotMemoryHub.GrudgeCap}) — 심판 막타 뒤 1개마다 참격이 1줄 나간다");
            yield return ("광기 칸", new[] { "광기 칸" }, $"광기 10 · 20 · 30 · {MadnessMax} — 이 칸 조각이 2 · 3 · 4개면 한 단계씩 세진다");
        }
        else
        {
            yield return ("여명 · 정오 · 황혼", new[] { "여명", "정오", "황혼" }, $"여명 쳐서 쌓기 → 정오 터짐 · Q → 황혼 Q 잠김{PhaseSeconds(player)}");
            yield return ("태양흔", new[] { "태양흔" }, $"여명에 친 적에게 쌓이는 표식(상한 {GawainMemoryHub.SunmarkCapOf(player)})");
            yield return ("개화",   new[] { "개화" }, "정오가 열리면 태양흔이 터진다");
            yield return ("낙일",   new[] { "낙일" }, "정오에만 쓰는 Q — 해를 떨어뜨린다");
            yield return ("흑점",   new[] { "흑점" }, "잠식 50 이상일 때 태양흔 대신 쌓이는 표식(상한 없음)");
        }
        yield return ("반응", new[] { "반응" }, "그 룬 1단계가 켜져 있을 때만 작동하는 조각");
    }

    /// <summary>가웨인 시간대 길이 「(20 · 10 · 15초)」 — 게이지를 못 읽으면 빈 글.</summary>
    private static string PhaseSeconds(PlayerController player)
    {
        var gauge = player != null && player.RelicBehavior is GawainZenithRelic g ? g.Gauge : null;
        if (gauge == null) return string.Empty;
        return $"({gauge.DurationOf(ZenithGauge.ZPhase.Charging):0} · {gauge.DurationOf(ZenithGauge.ZPhase.Noon):0} · {gauge.DurationOf(ZenithGauge.ZPhase.Cooldown):0}초)";
    }

    private static bool ContainsAny(string text, string[] keys)
    {
        foreach (var k in keys) if (text.Contains(k)) return true;
        return false;
    }

    /// <summary>
    /// 안내 한 단계 — 금 번호 + 굵은 한 줄(잉크 · 조금 크게) / 풀이(들여쓰기). 풀이 색은 부르는 쪽 글 색(옅은 잉크).
    /// </summary>
    private static void GuideStep(StringBuilder sb, int n, string head, string body)
    {
        if (sb.Length > 0) sb.Append('\n');
        sb.Append("<color=").Append(UIPalette.GoldHex).Append('>').Append((char)('\u2460' + n - 1)).Append("</color>  ")
          .Append("<size=118%><color=").Append(TermHex).Append('>').Append(head).Append("</color></size>\n")
          .Append("<indent=34>").Append(body).Append("</indent>");
    }
}
