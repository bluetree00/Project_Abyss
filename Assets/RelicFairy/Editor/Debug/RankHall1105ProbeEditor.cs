#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cinemachine;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 11-05 #21 사용자 「랭킹방 카메라 고정 버그 · 클리어하면 바로 랭킹방에 갔다가 베이스캠프로」 실측(플레이 중 · 거점 씬).
/// 「클리어 귀환」(RankingFlow 예약 = 해방기)을 걸고 거점을 다시 부른다 → 거점이 밝아지기 전에 랭킹방이 열리나 · 둘러보기 뒤 Brain 켜짐 ·
/// 플레이어를 옮기면 카메라가 따라오나 · 비석 닫기 → 돌아가는 문 → 거점 소환 · 밝히기 · Brain 켜짐.
/// 결과 Temp/rankhall1105.txt · Temp/ui_shots/RH1105_*.png · 로그 「[RH1105] 끝」.
/// </summary>
public static class RankHall1105ProbeEditor
{
    private const string Tag = "[RH1105]";
    private const string ShotDir = "Temp/ui_shots";

    [MenuItem("RelicFairy/Debug/11-05 랭킹방 귀환 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-05 랭킹방 귀환\n");
        Directory.CreateDirectory(ShotDir);
        try
        {
            typeof(RankingFlow).GetField("s_pendingRoom", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, (StoryEra?)StoryEra.Liberated);
            float t0 = Time.realtimeSinceStartup;
            // 같은 거점을 다시 부르면 흐름이 무시한다(GameFlow — 같은 상태) → 테스트 허브에서 거점으로 간다(실제 귀환처럼 거점 시작이 처음부터 돈다)
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher == null) { sb.AppendLine("FAIL 테스트 허브에서 시작한다"); return; }
            if (!await WaitAsync(launcher.TryGoToBaseCamp, 30f)) { sb.AppendLine("FAIL 거점으로 못 갔다(초기화 중?)"); return; }
            sb.AppendLine("클리어 귀환 예약(해방기) → 테스트 허브에서 거점으로");

            if (!await WaitAsync(() => RankingHall.IsVisiting, 90f)) { sb.AppendLine("FAIL 랭킹방이 열리지 않았다(90초)"); return; }
            var boot = Object.FindFirstObjectByType<BaseCampBootstrapper>();
            sb.AppendLine($"PASS 랭킹방 열림 {Time.realtimeSinceStartup - t0:0.0}초 — 거점 준비 {(boot != null && boot.IsReady)}(false여야 = 거점보다 먼저)");
            await UniTask.Delay(1500, DelayType.Realtime);
            await Shot("RH1105_1_vista");

            // 비석 창 — 둘러보기(약 5초) 뒤 뜬다 → 사진 → 돌아가기
            if (!await WaitAsync(() => RankingBoardOverlay.IsOpen, 20f)) sb.AppendLine("CHECK 비석 창이 안 떴다(20초)");
            await UniTask.Delay(1500, DelayType.Realtime);
            await Shot("RH1105_2_board");
            var brain = Camera.main != null ? Camera.main.GetComponent<CinemachineBrain>() : null;
            sb.AppendLine($"{(brain != null && brain.enabled ? "PASS" : "FAIL")} 둘러보기 뒤 Brain 켜짐 {brain?.enabled}");
            var board = GameObject.Find("@RankingBoard");
            if (board != null) foreach (var b in board.GetComponentsInChildren<Button>()) if (b.name == "Btn_돌아가기") { b.onClick.Invoke(); break; }
            await WaitAsync(() => !RankingBoardOverlay.IsOpen, 5f);
            await UniTask.Delay(600, DelayType.Realtime);

            // 카메라가 플레이어를 따라오나 — 3 m 옆으로 옮겨 본다
            var p = Object.FindFirstObjectByType<PlayerController>();
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (p != null && cam != null)
            {
                Vector3 c0 = cam.position;
                Vector3 to = p.transform.position + p.transform.right * 3f;
                p.transform.position = to;
                if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = to; rb.linearVelocity = Vector3.zero; }
                await UniTask.Delay(1200, DelayType.Realtime);
                float moved = Vector3.Distance(c0, cam.position);
                sb.AppendLine($"{(moved > 1.5f ? "PASS" : "FAIL")} 카메라가 따라옴 — 플레이어 3 m · 카메라 {moved:0.0} m");
                await Shot("RH1105_3_follow");
            }

            // 돌아가는 문
            var hall = Object.FindFirstObjectByType<RankingHall>();
            hall?.RequestReturn();
            if (!await WaitAsync(() => !RankingHall.IsVisiting, 20f)) { sb.AppendLine("FAIL 랭킹방에서 안 돌아왔다(20초)"); return; }
            if (!await WaitAsync(() => boot != null && boot.IsReady, 30f)) sb.AppendLine("CHECK 거점 준비가 30초 안에 안 끝났다");
            await UniTask.Delay(800, DelayType.Realtime);
            await Shot("RH1105_4_basecamp");
            brain = Camera.main != null ? Camera.main.GetComponent<CinemachineBrain>() : null;
            sb.AppendLine($"{(brain != null && brain.enabled ? "PASS" : "FAIL")} 거점 귀환 뒤 Brain 켜짐 {brain?.enabled} · 거점 준비 {boot?.IsReady} · {Time.realtimeSinceStartup - t0:0.0}초");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/rankhall1105.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    private static async UniTask<bool> WaitAsync(System.Func<bool> cond, float seconds)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            try { if (cond()) return true; }
            catch (System.Exception) { }
            await UniTask.Delay(100, DelayType.Realtime);
        }
        return false;
    }

    private static async UniTask Shot(string name)
    {
        ScreenCapture.CaptureScreenshot($"{ShotDir}/{name}.png");
        await UniTask.Yield();
        await UniTask.Yield();
    }
}
#endif
