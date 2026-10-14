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
            // 10-10 — 계단 등장부터 회랑 도착까지 프레임 끊김(100 ms 넘는 프레임) · 회랑 짓기 시각
            float stairAt = Time.realtimeSinceStartup;
            s_hitches.Clear();
            s_shotAt.Clear();
            s_corridorBuiltAt = -1f;
            s_sampling = true;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            SampleFramesAsync().Forget();
            // 10-10 돌계단 — 카메라가 닿고(2.4초) 한 단씩 솟는 중 · 다 지어진 뒤(아치 · 포탈)
            await UniTask.Delay(3000, ignoreTimeScale: true);
            await Shot("Stair_0_Rising");
            await UniTask.Delay(3200, ignoreTimeScale: true);
            await Shot("Stair_1_Arena");
            var entry = stair.transform.Find("@StairEntry");
            int steps = 0, risen = 0;
            bool landingUp = false;
            if (entry != null)
                foreach (Transform c in entry)
                {
                    bool up = Mathf.Abs(c.localPosition.y) < 0.01f;
                    if (c.name.StartsWith("Step_")) { steps++; if (up) risen++; }
                    else if (c.name == "Landing") landingUp = up;
                }
            bool arch = false;
            foreach (Transform c in stair.transform) if (c.name.Contains("StoneArch")) arch = true;
            var portal = typeof(ChapterStairway).GetField("_doorFx", Inst)?.GetValue(stair) as GameObject;
            sb.AppendLine($"{Ts()} CHECK 돌계단 단 {steps}/8 · 제자리 {risen} · 층계참 {landingUp} · 아치 {arch} · 포탈 {portal != null} · 꼭대기 문 {stair.transform.Find("TopTrigger") != null}");
            // 디딤판이 층계참보다 카메라 쪽에 있어야 보인다(10-10 — 층계참 뒤에 가려 「돌벽 위 아치」로만 보였다)
            var s0 = entry != null ? entry.Find("Step_0") : null;
            var ld = entry != null ? entry.Find("Landing") : null;
            var camMain = Camera.main;
            if (s0 != null && ld != null && camMain != null)
            {
                var fwd = Vector3.ProjectOnPlane(camMain.transform.forward, Vector3.up).normalized;
                float near = Vector3.Dot(s0.position - ld.position, fwd);
                sb.AppendLine($"{Ts()} CHECK 계단이 카메라 쪽(보임): {near < 0f} (디딤판 − 층계참 · 카메라 정면 성분 {near:0.0} m)");
            }

            // 층계참 위에 서 본다 — 2 m 위에 머무르는가(콜라이더)
            var run = GameRunBootstrapper.Instance.Run;
            float arenaY = run.Player.transform.position.y;
            var landing = entry != null ? entry.Find("Landing") : null;
            if (landing != null)
            {
                Teleport(run.Player, landing.position + Vector3.up * 2.3f);
                await UniTask.Delay(900, ignoreTimeScale: true);
                float dy = run.Player.transform.position.y - arenaY;
                sb.AppendLine($"{Ts()} CHECK 층계참 위 높이 {dy:0.00} m(2 m 근처면 통과)");
                await Shot("Stair_1b_OnLanding");
            }

            // 계단 꼭대기(포탈)로
            var top = stair.transform.Find("TopTrigger");
            if (top != null) Teleport(run.Player, top.position + Vector3.down * 0.8f);
            bool inCorridor = await WaitAsync(() => run.Player != null && run.Player.transform.position.y > arenaY + 100f, 10f, 200);
            sb.AppendLine($"{Ts()} 회랑 {(inCorridor ? "도착" : "못 감")} · 높이 {run.Player.transform.position.y:0}");
            s_sampling = false;
            Application.logMessageReceived -= OnLog;
            float worst = 0f, worstAt = 0f;
            foreach (var (at, ms) in s_hitches) if (ms > worst) { worst = ms; worstAt = at - stairAt; }
            sb.AppendLine($"{Ts()} CHECK 프레임 끊김(계단 등장 → 회랑): 100 ms 넘는 프레임 {s_hitches.Count}개 · 가장 긴 {worst:0} ms(+{worstAt:0.0}초)" +
                          $" · 회랑 짓기 +{(s_corridorBuiltAt > 0f ? s_corridorBuiltAt - stairAt : -1f):0.0}초");
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
            // 10-10 — 도착 화면: 덮개가 걷혔는데 플레이어가 없는 프레임(빈 대기방 노출) · 걷힌 뒤 다시 덮이는 횟수(튐)
            var arrival = WatchArrivalAsync(45f);
            bool arrived = await WaitAsync(() => GameRunBootstrapper.Instance != null && GameRunBootstrapper.Instance.Run?.CurrentChapter == ChapterId.Chapter2
                                                  && GameRunBootstrapper.Instance.Run.Player != null, 40f, 200);
            sb.AppendLine($"{Ts()} 다음 챕터 {(arrived ? "도착" : "대기 초과")}(문 +{Time.realtimeSinceStartup - doorAt:0.0}초)");
            var (bare, recover) = await arrival;
            sb.AppendLine($"{Ts()} CHECK 도착 화면 — 빈 대기방 노출 {bare}프레임 · 다시 덮임 {recover}회(둘 다 0이면 통과)");
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

    // ── 10-10 프레임 끊김 · 도착 화면 ──
    private static readonly System.Collections.Generic.List<(float at, float ms)> s_hitches = new();
    private static readonly System.Collections.Generic.List<float> s_shotAt = new();
    private static float s_corridorBuiltAt = -1f;
    private static bool  s_sampling;

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (msg.StartsWith("[ChapterStairway] 계단 · 회랑 준비")) s_corridorBuiltAt = Time.realtimeSinceStartup;
    }

    /// <summary>100 ms 넘는 프레임을 적는다 — 촬영(ScreenCapture) 0.3초 안은 사진 탓이라 뺀다.</summary>
    private static async UniTaskVoid SampleFramesAsync()
    {
        while (s_sampling)
        {
            await UniTask.Yield(PlayerLoopTiming.Update);
            float dt = Time.unscaledDeltaTime, now = Time.realtimeSinceStartup;
            if (dt <= 0.1f) continue;
            bool shot = false;
            foreach (var s in s_shotAt) if (Mathf.Abs(now - s) < 0.3f) shot = true;
            if (!shot) s_hitches.Add((now, dt * 1000f));
        }
    }

    /// <summary>
    /// 다음 챕터 씬으로 바뀐 뒤 — 덮개가 걷혔는데 플레이어가 없는 프레임 · 걷힌 뒤 다시 덮인 횟수.
    /// 걷힘 = 덮개 판이 꺼졌거나 알파가 낮거나 판이 화면 가운데를 벗어남(밀어내기 와이프).
    /// </summary>
    private static async UniTask<(int bare, int recover)> WatchArrivalAsync(float seconds)
    {
        const BindingFlags SAll = BindingFlags.Static | BindingFlags.NonPublic;
        var cgF  = typeof(ScreenFade).GetField("_cg", SAll);
        var imgF = typeof(ScreenFade).GetField("_img", SAll);
        string from = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        float t0 = Time.realtimeSinceStartup, openSince = -1f;
        int bare = 0, recover = 0;
        bool switched = false, wasOpen = false;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
            if (!switched)
            {
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == from) continue;
                switched = true;
            }
            var cg  = cgF?.GetValue(null) as CanvasGroup;
            var img = imgF?.GetValue(null) as UnityEngine.UI.Image;
            bool covered = cg != null && img != null && img.enabled && cg.alpha > 0.5f
                           && img.rectTransform.anchorMin.x < 0.5f && img.rectTransform.anchorMax.x > 0.5f
                           && img.rectTransform.anchorMin.y < 0.5f && img.rectTransform.anchorMax.y > 0.5f;
            bool hasPlayer = GameRunBootstrapper.Instance != null && GameRunBootstrapper.Instance.Run?.Player != null;
            if (!covered && !hasPlayer) bare++;
            if (covered && wasOpen) recover++;
            if (!covered) { if (!wasOpen) openSince = Time.realtimeSinceStartup; wasOpen = true; }
            else wasOpen = false;
            if (hasPlayer && !covered && openSince > 0f && Time.realtimeSinceStartup - openSince > 1.5f) break;
        }
        return (bare, recover);
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
        s_shotAt.Add(Time.realtimeSinceStartup);
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", SNonPub);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
