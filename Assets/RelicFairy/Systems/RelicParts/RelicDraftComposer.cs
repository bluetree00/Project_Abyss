using System;
using System.Collections.Generic;

/// <summary>
/// 유물 기억 드래프트 카드 생성기(유물 성장 v2 §1-3) — 상태 없는 순수 계산, 난수는 주입받는다(실측 도구가 수만 번 돌린다).
///
/// <b>세 자리</b>
/// <list type="number">
/// <item>잇기 — 안 가진 조각 중 가진 조각과 같은 자리(가웨인 = 시간대가 겹침 · 랜슬롯 = 같은 계단/광란/심판 · 반응끼리).</item>
/// <item>넓히기 — 안 가진 조각 중 아직 아무것도 없는 자리.</item>
/// <item>자유 — 안 가진 조각(반응 조각 가중치 ×2) · 25% 확률로 「선명하게」.</item>
/// </list>
/// <b>채우기 사슬</b>: 자리에 후보가 없으면 새 조각 → 선명하게 → 메아리 → 잔향. 그래서 <b>늘 3장</b>이고, 조건 미충족 카드는 없다
/// (반응 조각은 그 룬 속성 1단계가 켜져 있을 때만 후보 — 사용자 「선택지가 비면 안 돼」 · 「제시되는 칸 전부 유효」).
/// </summary>
public static class RelicDraftComposer
{
    public const int CardCount = 3;
    private const double SharpenChanceOnFree = 0.25;   // ③ 자유 자리가 「선명하게」일 확률
    private const int    ReactionWeight      = 2;      // ③ 자유 자리에서 반응 조각 가중치
    private static readonly int[] ResidueEssenceByEra = { 40, 60, 80 };

    /// <summary>룬 속성(fire · ice · electric · grass · light · dark) → 그 속성 1단계 효과 키.</summary>
    public static string RuneTier1Key(string element) => element switch
    {
        "fire"     => "FireEmber",
        "ice"      => "IceFrost",
        "electric" => "ElecStatic",
        "grass"    => "GrassMist",
        "light"    => "LightRadiance",
        "dark"     => "DarkErosion",
        _          => null,
    };

    /// <summary>
    /// 카드 3장을 만든다.
    /// <paramref name="all"/> = 이 유물의 조각 전부(데이터) · <paramref name="runeTier1Active"/> = 속성 이름 → 1단계 켜짐.
    /// </summary>
    public static RelicDraft Compose(string relicId, IReadOnlyList<RelicPartEntry> all, PlayerLoadout l,
                                     int era, int band, Func<string, bool> runeTier1Active, Random rng)
    {
        var draft = new RelicDraft { RelicId = relicId };
        if (all == null || l == null || rng == null) return draft;

        var lookup = new Dictionary<string, RelicPartEntry>(all.Count);
        foreach (var e in all) if (e != null && !string.IsNullOrEmpty(e.part_id)) lookup[e.part_id] = e;
        Func<string, RelicPartEntry> get = id => id != null && lookup.TryGetValue(id, out var x) ? x : null;

        // 후보 풀
        var fresh = new List<RelicPartEntry>();      // 안 가진 · 조건 충족
        var sharpen = new List<RelicPartEntry>();    // 가진 · 찬란 미만
        var ownedGroups = new HashSet<string>();
        foreach (var e in all)
        {
            if (e == null || e.relic_id != relicId) continue;
            var g = l.GetRelicPartGrade(e.part_id);
            if (g != RelicMemoryGrade.None)
            {
                foreach (var grp in GroupsOf(e)) ownedGroups.Add(grp);
                if (g < RelicMemoryGrade.Radiant) sharpen.Add(e);
                continue;
            }
            if (e.IsReaction && (runeTier1Active == null || !runeTier1Active(e.rune_element))) continue;
            fresh.Add(e);
        }

        var used = new HashSet<string>();   // 이번 드래프트에 이미 쓴 조각 id · 메아리 자리("echo:" 접두)

        // ① 잇기
        Pick(draft, PickFresh(fresh, used, rng, e => ownedGroups.Count == 0 || Overlaps(e, ownedGroups), 1),
             fresh, sharpen, used, l, relicId, era, band, rng, get);
        // ② 넓히기
        Pick(draft, PickFresh(fresh, used, rng, e => !Overlaps(e, ownedGroups), 1),
             fresh, sharpen, used, l, relicId, era, band, rng, get);
        // ③ 자유 — 선명하게 25% 또는 반응 ×2 가중
        RelicDraftCard third = null;
        bool wantSharpen = sharpen.Count > 0 && rng.NextDouble() < SharpenChanceOnFree;
        if (wantSharpen) third = MakeSharpen(sharpen, used, rng, l);
        if (third == null)
        {
            var e = PickFresh(fresh, used, rng, _ => true, ReactionWeight);
            if (e != null) third = MakeNew(e, era, band, rng);
        }
        Pick(draft, third, fresh, sharpen, used, l, relicId, era, band, rng, get);

        // 천장 — 찬란 없이 PityDrafts번 지났다면 한 장을 찬란으로
        if (l.RelicDraftsSinceRadiant >= RelicMemoryOdds.PityDrafts && !draft.HasRadiant)
        {
            foreach (var c in draft.Cards)
            {
                if (c.Kind == RelicDraftCardKind.NewFragment) { c.Grade = RelicMemoryGrade.Radiant; draft.PityApplied = true; break; }
            }
            if (!draft.PityApplied)
                foreach (var c in draft.Cards)
                    if (c.Kind == RelicDraftCardKind.Sharpen && c.Grade == RelicMemoryGrade.Radiant) { draft.PityApplied = true; break; }
        }

        // 기억의 빛 — 세 장 중 가장 높은 등급(메아리 · 잔향은 흐릿으로 친다)
        draft.TopGrade = RelicMemoryGrade.Faint;
        foreach (var c in draft.Cards)
            if ((c.Kind == RelicDraftCardKind.NewFragment || c.Kind == RelicDraftCardKind.Sharpen) && c.Grade > draft.TopGrade)
                draft.TopGrade = c.Grade;
        return draft;
    }

    /// <summary>조각이 속한 자리 묶음 — 가웨인은 덮는 시간대들, 랜슬롯은 자리 그대로, 반응은 「reaction」.</summary>
    public static IEnumerable<string> GroupsOf(RelicPartEntry e)
    {
        if (e == null) yield break;
        if (e.IsReaction) { yield return RelicPartAnchor.Reaction; yield break; }
        if (e.relic_id == "gawain")
        {
            foreach (var phase in RelicPartAnchor.GawainPhases)
                if (RelicPartAnchor.Covers(e.anchor, phase)) yield return phase;
            yield break;
        }
        yield return e.anchor;
    }

    private static bool Overlaps(RelicPartEntry e, HashSet<string> groups)
    {
        foreach (var g in GroupsOf(e)) if (groups.Contains(g)) return true;
        return false;
    }

    /// <summary>후보 중 하나를 가중 무작위로(반응 조각 가중치 <paramref name="reactionWeight"/>).</summary>
    private static RelicPartEntry PickFresh(List<RelicPartEntry> fresh, HashSet<string> used, Random rng,
                                            Func<RelicPartEntry, bool> filter, int reactionWeight)
    {
        int total = 0;
        foreach (var e in fresh)
            if (!used.Contains(e.part_id) && filter(e)) total += e.IsReaction ? reactionWeight : 1;
        if (total <= 0) return null;
        int r = rng.Next(total);
        foreach (var e in fresh)
        {
            if (used.Contains(e.part_id) || !filter(e)) continue;
            r -= e.IsReaction ? reactionWeight : 1;
            if (r < 0) return e;
        }
        return null;
    }

    private static RelicDraftCard MakeNew(RelicPartEntry e, int era, int band, Random rng) => new RelicDraftCard
    {
        Kind = RelicDraftCardKind.NewFragment, Entry = e, Anchor = e.anchor, Grade = RelicMemoryOdds.Roll(era, band, rng),
    };

    private static RelicDraftCard MakeSharpen(List<RelicPartEntry> sharpen, HashSet<string> used, Random rng, PlayerLoadout l)
    {
        int n = 0;
        foreach (var e in sharpen) if (!used.Contains(e.part_id)) n++;
        if (n == 0) return null;
        int r = rng.Next(n);
        foreach (var e in sharpen)
        {
            if (used.Contains(e.part_id)) continue;
            if (r-- == 0)
                return new RelicDraftCard { Kind = RelicDraftCardKind.Sharpen, Entry = e, Anchor = e.anchor, Grade = l.GetRelicPartGrade(e.part_id) + 1 };
        }
        return null;
    }

    /// <summary>자리에 카드를 넣는다 — 주어진 카드가 없으면 채우기 사슬로 내려간다.</summary>
    private static void Pick(RelicDraft draft, object preferred, List<RelicPartEntry> fresh, List<RelicPartEntry> sharpen,
                             HashSet<string> used, PlayerLoadout l, string relicId, int era, int band, Random rng,
                             Func<string, RelicPartEntry> get)
    {
        RelicDraftCard card = preferred switch
        {
            RelicDraftCard c  => c,
            RelicPartEntry e  => MakeNew(e, era, band, rng),
            _                 => null,
        };

        // 채우기 사슬 ① 새 조각 아무거나
        if (card == null)
        {
            var e = PickFresh(fresh, used, rng, _ => true, 1);
            if (e != null) card = MakeNew(e, era, band, rng);
        }
        // ② 선명하게
        if (card == null) card = MakeSharpen(sharpen, used, rng, l);
        // ③ 메아리
        if (card == null) card = MakeEcho(relicId, used, l, rng, get);
        // ④ 잔향
        if (card == null)
            card = new RelicDraftCard { Kind = RelicDraftCardKind.Residue, Grade = RelicMemoryGrade.Faint,
                                        ResidueEssence = ResidueEssenceByEra[era < 0 ? 0 : era > 2 ? 2 : era] };

        if (card.Entry != null) used.Add(card.Entry.part_id);
        if (card.Kind == RelicDraftCardKind.Echo) used.Add("echo:" + card.Anchor);
        draft.Cards.Add(card);
    }

    /// <summary>메아리 — 공명이 아직 다 차지 않은 자리 하나(가웨인: 4 미만 시간대 · 랜슬롯: 비어 있는 계단).</summary>
    private static RelicDraftCard MakeEcho(string relicId, HashSet<string> used, PlayerLoadout l, Random rng,
                                           Func<string, RelicPartEntry> get)
    {
        var open = new List<string>(4);
        if (relicId == "gawain")
        {
            foreach (var phase in RelicPartAnchor.GawainPhases)
                if (!used.Contains("echo:" + phase) && RelicResonance.GawainCount(l, phase, get) < 4) open.Add(phase);
        }
        else if (relicId == "lancelot")
        {
            foreach (var rung in RelicPartAnchor.LancelotRungs)
                if (!used.Contains("echo:" + rung) && !RelicResonance.LancelotRungFilled(l, rung, get)) open.Add(rung);
        }
        if (open.Count == 0) return null;
        return new RelicDraftCard { Kind = RelicDraftCardKind.Echo, Anchor = open[rng.Next(open.Count)], Grade = RelicMemoryGrade.Faint };
    }
}
