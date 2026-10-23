using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 런 결과 Presenter(10-07) — 사본(<see cref="RunResultSnapshot"/>)을 화면 글자 · 비율로 바꾼다. 순수 함수만(자가 검사 대상).
/// </summary>
public static class RunResultPresenter
{
    // ── Constants ──
    private const string Dot = "  ·  ";
    private const float DoneShare = 0.86f;   // 못 간 챕터가 있으면 마친 구간이 띠의 86%를 쓴다(시안 1)

    // ── Public Methods ──

    /// <summary>m:ss · 1시간 넘으면 h:mm:ss.</summary>
    public static string FormatTime(int seconds)
    {
        seconds = Mathf.Max(0, seconds);
        int h = seconds / 3600, m = seconds / 60 % 60, s = seconds % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{seconds / 60}:{s:00}";
    }

    /// <summary>
    /// 「완주」 · 「쓰러짐」 — 봉인기에 시작해 리치에게 추방되고(Ch1 착지 · 재정비) 클리어로 정산된 런은 「봉인기 완주」(10-22 A10).
    /// 그 런은 추방 때 챕터가 Ch1로 바뀐 채 끝난다 — 봉인기 런이 Ch1에서 클리어로 끝나는 길은 이것뿐이다.
    /// </summary>
    public static string Title(RunResultSnapshot s)
    {
        if (!s.Cleared) return "쓰러짐";
        return s.Era == StoryEra.Sealed && s.ChapterNumber == 1 ? "봉인기 완주" : "완주";
    }

    public static string Subtitle(RunResultSnapshot s)
    {
        var sb = new StringBuilder();
        sb.Append(s.ChapterNumber > 0 ? $"Ch{s.ChapterNumber}" : "Ch?");
        if (!string.IsNullOrEmpty(s.ChapterName)) sb.Append(' ').Append(s.ChapterName);
        sb.Append(Dot).Append($"방 {s.RoomClears}개 정리");
        if (!s.Cleared && !string.IsNullOrEmpty(s.KilledBy)) sb.Append(Dot).Append(s.KilledBy).Append("에게");
        return sb.ToString();
    }

    public static string EraLabel(RunResultSnapshot s)
    {
        string era = s.Era switch
        {
            StoryEra.Liberated     => "해방된 세계",
            StoryEra.NightmareMode => "악몽",
            _                      => "봉인된 세계",
        };
        return s.AbyssDepth > 0 ? $"{era} · 심연 {s.AbyssDepth}" : era;
    }

    /// <summary>각성 정수 내역 — 처치 · 봉인(나머지) / 계약 / 초행. 0인 줄은 뺀다(첫 줄은 늘 있다).</summary>
    public static List<(string label, int value)> EssenceRows(RunResultSnapshot s)
    {
        int rest = Mathf.Max(0, s.Essence - s.EssenceFromContracts - s.EssenceFromFirstRun);
        var rows = new List<(string, int)> { ("처치 · 봉인", rest) };
        if (s.EssenceFromContracts > 0) rows.Add(($"계약 {s.ContractsDone}건 달성", s.EssenceFromContracts));
        if (s.EssenceFromFirstRun  > 0) rows.Add(("초행 보너스", s.EssenceFromFirstRun));
        return rows;
    }

    public static string SourceLabel(DamageSource src) => src switch
    {
        DamageSource.Skill    => "스킬 E · R",
        DamageSource.RelicQ   => "유물 Q",
        DamageSource.Covenant => "서약",
        DamageSource.Rune     => "룬 시너지",
        DamageSource.Dot      => "지속 피해",
        _                     => "평타",
    };

    /// <summary>전투 둘째 줄 — 「정예 9 · 보스 2 · 가장 큰 한 방 3,412 (서약)」.</summary>
    public static string CombatLine(RunResultSnapshot s)
    {
        var line = $"정예 {s.EliteKills}{Dot}보스 {s.BossKills}";
        if (s.BiggestHit > 0) line += $"{Dot}가장 큰 한 방 {s.BiggestHit:N0} ({SourceLabel(s.BiggestHitSource)})";
        return line;
    }

    public static string WeaponTitle(RunResultSnapshot.WeaponLine w) => w == null ? "무기 없음" : w.Name;

    /// <summary>
    /// 「재련 3회 · 형 쌍풍 2단 · 튕기는 칼끝 II · 검기 I」 — 지금 든 무기의 빌드 한 줄(없는 조각은 뺀다).
    /// 10-22 사용자 「무기 빌드도 간단히 보이게」 — 증강 · 파츠를 붙이고, 보조 무기는 <see cref="SubWeaponLine"/>으로 따로.
    /// </summary>
    public static string WeaponSub(RunResultSnapshot.WeaponLine w, RunResultSnapshot.WeaponLine sub)
    {
        if (w == null) return string.Empty;
        string line = BuildLine(w);
        return string.IsNullOrEmpty(line) ? "기본" : line;
    }

    /// <summary>「보조 클라렌트 · 재련 4회 · 튕기는 칼끝 II」 — 보조 무기가 없으면 빈 글.</summary>
    public static string SubWeaponLine(RunResultSnapshot.WeaponLine sub)
    {
        if (sub == null) return string.Empty;
        string line = BuildLine(sub);
        return string.IsNullOrEmpty(line) ? $"보조 {sub.Name}" : $"보조 {sub.Name}{Dot}{line}";
    }

    private static string BuildLine(RunResultSnapshot.WeaponLine w)
    {
        var parts = new List<string>();
        if (w.Enhance > 0) parts.Add($"재련 {WeaponReforgeService.CountAt(w.Enhance)}회");
        if (!string.IsNullOrEmpty(w.FormName)) parts.Add(w.FormTier > 0 ? $"형 {w.FormName} {w.FormTier}단" : $"형 {w.FormName}");
        if (!string.IsNullOrEmpty(w.Build)) parts.Add(w.Build);
        else if (w.Ranged) parts.Add("파츠 없음");
        return string.Join(Dot, parts);
    }

    public static string MemoryLine(RunResultSnapshot s)
    {
        int n = s.MemFaint + s.MemClear + s.MemRadiant;
        return n == 0 ? "되살아난 기억 없음" : $"되살아난 기억 {n}";
    }

    public static string ElementTier(string elementId, int tier)
    {
        var def = ElementDef.GetById(elementId);
        return $"{(def != null ? def.Name : elementId)} {tier}단";
    }

    /// <summary>서약 이름 — 조각마다 등급색 점(●). 등급을 모르면 점 없이 이름만.</summary>
    public static string CovenantRich(RunResultSnapshot.CovenantLine c)
    {
        if (string.IsNullOrEmpty(c.Name)) return string.Empty;
        var parts = c.Name.Split(new[] { " → " }, System.StringSplitOptions.None);
        var sb = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) sb.Append("  <color=#8C877F>→</color>  ");
            if (c.Tiers != null && i < c.Tiers.Length)
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(TierColor(c.Tiers[i]))).Append(">●</color> ");
            sb.Append(parts[i]);
        }
        return sb.ToString();
    }

    public static Color TierColor(CovenantTier t) => t switch
    {
        CovenantTier.Gold => UITheme.Thin.Gold,
        CovenantTier.Ruby => new Color(0.84f, 0.33f, 0.38f, 1f),
        _                 => new Color(0.78f, 0.80f, 0.86f, 1f),
    };

    /// <summary>챕터 이름 글자를 그릴 만한 폭인가(좁은 구간에서 이웃 이름끼리 겹침 방지).</summary>
    public static bool SegmentShowsLabel(float width) => width >= 70f;

    /// <summary>구간 시간 글자를 그릴 만한 폭인가 — 마지막(쓰러진/완주) 구간은 끝 표시 글자 자리를 비워 둔다.</summary>
    public static bool SegmentShowsTime(float width, bool lastDone) => width >= (lastDone ? 270f : 90f);

    /// <summary>챕터 띠 폭 — 마친 구간은 걸린 시간 비율로 86%(다 마쳤으면 100%), 못 간 챕터는 나머지를 똑같이.</summary>
    public static float[] SegmentWidths(IReadOnlyList<RunResultSnapshot.Segment> segs, float width)
    {
        var w = new float[segs.Count];
        int pending = 0;
        long doneSec = 0;
        int doneCount = 0;
        foreach (var g in segs)
        {
            if (g.Done) { doneSec += g.Seconds; doneCount++; }
            else pending++;
        }
        float doneW = pending > 0 ? width * DoneShare : width;
        for (int i = 0; i < segs.Count; i++)
        {
            if (segs[i].Done) w[i] = doneSec > 0 ? doneW * segs[i].Seconds / doneSec : doneW / Mathf.Max(1, doneCount);
            else              w[i] = (width - doneW) / pending;
        }
        return w;
    }

    public static float DoneWidth(IReadOnlyList<RunResultSnapshot.Segment> segs, float width)
    {
        var w = SegmentWidths(segs, width);
        float sum = 0f;
        for (int i = 0; i < segs.Count; i++) if (segs[i].Done) sum += w[i];
        return sum;
    }
}
