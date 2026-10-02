#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-02 「되찾은 기억」 연출 실측 — 보스방(테스트 허브 보스 대기방 직행 → 출구 → 입구 1~3)에서 실행한다.
/// 보스를 강제로 쓰러뜨리고, 처치 → 끝 장면 → 기억의 빛 → 회상 한 줄 → 고르기 창 → 한 마디를 0.5초마다 찍으며
/// 순간마다의 상태(컷신 HUD · 자막 · 회상 화면 · 고르기 창)를 기록한다. 고르기 창은 첫 카드를 골라 닫는다.
/// 결과: Temp/relic_memory_probe/&lt;시각&gt;/report.txt · 화면 Temp/ui_shots/S1002M_*.png
/// </summary>
public static class RelicMemoryProbeEditor
{
    private const BindingFlags NonPub  = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags SNonPub = BindingFlags.Static | BindingFlags.NonPublic;
    private const string SkipDialogueMenu = "RelicFairy/Debug/서약 발동 실측/대사 전부 넘기기 (플레이 중)";

    [MenuItem("RelicFairy/Debug/10-02 되찾은 기억 연출 기록 (보스방에서)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            Debug.LogWarning("[RelicMemoryProbe] 런의 보스방에 들어간 뒤 실행해야 한다.");
            return;
        }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string dir = Path.Combine("Temp", "relic_memory_probe", DateTime.Now.ToString("HHmmss"));
        Directory.CreateDirectory(dir);
        var sb  = new StringBuilder("되찾은 기억 연출 실측\n");
        var run = GameRunBootstrapper.Instance.Run;
        Probe("HideTestHubGui");
        try
        {
            // ① 보스 — 나타날 때까지 기다리며 대사창을 넘기고, 쓰러질 때까지 강제 처치(페이지 보스는 한 번에 한 페이지)
            var spawner = UnityEngine.Object.FindFirstObjectByType<BossSpawner>();
            float t0 = Time.realtimeSinceStartup;
            while ((spawner == null || spawner.SpawnedBoss == null) && Time.realtimeSinceStartup - t0 < 30f)
            {
                EditorApplication.ExecuteMenuItem(SkipDialogueMenu);
                await UniTask.Delay(1000, ignoreTimeScale: true);
                spawner = spawner != null ? spawner : UnityEngine.Object.FindFirstObjectByType<BossSpawner>();
            }
            var boss = spawner != null ? spawner.SpawnedBoss : null;
            if (boss == null) { sb.AppendLine("보스 없음 — 보스방에서 실행할 것"); return; }
            sb.AppendLine($"보스 {boss.name} · 시기 {StoryProgress.Era}");

            t0 = Time.realtimeSinceStartup;
            while (!boss.IsDead && Time.realtimeSinceStartup - t0 < 40f)
            {
                EditorApplication.ExecuteMenuItem(SkipDialogueMenu);
                TestHubDebugMenu.ForceKillBoss();
                await UniTask.Delay(2000, ignoreTimeScale: true);
            }
            if (!boss.IsDead) { sb.AppendLine("보스가 40초 안에 안 쓰러짐"); return; }

            // ② 타임라인 — 쓰러진 순간부터 25초. 상태가 바뀔 때마다 한 줄 · 0.5초마다 한 장
            float dead = Time.realtimeSinceStartup;
            string last = null;
            int shot = 0;
            float nextShot = 0f;
            bool picked = false;
            while (Time.realtimeSinceStartup - dead < 25f)
            {
                float t = Time.realtimeSinceStartup - dead;
                var popup = UnityEngine.Object.FindFirstObjectByType<UI_RelicPartDraftPopup>();
                bool popupOn = popup != null && popup.gameObject.activeInHierarchy;
                string state = $"컷신 {(run.InCutscene ? "O" : "-")} · 자막 {(UI_BossBark.IsShowing ? "O" : "-")} · " +
                               $"회상 {(GameObject.Find("RelicMemoryLineCanvas") != null ? "O" : "-")} · 창 {(popupOn ? "O" : "-")} · HUD {run.CurrentHudMode}";
                if (state != last) { sb.AppendLine($"  {t,5:0.0}초  {state}"); last = state; }

                if (t >= nextShot)
                {
                    await Shot($"S1002M_{shot++:00}_{t * 1000f:00000}");
                    nextShot = t + 0.5f;
                }

                // 고르기 창 — 1.2초 보여 준 뒤 첫 카드로 닫는다(이어서 한 마디가 나와야 한다)
                if (popupOn && !picked && t - FirstSeen(ref _popupSeenAt, t) > 1.2f)
                {
                    typeof(UI_RelicPartDraftPopup).GetField("_selected", NonPub)?.SetValue(popup, 0);
                    typeof(UI_RelicPartDraftPopup).GetMethod("OnConfirmClicked", NonPub)?.Invoke(popup, null);
                    picked = true;
                    sb.AppendLine($"  {t,5:0.0}초  첫 카드 고름 → {popup.Result?.part_name}");
                }
                await UniTask.Yield();
            }
            _popupSeenAt = -1f;
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Probe("RestoreTestHubGui");
            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString());
            Debug.Log($"[RelicMemoryProbe] 끝 → {dir}/report.txt");
        }
    }

    private static float _popupSeenAt = -1f;

    private static float FirstSeen(ref float at, float now)
    {
        if (at < 0f) at = now;
        return at;
    }

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", SNonPub);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }

    private static void Probe(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, SNonPub)?.Invoke(null, null);
}
#endif
