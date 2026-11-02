#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 11-02 사용자 「리치 2페이지 끝 봉인석을 하나씩 카메라로 비추면 어지럽다」 실측 — 봉인석이 서기 시작할 때부터 목표 띠(UI_ChallengeHud)가 뜰 때까지
/// 매 프레임 카메라 수평각(요) · 높이를 적고, 네 돌이 다 선 뒤 돌마다 화면 안 자리(뷰포트)를 잰다. 사진 3장(첫 돌 · 넷 다 선 뒤 · 돌아온 뒤).
/// 길은 10-11 추방 실측과 같다(테스트 허브 Ch4 보스 대기방 직행 → 페이지 넘김 → 봉인 의식 · 봉인기 오버라이드 0). 봉인석은 치지 않는다.
/// 결과: Temp/sealcam1102.txt · Temp/ui_shots/SC1102_*.png · 로그 「[SC1102] 끝」. 실측 뒤 플레이를 멈출 것.
/// </summary>
public static class SealCam1102ProbeEditor
{
    private const string MenuPath = "RelicFairy/Debug/11-02 리치 봉인석 세우기 카메라 실측 (테스트 허브, 플레이 중)";
    private const string OutPath  = "Temp/sealcam1102.txt";
    private const string ShotDir  = "Temp/ui_shots";

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic;

    private struct Sample { public float t, yaw, pitch, y; public int stones; }

    private static bool        s_running;
    private static LichMonster s_lich;
    private static bool        s_debugWas;

    [MenuItem(MenuPath)]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[SC1102] 플레이 모드(테스트 허브)에서"); return; }
        if (s_running) { Debug.LogWarning("[SC1102] 이미 도는 중"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        s_running = true;
        s_lich    = null;
        s_debugWas = false;
        var sb = new StringBuilder("리치 봉인석 세우기 카메라 실측 (11-02)\n");
        int prevOverride = StoryProgress.DebugNightmareOverride;
        try
        {
            SetOverride(0);
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher != null)
            {
                launcher.SelectChapter(4);
                if (!launcher.ToggleBossApproach()) launcher.ToggleBossApproach();
                if (!await WaitAsync(launcher.TryLaunch, 30f)) { sb.AppendLine("허브 시작 실패"); return; }
                if (!await WaitAsync(() => SceneManager.GetActiveScene().name == "GameScene_Ch4", 90f)) { sb.AppendLine("Ch4 진입 실패"); return; }
            }
            if (!await WaitAsync(() => RunPlayer() != null && (GameRunBootstrapper.Instance?.Run?.IsRunning ?? false), 90f)) { sb.AppendLine("런 없음"); return; }
            RunPlayer().SetInvincible(3600f);
            if (!await EnterBossRoomAsync(sb)) return;

            var lich = Object.FindFirstObjectByType<LichMonster>();
            GuardLich(lich, sb);

            // 페이지를 넘겨 봉인 의식까지 — 첫 돌이 서는 순간을 놓치지 않게 매 프레임 본다(처치 · 대사 넘기기는 1.2초마다)
            var   samples = new List<Sample>(4096);
            float start   = Time.realtimeSinceStartup;
            float nextAct = start;
            bool  engaged = false;
            while (Time.realtimeSinceStartup - start < 150f && Stones().Length == 0)
            {
                Record(samples);
                if (Time.realtimeSinceStartup >= nextAct)
                {
                    nextAct = Time.realtimeSinceStartup + 1.2f;
                    lich = Object.FindFirstObjectByType<LichMonster>();
                    if (lich != null && !engaged)
                    {
                        engaged = lich.CurrentHp < lich.BossMaxHp;
                        if (!engaged) StepNearLich(lich);
                    }
                    if (Object.FindFirstObjectByType<UI_DialoguePopup>() != null) TestHubDebugMenu.AdvanceDialogue();
                    else if (lich != null) TestHubDebugMenu.KillBoss();
                }
                await UniTask.Yield();
            }
            if (Stones().Length == 0) { sb.AppendLine("봉인 의식에 닿지 못했다(150초)"); return; }
            float first = Time.realtimeSinceStartup;
            sb.AppendLine($"첫 돌 {first - start:0.0}초");

            // 첫 돌 · 넷 다 선 뒤 · 목표 띠(= 카메라가 돌아옴) 뒤 — 사이 내내 매 프레임 기록
            bool shot1 = false, shot4 = false;
            float fourAt = -1f;
            while (Time.realtimeSinceStartup - first < 15f && Object.FindFirstObjectByType<UI_ChallengeHud>() == null)
            {
                Record(samples);
                float now = Time.realtimeSinceStartup;
                int   n   = Stones().Length;
                if (!shot1 && now - first >= 0.3f) { shot1 = true; await Shot("SC1102_1_first", sb); }
                if (n >= 4 && fourAt < 0f) fourAt = now;
                if (!shot4 && fourAt > 0f && now - fourAt >= 0.35f)
                {
                    shot4 = true;
                    Viewports(sb);
                    await Shot("SC1102_2_all", sb);
                }
                if (Object.FindFirstObjectByType<UI_DialoguePopup>() != null) TestHubDebugMenu.AdvanceDialogue();
                await UniTask.Yield();
            }
            float back = Time.realtimeSinceStartup;
            bool  hud  = Object.FindFirstObjectByType<UI_ChallengeHud>() != null;
            sb.AppendLine($"목표 띠 {(hud ? $"뜸 — 첫 돌 뒤 {back - first:0.00}초" : "안 뜸(15초)")}");
            for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 1.2f; ) { Record(samples); await UniTask.Yield(); }
            await Shot("SC1102_3_back", sb);

            Analyze(samples, first - 2f, back + 1.2f, sb);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            RestoreLich(sb);
            SetOverride(prevOverride);
            File.WriteAllText(OutPath, sb.ToString());
            Debug.Log("[SC1102] 끝\n" + sb);
            s_running = false;
        }
    }

    // ── 길 ──────────────────────────────────────────────────────────
    private static async UniTask<bool> EnterBossRoomAsync(StringBuilder sb)
    {
        if (Object.FindFirstObjectByType<LichMonster>() != null) return true;
        if (!await WaitAsync(() => Object.FindObjectsByType<ProcRoomGate>(FindObjectsSortMode.None).Any(g => g.IsArmed)
                                   || Object.FindFirstObjectByType<BossRoomController>() != null, 40f)) { sb.AppendLine("열린 출구 · 보스방 없음"); return false; }
        if (Object.FindFirstObjectByType<BossRoomController>() == null)
        {
            TestHubDebugMenu.EnterOpenExit();
            if (!await WaitAsync(() => Object.FindFirstObjectByType<BossRoomController>() != null, 40f)) { sb.AppendLine("보스방에 못 들어갔다"); return false; }
        }
        await UniTask.Delay(2500, DelayType.Realtime);
        TestHubDebugMenu.StepIntoBossEntrance();
        await UniTask.Delay(400, DelayType.Realtime);
        TestHubDebugMenu.StepPastBossEntrance();
        if (!await WaitAsync(() => Object.FindFirstObjectByType<LichMonster>() != null, 30f)) { sb.AppendLine("리치가 나타나지 않았다"); return false; }
        return true;
    }

    private static void GuardLich(LichMonster lich, StringBuilder sb)
    {
        if (lich == null) return;
        s_lich = lich;
        var field = typeof(LichMonster).GetField("_debugOverrideEncounter", Any);
        s_debugWas = field != null && (bool)field.GetValue(lich);
        if (!s_debugWas) return;
        field.SetValue(lich, false);
        var handler = typeof(LichMonster).GetMethod("Editor_HandleDied", Any);
        if (handler != null)
            lich.OnDied -= (System.Action<MonsterBase>)System.Delegate.CreateDelegate(typeof(System.Action<MonsterBase>), lich, handler);
        sb.AppendLine("테스트 조우 길 꺼 둠(실측 동안)");
    }

    private static void RestoreLich(StringBuilder sb)
    {
        if (s_lich == null) return;
        if (s_debugWas) typeof(LichMonster).GetField("_debugOverrideEncounter", Any)?.SetValue(s_lich, true);
        s_lich = null;
    }

    private static void StepNearLich(LichMonster lich)
    {
        var player = RunPlayer();
        if (player == null || lich == null) return;
        Vector3 l   = lich.transform.position;
        Vector3 dir = player.transform.position - l;
        dir.y = 0f;
        dir   = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.back;
        Vector3 at = l + dir * 8f;
        at.y = Physics.Raycast(new Vector3(at.x, l.y + 10f, at.z), Vector3.down, out var hit, 60f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore)
            ? hit.point.y + 0.25f
            : player.transform.position.y;
        player.transform.position = at;
        if (player.TryGetComponent<Rigidbody>(out var rb) && !rb.isKinematic) { rb.position = at; rb.linearVelocity = Vector3.zero; }
    }

    // ── 기록 · 판정 ─────────────────────────────────────────────────
    private static void Record(List<Sample> samples)
    {
        var cam = Camera.main;
        if (cam == null) return;
        var e = cam.transform.eulerAngles;
        samples.Add(new Sample { t = Time.realtimeSinceStartup, yaw = e.y, pitch = e.x, y = cam.transform.position.y, stones = Stones().Length });
    }

    /// <summary>창 안 — 수평각 누적 이동(돌아간 각의 합) · 가장 크게 벌어진 폭 · 가장 빠른 회전 · 30° 넘는 방향 바꿈 수 · 높이 폭.</summary>
    private static void Analyze(List<Sample> all, float from, float to, StringBuilder sb)
    {
        var s = all.Where(x => x.t >= from && x.t <= to).ToList();
        if (s.Count < 2) { sb.AppendLine("기록 부족"); return; }
        float path = 0f, acc = 0f, lo = 0f, hi = 0f, maxSpeed = 0f, legStart = 0f, minY = s[0].y, maxY = s[0].y;
        int   turns = 0, legSign = 0;
        for (int i = 1; i < s.Count; i++)
        {
            float d  = Mathf.DeltaAngle(s[i - 1].yaw, s[i].yaw);
            float dt = Mathf.Max(0.0001f, s[i].t - s[i - 1].t);
            path += Mathf.Abs(d);
            acc  += d;
            lo = Mathf.Min(lo, acc); hi = Mathf.Max(hi, acc);
            if (dt > 0.005f) maxSpeed = Mathf.Max(maxSpeed, Mathf.Abs(d) / dt);
            int sign = Mathf.Abs(d) < 0.05f ? 0 : (d > 0 ? 1 : -1);
            if (sign != 0 && sign != legSign) { if (Mathf.Abs(acc - legStart) >= 30f) turns++; legStart = acc; legSign = sign; }
            minY = Mathf.Min(minY, s[i].y); maxY = Mathf.Max(maxY, s[i].y);
        }
        if (Mathf.Abs(acc - legStart) >= 30f) turns++;
        sb.AppendLine($"카메라 수평각(첫 돌 2초 전 ~ 돌아온 뒤 1.2초 · {s.Count}프레임 · {s[s.Count - 1].t - s[0].t:0.0}초):");
        sb.AppendLine($"  돌아간 각 합 {path:0}° · 벌어진 폭 {hi - lo:0}° · 처음 → 끝 {acc:0}° · 가장 빠른 회전 {maxSpeed:0}°/초 · 30° 넘는 한 방향 회전 {turns}번");
        sb.AppendLine($"  카메라 높이 {minY:0.0} ~ {maxY:0.0} m");
        // 0.25초마다 한 줄 — 언제 얼마나 도는지
        sb.AppendLine("  시각(첫 돌 기준) · 수평각 · 내려다봄 · 높이 · 선 돌:");
        float firstT = from + 2f, next = from;
        foreach (var x in s)
        {
            if (x.t < next) continue;
            next = x.t + 0.25f;
            sb.AppendLine($"    {x.t - firstT,6:+0.00;-0.00} · {x.yaw,5:0} · {x.pitch,4:0} · {x.y,5:0.0} · {x.stones}");
        }
    }

    /// <summary>네 돌 · 리치가 화면 안인가(뷰포트 x 0.03~0.97 · y 0.12~0.92 — 아래 HUD · 위 보스 바를 피한 띠).</summary>
    private static void Viewports(StringBuilder sb)
    {
        var cam = Camera.main;
        if (cam == null) { sb.AppendLine("카메라 없음"); return; }
        int inside = 0;
        var stones = Stones();
        foreach (var st in stones)
        {
            Vector3 vp = cam.WorldToViewportPoint(st.transform.position + Vector3.up * 1.3f);
            bool ok = vp.z > 0f && vp.x >= 0.03f && vp.x <= 0.97f && vp.y >= 0.12f && vp.y <= 0.92f;
            if (ok) inside++;
            sb.AppendLine($"  돌 ({st.transform.position.x:0.0}, {st.transform.position.z:0.0}) 화면 ({vp.x:0.00}, {vp.y:0.00}) 거리 {vp.z:0.0} m → {(ok ? "안" : "밖")}");
        }
        var lich = Object.FindFirstObjectByType<LichMonster>();
        if (lich != null)
        {
            Vector3 vp = cam.WorldToViewportPoint(lich.transform.position + Vector3.up * 1.5f);
            sb.AppendLine($"  리치 화면 ({vp.x:0.00}, {vp.y:0.00}) 거리 {vp.z:0.0} m");
        }
        sb.AppendLine($"  넷 다 선 뒤 화면 안 돌 {inside}/{stones.Length}");
    }

    private static LichSealStone[] Stones() => Object.FindObjectsByType<LichSealStone>(FindObjectsSortMode.None);

    private static PlayerController RunPlayer() => GameRunBootstrapper.Instance?.Run?.Player;

    private static void SetOverride(int value)
    {
        EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, value);
        StoryProgress.RefreshDebugOverride();
    }

    private static async UniTask Shot(string name, StringBuilder sb)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null) return;
        Directory.CreateDirectory(ShotDir);
        File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        sb.AppendLine($"  사진 {name}");
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
}
#endif
