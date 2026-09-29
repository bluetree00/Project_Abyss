using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [검증 도구 · 편집 모드] 서약 조립 화면의 뽑기가 <b>고를 수 없는 조합을 한 번도 제시하지 않는지</b> 전수로 확인한다.
///
/// 제단 개방 단계 0~3마다 가상의 런을 여러 번 돌린다. 한 런은 뽑기 → (교체 몇 번) → 하나를 골라 보유 → 다시 뽑기를
/// 칸 수만큼 반복한다. 매 화면에서 확인하는 것:
///  • 원인 3장·효과 3장이 다 채워졌는가, 같은 열에 중복이 없는가
///  • 원인 × 효과 9칸이 전부 <see cref="CovenantPalette.CanPair"/>를 통과하는가(봉인 짝·이미 가진 짝 없음)
///  • 뽑기에서 뺀 원인(개선)과 아직 안 열린 카드가 나오지 않는가
///  • 소모형은 그 통화를 거는 서약을 들고 있을 때만 나오는가(C4)
///  • 방어축 카드가 한 장 이상 있는가(후보에 있을 때)
/// 결과: Temp/covenant_draft_audit.json
/// </summary>
public static class CovenantDraftAuditEditor
{
    private const int RunsPerStep = 3000;
    private const int Slots       = 4;
    private const int DraftCount  = 3;

    [MenuItem("RelicFairy/Debug/서약 뽑기 전수 검증")]
    private static void Run()
    {
        var overrideField = typeof(CovenantAssembleService).GetField("_stepOverride", BindingFlags.Static | BindingFlags.NonPublic);
        var sb  = new StringBuilder("{\"steps\":[");
        var rng = new System.Random(20260917);
        try
        {
            for (int step = 0; step <= 3; step++)
            {
                overrideField.SetValue(null, (int?)step);
                if (step > 0) sb.Append(',');
                sb.Append(AuditStep(step, rng));
            }
        }
        finally
        {
            overrideField.SetValue(null, null);
        }
        sb.Append("]}");
        File.WriteAllText(Path.Combine("Temp", "covenant_draft_audit.json"), sb.ToString());
        Debug.Log("[CovenantDraftAudit] " + sb);
    }

    private static string AuditStep(int step, System.Random rng)
    {
        int boards = 0, shortBoards = 0, dupCards = 0, badPairs = 0, lockedCards = 0, excludedCause = 0,
            orphanConsumer = 0, noSurvival = 0, rerolls = 0, badRerolls = 0, nullRerolls = 0;
        var causeSeen  = new Dictionary<string, int>();
        var effectSeen = new Dictionary<string, int>();
        var forged     = new HashSet<string>();

        for (int run = 0; run < RunsPerStep; run++)
        {
            var held = new List<CovenantBase>();
            for (int slot = 0; slot < Slots; slot++)
            {
                CovenantAssembleService.DraftBoard(DraftCount, rng, false, held, out var causes, out var effects);
                boards++;
                Check(step, held, causes, effects, ref shortBoards, ref dupCards, ref badPairs, ref lockedCards,
                      ref excludedCause, ref orphanConsumer, ref noSurvival, causeSeen, effectSeen);

                // 교체 두 번(아무 열 · 아무 칸)
                for (int r = 0; r < 2; r++)
                {
                    bool isCause = rng.Next(2) == 0;
                    int  idx     = rng.Next(DraftCount);
                    CovenantDraftCard? rolled;
                    if (isCause)
                    {
                        var exclude = new HashSet<string>();
                        foreach (var c in causes) exclude.Add(c.id);
                        rolled = CovenantAssembleService.RerollCauseCard(exclude, rng, false, effects, held);
                    }
                    else rolled = CovenantAssembleService.RerollEffectCard(effects, idx, rng, false, held, causes);

                    rerolls++;
                    if (rolled == null) { nullRerolls++; continue; }
                    (isCause ? causes : effects)[idx] = rolled.Value;
                    int before = badPairs + dupCards + lockedCards + orphanConsumer;
                    int dummyShort = 0, dummyExcl = 0, dummySurv = 0;
                    Check(step, held, causes, effects, ref dummyShort, ref dupCards, ref badPairs, ref lockedCards,
                          ref dummyExcl, ref orphanConsumer, ref dummySurv, null, null);
                    excludedCause += dummyExcl;
                    if (badPairs + dupCards + lockedCards + orphanConsumer > before) badRerolls++;
                }

                // 아무 칸이나 골라 벼린다(9칸 모두 벼릴 수 있어야 하므로 무작위로 충분하다)
                var cc = causes[rng.Next(causes.Count)];
                var ee = effects[rng.Next(effects.Count)];
                forged.Add(cc.id + "|" + ee.id);
                held.Add(new AssembledCovenant(cc.id + "@" + cc.tier.Code(), ee.id + "@" + ee.tier.Code()));
            }
        }

        var s = new StringBuilder();
        s.Append("{\"step\":").Append(step)
         .Append(",\"causePool\":").Append(CovenantPalette.DraftableCauseIds(step).Count)
         .Append(",\"effectPool\":").Append(CountEffects(step))
         .Append(",\"boards\":").Append(boards)
         .Append(",\"shortBoards\":").Append(shortBoards)
         .Append(",\"dupCards\":").Append(dupCards)
         .Append(",\"badPairs\":").Append(badPairs)
         .Append(",\"lockedCards\":").Append(lockedCards)
         .Append(",\"excludedCause\":").Append(excludedCause)
         .Append(",\"orphanConsumer\":").Append(orphanConsumer)
         .Append(",\"noSurvival\":").Append(noSurvival)
         .Append(",\"rerolls\":").Append(rerolls)
         .Append(",\"badRerolls\":").Append(badRerolls)
         .Append(",\"nullRerolls\":").Append(nullRerolls)
         .Append(",\"distinctForged\":").Append(forged.Count)
         .Append(",\"causeSeen\":").Append(Dict(causeSeen))
         .Append(",\"effectSeen\":").Append(Dict(effectSeen))
         .Append('}');
        return s.ToString();
    }

    private static void Check(int step, List<CovenantBase> held,
                              List<CovenantDraftCard> causes, List<CovenantDraftCard> effects,
                              ref int shortBoards, ref int dupCards, ref int badPairs, ref int lockedCards,
                              ref int excludedCause, ref int orphanConsumer, ref int noSurvival,
                              Dictionary<string, int> causeSeen, Dictionary<string, int> effectSeen)
    {
        if (causes.Count < DraftCount || effects.Count < DraftCount) shortBoards++;

        var cset = new HashSet<string>();
        foreach (var c in causes)
        {
            if (!cset.Add(c.id)) dupCards++;
            if (c.id == "clear") excludedCause++;
            if (CovenantPalette.UnlockStep(c.id) > step) lockedCards++;
            if (causeSeen != null) causeSeen[c.id] = causeSeen.TryGetValue(c.id, out int n) ? n + 1 : 1;
        }

        var draftable = new HashSet<string>(CovenantPalette.DraftableEffectIds(held, step));
        var eset = new HashSet<string>();
        bool survival = false;
        foreach (var e in effects)
        {
            if (!eset.Add(e.id)) dupCards++;
            if (CovenantPalette.UnlockStep(e.id) > step) lockedCards++;
            if (!draftable.Contains(e.id)) orphanConsumer++;   // 단계 안인데 후보 밖 = 페어링 위반
            if (CovenantPalette.IsGuaranteedSurvivalEffect(e.id)) survival = true;
            if (effectSeen != null) effectSeen[e.id] = effectSeen.TryGetValue(e.id, out int n) ? n + 1 : 1;
        }
        if (!survival) noSurvival++;

        foreach (var c in causes)
            foreach (var e in effects)
                if (!CovenantPalette.CanPair(c.id, e.id, held)) badPairs++;
    }

    private static int CountEffects(int step) => CovenantPalette.DraftableEffectIds(null, step).Count;

    private static string Dict(Dictionary<string, int> d)
    {
        var sb = new StringBuilder("{");
        bool first = true;
        foreach (var kv in d)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(kv.Key).Append("\":").Append(kv.Value);
        }
        return sb.Append('}').ToString();
    }
}
