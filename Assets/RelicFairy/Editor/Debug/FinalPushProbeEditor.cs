#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-29 F2 해방 보스 「한 번 더」 확인 — 해방기 오버라이드 · 보스방에서 Fight Sim을 켠 뒤 부른다.
/// 2페이지(전환 끝)를 기다려 간판을 건너뛰고 체력을 2페이지 22%로 → 80초 동안 10% 아래로 안 내려가게 붙잡는다.
/// 잰다: 「한 번 더」 횟수 · 앞 패턴 끝 → 시작 · 끝 → 다음 패턴(큰 틈) vs 다른 패턴 뒤 공백 · 그동안 플레이어가 맞은 횟수. 사진 첫 두 번(시작 0.9초).
/// 결과 Temp/final_push/&lt;보스&gt;.txt · 로그 「[FinalPushProbe] 끝」.
/// </summary>
public static class FinalPushProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const float PinSeconds = 80f, PinAt = 0.22f, PinFloor = 0.10f;

    [MenuItem("RelicFairy/Debug/10-29 F2 한 번 더 확인 (해방기 보스방 · Fight Sim 중, Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[FinalPushProbe] 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        Directory.CreateDirectory("Temp/final_push");
        var sb = new StringBuilder();
        string tag = "boss";
        bool pendingPush = false;
        int pushLogs = 0, endLogs = 0;
        void OnLog(string msg, string st, LogType t)
        {
            if (!msg.StartsWith("[FinalPush] ")) return;
            if (msg.Contains("「한 번 더」")) { pendingPush = true; pushLogs++; }
            else if (msg.Contains("큰 틈")) endLogs++;
        }
        Application.logMessageReceived += OnLog;
        try
        {
            MonsterBase boss = null; BossPages pages = null;
            for (float until = Time.realtimeSinceStartup + 120f; Time.realtimeSinceStartup < until; )
            {
                boss = UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None).FirstOrDefault(m => m is IPagedBoss p && p.Pages != null && p.Pages.Enabled && !m.IsDead);
                pages = (boss as IPagedBoss)?.Pages;
                if (pages != null && pages.IsPage2 && !pages.Transitioning) break;
                await UniTask.Delay(500, DelayType.Realtime);
            }
            if (boss == null || pages == null || !pages.IsPage2) { sb.AppendLine("2페이지 보스 없음(해방기 오버라이드 · 보스방 · 전환 끝?)"); return; }
            tag = boss.name.Replace("(Clone)", "").Trim();
            pages.MarkSignatureDone();
            SetHp(boss, Mathf.CeilToInt(pages.Page2Hp * PinAt));
            sb.AppendLine($"{tag} — 2페이지 {pages.Page2Hp} · 붙잡음 {PinAt:P0}(바닥 {PinFloor:P0}) {PinSeconds}초");

            var player = GameRunBootstrapper.Instance?.Run?.Player;
            int lastHp = player != null && player.RuntimeStats != null ? player.RuntimeStats.Hp : 0;
            var pushGaps = new List<float>(); var otherGaps = new List<float>(); var links = new List<float>();
            int pushes = 0, hitsInPush = 0, hitsOther = 0, shots = 0, pins = 0;
            bool wasIn = boss.IsInSpecialState, running = false, lastWasPush = false;
            float lastEnd = -1f, pushStart = -1f;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < PinSeconds && boss != null && !boss.IsDead)
            {
                float now = Time.time;
                if (boss.CurrentHp < pages.Page2Hp * PinFloor) { SetHp(boss, Mathf.CeilToInt(pages.Page2Hp * PinAt)); pins++; }
                bool inPat = boss.IsInSpecialState;
                if (inPat && !wasIn)   // 패턴 시작
                {
                    if (pendingPush)
                    {
                        pendingPush = false; running = true; pushes++; pushStart = Time.realtimeSinceStartup;
                        if (lastEnd >= 0f) links.Add(now - lastEnd);
                    }
                    else if (lastEnd >= 0f) (lastWasPush ? pushGaps : otherGaps).Add(now - lastEnd);
                }
                if (!inPat && wasIn)   // 패턴 끝
                {
                    lastEnd = now; lastWasPush = running; running = false;
                }
                if (running && shots < 2 && pushStart > 0f && Time.realtimeSinceStartup - pushStart >= 0.9f)
                {
                    ScreenCapture.CaptureScreenshot($"Temp/final_push/{tag}_{++shots}.png");
                    pushStart = -1f;
                }
                int hp = player != null && player.RuntimeStats != null ? player.RuntimeStats.Hp : 0;
                if (hp < lastHp) { if (running) hitsInPush++; else hitsOther++; }
                lastHp = hp;
                wasIn = inPat;
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            sb.AppendLine($"「한 번 더」 로그 {pushLogs} · 시작 {pushes} · 끝 로그 {endLogs} · 체력 붙잡음 {pins}");
            sb.AppendLine($"앞 패턴 끝 → 「한 번 더」 시작 {Avg(links)} ({links.Count})");
            sb.AppendLine($"「한 번 더」 끝 → 다음 패턴(큰 틈) {Avg(pushGaps)} ({pushGaps.Count}) · 다른 패턴 뒤 공백 {Avg(otherGaps)} ({otherGaps.Count})");
            sb.AppendLine($"플레이어 맞음 — 「한 번 더」 중 {hitsInPush} · 다른 때 {hitsOther}");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Application.logMessageReceived -= OnLog;
            File.WriteAllText($"Temp/final_push/{tag}.txt", sb.ToString(), Encoding.UTF8);
            Debug.Log($"[FinalPushProbe] 끝\n{sb}");
        }
    }

    /// <summary>F1 넘긴 방 — 해방기 자동 실측(피해 모사) 중 정해진 시간을 1.5초 넘긴 순간 한 장(「빠른 정리」 한 줄이 흐려졌나 · 4c HUD). Temp/final_push/f1_slow.png</summary>
    [MenuItem("RelicFairy/Debug/10-29 F1 넘긴 방 사진 (해방기 자동 실측 중, Play)")]
    private static void SlowShot()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[F1SlowShot] 플레이 중에"); return; }
        SlowShotAsync().Forget();
    }

    private static async UniTaskVoid SlowShotAsync()
    {
        Directory.CreateDirectory("Temp/final_push");
        for (float until = Time.realtimeSinceStartup + 300f; Time.realtimeSinceStartup < until;)
        {
            if (RoomSpeedClear.IsActive && RoomSpeedClear.FightSeconds > RoomSpeedClear.LimitSeconds + 1.5f) break;
            await UniTask.Delay(250, DelayType.Realtime);
        }
        string s = $"싸움 {RoomSpeedClear.FightSeconds:0.0} / 정해진 {RoomSpeedClear.LimitSeconds:0.0} · 빠름 {RoomSpeedClear.IsFast}";
        ScreenCapture.CaptureScreenshot("Temp/final_push/f1_slow.png");
        await UniTask.Delay(600, DelayType.Realtime);
        Debug.Log("[F1SlowShot] 끝 — " + s);
    }

    private static string Avg(List<float> v) => v.Count == 0 ? "-" : $"{v.Average():0.00}초(최소 {v.Min():0.00})";

    private static void SetHp(MonsterBase boss, int hp)
    {
        var runtime = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(boss);
        var field   = runtime?.GetType().GetField("CurrentHp", Inst);
        field?.SetValue(runtime, Mathf.Clamp(hp, 1, boss.EffectiveMaxHp));
    }
}
#endif
