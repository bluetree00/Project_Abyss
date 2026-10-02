#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 유물 성장 v2 실측(구현 계획 1 — 엔진). 에디터 모드에서 돈다(데이터는 프로젝트 JSON을 직접 읽는다).
/// <list type="number">
/// <item>데이터 집계 — 유물 · 자리별 조각 수, v2 칸 비었는지.</item>
/// <item>저장 왕복 — 등급 · 메아리 · 천장 · 다시 떠올리기 직렬화 → 복원 → 같은지 + 옛 저장 문자열.</item>
/// <item>카드 생성 불변식 — 상태 6 × 시기 3 × 띠 3 × 룬 4 × 2,000회: 늘 3장 · 중복 0 · 조건 미충족 0 · 잔향은 끝에서만 · 등급 분포 · 천장.</item>
/// <item>공명 표 — 표본 로드아웃의 시간대 공명 · 이어진 계단 · 미리 보기 줄.</item>
/// </list>
/// 결과: Temp/relic_growth_v2/report.txt (콘솔에는 요약 한 줄씩).
/// </summary>
public static class RelicGrowthV2ProbeEditor
{
    private const string Root     = "RelicFairy/Debug/유물 성장 v2/";
    private const string DataPath = "Assets/RelicFairy/Systems/RelicParts/RELIC_PARTS_DATA.json";
    private const string OutDir   = "Temp/relic_growth_v2";

    [MenuItem(Root + "0 전부 (1~4)")]
    private static void RunAll()
    {
        var sb = new StringBuilder();
        bool ok = Data(sb) & SaveRoundTrip(sb) & Composer(sb) & Resonance(sb);
        Write(sb, ok);
    }

    [MenuItem(Root + "1 데이터 집계")]          private static void MenuData()      { var sb = new StringBuilder(); Write(sb, Data(sb)); }
    [MenuItem(Root + "2 저장 왕복")]            private static void MenuSave()      { var sb = new StringBuilder(); Write(sb, SaveRoundTrip(sb)); }
    [MenuItem(Root + "3 카드 생성 불변식")]      private static void MenuComposer()  { var sb = new StringBuilder(); Write(sb, Composer(sb)); }
    [MenuItem(Root + "4 공명 표")]              private static void MenuResonance() { var sb = new StringBuilder(); Write(sb, Resonance(sb)); }

    // ── 데이터 ───────────────────────────────────────────

    private static List<RelicPartEntry> LoadEntries()
    {
        var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
        if (ta == null) return new List<RelicPartEntry>();
        var col = JsonUtility.FromJson<RelicPartEntryCollection>(ta.text);
        return col?.entries ?? new List<RelicPartEntry>();
    }

    private static Dictionary<string, RelicPartEntry> Index(List<RelicPartEntry> all)
    {
        var d = new Dictionary<string, RelicPartEntry>();
        foreach (var e in all) d[e.part_id] = e;
        return d;
    }

    private static bool Data(StringBuilder sb)
    {
        var all = LoadEntries();
        sb.AppendLine("== 1 데이터 집계 ==");
        bool ok = all.Count == 42;
        var count = new SortedDictionary<string, int>();
        int missing = 0;
        foreach (var e in all)
        {
            string k = $"{e.relic_id}/{e.anchor}";
            count[k] = count.TryGetValue(k, out int n) ? n + 1 : 1;
            if (!e.IsV2 || string.IsNullOrEmpty(e.line1) || string.IsNullOrEmpty(e.line2) || string.IsNullOrEmpty(e.line3)
                || string.IsNullOrEmpty(e.effect_key) || e.part_id != e.effect_key) missing++;
            if (e.IsReaction && RelicDraftComposer.RuneTier1Key(e.rune_element) == null) missing++;
        }
        foreach (var kv in count) sb.AppendLine($"  {kv.Key}: {kv.Value}");
        sb.AppendLine($"  합계 {all.Count}(기대 42) · 칸 빠짐 {missing}");
        ok &= missing == 0;
        int g = 0, l = 0;
        foreach (var e in all) { if (e.relic_id == "gawain") g++; else if (e.relic_id == "lancelot") l++; }
        ok &= g == 20 && l == 22;
        sb.AppendLine($"  가웨인 {g}(기대 20) · 랜슬롯 {l}(기대 22) → {(ok ? "통과" : "실패")}");
        return ok;
    }

    // ── 저장 왕복 ────────────────────────────────────────

    private static bool SaveRoundTrip(StringBuilder sb)
    {
        sb.AppendLine("== 2 저장 왕복 ==");
        var a = new PlayerLoadout();
        a.AddRelicPart("g_zenith", RelicMemoryGrade.Clear);
        a.AddRelicPart("g_dawn_sowing", RelicMemoryGrade.Faint);
        a.AddRelicPart("g_rx_thaw", RelicMemoryGrade.Radiant);
        a.RaiseRelicPartGrade("g_dawn_sowing");
        a.AddRelicEcho("noon");
        a.AddRelicEcho("noon");
        a.NoteRelicDraft(false); a.NoteRelicDraft(false);
        a.MarkRelicRedrawUsed();
        string ids = a.SerializeRelicParts(), extra = a.SerializeRelicExtra();

        var b = new PlayerLoadout();
        b.RestoreRelicParts(ids, extra);
        bool ok = b.SerializeRelicParts() == ids && b.SerializeRelicExtra() == extra
                  && b.GetRelicPartGrade("g_dawn_sowing") == RelicMemoryGrade.Clear
                  && b.GetRelicEcho("noon") == 2 && b.RelicDraftsSinceRadiant == 2 && b.RelicRedrawUsed;
        sb.AppendLine($"  조각 「{ids}」 · 덤 「{extra}」 → 복원 일치 {ok}");

        // 옛 저장(등급 없는 id) — 에디터 모드엔 데이터 매니저가 없어 거르지 않는다(플레이 중엔 모르는 id를 버린다)
        var c = new PlayerLoadout();
        c.RestoreRelicParts("g_zenith,g_noon_bloom:9,,g_sundial:3", null);
        bool legacy = c.GetRelicPartGrade("g_zenith") == RelicMemoryGrade.Faint
                      && c.GetRelicPartGrade("g_noon_bloom") == RelicMemoryGrade.Faint
                      && c.GetRelicPartGrade("g_sundial") == RelicMemoryGrade.Radiant;
        sb.AppendLine($"  옛 · 잘못된 등급 문자열 → 흐릿 폴백 · 정상 등급 유지 {legacy}");
        ok &= legacy;

        // 천장 카운터
        var d = new PlayerLoadout();
        d.NoteRelicDraft(false); d.NoteRelicDraft(false); d.NoteRelicDraft(true);
        bool pity = d.RelicDraftsSinceRadiant == 0;
        sb.AppendLine($"  천장 카운터 리셋 {pity}");
        ok &= pity;
        sb.AppendLine($"  → {(ok ? "통과" : "실패")}");
        return ok;
    }

    // ── 카드 생성 불변식 ──────────────────────────────────

    private static bool Composer(StringBuilder sb)
    {
        sb.AppendLine("== 3 카드 생성 불변식 ==");
        var all = LoadEntries();
        var idx = Index(all);
        Func<string, RelicPartEntry> get = id => id != null && idx.TryGetValue(id, out var e) ? e : null;
        var rng = new System.Random(20261002);
        bool ok = true;
        long cards = 0, residue = 0, pityChecks = 0, pityHits = 0;
        var gradeCount = new long[3, 3, 4];   // era, band, grade(1..3)

        string[] runeSets = { "", "fire,ice", "electric,grass,light", "fire,ice,electric,grass,light,dark" };
        foreach (var relic in new[] { "gawain", "lancelot" })
        {
            var mine = all.FindAll(e => e.relic_id == relic);
            for (int state = 0; state < 6; state++)
            for (int era = 0; era < 3; era++)
            for (int band = 0; band < 3; band++)
            foreach (var runeSet in runeSets)
            {
                var active = new HashSet<string>(runeSet.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                Func<string, bool> rune = el => active.Contains(el);
                for (int k = 0; k < 2000; k++)
                {
                    var l = MakeState(relic, mine, state, rng, get);
                    bool pityState = k % 5 == 0;
                    if (pityState) for (int p = 0; p < RelicMemoryOdds.PityDrafts; p++) l.NoteRelicDraft(false);
                    var d = RelicDraftComposer.Compose(relic, mine, l, era, band, rune, rng);
                    cards += d.Cards.Count;
                    string why = Check(d, l, rune, relic, get);
                    if (why != null)
                    {
                        if (ok) sb.AppendLine($"  ✗ 첫 위반: {relic} 상태{state} 시기{era} 띠{band} 룬[{runeSet}] — {why}");
                        ok = false;
                    }
                    foreach (var c in d.Cards)
                    {
                        if (c.Kind == RelicDraftCardKind.Residue) residue++;
                        // 분포는 천장 상태가 아닌 표본만 센다(천장 상태에서 찬란이 이미 나온 표본만 남기면 찬란 쪽으로 기운다)
                        if (c.Kind == RelicDraftCardKind.NewFragment && !pityState) gradeCount[era, band, (int)c.Grade]++;
                    }
                    if (l.RelicDraftsSinceRadiant >= RelicMemoryOdds.PityDrafts && d.Cards.Exists(c => c.Kind == RelicDraftCardKind.NewFragment))
                    {
                        pityChecks++;
                        if (d.HasRadiant) pityHits++;
                    }
                }
            }
        }
        sb.AppendLine($"  카드 {cards:N0}장 · 잔향 {residue:N0}장 · 천장 대상 {pityChecks:N0} 중 찬란 포함 {pityHits:N0}");
        ok &= pityChecks == pityHits;

        // 등급 분포(천장 제외) — 표 ±1.5%p
        for (int era = 0; era < 3; era++)
        for (int band = 0; band < 3; band++)
        {
            long n = gradeCount[era, band, 1] + gradeCount[era, band, 2] + gradeCount[era, band, 3];
            if (n == 0) continue;
            var (f, c2, r) = RelicMemoryOdds.Table(era, band);
            double pf = 100.0 * gradeCount[era, band, 1] / n, pc = 100.0 * gradeCount[era, band, 2] / n, pr = 100.0 * gradeCount[era, band, 3] / n;
            bool within = Math.Abs(pf - f) <= 1.5 && Math.Abs(pc - c2) <= 1.5 && Math.Abs(pr - r) <= 1.5;
            ok &= within;
            sb.AppendLine($"  시기{era} 띠{band}: {pf:0.0}/{pc:0.0}/{pr:0.0} (표 {f}/{c2}/{r}) {(within ? "" : "✗")}");
        }
        sb.AppendLine($"  → {(ok ? "통과" : "실패")}");
        return ok;
    }

    /// <summary>상태 0 빈 · 1 넷 · 2 여덟 · 3 전부 흐릿 · 4 전부 찬란 · 5 전부 찬란 + 메아리 최대.</summary>
    private static PlayerLoadout MakeState(string relic, List<RelicPartEntry> mine, int state, System.Random rng, Func<string, RelicPartEntry> get)
    {
        var l = new PlayerLoadout();
        if (state == 0) return l;
        var shuffled = new List<RelicPartEntry>(mine);
        for (int i = shuffled.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]); }
        int take = state == 1 ? 4 : state == 2 ? 8 : shuffled.Count;
        for (int i = 0; i < take && i < shuffled.Count; i++)
        {
            var g = state >= 4 ? RelicMemoryGrade.Radiant : state == 3 ? RelicMemoryGrade.Faint : (RelicMemoryGrade)(1 + rng.Next(3));
            l.AddRelicPart(shuffled[i].part_id, g);
        }
        if (state == 5)
        {
            if (relic == "gawain") foreach (var ph in RelicPartAnchor.GawainPhases) for (int k = 0; k < 4; k++) l.AddRelicEcho(ph);
            else foreach (var r in RelicPartAnchor.LancelotRungs) l.AddRelicEcho(r);
        }
        return l;
    }

    private static string Check(RelicDraft d, PlayerLoadout l, Func<string, bool> rune, string relic, Func<string, RelicPartEntry> get)
    {
        if (d.Cards.Count != RelicDraftComposer.CardCount) return $"카드 {d.Cards.Count}장";
        var seen = new HashSet<string>();
        bool anyFreshLeft = false, anySharpenLeft = false;
        foreach (var c in d.Cards)
        {
            switch (c.Kind)
            {
                case RelicDraftCardKind.NewFragment:
                    if (c.Entry == null) return "새 조각 데이터 없음";
                    if (l.GetRelicPartGrade(c.PartId) != RelicMemoryGrade.None) return $"가진 조각을 새로 제시 {c.PartId}";
                    if (c.Entry.IsReaction && !rune(c.Entry.rune_element)) return $"조건 미충족 반응 {c.PartId}";
                    if (!seen.Add(c.PartId)) return $"중복 {c.PartId}";
                    break;
                case RelicDraftCardKind.Sharpen:
                    var have = l.GetRelicPartGrade(c.PartId);
                    if (have == RelicMemoryGrade.None || have >= RelicMemoryGrade.Radiant) return $"올릴 수 없는 선명하게 {c.PartId}";
                    if (c.Grade != have + 1) return $"선명하게 등급 {c.Grade} ≠ {have + 1}";
                    if (!seen.Add(c.PartId)) return $"중복 {c.PartId}";
                    break;
                case RelicDraftCardKind.Echo:
                    if (!seen.Add("echo:" + c.Anchor)) return $"메아리 중복 {c.Anchor}";
                    break;
                case RelicDraftCardKind.Residue:
                    // 잔향은 새 조각 · 선명하게 · 메아리가 모두 바닥났을 때만
                    foreach (var e in get == null ? null : (IEnumerable<RelicPartEntry>)AllOf(relic, get))
                    {
                        var g = l.GetRelicPartGrade(e.part_id);
                        if (g == RelicMemoryGrade.None && (!e.IsReaction || rune(e.rune_element)) && !seen.Contains(e.part_id)) anyFreshLeft = true;
                        if (g != RelicMemoryGrade.None && g < RelicMemoryGrade.Radiant && !seen.Contains(e.part_id)) anySharpenLeft = true;
                    }
                    if (anyFreshLeft || anySharpenLeft) return "새 조각 · 선명하게가 남았는데 잔향";
                    break;
            }
        }
        return null;
    }

    private static List<RelicPartEntry> s_allCache;
    private static IEnumerable<RelicPartEntry> AllOf(string relic, Func<string, RelicPartEntry> get)
    {
        s_allCache ??= LoadEntries();
        foreach (var e in s_allCache) if (e.relic_id == relic) yield return e;
    }

    // ── 공명 ─────────────────────────────────────────────

    private static bool Resonance(StringBuilder sb)
    {
        sb.AppendLine("== 4 공명 표 ==");
        var all = LoadEntries();
        var idx = Index(all);
        Func<string, RelicPartEntry> get = id => id != null && idx.TryGetValue(id, out var e) ? e : null;
        bool ok = true;

        var g = new PlayerLoadout();
        g.AddRelicPart("g_zenith"); g.AddRelicPart("g_sundial"); g.AddRelicPart("g_dawn_to_noon");
        int noon = RelicResonance.GawainCount(g, "noon", get), dawn = RelicResonance.GawainCount(g, "dawn", get);
        ok &= noon == 3 && dawn == 1 && RelicResonance.GawainTier(noon) == 2;
        string prev = RelicResonance.PreviewLine(g, "gawain", "noon", get);
        ok &= prev != null && prev.Contains("3 → 4");
        sb.AppendLine($"  가웨인 정오 {noon}(기대 3) · 여명 {dawn}(기대 1) · 미리 보기 「{prev}」");

        var l = new PlayerLoadout();
        l.AddRelicPart("l_split_oath"); l.AddRelicPart("l_madness_eye");   // r10 · r30 — 20이 비어 이어짐 1
        int ladder = RelicResonance.LancelotLadder(l, get);
        string lp = RelicResonance.PreviewLine(l, "lancelot", "r20", get);
        ok &= ladder == 1 && lp != null && lp.Contains("1 → 3");
        l.AddRelicEcho("r20");
        int ladder2 = RelicResonance.LancelotLadder(l, get);
        ok &= ladder2 == 3;
        sb.AppendLine($"  랜슬롯 이어진 계단 {ladder}(기대 1) · r20 미리 보기 「{lp}」 · 빈 받침 뒤 {ladder2}(기대 3)");
        sb.AppendLine($"  → {(ok ? "통과" : "실패")}");
        return ok;
    }

    private static void Write(StringBuilder sb, bool ok)
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "report.txt"), sb.ToString());
        foreach (var line in sb.ToString().Split('\n'))
            if (line.StartsWith("==") || line.Contains("→") || line.Contains("✗")) Debug.Log("[RelicGrowthV2] " + line.TrimEnd());
        Debug.Log($"[RelicGrowthV2] 실측 {(ok ? "통과" : "실패")} → {OutDir}/report.txt");
    }
}
#endif
