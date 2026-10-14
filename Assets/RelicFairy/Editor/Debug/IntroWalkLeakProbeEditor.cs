#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-10 — 인트로 통로 대사(Intro_Walk1~3)가 본편 Ch3 보스 통로에서 나오는지 잰다. 사용자 「챕터 3 대기방과 인트로가 겹친다」.
/// 트리거 3개가 인트로 씬이 아니라 공용 프리팹 Road_Boss_Ch3(본편 Ch3 보스 앞 통로)에 박혀 있다.
/// 테스트 허브 → Ch3 보스 대기방 직행 → 인트로 트리거마다 플레이어를 옮겨 대사 창이 뜨는지 센다.
/// 결과: Temp/intro_walk_leak_probe.txt · 로그 「[IntroWalkLeak] 끝」. 실측 뒤 플레이를 멈출 것.
/// </summary>
public static class IntroWalkLeakProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-10 Ch3 보스 통로 인트로 대사 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[IntroWalkLeak] 플레이 모드(테스트 허브)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("Ch3 보스 통로 · 인트로 대사\n");
        try
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher == null) { sb.AppendLine("테스트 허브가 아니다"); return; }
            typeof(TestHubLauncher).GetField("_chapter", Inst)?.SetValue(launcher, 3);
            typeof(TestHubLauncher).GetField("_bossApproach", Inst)?.SetValue(launcher, true);
            if (!launcher.TryLaunch()) { sb.AppendLine("런 시작 실패"); return; }
            if (!await WaitAsync(() => GameRunBootstrapper.Instance?.Run?.Player != null, 60f)) { sb.AppendLine("런 대기 초과"); return; }
            await SkipAsync(10);
            await UniTask.Delay(1500, ignoreTimeScale: true);

            var player   = GameRunBootstrapper.Instance.Run.Player;
            var idField  = typeof(DialogueZoneTrigger).GetField("csvSequenceId", Inst);
            var triggers = Object.FindObjectsByType<DialogueZoneTrigger>(FindObjectsSortMode.None)
                                 .Where(t => (idField?.GetValue(t) as string ?? "").StartsWith("Intro_"))
                                 .OrderBy(t => idField?.GetValue(t) as string).ToList();   // Walk1 → 2 → 3(보스 입구 쪽이 마지막)
            sb.AppendLine($"씬 {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} · 인트로 대사 트리거 {triggers.Count}개");

            int shown = 0;
            foreach (var t in triggers)
            {
                if (t == null) { sb.AppendLine("  (트리거가 사라졌다 — 건너뜀)"); continue; }
                var col = t.GetComponent<Collider>();
                if (col == null || player == null) continue;
                Teleport(player, col.bounds.center);
                await UniTask.Delay(800, ignoreTimeScale: true);
                var popup = Object.FindFirstObjectByType<UI_DialoguePopup>();
                bool open = popup != null && popup.gameObject.activeInHierarchy;
                if (open) shown++;
                sb.AppendLine($"  {idField?.GetValue(t)} · {t.transform.root.name} → 대사 창 {(open ? "뜸" : "안 뜸")}");
                await SkipAsync(4);
            }
            sb.AppendLine($"CHECK 본편 Ch3에서 인트로 대사가 안 나옴: {(triggers.Count > 0 && shown == 0)} (뜸 {shown}/{triggers.Count})");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/intro_walk_leak_probe.txt", sb.ToString());
            Debug.Log("[IntroWalkLeak] 끝\n" + sb);
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

    private static void Teleport(PlayerController p, Vector3 pos)
    {
        p.transform.position = pos;
        if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
    }
}
#endif
