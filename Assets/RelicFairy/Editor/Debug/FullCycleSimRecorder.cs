using System;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [전주기 시뮬레이션 · 플레이 중] 런 자동 실측 · 이야기 로그를 받아 순간마다 화면을 찍고 시각표를 남긴다(10-01 사용자 지시).
/// <para>찍는 순간: 방 진입 · 대사 첫 줄 · 게임 정지 팝업 · 보스 등장 · 보상 등장 · 챕터 포탈 · 이야기 장면(봉인 · 각성 · 붕괴 · 엔딩).
/// 대사창 · 보스 말풍선의 글은 줄마다 시각표에 적는다(자동 진행은 대사를 곧바로 넘겨 화면에는 첫 줄만 남는다).</para>
/// 결과: Temp/sim/{구간}/NNNN_태그.jpg(960×540) · Temp/sim/{구간}/log.txt
/// </summary>
public static class FullCycleSimRecorder
{
    private const string Root = "RelicFairy/Debug/전주기 시뮬/";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const int ShotW = 960, ShotH = 540;
    private const float MinShotGap = 0.35f;

    private static bool   s_on;
    private static string s_dir;
    private static int    s_seq;
    private static double s_t0;
    private static float  s_lastShot = -10f;
    private static bool   s_capturing;
    private static string s_lastDialogue, s_lastBark;
    private static StreamWriter s_log;

    [MenuItem(Root + "기록 켜기 — 봉인기")]    private static void BeginSealed()     => Begin("S1_sealed");
    [MenuItem(Root + "기록 켜기 — 해방기")]    private static void BeginLiberated()  => Begin("S2_liberated");
    [MenuItem(Root + "기록 켜기 — 악몽 모드")] private static void BeginNightmare()  => Begin("S3_nightmare");
    [MenuItem(Root + "기록 끄기")]             private static void Stop()            => End("manual");
    [MenuItem(Root + "지금 찍기")]             private static void ShotNow()         { if (s_on) Capture("manual", 0).Forget(); }

    /// <summary>엔딩 카드 · 크레딧만 다시 튼다(플레이 중 · 기록이 켜져 있어야 찍힌다) — 1.5초마다 30초 찍는다.</summary>
    [MenuItem(Root + "엔딩 카드 다시 보기 (플레이 중)")]
    private static void ReplayEndingCard()
    {
        if (!Application.isPlaying || !s_on) { Debug.LogWarning("[SimRec] 플레이 중 · 기록 켠 뒤에"); return; }
        ReplayEndingCardAsync().Forget();
    }

    private static async UniTaskVoid ReplayEndingCardAsync()
    {
        var dlg = Managers.DialogueData;
        if (dlg == null) return;
        if (!dlg.IsInitialized) await dlg.InitializeAsync();
        Write("── 엔딩 카드 다시 보기");
        // 테스트 허브 화면(IMGUI)이 카드 위를 덮는다 — 찍는 동안 끈다.
        var probe = typeof(UILayoutRuntimeProbeEditor);
        probe.GetMethod("HideTestHubGui", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
        var play = EndingCardOverlay.PlayAsync(dlg.GetLines(EndingSequence.CardKey), dlg.GetLines(EndingSequence.CreditsKey), default);
        for (int i = 0; i < 20; i++) Capture($"ending_{i:00}", i * 1500).Forget();
        await play;
        probe.GetMethod("RestoreTestHubGui", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
        Write("── 엔딩 카드 끝");
    }

    private static void Begin(string segment)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[SimRec] 플레이 모드에서만"); return; }
        End("restart");
        s_dir = Path.Combine("Temp", "sim", segment);
        Directory.CreateDirectory(s_dir);
        s_seq = Directory.GetFiles(s_dir, "*.jpg").Length;   // 같은 구간을 이어서 켜면 번호를 잇는다
        s_log = new StreamWriter(Path.Combine(s_dir, "log.txt"), append: true, Encoding.UTF8) { AutoFlush = true };
        s_t0  = EditorApplication.timeSinceStartup;
        s_on  = true;
        s_lastDialogue = s_lastBark = null;
        Application.logMessageReceived += OnLog;
        EditorApplication.update += Watch;
        EditorApplication.playModeStateChanged += OnPlayMode;
        Write($"── 기록 시작 {segment} · 시기 {StoryProgress.Era} · {DateTime.Now:HH:mm:ss}");
        Debug.Log($"[SimRec] 기록 켬 — {s_dir}");
    }

    private static void End(string why)
    {
        if (!s_on) return;
        Write($"── 기록 끝({why}) · {DateTime.Now:HH:mm:ss}");
        s_on = false;
        Application.logMessageReceived -= OnLog;
        EditorApplication.update -= Watch;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        s_log?.Dispose();
        s_log = null;
    }

    private static void OnPlayMode(PlayModeStateChange s)
    {
        if (s == PlayModeStateChange.ExitingPlayMode) End("play-stopped");
    }

    private static void Write(string line)
    {
        double t = EditorApplication.timeSinceStartup - s_t0;
        s_log?.WriteLine($"{t,7:0.0} {line}");
    }

    // ── 로그 → 기록 · 찍기 ─────────────────────────────────────

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (!s_on || string.IsNullOrEmpty(msg)) return;
        string first = msg.Split('\n')[0];

        if (type == LogType.Error || type == LogType.Exception)
        {
            Write("✗ " + Trim(first, 180));
            return;
        }

        bool keep = first.StartsWith("[RunAuto]") || first.StartsWith("[Story]") || first.StartsWith("[BossStory]")
                 || first.StartsWith("[LichSeal]") || first.StartsWith("[BossClear]") || first.Contains("챕터 전환")
                 || first.StartsWith("[RunFlow]") || first.StartsWith("[SimRec]");
        if (!keep || first.Contains(" 보스 상태 ")) return;
        Write(Trim(first, 220));

        // 자동 실측 줄은 「[RunAuto] t=12.3 방 진입 …」 — 시각 앞머리를 떼고 본다.
        string ev = first.StartsWith("[RunAuto]") ? AfterTime(first) : first;
        if      (ev.StartsWith("방 진입"))       Capture("room " + Tail(ev, "방 진입"), 2600).Forget();   // 잉크 와이프가 걷힌 뒤
        else if (ev.StartsWith("차단 팝업") && !ev.Contains("→")) Capture("popup " + Tail(ev, "차단 팝업"), 0).Forget();
        else if (ev.StartsWith("보스 등장"))     Capture("boss", 800).Forget();
        else if (ev.StartsWith("클리어") && ev.Contains("보상 등장")) Capture("reward", 900).Forget();
        else if (ev.StartsWith("챕터 포탈"))     Capture("portal", 0).Forget();
        else if (first.StartsWith("[RunFlow] 출구 없음"))   // 런 끝 — 끝 카드 · 귀환 화면
        {
            Capture("runend+2s", 2000).Forget();
            Capture("runend+5s", 5000).Forget();
        }
        else if (first.StartsWith("[BossStory]") || first.StartsWith("[LichSeal]") || first.StartsWith("[Story]"))
        {
            Capture("story", 400).Forget();
            Capture("story+2s", 2400).Forget();
        }
    }

    /// <summary>대사창 · 보스 말풍선 글을 줄마다 적고, 대사창이 새로 열리면 첫 줄을 찍는다.</summary>
    private static void Watch()
    {
        if (!s_on || !Application.isPlaying) return;

        // 오류 로그가 콘솔 Error Pause로 게임을 멈추면 자동 진행만 돌고 게임은 선다 — 기록 중엔 풀고 적는다(오류 줄은 이미 적혔다).
        if (EditorApplication.isPaused)
        {
            EditorApplication.isPaused = false;
            Write("  [일시정지 해제 — 직전 오류로 Error Pause]");
        }

        var popup = UnityEngine.Object.FindFirstObjectByType<UI_DialoguePopup>();
        if (popup != null && popup.isActiveAndEnabled)
        {
            var body    = typeof(UI_DialoguePopup).GetField("bodyText", Inst)?.GetValue(popup) as TMP_Text;
            var speaker = typeof(UI_DialoguePopup).GetField("speakerNameText", Inst)?.GetValue(popup) as TMP_Text;
            string line = $"{speaker?.text} | {body?.text}";
            if (body != null && !string.IsNullOrEmpty(body.text) && line != s_lastDialogue)
            {
                if (s_lastDialogue == null) Capture("dialogue", 0).Forget();
                s_lastDialogue = line;
                Write("대사 " + Trim(line, 200));
            }
        }
        else s_lastDialogue = null;

        var bark = RelicFairy.UI.UI_BossBark.Instance;
        if (bark != null && bark.isActiveAndEnabled)
        {
            var label = typeof(RelicFairy.UI.UI_BossBark).GetField("_label", Inst)?.GetValue(bark) as TMP_Text;
            var who   = typeof(RelicFairy.UI.UI_BossBark).GetField("_speakerLabel", Inst)?.GetValue(bark) as TMP_Text;
            if (label != null && label.gameObject.activeInHierarchy && !string.IsNullOrEmpty(label.text))
            {
                string line = $"{who?.text} | {label.text}";
                if (line != s_lastBark) { s_lastBark = line; Write("말풍선 " + Trim(line, 200)); }
            }
        }
    }

    // ── 찍기 ───────────────────────────────────────────────

    private static async UniTaskVoid Capture(string tag, int delayMs)
    {
        try
        {
            if (delayMs > 0) await UniTask.Delay(delayMs, DelayType.Realtime);
            // 한 프레임에 둘이 겹치면 둘째 캡처가 실패한다 — 차례로(간격 MinShotGap).
            while (s_on && (s_capturing || Time.realtimeSinceStartup - s_lastShot < MinShotGap))
                await UniTask.Delay(120, DelayType.Realtime);
            if (!s_on || !Application.isPlaying) return;
            s_capturing = true;
            var runner = UnityEngine.Object.FindFirstObjectByType<Managers>();
            if (runner == null) { s_capturing = false; return; }
            await UniTask.WaitForEndOfFrame(runner);
            s_lastShot = Time.realtimeSinceStartup;
            s_capturing = false;
            if (!s_on) return;

            var full = ScreenCapture.CaptureScreenshotAsTexture();
            if (full == null) return;
            // Blit이 활성 렌더 타깃을 바꾼다 — 원래 값을 먼저 잡아 두고 되돌린다
            // (안 그러면 게임 뷰가 960×540 타깃에 묶여 다음 캡처가 실패하고, 그 오류가 Error Pause로 게임을 멈췄다).
            var prev = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(ShotW, ShotH, 0);
            Graphics.Blit(full, rt);
            RenderTexture.active = rt;
            var small = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false);
            small.ReadPixels(new Rect(0, 0, ShotW, ShotH), 0, 0);
            small.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.Destroy(full);

            string name = $"{s_seq++:0000}_{Safe(tag)}.jpg";
            File.WriteAllBytes(Path.Combine(s_dir, name), small.EncodeToJPG(82));
            UnityEngine.Object.Destroy(small);
            Write("  [찍음] " + name);
        }
        catch (Exception e) { Write("  [찍기 실패] " + tag + " — " + e.Message); }
    }

    /// <summary>「[RunAuto] t=12.3 방 진입 1:0」 → 「방 진입 1:0」.</summary>
    private static string AfterTime(string s)
    {
        int i = s.IndexOf(" t=", StringComparison.Ordinal);
        if (i < 0) return s.Substring(Mathf.Min(s.Length, 10)).Trim();
        int sp = s.IndexOf(' ', i + 3);
        return sp < 0 ? s : s.Substring(sp + 1).Trim();
    }

    private static string Tail(string s, string after)
    {
        int i = s.IndexOf(after, StringComparison.Ordinal);
        return i < 0 ? s : s.Substring(i + after.Length).Trim();
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";

    private static string Safe(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '+' ? c : '_');
        string r = sb.ToString();
        return r.Length > 48 ? r.Substring(0, 48) : r;
    }
}
