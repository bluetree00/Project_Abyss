#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-02 S5 「층계 회랑」 실측 — 테스트 허브에서 Ch1 보스 대기방 직행 → 보스방 → 강제 처치 → 기억 카드 고름 →
/// 계단 등장 → 계단 꼭대기(봉인 정리 · 잉크 와이프 · 회랑) → 회랑 층계참 · 끝 문 → Ch2 대기방까지. 순간마다 시각 · 사진.
/// 걷기는 순간이동으로 대신한다(계단 오르기 자체는 플레이어 StepClimb 0.35 m — 한 칸 0.25 m).
/// 결과: Temp/stairway_probe.txt · Temp/ui_shots/Stair_*.png · 로그 「[StairProbe] 끝」. 실측 뒤 플레이를 멈출 것.
/// </summary>
public static class StairwayProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags SNonPub = BindingFlags.Static | BindingFlags.NonPublic;
    private const string SkipDialogueMenu = "RelicFairy/Debug/서약 발동 실측/대사 전부 넘기기 (플레이 중)";

    [MenuItem("RelicFairy/Debug/10-02 층계 회랑 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[StairProbe] 플레이 모드(테스트 허브)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder($"층계 회랑 실측 · 시기 {StoryProgress.Era}\n");
        float T0 = Time.realtimeSinceStartup;
        string Ts() => $"{Time.realtimeSinceStartup - T0,6:0.0}초";
        try
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher == null) { sb.AppendLine("테스트 허브가 아니다"); return; }
            typeof(TestHubLauncher).GetField("_chapter", Inst)?.SetValue(launcher, 1);
            typeof(TestHubLauncher).GetField("_bossApproach", Inst)?.SetValue(launcher, true);
            if (!launcher.TryLaunch()) { sb.AppendLine("런 시작 실패"); return; }
            if (!await WaitAsync(() => GameRunBootstrapper.Instance?.Run?.Player != null, 60f)) { sb.AppendLine("런 대기 초과"); return; }
            await SkipAsync(10);

            // 보스 대기방 → 보스방 → 입구 트리거
            TestHubDebugMenu.EnterOpenExit();
            await UniTask.Delay(5000, ignoreTimeScale: true);
            await SkipAsync(4);
            TestHubDebugMenu.StepIntoBossEntrance();
            await UniTask.Delay(400, ignoreTimeScale: true);
            TestHubDebugMenu.StepPastBossEntrance();
            sb.AppendLine($"{Ts()} 보스방 입구 통과");

            var spawner = Object.FindFirstObjectByType<BossSpawner>();
            await WaitAsync(() => { EditorApplication.ExecuteMenuItem(SkipDialogueMenu); spawner ??= Object.FindFirstObjectByType<BossSpawner>(); return spawner != null && spawner.SpawnedBoss != null; }, 30f, 1000);
            var boss = spawner != null ? spawner.SpawnedBoss : null;
            if (boss == null) { sb.AppendLine("보스 없음"); return; }
            await WaitAsync(() => { EditorApplication.ExecuteMenuItem(SkipDialogueMenu); TestHubDebugMenu.ForceKillBoss(); return boss.IsDead; }, 40f, 2000);
            if (!boss.IsDead) { sb.AppendLine("보스가 안 쓰러짐"); return; }
            float killed = Time.realtimeSinceStartup;
            sb.AppendLine($"{Ts()} 보스 처치({boss.name})");

            // 기억 카드 고르기 → 계단
            await WaitAsync(() =>
            {
                var popup = Object.FindFirstObjectByType<UI_RelicPartDraftPopup>();
                if (popup == null || !popup.gameObject.activeInHierarchy) return false;
                typeof(UI_RelicPartDraftPopup).GetField("_selected", Inst)?.SetValue(popup, 0);
                typeof(UI_RelicPartDraftPopup).GetMethod("OnConfirmClicked", Inst)?.Invoke(popup, null);
                return true;
            }, 40f, 500);
            sb.AppendLine($"{Ts()} 카드 고름(처치 +{Time.realtimeSinceStartup - killed:0.0}초)");

            ChapterStairway stair = null;
            await WaitAsync(() => (stair = Object.FindFirstObjectByType<ChapterStairway>()) != null, 20f, 300);
            if (stair == null)
            {
                sb.AppendLine($"계단 없음 — 게이트: {(GameObject.Find("@ChapterGate") != null ? "있음(대체)" : "없음")}");
                return;
            }
            sb.AppendLine($"{Ts()} 계단 등장(처치 +{Time.realtimeSinceStartup - killed:0.0}초) · 자리 {stair.transform.position}");
            await UniTask.Delay(4500, ignoreTimeScale: true);   // 출구 카메라 연출 · 디졸브
            await Shot("Stair_1_Arena");

            // 계단 꼭대기로
            var run = GameRunBootstrapper.Instance.Run;
            var top = stair.transform.Find("TopTrigger");
            float arenaY = run.Player.transform.position.y;
            if (top != null) Teleport(run.Player, top.position + Vector3.down * 0.8f);
            bool inCorridor = await WaitAsync(() => run.Player != null && run.Player.transform.position.y > arenaY + 100f, 10f, 200);
            sb.AppendLine($"{Ts()} 회랑 {(inCorridor ? "도착" : "못 감")} · 높이 {run.Player.transform.position.y:0}");
            if (!inCorridor) return;
            await UniTask.Delay(1200, ignoreTimeScale: true);
            await Shot("Stair_2_CorridorStart");

            // 층계참(꺾이는 곳) · 끝 문 앞
            var corridor = GameObject.Find("@StairCorridor");
            if (corridor != null)
            {
                Teleport(run.Player, corridor.transform.TransformPoint(new Vector3(0f, 3.6f, 19f)));
                await UniTask.Delay(1200, ignoreTimeScale: true);
                await Shot("Stair_3_Landing");
                Teleport(run.Player, corridor.transform.TransformPoint(new Vector3(18f, 7.1f, 19f)));
                await UniTask.Delay(1200, ignoreTimeScale: true);
                await Shot("Stair_4_Door");
            }

            // 끝 문 → 다음 챕터
            var door = corridor != null ? corridor.transform.Find("DoorTrigger") : null;
            float doorAt = Time.realtimeSinceStartup;
            sb.AppendLine($"{Ts()} 끝 문 — 미리 로드 중 {ScenePreloader.IsPending}({ScenePreloader.PendingScene})");
            if (door != null) Teleport(run.Player, door.position + Vector3.down * 0.6f);
            bool arrived = await WaitAsync(() => GameRunBootstrapper.Instance != null && GameRunBootstrapper.Instance.Run?.CurrentChapter == ChapterId.Chapter2
                                                  && GameRunBootstrapper.Instance.Run.Player != null, 40f, 200);
            sb.AppendLine($"{Ts()} 다음 챕터 {(arrived ? "도착" : "대기 초과")}(문 +{Time.realtimeSinceStartup - doorAt:0.0}초)");
            await UniTask.Delay(2500, ignoreTimeScale: true);
            await Shot("Stair_5_NextWaitingRoom");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/stairway_probe.txt", sb.ToString());
            Debug.Log("[StairProbe] 끝");
        }
    }

    private static async UniTask<bool> WaitAsync(Func<bool> cond, float seconds, int stepMs = 500)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            try { if (cond()) return true; } catch { }
            await UniTask.Delay(stepMs, ignoreTimeScale: true);
        }
        return false;
    }

    private static async UniTask SkipAsync(int times)
    {
        for (int i = 0; i < times; i++)
        {
            TestHubDebugMenu.AdvanceDialogue();
            await UniTask.Delay(400, ignoreTimeScale: true);
        }
    }

    private static void Teleport(PlayerController p, Vector3 pos)
    {
        if (p == null) return;
        p.transform.position = pos;
        if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
    }

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", SNonPub);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
