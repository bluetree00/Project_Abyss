#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q32(26 몫) 고침 확인(테스트 허브 런 중) — 결과 Temp/qa1102.txt · 로그 「[QA1102] 끝」.
///  · 환전소를 연 채 ESC 처리기 한 번(키 없이) → ESC 메뉴가 안 열리고 환전소는 그대로(예전엔 밑의 창을 닫거나 메뉴가 열렸다) → 환전소 닫기.
/// </summary>
public static class Qa1102ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/11-02 Q32 UI 허점 고침 확인 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[QA1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-02 Q32 UI 허점 고침 확인\n");
        try
        {
            var run = GameRunBootstrapper.Instance?.Run;
            var esc = Object.FindFirstObjectByType<EscKeyListener>(FindObjectsInactive.Include);
            var menu = Object.FindFirstObjectByType<UI_EscMenu>(FindObjectsInactive.Include);
            if (run == null || esc == null) { sb.AppendLine($"런 {run != null} · ESC 처리기 {esc != null}"); return; }

            var open = ExchangePopupView.OpenAsync(run, default);
            await UniTask.Delay(400, ignoreTimeScale: true);
            typeof(EscKeyListener).GetMethod("Handle", Inst).Invoke(esc, null);
            await UniTask.Delay(300, ignoreTimeScale: true);
            bool menuOpen = menu != null && menu.IsOpen;
            sb.AppendLine($"{(!menuOpen && ExchangePopupView.IsOpen ? "PASS" : "FAIL")} 환전소 위 ESC 처리기 — 메뉴 열림 {menuOpen} · 환전소 그대로 {ExchangePopupView.IsOpen}");
            if (menuOpen) menu.Close();

            var go = GameObject.Find("@Exchange");
            if (go != null) foreach (var b in go.GetComponentsInChildren<UnityEngine.UI.Button>()) if (b.name == "Btn_닫기") b.onClick.Invoke();
            await open;
            sb.AppendLine($"환전소 닫힘 {!ExchangePopupView.IsOpen} · 시간 배율 {Time.timeScale:0.00}");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/qa1102.txt", sb.ToString());
            Debug.Log("[QA1102] 끝 → Temp/qa1102.txt");
        }
    }
}
#endif
