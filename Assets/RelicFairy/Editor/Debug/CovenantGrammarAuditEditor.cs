#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-02 「한 장의 서약서」 E3 — 문법 · 후보 전수 검증(플레이 불필요).
/// 개방 단계 0~3 × 조건 × 첫 결과 마다 이어 쓰기를 무작위로 끝까지 걸어 보며:
/// <list type="bullet">
/// <item>제시된 카드가 <b>전부</b> 새길 수 있는가(이어 쓰기 · 고쳐 쓰기 · 조건 고쳐 쓰기).</item>
/// <item>막다른 끝 — 길이가 남았고 마침표도 아닌데 이을 카드가 0장인 문장.</item>
/// <item>제시 장수 — 3장을 못 채운 판의 비율.</item>
/// <item>id 왕복 — 만든 문장 id를 다시 읽으면 같은 문장인가.</item>
/// </list>
/// 결과: Temp/covenant_grammar_audit.txt · 로그 「[GrammarAudit] 끝」.
/// </summary>
public static class CovenantGrammarAuditEditor
{
    private const int WalksPerStart = 12;

    [MenuItem("RelicFairy/Debug/10-02 서약서 문법 전수 검증")]
    private static void Run()
    {
        var sb = new StringBuilder("서약서 문법 전수 검증\n");
        var rng = new System.Random(20261002);
        int boards = 0, invalid = 0, deadEnds = 0, shortBoards = 0, roundTripFail = 0, rewriteBoards = 0, rewriteInvalid = 0, causeBoards = 0, causeInvalid = 0;
        var lengthHist = new int[CovenantGrammar.MaxResults + 1];
        var linkHist = new Dictionary<ClauseLink, int>();
        var examples = new List<string>();

        for (int step = 0; step <= 3; step++)
        {
            foreach (var cause in CovenantPalette.DraftableCauseIds(step))
            foreach (var first in CovenantPalette.EffectIds)
            {
                if (CovenantPalette.UnlockStep(first) > step) continue;
                var e0 = new List<string> { first };
                var l0 = new List<ClauseLink> { ClauseLink.Immediate };
                if (!CovenantSentenceService.IsValid(cause, e0, l0)) continue;

                for (int w = 0; w < WalksPerStart; w++)
                {
                    var effects = new List<string>(e0);
                    var links   = new List<ClauseLink>(l0);
                    while (true)
                    {
                        var cards = CovenantSentenceService.DraftAppend(cause, effects, links, 3, rng, null, step);
                        bool full = effects.Count >= CovenantGrammar.MaxResultsNow || CovenantGrammar.IsTerminal(effects[effects.Count - 1]);   // 지금 상한(「다섯째 절」 미해금 = 4)
                        if (cards.Count == 0)
                        {
                            if (!full) { deadEnds++; if (examples.Count < 12) examples.Add($"막다른 끝 [{step}] {cause} > {string.Join(" > ", effects)}"); }
                            break;
                        }
                        boards++;
                        if (cards.Count < 3) shortBoards++;
                        foreach (var c in cards)
                        {
                            var te = new List<string>(effects) { c.effectId };
                            var tl = new List<ClauseLink>(links) { c.link };
                            if (!CovenantSentenceService.IsValid(cause, te, tl)) { invalid++; if (examples.Count < 12) examples.Add($"무효 카드 {cause} > {string.Join(" > ", te)}"); }
                        }

                        // 고쳐 쓰기 — 아무 자리 하나
                        int idx = rng.Next(effects.Count);
                        var rw = CovenantSentenceService.DraftRewrite(cause, effects, links, idx, 3, rng, null, step);
                        rewriteBoards++;
                        foreach (var c in rw)
                        {
                            var te = new List<string>(effects); te[idx] = c.effectId;
                            if (!CovenantSentenceService.IsValid(cause, te, links)) rewriteInvalid++;
                        }
                        var rc = CovenantSentenceService.DraftRewriteCause(cause, effects, links, 3, rng, null, step);
                        causeBoards++;
                        foreach (var c in rc)
                            if (!CovenantSentenceService.IsValid(c.id, effects, links)) causeInvalid++;

                        var pick = cards[rng.Next(cards.Count)];
                        effects.Add(pick.effectId);
                        links.Add(pick.link);
                        linkHist[pick.link] = linkHist.TryGetValue(pick.link, out var n) ? n + 1 : 1;
                    }
                    lengthHist[effects.Count]++;

                    // id 왕복
                    var parts = new List<(string, CovenantTier, ClauseLink)>();
                    for (int i = 0; i < effects.Count; i++) parts.Add((effects[i], CovenantTier.Gold, links[i]));
                    string id = CovenantSentence.MakeId(cause, CovenantTier.Ruby, parts);
                    var back = new CovenantSentence(id.Substring(CovenantSentence.Prefix.Length));
                    if (!back.Resolved || back.CovenantId != id) { roundTripFail++; if (examples.Count < 12) examples.Add("왕복 실패 " + id); }
                }
            }
        }

        sb.AppendLine($"이어 쓰기 판 {boards} · 무효 카드 {invalid} · 3장 못 채운 판 {shortBoards} ({(boards > 0 ? 100f * shortBoards / boards : 0f):0.0}%) · 막다른 끝 {deadEnds}");
        sb.AppendLine($"고쳐 쓰기 판 {rewriteBoards} · 무효 {rewriteInvalid} · 조건 고쳐 쓰기 판 {causeBoards} · 무효 {causeInvalid}");
        sb.AppendLine($"id 왕복 실패 {roundTripFail}");
        sb.Append("문장 길이 분포:");
        for (int i = 1; i < lengthHist.Length; i++) sb.Append($" {i}절 {lengthHist[i]}");
        sb.AppendLine();
        sb.Append("고른 이음:");
        foreach (var kv in linkHist) sb.Append($" {kv.Key} {kv.Value}");
        sb.AppendLine();
        foreach (var e in examples) sb.AppendLine("  " + e);

        File.WriteAllText("Temp/covenant_grammar_audit.txt", sb.ToString());
        Debug.Log("[GrammarAudit] 끝\n" + sb);
    }
}
#endif
