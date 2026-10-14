#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-13 실측 도우미 — 테스트 허브에서 유물을 골라(Temp/relic_launch.txt 첫 줄 = RelicId 이름, 예: Gawain · Lancelot) Ch1 런을 열고
/// 첫 전투방까지 들어간다(서비스 방 둘러보기 도구의 진입 절차). 유물 · 장비 HUD 신호 실측처럼 「지금 런의 유물」만 찍는 도구 앞에 쓴다.
/// 로그 「[RelicLaunch] 끝 — 유물 …」.
/// </summary>
public static class RelicLaunchProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string ArgsPath = "Temp/relic_launch.txt";

    [MenuItem("RelicFairy/Debug/10-13 테스트 허브 런 시작 — 유물 지정 (Temp/relic_launch.txt, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[RelicLaunch] 플레이 모드(테스트 허브)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string want = File.Exists(ArgsPath) ? File.ReadAllText(ArgsPath).Trim() : "Gawain";
        string result = "실패";
        try
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher == null) { result = "TestHubLauncher 없음"; return; }
            var relics = typeof(TestHubLauncher).GetField("relics", Inst)?.GetValue(launcher) as RelicClassSO[];
            int idx = -1;
            if (relics != null)
                for (int i = 0; i < relics.Length; i++)
                    if (relics[i] != null && string.Equals(relics[i].Id.ToString(), want, System.StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
            if (idx < 0) { result = $"유물 {want}을 허브 목록에서 못 찾음"; return; }
            typeof(TestHubLauncher).GetField("_relicIndex", Inst)?.SetValue(launcher, idx);
            typeof(TestHubLauncher).GetField("_chapter", Inst)?.SetValue(launcher, 1);
            typeof(TestHubLauncher).GetField("_bossApproach", Inst)?.SetValue(launcher, false);
            if (GameRunBootstrapper.Instance?.Run?.Player == null) launcher.TryLaunch();

            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 60f && GameRunBootstrapper.Instance?.Run?.Player == null)
                await UniTask.Delay(500, DelayType.Realtime);
            var ensure = typeof(ServiceRoomTourProbeEditor).GetMethod("EnsureProcRunAsync", BindingFlags.Static | BindingFlags.NonPublic);
            if (ensure != null) await (UniTask<bool>)ensure.Invoke(null, null);
            await UniTask.Delay(2500, DelayType.Realtime);   // 첫 방 입장 연출
            var relic = AppBootstrapper.Instance?.Loadout?.Relic;
            result = $"유물 {(relic != null ? relic.Id.ToString() : "없음")} · 방 {GameRunBootstrapper.Instance?.Run?.CurrentRoomKind}";
        }
        catch (System.Exception e) { result = "예외: " + e.Message; }
        finally { Debug.Log($"[RelicLaunch] 끝 — {result}"); }
    }
}
#endif
