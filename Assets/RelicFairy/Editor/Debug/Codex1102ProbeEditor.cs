#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q2 도감 실측(테스트 허브 런 중) — 결과 Temp/codex1102.txt · 사진 Temp/ui_shots/CD1102_tab{0~3}.png · 로그 「[CD1102] 끝」.
/// ESC 메뉴를 열고 「도감」 → 탭마다 줄 수 · 지금 · 써 봄 수 → 첫 줄을 눌러 설명 → 사진 → ESC로 패널 · 메뉴 닫기.
/// </summary>
public static class Codex1102ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/11-02 도감 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[CD1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-02 도감\n");
        Directory.CreateDirectory("Temp/ui_shots");
        UI_EscMenu menu = null;
        try
        {
            menu = Object.FindFirstObjectByType<UI_EscMenu>(FindObjectsInactive.Include);
            if (menu == null) { sb.AppendLine("ESC 메뉴 없음"); return; }
            menu.Open();
            await UniTask.Delay(400, ignoreTimeScale: true);
            typeof(UI_EscMenu).GetMethod("OnCodex", Inst).Invoke(menu, null);
            await UniTask.Delay(500, ignoreTimeScale: true);
            var page = Object.FindFirstObjectByType<UI_CodexPage>();
            if (page == null) { sb.AppendLine("FAIL 도감 쪽이 안 열림"); return; }
            var show = typeof(UI_CodexPage).GetMethod("ShowTab", Inst);
            var pick = typeof(UI_CodexPage).GetMethod("Pick", Inst);
            var entries = typeof(UI_CodexPage).GetField("_entries", Inst);
            var detail = typeof(UI_CodexPage).GetField("_detailBody", Inst);
            for (int tab = 0; tab < 4; tab++)
            {
                show.Invoke(page, new object[] { tab });
                await UniTask.Delay(300, ignoreTimeScale: true);
                var list = entries.GetValue(page) as IList;
                int n = list?.Count ?? 0, active = 0, seen = 0;
                if (list != null)
                    foreach (var e in list)
                    {
                        if ((bool)e.GetType().GetField("Active").GetValue(e)) active++;
                        if ((bool)e.GetType().GetField("Seen").GetValue(e)) seen++;
                    }
                if (n > 0) pick.Invoke(page, new object[] { 0 });
                await UniTask.Delay(250, ignoreTimeScale: true);
                string body = (detail.GetValue(page) as TMPro.TMP_Text)?.text ?? "";
                sb.AppendLine($"{(n > 0 ? "PASS" : "FAIL")} 탭 {tab} — 줄 {n} · 지금 {active} · 써 봄 {seen} · 첫 줄 설명 「{(body.Length > 40 ? body.Substring(0, 40) + "…" : body).Replace("\n", " ")}」");
                ScreenCapture.CaptureScreenshot($"Temp/ui_shots/CD1102_tab{tab}.png");
                await UniTask.Delay(300, ignoreTimeScale: true);
            }
            menu.HandleEscape();
            await UniTask.Delay(300, ignoreTimeScale: true);
            sb.AppendLine($"ESC → 도감 닫힘 {!page.gameObject.activeSelf} · 메뉴 열림 {menu.IsOpen}");
            sb.AppendLine($"써 봄 기록(메타) 길이 {BackendGameData.Instance?.Data?.codexSeen?.Length ?? 0}");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (menu != null && menu.IsOpen) menu.Close();
            File.WriteAllText("Temp/codex1102.txt", sb.ToString());
            Debug.Log("[CD1102] 끝 → Temp/codex1102.txt");
        }
    }
}
#endif
