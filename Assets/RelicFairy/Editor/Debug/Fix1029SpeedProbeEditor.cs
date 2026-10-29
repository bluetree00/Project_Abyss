#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-29 F1 4c 몫 실측 — 해방기로 자동 진행하며 ① 싸우는 동안 「빠른 정리」 한 줄이 서나 ② 빠른 방의 룬 첫 장이 희귀 이상(+1)인가 ③ 클리어 알림 줄.
/// 시기 오버라이드(EditorPrefs)를 잠깐 해방기로 두고 끝나면 원래 값으로 되돌린다. 결과 Temp/fix1029_f.txt · Temp/ui_shots/F29F_*.png · 로그 「[Fix29F] 끝」.
/// </summary>
public static class Fix1029SpeedProbeEditor
{
    private const string Tag = "[Fix29F]";
    private const string ShotDir = "Temp/ui_shots";
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private const int RoomsToSee = 4;

    private static double s_until;
    private static int s_prevEra, s_rooms, s_pass, s_total;
    private static float s_maxAlpha;
    private static bool s_hudShot;
    private static ClearRewardTrigger s_seen;
    private static StringBuilder s_sb;

    [MenuItem("RelicFairy/Debug/10-29 F 빠른 정리 실측 — 해방기로 자동 진행 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에"); return; }
        s_prevEra = EditorPrefs.GetInt(StoryProgress.DebugOverridePrefsKey, -1);
        EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, 1);   // 해방기(엔딩 전)
        StoryProgress.RefreshDebugOverride();
        s_sb = new StringBuilder("10-29 F 빠른 정리(F1 4c 몫)\n");
        s_rooms = s_pass = s_total = 0;
        s_maxAlpha = 0f;
        s_hudShot = false;
        s_seen = null;
        Directory.CreateDirectory(ShotDir);
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
        s_until = EditorApplication.timeSinceStartup + 300.0;
        EditorApplication.update -= Watch;
        EditorApplication.update += Watch;
    }

    private static void Check(string what, bool ok, string detail = "")
    {
        s_total++; if (ok) s_pass++;
        s_sb.AppendLine($"CHECK {what}: {ok}{(detail.Length > 0 ? "  — " + detail : "")}");
    }

    private static void Watch()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > s_until) { Finish("시간 초과"); return; }

        // ① 싸우는 동안 한 줄
        var hud = Object.FindFirstObjectByType<RoomSpeedClearHud>();
        var grp = hud != null ? hud.GetComponent<CanvasGroup>() : null;
        if (grp != null && RoomSpeedClear.IsActive && RoomSpeedClear.FightSeconds > 0f)
        {
            s_maxAlpha = Mathf.Max(s_maxAlpha, grp.alpha);
            if (!s_hudShot && grp.alpha > 0.9f)
            {
                s_hudShot = true;
                var txt = hud.GetComponentInChildren<TMP_Text>();
                s_sb.AppendLine($"싸우는 중 한 줄: 「{txt?.text}」 · 알파 {grp.alpha:0.00}");
                ShotAsync("F29F_hud_fight").Forget();
            }
        }

        // ② 보상이 서는 순간 — 빠른 방이면 후보 전부 희귀 이상(일반 + 1)
        var trig = Object.FindFirstObjectByType<ClearRewardTrigger>();
        if (trig == null || trig == s_seen) return;
        s_seen = trig;
        s_rooms++;
        var list = typeof(ClearRewardTrigger).GetField("_rewards", Any)?.GetValue(trig) as IList;
        var rar  = list?.Cast<object>().Select(o => (o.GetType().GetField("Item2")?.GetValue(o) as ItemSO)?.rarity).ToList();
        bool fast = RoomSpeedClear.IsFast;
        s_sb.AppendLine($"방 {s_rooms}: 대상 {RoomSpeedClear.IsActive} · 싸움 {RoomSpeedClear.FightSeconds:0.0} / 정해진 {RoomSpeedClear.LimitSeconds:0.0} 초 · 빠름 {fast} · 후보 {(rar == null ? "-" : string.Join(", ", rar))}");
        if (fast) Check($"② 방 {s_rooms} 빠름 → 첫 장 희귀 이상(일반 + 1)", rar != null && rar.Count > 0 && rar[0] >= ItemRarity.Rare);
        if (fast)
        {
            string line = hud != null ? hud.GetComponentInChildren<TMP_Text>()?.text ?? "" : "";
            Check($"③ 방 {s_rooms} 빠름 → 위 줄에 「첫 룬 한 단계 위」", line.Contains("첫 룬 한 단계 위"), $"「{line}」");
        }
        if (fast && s_rooms <= 2) ShotAsync($"F29F_after_clear_{s_rooms}").Forget();
        if (s_rooms >= RoomsToSee) Finish($"{RoomsToSee}방");
    }

    private static void Finish(string why)
    {
        EditorApplication.update -= Watch;
        if (EditorApplication.isPlaying) EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
        EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, s_prevEra);
        StoryProgress.RefreshDebugOverride();
        Check("① 싸우는 동안 「빠른 정리」 한 줄이 보인다", s_maxAlpha > 0.9f, $"가장 진했던 알파 {s_maxAlpha:0.00}");
        s_sb.AppendLine($"\n끝({why}) · 시기 오버라이드 되돌림 {s_prevEra}\n통과 {s_pass}/{s_total}");
        File.WriteAllText("Temp/fix1029_f.txt", s_sb.ToString());
        Debug.Log($"{Tag} 끝 — {s_pass}/{s_total}");
    }

    private static async UniTask ShotAsync(string name)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null) return;
        File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
        Object.Destroy(tex);
    }
}
#endif
