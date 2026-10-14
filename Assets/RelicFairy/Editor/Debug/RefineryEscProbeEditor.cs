#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-11 정제소 ESC 실측 — 굴린 직후(공개 연출 중) ESC를 눌러도 창이 닫히지 않고, 룬이 룬판으로 넘어가는지 본다.
/// 테스트 허브에서 Ch1 런을 띄우고 원석을 준 뒤 정제소 패널을 열어 [돌리기] → 0.3초 뒤 ESC(UIManager 공용 경로).
/// 결과: Temp/refinery_esc_probe.txt · 로그 「[RefineryEsc] 끝」. CHECK 두 줄이 모두 True면 합격.
/// </summary>
public static class RefineryEscProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-11 정제소 ESC 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[RefineryEsc] 플레이 모드(테스트 허브)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("정제소 ESC 실측\n");
        try
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher != null && GameRunBootstrapper.Instance?.Run?.Player == null)
            {
                launcher.SelectChapter(1);
                typeof(TestHubLauncher).GetField("_bossApproach", Inst)?.SetValue(launcher, false);
                launcher.TryLaunch();
            }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 60f && GameRunBootstrapper.Instance?.Run?.Player == null)
                await UniTask.Delay(500, DelayType.Realtime);
            var run = GameRunBootstrapper.Instance?.Run;
            if (run == null) { sb.AppendLine("런이 시작되지 않음"); return; }
            await UniTask.Delay(3000, DelayType.Realtime);

            run.FuelBank?.Add(FuelKind.RuneOre, 40);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_RefineryPanel>();
            if (panel == null) { sb.AppendLine("정제소 패널을 못 열었다"); return; }
            await UniTask.Delay(600, DelayType.Realtime);

            int oreBefore = run.FuelBank?.RuneOre ?? -1;
            typeof(UI_RefineryPanel).GetMethod("OnSpinClicked", Inst)?.Invoke(panel, null);
            await UniTask.Delay(300, DelayType.Realtime);
            bool consumed = Managers.UI.TryCloseTopPopupOnEscape();
            await UniTask.Delay(200, DelayType.Realtime);
            bool alive = panel != null;
            sb.AppendLine($"원석 {oreBefore} → {run.FuelBank?.RuneOre} · ESC 소비 {consumed} · ESC 뒤 창 살아 있음 {alive}");

            bool delivered = false;
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 8f)
            {
                if (UI_GridPanel.Instance != null && UI_GridPanel.Instance.IsOpen) { delivered = true; break; }
                await UniTask.Delay(200, DelayType.Realtime);
            }
            sb.AppendLine($"룬판 열림(룬 넘김) {delivered} · {Time.realtimeSinceStartup - t0:0.0}초");
            sb.AppendLine($"CHECK ESC 뒤 창 유지: {alive}");
            sb.AppendLine($"CHECK 룬이 룬판으로 넘어감: {delivered}");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/refinery_esc_probe.txt", sb.ToString());
            Debug.Log("[RefineryEsc] 끝\n" + sb);
        }
    }
}
#endif
