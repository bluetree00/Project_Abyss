#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using RelicFairy.Monster;
using RelicFairy.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 도구 · 플레이 중] 10-11 기사 위험 공격 알림 — 큰 기술 이름 · 멀린 요령 자막이 뜨는지, 판정 직전 위험 칸이 붉어지는지.
/// 보스 말풍선 글자가 바뀔 때마다 기록하고(알림 문구면 사진), 붉은 칸이 생기는 순간(0 → N)마다 사진을 찍는다.
/// 결과: Logs/boss_fight_sim/dk_alert_시각.txt · _armN.png · _barkN.png(플레이를 멈출 때 쓴다).
/// </summary>
internal static class DKAlertProbe
{
    private const BindingFlags StaticPrivate   = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int MaxArmShots  = 12;
    private const int MaxBarkShots = 8;

    private static readonly FieldInfo s_armedField = typeof(DKGridPatternHelper).GetField("s_armed", StaticPrivate);
    private static readonly FieldInfo s_labelField = typeof(UI_BossBark).GetField("_label", InstancePrivate);
    private static readonly string[] AlertWords = { "검의 심판", "무너지는 원탁", "세 번의 십자", "방패 안이", "옆 고리로", "대각선으로" };
    private static readonly StringBuilder s_log = new();

    private static bool   s_on;
    private static string s_stem;
    private static float  s_t0;
    private static int    s_lastArmed, s_armShots, s_barkShots, s_armEvents, s_alertLines;
    private static string s_lastText;

    [MenuItem("RelicFairy/Boss/DeathKnight/Alert Probe Start (Play)")]
    private static void StartProbe()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[DKAlertProbe] 플레이 중에만"); return; }
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim");
        Directory.CreateDirectory(dir);
        s_stem = Path.Combine(dir, $"dk_alert_{System.DateTime.Now:MMdd_HHmmss}");
        s_log.Clear();
        s_lastArmed = s_armShots = s_barkShots = s_armEvents = s_alertLines = 0;
        s_lastText = null;
        s_t0 = Time.time;
        s_on = true;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        EditorApplication.playModeStateChanged += OnPlayMode;
        Debug.Log($"[DKAlertProbe] 시작 — {s_stem}");
    }

    private static void OnPlayMode(PlayModeStateChange s)
    {
        if (s != PlayModeStateChange.ExitingPlayMode || !s_on) return;
        s_on = false;
        EditorApplication.update -= Tick;
        File.WriteAllText(s_stem + ".txt",
            $"붉은 칸 사건 {s_armEvents}회 · 알림 문구 {s_alertLines}줄\n" + s_log, Encoding.UTF8);
        Debug.Log($"[DKAlertProbe] {s_stem}.txt");
    }

    private static void Tick()
    {
        if (!s_on || !EditorApplication.isPlaying) return;
        float t = Time.time - s_t0;

        int armed = (s_armedField?.GetValue(null) as System.Collections.ICollection)?.Count ?? 0;
        if (armed > 0 && s_lastArmed == 0)
        {
            s_armEvents++;
            s_log.AppendLine($"[{t,6:0.0}] 붉은 칸 {armed}");
            if (s_armShots < MaxArmShots) Shot($"arm{++s_armShots}");
        }
        s_lastArmed = armed;

        var bark  = UI_BossBark.Instance;
        var label = bark != null ? s_labelField?.GetValue(bark) as TMP_Text : null;
        string text = label != null && label.gameObject.activeInHierarchy ? label.text : null;
        if (!string.IsNullOrEmpty(text) && text != s_lastText)
        {
            bool alert = IsAlert(text);
            if (alert) s_alertLines++;
            s_log.AppendLine($"[{t,6:0.0}] 말풍선{(alert ? "(알림)" : "")} 「{text}」");
            if (alert && s_barkShots < MaxBarkShots) Shot($"bark{++s_barkShots}");
        }
        s_lastText = text;
    }

    private static bool IsAlert(string text)
    {
        foreach (var w in AlertWords)
            if (text.Contains(w)) return true;
        return false;
    }

    private static void Shot(string tag) => ScreenCapture.CaptureScreenshot($"{s_stem}_{tag}.png");
}
#endif
