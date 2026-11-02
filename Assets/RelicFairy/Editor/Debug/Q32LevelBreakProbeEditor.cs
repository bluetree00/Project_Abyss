#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q32 레벨 몫 「일부러 깨 보기」 — 보스 연출 도중 ESC 메뉴를 열고 닫는다(키 없이 EscKeyListener.Handle).
/// A 보스 등장 중 1.0 · 2.5 · 4.0초에 열고 0.7초 뒤 닫기 → 등장이 끝난 뒤 상태 검사.
/// B 2페이지 전환(TestHub 「5 Kill Boss or Next Page」) 중 0.8 · 2.0초에 열고 닫기 → 전환이 끝난 뒤 상태 검사.
/// 검사: timeScale 1 · 시간 잡은 쪽 없음 · 레터박스 꺼짐 · 컷신 HUD 아님 · 조작 켜짐 · 메뉴 닫힘.
/// 결과: Temp/q32_level/report.txt · 사진 · 로그 「[Q32Level] 끝」.
/// </summary>
public static class Q32LevelBreakProbeEditor
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/11-02 Q32 레벨 — 보스 연출 중 ESC 열고 닫기 (보스방, Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Q32Level] 보스방 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "q32_level");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("11-02 Q32 레벨 — 보스 연출 중 ESC" + Environment.NewLine);
        int fails = 0;
        try
        {
            var esc = UnityEngine.Object.FindFirstObjectByType<EscKeyListener>(FindObjectsInactive.Include);
            var menu = esc != null ? esc.GetComponent<UI_EscMenu>() : null;
            var handle = typeof(EscKeyListener).GetMethod("Handle", Any);
            if (esc == null || handle == null) { sb.AppendLine("ESC 입구 없음"); return; }

            // ── A 등장 ──
            MonsterBase FindBoss() => UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None).FirstOrDefault(m => m is IBoss);
            if (FindBoss() == null)
            {
                TestHubDebugMenu.StepIntoBossEntrance();
                await UniTask.Delay(300, DelayType.Realtime);
                TestHubDebugMenu.StepPastBossEntrance();
            }
            MonsterBase boss = null;
            float until = Time.realtimeSinceStartup + 10f;
            while ((boss = FindBoss()) == null && Time.realtimeSinceStartup < until) await UniTask.Delay(100, DelayType.Realtime);
            if (boss == null) { sb.AppendLine("보스 없음"); return; }
            sb.AppendLine($"A 등장 — {boss.name}");
            float t0 = Time.realtimeSinceStartup;
            foreach (float at in new[] { 1.0f, 2.5f, 4.0f })
            {
                while (Time.realtimeSinceStartup - t0 < at) await UniTask.Yield();
                handle.Invoke(esc, null);
                await UniTask.Delay(700, DelayType.Realtime);
                bool opened = menu != null && menu.IsOpen;
                float scale = Time.timeScale;
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"a_{at:0.0}s_menu.png"));
                await UniTask.Delay(150, DelayType.Realtime);
                handle.Invoke(esc, null);
                sb.AppendLine($"  {at:0.0}초 ESC → 메뉴 {(opened ? "열림" : "안 열림")} · 연 동안 시간 {scale:0.00} → 닫기");
            }
            fails += await CheckAfterAsync("A 등장 끝", sb, dir, 25f);

            // ── B 2페이지 전환 ──
            // 기사는 플레이어가 다가오기 전엔 잠든 채(패턴 · 전환 없음) — 앞 5 m로 옮겨 깨우고, 2페이지 경계로 보낸 뒤 한 대
            var player = GameRunBootstrapper.Instance?.Run?.Player;
            if (player != null)
            {
                player.SetInvincible(60f);   // 실측 중 쓰러지면 결과창이 ESC를 「계속」으로 받아 검사가 흐려진다

                Vector3 front = boss.transform.position + boss.transform.forward * 5f;
                if (player.TryGetComponent<Rigidbody>(out var rb)) rb.position = front;
                player.transform.position = front;
            }
            var dormant = boss.GetType().GetField("_dormantState", Any)?.GetValue(boss);
            var dormantActive = dormant?.GetType().GetProperty("IsActive", Any);
            float tw = Time.realtimeSinceStartup;
            while (dormantActive != null && (bool)dormantActive.GetValue(dormant) && Time.realtimeSinceStartup - tw < 10f)
                await UniTask.Delay(100, DelayType.Realtime);
            sb.AppendLine($"  깨움 {Time.realtimeSinceStartup - tw:0.0}초 · 잠든 채 {(dormantActive != null && (bool)dormantActive.GetValue(dormant))}");
            BossPageDebugMenu.JumpToPage2();
            // 영혼 소환(1페이지 50% 하한)이 끝나야 피해가 들어간다 — 끝나길 기다렸다가 한 대
            float tg = Time.realtimeSinceStartup;
            while (boss is DeathKnightBossMonster g && !g.SoulGateCleared && Time.realtimeSinceStartup - tg < 25f)
                await UniTask.Delay(200, DelayType.Realtime);
            sb.AppendLine($"  영혼 소환 끝까지 {Time.realtimeSinceStartup - tg:0.0}초");
            await UniTask.Delay(500, DelayType.Realtime);
            float tb = Time.realtimeSinceStartup;
            bool began = false;
            var run = GameRunBootstrapper.Instance?.Run;
            // 기사는 패턴 · 단계 진입 중 잠깐 무적이다(「무적 상태라 무시됨」) — 들어갈 때까지 1.5초마다 다시 한 대
            for (int hp0 = boss.CurrentHp; Time.realtimeSinceStartup - tb < 20f && boss.CurrentHp == hp0 && !CinematicFrame.IsShown && !(run?.InCutscene ?? false); )
            {
                TestHubDebugMenu.KillBoss();
                await UniTask.Delay(1500, DelayType.Realtime);
            }
            sb.AppendLine($"  피해 들어감 {Time.realtimeSinceStartup - tb:0.0}초 · HP {boss.CurrentHp}/{boss.EffectiveMaxHp}");
            tb = Time.realtimeSinceStartup;
            var fsm = typeof(MonsterBase).GetField("_fsm", Any)?.GetValue(boss);
            var dk  = boss as DeathKnightBossMonster;
            float nextLog = 0f;
            while (Time.realtimeSinceStartup - tb < 30f)
            {
                if (CinematicFrame.IsShown || (run?.InCutscene ?? false)) { began = true; break; }
                float e = Time.realtimeSinceStartup - tb;
                if (e >= nextLog)
                {
                    nextLog += 2f;
                    var type = fsm?.GetType().GetProperty("CurrentType")?.GetValue(fsm) as Type;
                    float dist = player != null ? Vector3.Distance(player.transform.position, boss.transform.position) : -1f;
                    sb.AppendLine($"    [{e,4:0.0}] 상태 {type?.Name ?? "?"} · 패턴 중 {boss.IsInSpecialState} · HP {boss.CurrentHp}/{boss.EffectiveMaxHp}" +
                                  (dk != null ? $" · HpRatio {dk.HpRatio:0.000} · 영혼 소환 끝 {dk.SoulGateCleared}" : "") + $" · 플레이어 거리 {dist:0.0} m");
                }
                await UniTask.Delay(50, DelayType.Realtime);
            }
            sb.AppendLine($"B 2페이지 전환 — 시작 {(began ? $"확인({Time.realtimeSinceStartup - tb:0.0}초 뒤)" : "20초 안에 못 봄")} · 보스 HP {boss.CurrentHp}/{boss.EffectiveMaxHp}");
            if (!began) fails++;
            float t1 = Time.realtimeSinceStartup;
            foreach (float at in new[] { 0.8f, 2.0f })
            {
                while (Time.realtimeSinceStartup - t1 < at) await UniTask.Yield();
                handle.Invoke(esc, null);
                await UniTask.Delay(700, DelayType.Realtime);
                bool opened = menu != null && menu.IsOpen;
                float scale = Time.timeScale;
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"b_{at:0.0}s_menu.png"));
                await UniTask.Delay(150, DelayType.Realtime);
                handle.Invoke(esc, null);
                sb.AppendLine($"  {at:0.0}초 ESC → 메뉴 {(opened ? "열림" : "안 열림")} · 연 동안 시간 {scale:0.00} → 닫기");
            }
            fails += await CheckAfterAsync("B 전환 끝", sb, dir, 25f);
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); fails++; }
        finally
        {
            sb.AppendLine($"실패 {fails}");
            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString(), Encoding.UTF8);
            Debug.Log($"[Q32Level] 끝 — 실패 {fails} · {dir}");
        }
    }

    /// <summary>연출이 끝나길(레터박스 · 컷신 HUD · 조작 잠금이 풀리길) 기다린 뒤 상태를 검사한다. 실패 수.</summary>
    private static async UniTask<int> CheckAfterAsync(string label, StringBuilder sb, string dir, float timeout)
    {
        var run = GameRunBootstrapper.Instance?.Run;
        var player = run?.Player;
        var lockField = typeof(PlayerController).GetField("_inputDisabledExternally", Any);
        bool Locked() => player != null && (bool)(lockField?.GetValue(player) ?? false);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < timeout && (CinematicFrame.IsShown || (run?.InCutscene ?? false) || Locked()))
            await UniTask.Delay(100, DelayType.Realtime);
        await UniTask.Delay(500, DelayType.Realtime);
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, label.Replace(' ', '_') + ".png"));
        await UniTask.Delay(150, DelayType.Realtime);

        var esc  = UnityEngine.Object.FindFirstObjectByType<UI_EscMenu>(FindObjectsInactive.Include);
        string holders = TimeScaleArbiter.DescribeHolders();
        int f = 0;
        void Check(bool ok, string what) { if (!ok) f++; sb.AppendLine($"  {(ok ? "PASS" : "FAIL")} {what}"); }
        sb.AppendLine($"{label} — {Time.realtimeSinceStartup - t0:0.0}초 뒤");
        Check(Mathf.Approximately(Time.timeScale, 1f), $"시간 배속 {Time.timeScale:0.00}");
        Check(holders == "(none)", $"시간 잡은 쪽 「{holders}」");
        Check(!CinematicFrame.IsShown, "레터박스 꺼짐");
        Check(!(run?.InCutscene ?? false), $"HUD 모드 {run?.CurrentHudMode}");
        Check(!Locked(), "조작 켜짐");
        Check(esc == null || !esc.IsOpen, "ESC 메뉴 닫힘");
        return f;
    }
}
#endif
