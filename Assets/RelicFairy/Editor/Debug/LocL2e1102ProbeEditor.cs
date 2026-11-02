#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q11 L2 5·6차 실측(플레이 중 아무 곳) — 결과 Temp/loc_l2e1102.txt · 로그 「[LE1102] 끝」.
/// 영어에서 문 라벨(방 종류 · 둘째 줄) · 이벤트방 배지 · 줄바꿈 든 열쇠(유물 HUD 「여명\n조각 {0}/{1}」) · 38 공명 줄.
/// </summary>
public static class LocL2e1102ProbeEditor
{
    private const BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/11-02 영어 문 라벨 · 유물 HUD 실측 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[LE1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-02 영어 문 라벨 · 유물 HUD\n");
        var lang0 = Loc.Current;
        try
        {
            await Loc.PreloadAsync();
            Loc.SetLanguage(Loc.Language.English);
            var sub   = typeof(RunFlowController).GetMethod("KindSubtitle", Stat);
            var title = typeof(RunFlowController).GetMethod("KindTitle", Stat);
            Check(sb, "보스 제목", (string)title.Invoke(null, new object[] { RoomPlanKind.Boss, null }), "Boss", true);
            Check(sb, "보스 둘째 줄", (string)sub.Invoke(null, new object[] { RoomPlanKind.Boss, null, null }), "Boss fight · win for a Relic Memory card", true);
            Check(sb, "전투 둘째 줄", (string)sub.Invoke(null, new object[] { RoomPlanKind.Normal, null, "FIRE" }), "1 of ", false);
            Check(sb, "쉼터 둘째 줄", (string)sub.Invoke(null, new object[] { RoomPlanKind.Rest, null, null }), " or ", false);
            Check(sb, "이벤트 배지(보물 창고)", (string)sub.Invoke(null, new object[] { RoomPlanKind.Event, "event_vault", null }), "Reward", false);
            Check(sb, "줄바꿈 열쇠", Loc.T("여명\n조각 {0}/{1}"), "Dawn\nPieces {0}/{1}", true);
            Check(sb, "38 공명 줄", Loc.T("여명"), "Dawn", true);
            // L3 룬 설명 — 효과 표시 입구 · 상태이상 · 단위 · 용어 풀이
            Check(sb, "룬 설명(일반)", EffectDescriptionFormatter.Describe("AllDamage", 0.08f, "Always", "공격력 +8%").Combined, "ATK +8%", true);
            string heavy = EffectDescriptionFormatter.Describe("TriggerBurst", 0.6f, "OnHeavyHit", "강타 때 대상 자리에 속성 폭발 — 반경 2.5m · 공격력 60%").Combined;
            Check(sb, "룬 설명(강타)", heavy, "On Heavy Hit, an elemental explosion at the target — 2.5m radius · 60% ATK", true);
            Check(sb, "용어 풀이 영어", UIGlossary.Append(heavy), "Heavy Hit = a weighty blow", false);
            Check(sb, "설명 없는 라벨", EffectDescriptionFormatter.Describe("AttackSpeed", 0.04f, "OnHit").Combined, "Attack Speed +4% (On hit)", true);
            Check(sb, "상태이상 이름", EffectDescriptionFormatter.LabelForStatus("burn"), "Burn", true);
            Check(sb, "단위 글", EffectDescriptionFormatter.FormatValue(EffectUnit.Duration, 2f), "2s", true);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Loc.SetLanguage(lang0);
            File.WriteAllText("Temp/loc_l2e1102.txt", sb.ToString());
            Debug.Log("[LE1102] 끝 → Temp/loc_l2e1102.txt");
        }
    }

    private static void Check(StringBuilder sb, string label, string got, string want, bool exact)
    {
        bool ok = exact ? got == want : got != null && got.Contains(want) && !System.Text.RegularExpressions.Regex.IsMatch(got, "[가-힣]");
        sb.AppendLine($"{(ok ? "PASS" : "FAIL")} {label} 「{got?.Replace("\n", "\\n")}」");
    }
}
#endif
