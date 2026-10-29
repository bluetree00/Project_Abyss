#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-29 B 랭킹방 = 높은 탑 꼭대기 실측 — 거점(Build/BaseCamp)에서 플레이.
/// 해방기로 들러 ① 도착 둘러보기(시작 · 가운데 · 끝) ② 비석 창 ③ 꼭대기 게임 화면 ④ 난간 가장자리에서 아래 풍경 ⑤ 프레임 시간(홀 안 2초 평균) ⑥ 돌아옴.
/// 결과 Temp/tower_hall/*.png · view.txt · 로그 「[TowerHall] 끝」.
/// </summary>
public static class Fix1029TowerHallProbeEditor
{
    private const string Dir = "Temp/tower_hall";

    [MenuItem("RelicFairy/Debug/10-29 B 랭킹방 탑 실측 (거점, Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[TowerHall] 거점에서 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        Directory.CreateDirectory(Dir);
        var sb = new StringBuilder("랭킹방 탑\n");
        try
        {
            await Wait(3f);
            for (float until = Time.realtimeSinceStartup + 60f; Time.realtimeSinceStartup < until && BaseCampBootstrapper.Instance?.Player == null;)
                await Wait(0.3f);
            await Wait(4f);
            float calm = -1f;
            for (float until = Time.realtimeSinceStartup + 60f; Time.realtimeSinceStartup < until;)
            {
                bool talking = UnityEngine.Object.FindFirstObjectByType<UI_DialoguePopup>() != null;
                if (talking) { TestHubDebugMenu.AdvanceDialogue(); calm = -1f; }
                else if (Time.timeScale > 0f) { if (calm < 0f) calm = Time.realtimeSinceStartup; else if (Time.realtimeSinceStartup - calm > 2f) break; }
                await Wait(0.3f);
            }
            var player = BaseCampBootstrapper.Instance?.Player ?? UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            if (player == null) { sb.AppendLine("플레이어 없음"); return; }
            Vector3 home = player.transform.position;
            float baseMs = await FrameMsAsync(2f);
            sb.AppendLine($"거점 프레임 {baseMs:0.0} ms");

            var visit = RankingHall.VisitAsync(StoryEra.Liberated, player, player.GetCancellationTokenOnDestroy());
            await Wait(1.2f); Shot("1_vista_start");
            await Wait(1.9f); Shot("2_vista_mid");
            await Wait(1.9f); Shot("3_vista_end");
            for (float until = Time.realtimeSinceStartup + 10f; Time.realtimeSinceStartup < until && !RankingBoardOverlay.IsOpen;) await Wait(0.2f);
            sb.AppendLine($"둘러보기 뒤 비석 창 {RankingBoardOverlay.IsOpen} · 들름 {RankingHall.IsVisiting}");
            await Wait(0.8f); Shot("4_board");
            Click("@RankingBoard", "Veil"); await Wait(1.2f);
            Shot("5_hall_gameplay");
            float hallMs = await FrameMsAsync(2f);
            var hall = UnityEngine.Object.FindFirstObjectByType<RankingHall>();
            int rs = hall != null ? hall.GetComponentsInChildren<Renderer>(true).Length : -1;
            sb.AppendLine($"홀 프레임 {hallMs:0.0} ms(거점 {baseMs:0.0}) · 렌더러 {rs}");

            if (hall != null)
            {
                // 난간 앞 — 석상 사이(앞쪽)에서 바깥을 본다
                Place(player, hall.transform.position + hall.transform.forward * 10f);
                await Wait(1.5f); Shot("6_rail_front");
                Place(player, hall.transform.position + hall.transform.right * 10f);
                await Wait(1.5f); Shot("7_rail_side");
                var door = hall.transform.Find("ReturnDoor/Trigger");
                if (door != null) { Place(player, door.position); await Wait(2.5f); }
            }
            await visit;
            sb.AppendLine($"돌아옴 — 들름 {RankingHall.IsVisiting} · 원래 자리까지 {Vector3.Distance(player.transform.position, home):0.0} m");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText($"{Dir}/view.txt", sb.ToString(), Encoding.UTF8);
            Debug.Log($"[TowerHall] 끝\n{sb}");
        }
    }

    private static async UniTask<float> FrameMsAsync(float seconds)
    {
        int frames = 0; float start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < seconds) { frames++; await UniTask.Yield(PlayerLoopTiming.Update); }
        return frames > 0 ? (Time.realtimeSinceStartup - start) * 1000f / frames : -1f;
    }

    private static void Shot(string name) => ScreenCapture.CaptureScreenshot($"{Dir}/{name}.png");
    private static UniTask Wait(float s) => UniTask.Delay(TimeSpan.FromSeconds(s), DelayType.Realtime);

    private static void Click(string root, string name)
    {
        var r = GameObject.Find(root);
        if (r == null) return;
        foreach (var b in r.GetComponentsInChildren<Button>(true))
            if (b.name == name) { b.onClick.Invoke(); return; }
    }

    private static void Place(PlayerController p, Vector3 at)
    {
        if (Physics.Raycast(at + Vector3.up * 6f, Vector3.down, out var hit, 20f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore)) at.y = hit.point.y + 0.05f;
        p.transform.position = at;
        if (p.Rigid != null) { p.Rigid.position = at; p.Rigid.linearVelocity = Vector3.zero; }
    }
}
#endif
