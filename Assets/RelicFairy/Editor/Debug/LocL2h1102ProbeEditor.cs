#if UNITY_EDITOR
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q11 L3 서약 · 시너지 실측(플레이 중 아무 곳) — 결과 Temp/loc_l2h1102.txt · 로그 「[LH1102] 끝」.
/// 영어: 서약 원인 · 효과 이름/설명/태그(조회 사본) · 시너지 설명 · Perfect Dodge 용어 → 한국어로 되돌려 원본이 그대로인가.
/// </summary>
public static class LocL2h1102ProbeEditor
{
    [MenuItem("RelicFairy/Debug/11-02 영어 서약 · 시너지 실측 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[LH1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-02 영어 서약 · 시너지\n");
        var lang0 = Loc.Current;
        try
        {
            await Loc.PreloadAsync();
            Loc.SetLanguage(Loc.Language.English);
            CovenantPalette.TryGetCause("streak", out var c);
            Check(sb, "서약 원인", $"{c.name} | {c.desc} | {c.tag}", "Streak | Every 3 consecutive hits on the same enemy | Focus");
            CovenantPalette.TryGetEffect("sanguine", out var e);   // 별칭(흡정 → 결계)도 사본이 번역되나
            Check(sb, "서약 효과(별칭)", $"{e.name} | {e.tag}", "Ward | Survival");
            Check(sb, "시너지 설명", Loc.T("1단계 잔불: 적중마다 공격력 4%의 잔불이 모여 5번째 적중에 한 번에 터진다"),
                  "Tier 1 Embers: each hit gathers embers worth 4% ATK that burst all at once on the 5th hit");
            Check(sb, "Perfect Dodge 용어", Loc.T("긴급 회피"), "Perfect Dodge");

            Loc.SetLanguage(Loc.Language.Korean);
            CovenantPalette.TryGetCause("streak", out var k);
            Check(sb, "한국어로 되돌림(원본 그대로)", $"{k.name} | {k.tag}", "연격 | 집중");
        }
        catch (System.Exception ex) { sb.AppendLine("예외: " + ex); }
        finally
        {
            Loc.SetLanguage(lang0);
            File.WriteAllText("Temp/loc_l2h1102.txt", sb.ToString());
            Debug.Log("[LH1102] 끝 → Temp/loc_l2h1102.txt");
        }
    }

    private static void Check(StringBuilder sb, string label, string got, string want)
        => sb.AppendLine($"{(got == want ? "PASS" : "FAIL")} {label} 「{got}」");
}
#endif
