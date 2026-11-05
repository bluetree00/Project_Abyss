#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-05 사용자 「시간도 제대로 파악할 수 없는 숫자 · 32:23.48 같이 분인지 초인지 한눈에」 실측(테스트 허브 런 · 플레이 중).
/// ① 표시 형식(한 · 영) ② 랭킹 시계: 보통 2초 · 슬로 0.35배 2초(예전엔 0.7초로 셌다) · 시간 정지 1.5초(안 셈) — 해방기로 잠깐 보고 되돌린다
/// ③ 끌 때 시계만 잇기(SaveRunClock) ④ 랭킹판(클리어 시간 정의 줄 · 보스전) · 결과창 「랭킹 기록」 · 기록 카드 사진(한 · 영).
/// 결과 Temp/rankclock1105.txt · Temp/ui_shots/RC1105_*.png · 로그 「[RC1105] 끝」.
/// </summary>
public static class RankClock1105ProbeEditor
{
    private const string Tag      = "[RC1105]";
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string ShotDir  = "Temp/ui_shots";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool s_armed;

    [MenuItem("RelicFairy/Debug/11-05 랭킹 시계 · 시간 표시 실측 (테스트 허브, 플레이 중)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning($"{Tag} 플레이 모드에서만 동작한다."); return; }
        if (Object.FindFirstObjectByType<TestHubLauncher>() == null) { Debug.LogWarning($"{Tag} 테스트 허브에서 시작한다."); return; }
        s_armed = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (!s_armed || !msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        s_armed = false;
        Application.logMessageReceived -= OnLog;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (p == null) { Debug.LogWarning($"{Tag} 런 플레이어가 없다"); return; }
            RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("11-05 랭킹 시계 · 시간 표시\n");
        Directory.CreateDirectory(ShotDir);
        var lang = Loc.Current;
        int prevOverride = EditorPrefs.GetInt(StoryProgress.DebugOverridePrefsKey, -1);
        var owner = new object();
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            foreach (var mo in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (mo != null && !mo.IsDead) mo.gameObject.SetActive(false);
            typeof(UILayoutRuntimeProbeEditor).GetMethod("HideTestHubGui", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);

            // ① 표시 형식
            foreach (var l in new[] { Loc.Language.Korean, Loc.Language.English })
            {
                Loc.SetLanguage(l);
                sb.AppendLine($"① {l}: 5297 → {RankingService.Format(5297)} · 194348 → {RankingService.Format(194348)} · 372345 → {RankingService.Format(372345)} · 0 → {RankingService.Format(0)}");
            }
            Loc.SetLanguage(lang);

            // ② 랭킹 시계 — 해방기로 잠깐 본다(봉인기면 클리어 시간을 세지 않는다)
            var run = GameRunBootstrapper.Instance?.Run;
            EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, 1);
            StoryProgress.RefreshDebugOverride();
            float waited = 0f;
            while (run != null && run.TryGetHudMode(out var mode) && (mode == HUDIds.Mode.Cutscene || mode == HUDIds.Mode.BossCutscene) && waited < 20f)
            { await Wait(0.25f, ct); waited += 0.25f; }
            await Wait(0.5f, ct);
            sb.AppendLine($"② 시기 {StoryProgress.Era} · 런 {run != null && run.IsRunning} · 컷신 대기 {waited:0.0}초");
            if (run != null && run.IsRunning)
            {
                await ClockStep(sb, run, "보통 4초", 4f, true, ct);
                TimeScaleArbiter.Acquire(owner, 0.35f, TimeScaleArbiter.Priority.SlowMotion);
                await ClockStep(sb, run, "슬로 0.35배 4초(예전 ≈1.40)", 4f, true, ct);
                TimeScaleArbiter.Release(owner);
                TimeScaleArbiter.Acquire(owner, 0f, TimeScaleArbiter.Priority.Pause);
                await ClockStep(sb, run, "시간 정지 2초", 2f, false, ct);
                TimeScaleArbiter.Release(owner);

                // ③ 끌 때 시계만 잇기
                var pm = RunProgressManager.Instance;
                var d0 = pm != null ? pm.LoadLocalRun(pm.ActiveSlotIndex) : null;
                if (d0 == null || !d0.hasActiveRun || d0.isInStartRoom) sb.AppendLine("③ 건너뜀 — 런 세이브 없음");
                else
                {
                    float before = RunStats.FromJson(d0.runStatsJson).ClearSeconds;
                    pm.SaveRunClock(run);
                    float after = RunStats.FromJson(pm.LoadLocalRun(pm.ActiveSlotIndex).runStatsJson).ClearSeconds;
                    float live = run.RunStats.ClearSeconds;
                    bool ok = Mathf.Abs(after - live) < 0.1f && after >= before;
                    sb.AppendLine($"{(ok ? "PASS" : "FAIL")} ③ 끌 때 시계 — 세이브 {before:0.00} → {after:0.00}초 · 지금 {live:0.00}초");
                }
            }
            EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, prevOverride);
            StoryProgress.RefreshDebugOverride();

            // ④ 화면
            var menu = Object.FindFirstObjectByType<UI_EscMenu>(FindObjectsInactive.Include);
            foreach (var l in new[] { Loc.Language.Korean, Loc.Language.English })
            {
                Loc.SetLanguage(l);
                await Wait(0.4f, ct);
                string tag = l == Loc.Language.Korean ? "ko" : "en";
                if (menu != null)
                {
                    menu.Open();
                    await Wait(0.5f, ct);
                    typeof(UI_EscMenu).GetMethod("OnRanking", Inst)?.Invoke(menu, null);
                    var page = typeof(UI_EscMenu).GetField("_rankingPage", Inst)?.GetValue(menu) as Component;
                    var show = page != null ? page.GetType().GetMethod("ShowTab", Inst) : null;
                    if (show != null)
                    {
                        show.Invoke(page, new object[] { StoryEra.Liberated, 0, 1 });
                        await Wait(2.6f, ct);
                        await Shot($"RC1105_{tag}_clear");
                        Dump(sb, $"④ {tag} 랭킹 클리어 시간", page.gameObject);
                        show.Invoke(page, new object[] { StoryEra.Liberated, 1, 2 });
                        await Wait(2.6f, ct);
                        await Shot($"RC1105_{tag}_boss2");
                        Dump(sb, $"④ {tag} 랭킹 보스전 2", page.gameObject);
                    }
                    menu.HandleEscape();
                    await Wait(0.3f, ct);
                    menu.Close();
                    await Wait(0.5f, ct);
                }

                var snap = new RunResultSnapshot
                {
                    Cleared = true, ChapterNumber = 4, ChapterName = "왕좌", RoomClears = 31, Era = StoryEra.Liberated,
                    PlaySeconds = 2780, RankCentis = 194348,
                };
                snap.Segments.Add(new RunResultSnapshot.Segment { Label = "Ch1 숲", Seconds = 640, Done = true, CombatSeconds = 316 });
                snap.Segments.Add(new RunResultSnapshot.Segment { Label = "Ch2 동굴", Seconds = 720, Done = true, CombatSeconds = 415 });
                snap.Segments.Add(new RunResultSnapshot.Segment { Label = "Ch3 성채", Seconds = 690, Done = true, CombatSeconds = 277 });
                snap.Segments.Add(new RunResultSnapshot.Segment { Label = "Ch4 왕좌", Seconds = 730, Done = true, CombatSeconds = 258 });
                await RunResultProbeEditor.ShowAndShoot(snap, $"RC1105_{tag}_result", sb, new[] { 3.2f });

                var boss  = new UI_RecordCard.Line(Loc.T("보스전"), 24405, true, 0, RecordBoard.TableOf(true, 2, StoryEra.Liberated));
                var rooms = new UI_RecordCard.Line(Loc.F("챕터 {0} 전투", 2), 41589, false, 40210, RecordBoard.TableOf(false, 2, StoryEra.Liberated));
                UI_RecordCard.Show(2, StoryEra.Liberated, boss, rooms, UniTask.CompletedTask);
                await Wait(4.6f, ct);
                await Shot($"RC1105_{tag}_card");
                Dump(sb, $"④ {tag} 기록 카드", GameObject.Find("@RecordCard"));
                var card = GameObject.Find("@RecordCard");
                if (card != null) Object.Destroy(card);
            }
        }
        catch (System.OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            TimeScaleArbiter.Release(owner);
            EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, prevOverride);
            StoryProgress.RefreshDebugOverride();
            Loc.SetLanguage(lang);
            File.WriteAllText("Temp/rankclock1105.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    /// <summary>
    /// 실제 시간 <paramref name="seconds"/> 동안 클리어 시계가 얼마나 늘었나 — 프레임 끝(시계가 더한 뒤)에서 재고, 같은 프레임들의 실제 길이
    /// (한 프레임 0.25초 상한)를 기대값으로 삼는다. 에디터가 뒤에 깔려 프레임이 느려도 경계 오차가 끼지 않게(11-05 첫 실측 2초 → +2.25).
    /// </summary>
    private static async UniTask ClockStep(StringBuilder sb, GameRunSession run, string what, float seconds, bool counts, CancellationToken ct)
    {
        await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
        float c0 = run.RunStats.ClearSeconds, r0 = Time.realtimeSinceStartup;
        float expect = 0f, maxDt = 0f;
        int frames = 0;
        while (Time.realtimeSinceStartup - r0 < seconds)
        {
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
            frames++;
            maxDt = Mathf.Max(maxDt, Time.unscaledDeltaTime);
            if (counts) expect += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
        }
        float dc = run.RunStats.ClearSeconds - c0, dr = Time.realtimeSinceStartup - r0;
        bool ok = Mathf.Abs(dc - expect) <= 0.03f;
        sb.AppendLine($"{(ok ? "PASS" : "FAIL")} ② {what} — 실제 {dr:0.00}초 · 시계 +{dc:0.00}초 · 기대 {expect:0.00}초 · 프레임 {frames}(최대 {maxDt:0.000}초) · timeScale {Time.timeScale:0.00}");
    }

    private static UniTask Wait(float seconds, CancellationToken ct)
        => UniTask.Delay(System.TimeSpan.FromSeconds(seconds), ignoreTimeScale: true, cancellationToken: ct);

    private static async UniTask Shot(string name)
    {
        ScreenCapture.CaptureScreenshot($"{ShotDir}/{name}.png");
        await UniTask.Yield();
        await UniTask.Yield();
    }

    /// <summary>화면 글 중 시간처럼 보이는 줄 · 제목 줄만 적는다.</summary>
    private static void Dump(StringBuilder sb, string what, GameObject root)
    {
        sb.AppendLine($"  [{what}]");
        if (root == null) { sb.AppendLine("    없음"); return; }
        int n = 0;
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text)) continue;
            string s = t.text;
            if (s.IndexOf("m ") < 0 && s.IndexOf("s ") < 0 && !s.EndsWith("s") && s.IndexOf("기록") < 0 && s.IndexOf("time") < 0 && s.IndexOf("Live") < 0 && s.IndexOf("실시간") < 0) continue;
            sb.AppendLine($"    {s.Replace("\n", " / ")}");
            if (++n >= 14) break;
        }
    }
}
#endif
