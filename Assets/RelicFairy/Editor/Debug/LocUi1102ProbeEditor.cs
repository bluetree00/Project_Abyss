#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q11 L2 1차 실측(테스트 허브 런 중) — 결과 Temp/loc_ui1102.txt · 사진 Temp/ui_shots/LU1102_*.png · 로그 「[LU1102] 끝」.
/// 영어로 바꿔 ESC → 도감(탭 글 · 표시) · 환전소(제목 · 가진 것 · 재화 이름) · HUD Tab 칸 → 사진 → 한국어로 되돌림.
/// </summary>
public static class LocUi1102ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/11-02 영어 전환 새 UI 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[LU1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-02 영어 전환 새 UI\n");
        Directory.CreateDirectory("Temp/ui_shots");
        var lang0 = Loc.Current;
        UI_EscMenu menu = null;
        try
        {
            await Loc.PreloadAsync();
            Loc.SetLanguage(Loc.Language.English);
            menu = Object.FindFirstObjectByType<UI_EscMenu>(FindObjectsInactive.Include);
            if (menu != null)
            {
                menu.Open();
                await UniTask.Delay(300, ignoreTimeScale: true);
                typeof(UI_EscMenu).GetMethod("OnCodex", Inst).Invoke(menu, null);
                await UniTask.Delay(500, ignoreTimeScale: true);
                var page = Object.FindFirstObjectByType<UI_CodexPage>();
                var tabs = typeof(UI_CodexPage).GetField("_tabLabels", Inst)?.GetValue(page) as TMP_Text[];
                string tab0 = tabs != null && tabs.Length > 0 ? tabs[0].text : "(없음)";
                sb.AppendLine($"{(tab0.Contains("Runes") ? "PASS" : "FAIL")} 도감 첫 탭 「{tab0}」");
                ScreenCapture.CaptureScreenshot("Temp/ui_shots/LU1102_codex.png");
                await UniTask.Delay(300, ignoreTimeScale: true);
                menu.Close();
                await UniTask.Delay(200, ignoreTimeScale: true);
            }
            var run = GameRunBootstrapper.Instance?.Run;
            if (run != null)
            {
                var open = ExchangePopupView.OpenAsync(run, default);
                await UniTask.Delay(400, ignoreTimeScale: true);
                var go = GameObject.Find("@Exchange");
                string all = "";
                if (go != null) foreach (var t in go.GetComponentsInChildren<TMP_Text>()) all += t.text + " | ";
                bool ok = all.Contains("Exchange") && all.Contains("Owned") && all.Contains("Rune Ore");
                sb.AppendLine($"{(ok ? "PASS" : "FAIL")} 환전소 영어 — {(all.Length > 160 ? all.Substring(0, 160) + "…" : all)}");
                ScreenCapture.CaptureScreenshot("Temp/ui_shots/LU1102_exchange.png");
                await UniTask.Delay(300, ignoreTimeScale: true);
                if (go != null) foreach (var b in go.GetComponentsInChildren<UnityEngine.UI.Button>()) if (b.name == "Btn_닫기") b.onClick.Invoke();
                await open;
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (menu != null && menu.IsOpen) menu.Close();
            Loc.SetLanguage(lang0);
            File.WriteAllText("Temp/loc_ui1102.txt", sb.ToString());
            Debug.Log("[LU1102] 끝 → Temp/loc_ui1102.txt");
        }
    }
}
#endif
