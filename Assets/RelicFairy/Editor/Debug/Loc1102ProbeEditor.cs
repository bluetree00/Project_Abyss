#if UNITY_EDITOR
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q11 L1 실측(플레이 중 아무 곳) — 결과 Temp/loc1102.txt · 사진 Temp/ui_shots/LC1102_en.png · 로그 「[LC1102] 끝」.
/// 표(STRINGS) 읽기 → 영어로 바꿔 ESC 메뉴 글(제목 · 단추) → 표에 없는 글은 한글 그대로 → 한국어로 되돌려 글이 돌아오나 → 처음 언어로.
/// </summary>
public static class Loc1102ProbeEditor
{
    [MenuItem("RelicFairy/Debug/11-02 영어 전환 기반 실측 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[LC1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-02 영어 전환 기반\n");
        Directory.CreateDirectory("Temp/ui_shots");
        var lang0 = Loc.Current;
        UI_EscMenu menu = null;
        try
        {
            await Loc.PreloadAsync();
            Loc.SetLanguage(Loc.Language.English);
            string resume = Loc.T("계속하기"), missing = Loc.T("표에 없는 글");
            sb.AppendLine($"{(resume == "Resume" ? "PASS" : "FAIL")} 표 읽기 — 「계속하기」 → 「{resume}」");
            sb.AppendLine($"{(missing == "표에 없는 글" ? "PASS" : "FAIL")} 없는 글은 한글 그대로 — 「{missing}」");

            menu = Object.FindFirstObjectByType<UI_EscMenu>(FindObjectsInactive.Include);
            if (menu == null) { sb.AppendLine("ESC 메뉴 없음"); return; }
            menu.Open();
            await UniTask.Delay(400, ignoreTimeScale: true);
            string title = Find(menu, "Title"), btn = Find(menu, "Btn_도감/Label");
            sb.AppendLine($"{(title == "Paused" && btn == "Codex" ? "PASS" : "FAIL")} 영어 ESC 메뉴 — 제목 「{title}」 · 도감 단추 「{btn}」");
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/LC1102_en.png");
            await UniTask.Delay(300, ignoreTimeScale: true);

            Loc.SetLanguage(Loc.Language.Korean);
            await UniTask.Delay(100, ignoreTimeScale: true);
            title = Find(menu, "Title"); btn = Find(menu, "Btn_도감/Label");
            sb.AppendLine($"{(title == "일시정지" && btn == "도감" ? "PASS" : "FAIL")} 한국어로 되돌림(열린 채 다시 씀) — 제목 「{title}」 · 도감 단추 「{btn}」");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (menu != null && menu.IsOpen) menu.Close();
            Loc.SetLanguage(lang0);
            File.WriteAllText("Temp/loc1102.txt", sb.ToString());
            Debug.Log("[LC1102] 끝 → Temp/loc1102.txt");
        }
    }

    private static string Find(UI_EscMenu menu, string path)
    {
        foreach (var t in menu.GetComponentsInChildren<TMP_Text>(true))
        {
            string p = t.transform.parent != null ? t.transform.parent.name + "/" + t.name : t.name;
            if (t.name == path || p == path) return t.text;
        }
        return "(없음)";
    }
}
#endif
