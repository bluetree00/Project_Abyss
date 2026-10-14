#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-10 대사 · 텍스트 P0 실측 — 보스 장면 대사 키가 CSV에서 읽히는지, 리치 패턴 외침 키, 고친 줄, 부활 화면 문구.
/// 테스트 허브 → Ch1 런 → 대사 키 조회 → 부활 연출을 띄워 문구를 읽고 찍는다(Temp/ui_shots/P0_Revival.png).
/// 결과: Temp/text_p0_probe.txt · 로그 「[TextP0] 끝」. 실측 뒤 플레이를 멈출 것.
/// </summary>
public static class TextP0ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly string[] SceneKeys =
    {
        "Seal_Ch1_Cast", "Seal_Ch1_Scene", "Awaken_Ch1", "Kill_Ch1_Scene", "Kill_Ch1_Scene_R", "NM_Ch1_Boss", "NM_Ch1_Trait", "Unbound_Ch1", "Unbound_Roar_Ch1",
        "Seal_Ch2_Cast", "Seal_Ch2_Scene", "Awaken_Ch2", "Kill_Ch2_Scene", "Kill_Ch2_Scene_R", "NM_Ch2_Boss", "NM_Ch2_Trait", "Unbound_Ch2", "Unbound_Roar_Ch2",
        "Seal_Ch3_Cast", "Seal_Ch3_Scene", "Awaken_Ch3", "Kill_Ch3_Scene", "Kill_Ch3_Scene_R", "NM_Ch3_Boss", "NM_Ch3_Trait", "Unbound_Ch3", "Unbound_Roar_Ch3",
    };
    private static readonly string[] LichKeys = { "Lich_ReverseSeal", "Lich_MerlinVoice", "Lich_FinalSpell" };

    [MenuItem("RelicFairy/Debug/10-10 P0 대사·텍스트 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[TextP0] 플레이 모드(테스트 허브)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("P0 대사 · 텍스트\n");
        try
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher == null) { sb.AppendLine("테스트 허브가 아니다"); return; }
            typeof(TestHubLauncher).GetField("_chapter", Inst)?.SetValue(launcher, 1);
            typeof(TestHubLauncher).GetField("_bossApproach", Inst)?.SetValue(launcher, false);
            if (!launcher.TryLaunch()) { sb.AppendLine("런 시작 실패"); return; }
            if (!await WaitAsync(() => GameRunBootstrapper.Instance?.Run?.Player != null, 60f)) { sb.AppendLine("런 대기 초과"); return; }
            await SkipAsync(10);

            // ① 대사 키
            var dlg = Managers.DialogueData;
            if (dlg == null) { sb.AppendLine("DialogueData 없음"); return; }
            if (!dlg.IsInitialized) await dlg.InitializeAsync();
            int found = SceneKeys.Count(k => (dlg.GetLines(k)?.Length ?? 0) > 0);
            sb.AppendLine($"보스 장면 대사 키 {found}/{SceneKeys.Length}");
            foreach (var k in SceneKeys.Where(k => (dlg.GetLines(k)?.Length ?? 0) == 0)) sb.AppendLine($"  없음: {k}");
            foreach (var k in LichKeys)
            {
                var lines = dlg.GetLines(k);
                sb.AppendLine($"  {k}: " + (lines == null ? "없음" : string.Join(" / ", lines.Select(l => $"{l.speaker}「{l.text}」"))));
            }
            sb.AppendLine($"  Intro_LichCorrupt[1] = {dlg.GetLines("Intro_LichCorrupt")?.ElementAtOrDefault(1)?.text}");
            sb.AppendLine($"  Mission_Seal[2] = {dlg.GetLines("Mission_Seal")?.ElementAtOrDefault(2)?.text}");
            sb.AppendLine($"CHECK 보스 장면 대사 키 전부: {found == SceneKeys.Length}");
            sb.AppendLine($"CHECK 리치 외침 키 전부: {LichKeys.All(k => (dlg.GetLines(k)?.Length ?? 0) > 0)}");

            // ② 부활 화면 — 게임 메서드를 띄운 채 문구를 읽고 찍은 뒤 취소로 닫는다
            var boot = GameRunBootstrapper.Instance;
            var mi = typeof(GameRunBootstrapper).GetMethod("ShowMerlinRevivalAsync", Inst);
            using var cts = new CancellationTokenSource();
            object task = mi?.Invoke(boot, new object[] { cts.Token });
            await UniTask.Delay(3000, ignoreTimeScale: true);
            var shot = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
            if (shot != null) await (UniTask)shot.Invoke(null, new object[] { "P0_Revival", 0 });
            var go = GameObject.Find("@MerlinRevival");
            var texts = go != null ? go.GetComponentsInChildren<TMP_Text>(true).Select(t => t.text).ToArray() : new string[0];
            bool named = StoryProgress.IsMerlinNamed;
            foreach (var t in texts) sb.AppendLine($"  부활 화면: {t}");
            bool archaicGone = texts.Length > 0 && texts.All(t => !t.Contains("그대") && !t.Contains("..."));
            bool labelOk = texts.Any(t => t.StartsWith(named ? "멀린 —" : "??? —"));
            sb.AppendLine($"  멀린 이름 공개: {named}");
            sb.AppendLine($"CHECK 부활 화면 고어체 없음: {archaicGone}");
            sb.AppendLine($"CHECK 부활 화면 이름표가 공개 규칙대로: {labelOk}");
            cts.Cancel();
            try { if (task is UniTask ut) await ut; } catch (System.OperationCanceledException) { }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/text_p0_probe.txt", sb.ToString());
            Debug.Log("[TextP0] 끝\n" + sb);
        }
    }

    private static async UniTask<bool> WaitAsync(System.Func<bool> cond, float seconds)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            try { if (cond()) return true; } catch { }
            await UniTask.Delay(500, ignoreTimeScale: true);
        }
        return false;
    }

    private static async UniTask SkipAsync(int times)
    {
        for (int i = 0; i < times; i++)
        {
            TestHubDebugMenu.AdvanceDialogue();
            await UniTask.Delay(300, ignoreTimeScale: true);
        }
    }
}
#endif
