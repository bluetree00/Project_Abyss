#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q11 L2 3차 실측(테스트 허브 런 중) — 결과 Temp/loc_l2c1102.txt · 로그 「[LL1102] 끝」.
/// 병합한 줄(59 보스 · 38 전투 · 26 제단)이 영어로 나오나 → HUD Tab 칸이 언어를 바꾸자마자 다시 쓰이나(영어 → 한국어).
/// </summary>
public static class LocL2c1102ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/11-02 영어 표 병합 · HUD Tab 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[LL1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-02 영어 표 병합 · HUD Tab\n");
        var lang0 = Loc.Current;
        try
        {
            await Loc.PreloadAsync();
            Loc.SetLanguage(Loc.Language.English);
            Check(sb, "리치", "Lich");
            Check(sb, "봉인석 점화 ({0} / {1})", "Seal stones lit ({0} / {1})");
            Check(sb, "<color=#E8C07A>결의</color> — 쓰러질 한 방을 체력 1로 버텼다", "<color=#E8C07A>Resolve</color> — endured a fatal blow at 1 HP");
            Check(sb, "정수로 해금", "Unlock with Essence");
            string f = Loc.F("정수 {0:N0} · 「{1}」까지 {2:N0}", 1200, "X", 300);
            sb.AppendLine($"{(f == "Essence 1,200 · 300 more for \"X\"" ? "PASS" : "FAIL")} 형식 글 「{f}」");

            var hud = Object.FindFirstObjectByType<HudView>();
            var tab = typeof(HudView).GetField("_runeTabText", Inst)?.GetValue(hud) as TMP_Text;
            if (tab == null || !tab.gameObject.activeInHierarchy) { sb.AppendLine("HUD Tab 칸 없음/숨김"); return; }
            await UniTask.DelayFrame(3);
            sb.AppendLine($"{(tab.text.Contains("Rune Board") ? "PASS" : "FAIL")} 영어로 바꾼 직후 Tab 칸 「{tab.text}」");
            Loc.SetLanguage(Loc.Language.Korean);
            await UniTask.DelayFrame(3);
            sb.AppendLine($"{(tab.text.Contains("룬판") ? "PASS" : "FAIL")} 한국어로 되돌린 직후 Tab 칸 「{tab.text}」");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Loc.SetLanguage(lang0);
            File.WriteAllText("Temp/loc_l2c1102.txt", sb.ToString());
            Debug.Log("[LL1102] 끝 → Temp/loc_l2c1102.txt");
        }
    }

    private static void Check(StringBuilder sb, string ko, string en)
    {
        string got = Loc.T(ko);
        sb.AppendLine($"{(got == en ? "PASS" : "FAIL")} 「{ko}」 → 「{got}」");
    }
}
#endif
