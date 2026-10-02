using System.Collections.Generic;

/// <summary>
/// 옛 조립 서약(<c>asm:</c> 여러 개) → 서약서 한 장(<c>sen:</c>) — 이어하기 세이브 이행(설계서 §7-1).
/// 첫 서약을 문장(조건 + 결과 1)으로, 나머지는 즉시 이음으로 이을 수 있으면 붙이고 아니면 버린다(런 세이브는 짧게 산다).
/// </summary>
public static class CovenantSentenceMigration
{
    /// <summary>조립 서약 id들 → 서약서 id(하나도 못 읽으면 null).</summary>
    public static string FromAssembled(IEnumerable<string> asmIds)
    {
        if (asmIds == null) return null;
        string cause = null;
        var causeTier = CovenantTier.Silver;
        var effects = new List<string>();
        var links   = new List<ClauseLink>();
        var parts   = new List<(string, CovenantTier, ClauseLink)>();

        foreach (var id in asmIds)
        {
            if (!TryParse(id, out var c, out var ct, out var e, out var et)) continue;
            if (cause == null)
            {
                var e0 = new List<string> { e };
                var l0 = new List<ClauseLink> { ClauseLink.Immediate };
                if (!CovenantSentenceService.IsValid(c, e0, l0)) continue;
                cause = c; causeTier = ct;
                effects.Add(e); links.Add(ClauseLink.Immediate); parts.Add((e, et, ClauseLink.Immediate));
                continue;
            }
            effects.Add(e); links.Add(ClauseLink.Immediate);
            if (CovenantSentenceService.IsValid(cause, effects, links)) { parts.Add((e, et, ClauseLink.Immediate)); continue; }
            effects.RemoveAt(effects.Count - 1); links.RemoveAt(links.Count - 1);
        }
        return cause == null ? null : CovenantSentence.MakeId(cause, causeTier, parts);
    }

    private static bool TryParse(string id, out string cause, out CovenantTier causeTier, out string effect, out CovenantTier effectTier)
    {
        cause = effect = null; causeTier = effectTier = CovenantTier.Silver;
        if (string.IsNullOrEmpty(id) || !id.StartsWith(AssembledCovenant.Prefix)) return false;
        var body = id.Substring(AssembledCovenant.Prefix.Length);
        int sep = body.IndexOf('|');
        if (sep <= 0) return false;
        (cause, causeTier)   = Split(body.Substring(0, sep));
        (effect, effectTier) = Split(body.Substring(sep + 1));
        return CovenantPalette.TryGetCause(cause, out _) && CovenantPalette.TryGetEffect(effect, out _);
    }

    private static (string, CovenantTier) Split(string part)
    {
        int at = part.IndexOf('@');
        return at < 0 ? (part, CovenantTier.Silver) : (part.Substring(0, at), CovenantTierUtil.Parse(part.Substring(at + 1)));
    }
}
