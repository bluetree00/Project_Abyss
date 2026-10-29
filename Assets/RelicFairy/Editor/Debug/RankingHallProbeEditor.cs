#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-29 설계 §5 랭킹방 확인 — 거점(Build/BaseCamp)에서 플레이. 대사를 넘긴 뒤 <see cref="RankingHall.VisitAsync"/>(해방기 · 악몽 한 번씩):
/// 도착(비석 창) → 창 닫고 홀 한 장 → 비석 앞 [F] 안내 → 돌아가는 문 → 원래 자리. 사진 Temp/ranking_hall/ · 로그 「[RankingHallProbe] 끝」.
/// </summary>
public static class RankingHallProbeEditor
{
    [MenuItem("RelicFairy/Debug/10-29 §5 랭킹방 들르기 확인 (거점, Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[RankingHallProbe] 거점에서 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        const string Dir = "Temp/ranking_hall";
        Directory.CreateDirectory(Dir);
        var sb = new StringBuilder("랭킹방 들르기\n");
        try
        {
            await UniTask.Delay(3000, DelayType.Realtime);
            // 거점 플레이어 스폰 · 소환 연출이 끝나기 전에 시작하면 그 플레이어가 바뀐다(10-29 첫 판) — 진짜 플레이어를 기다린다
            for (float until = Time.realtimeSinceStartup + 60f; Time.realtimeSinceStartup < until && BaseCampBootstrapper.Instance?.Player == null;)
                await UniTask.Delay(300, DelayType.Realtime);
            await UniTask.Delay(4000, DelayType.Realtime);
            float calm = -1f;
            for (float until = Time.realtimeSinceStartup + 60f; Time.realtimeSinceStartup < until;)
            {
                bool talking = UnityEngine.Object.FindFirstObjectByType<UI_DialoguePopup>() != null;
                if (talking) { TestHubDebugMenu.AdvanceDialogue(); calm = -1f; }
                else if (Time.timeScale > 0f) { if (calm < 0f) calm = Time.realtimeSinceStartup; else if (Time.realtimeSinceStartup - calm > 2f) break; }
                await UniTask.Delay(300, DelayType.Realtime);
            }
            var player = BaseCampBootstrapper.Instance?.Player ?? UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            if (player == null) { sb.AppendLine("플레이어 없음"); return; }
            Vector3 home = player.transform.position;

            foreach (var era in new[] { StoryEra.Liberated, StoryEra.NightmareMode })
            {
                string tag = era == StoryEra.NightmareMode ? "nm" : "lib";
                var visit = RankingHall.VisitAsync(era, player, player.GetCancellationTokenOnDestroy());
                await UniTask.Delay(2600, DelayType.Realtime);
                sb.AppendLine($"[{tag}] 도착 — 들름 {RankingHall.IsVisiting} · 비석 창 {RankingBoardOverlay.IsOpen} · 플레이어 {player.transform.position}");
                ScreenCapture.CaptureScreenshot($"{Dir}/{tag}_1_arrival.png"); await UniTask.Delay(500, DelayType.Realtime);
                Click("@RankingBoard", "Veil"); await UniTask.Delay(1200, DelayType.Realtime);
                sb.AppendLine($"[{tag}] 창 닫음 — 창 {RankingBoardOverlay.IsOpen}");
                ScreenCapture.CaptureScreenshot($"{Dir}/{tag}_2_hall.png"); await UniTask.Delay(400, DelayType.Realtime);

                var hall = UnityEngine.Object.FindFirstObjectByType<RankingHall>();
                var stele = hall != null ? hall.transform.Find("Stele") : null;
                if (stele != null)
                {
                    Place(player, stele.position - hall.transform.forward * 2f);   // 홀은 카메라 방위로 돈다 — 시작 자리 쪽(홀 뒤)에서 비석 앞으로
                    await UniTask.Delay(1200, DelayType.Realtime);
                    var prompt = stele.Find("InteractPrompt");
                    sb.AppendLine($"[{tag}] 비석 앞 — [F] 안내 {(prompt != null && prompt.gameObject.activeSelf)}");
                    ScreenCapture.CaptureScreenshot($"{Dir}/{tag}_3_stele.png"); await UniTask.Delay(400, DelayType.Realtime);
                }
                var door = hall != null ? hall.transform.Find("ReturnDoor/Trigger") : null;
                if (door != null)
                {
                    Place(player, door.position);
                    await UniTask.Delay(2500, DelayType.Realtime);
                }
                await visit;
                sb.AppendLine($"[{tag}] 돌아옴 — 들름 {RankingHall.IsVisiting} · 원래 자리까지 {Vector3.Distance(player.transform.position, home):0.0} m · 홀 남음 {UnityEngine.Object.FindFirstObjectByType<RankingHall>() != null}");
                ScreenCapture.CaptureScreenshot($"{Dir}/{tag}_4_back.png"); await UniTask.Delay(800, DelayType.Realtime);
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText($"{Dir}/view.txt", sb.ToString(), Encoding.UTF8);
            Debug.Log($"[RankingHallProbe] 끝\n{sb}");
        }
    }

    private static void Click(string root, string name)
    {
        var r = GameObject.Find(root);
        if (r == null) { Debug.LogWarning($"[RankingHallProbe] {root} 없음"); return; }
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
