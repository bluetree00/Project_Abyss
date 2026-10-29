#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-29 F4 「위험한 문」 4c 몫 실측 — 위험한 문 강제(2a RunSequencer.DebugForceDanger)로 정예 우선 자동 진행.
/// ① 출구 배지에 「▲ 위험한 문」 · 「보상 Epic 이상 · 정예 둘」 ② 그 방에 들어서면 세션 CurrentRoomDanger ③ 보상 후보가 전부 Epic 이상.
/// 결과 Temp/fix1029_h.txt · Temp/ui_shots/F29H_*.png · 로그 「[Fix29H] 끝」.
/// </summary>
public static class Fix1029DangerDoorProbeEditor
{
    private const string Tag = "[Fix29H]";
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    private static double s_until;
    private static bool s_doorSeen, s_roomSeen;
    private static int s_pass, s_total;
    private static StringBuilder s_sb;
    private static ClearRewardTrigger s_seenTrig;
    private static int s_prevEra;

    [MenuItem("RelicFairy/Debug/10-29 F4 위험한 문 실측 — 강제 · 정예 우선 자동 진행 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에"); return; }
        SetForce(true);
        s_prevEra = EditorPrefs.GetInt(StoryProgress.DebugOverridePrefsKey, -1);
        EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, 1);   // 해방기 — 위험한 문은 해방기 · 악몽에만
        StoryProgress.RefreshDebugOverride();
        s_sb = new StringBuilder("10-29 F4 위험한 문\n");
        s_doorSeen = s_roomSeen = false; s_pass = s_total = 0; s_seenTrig = null;
        Directory.CreateDirectory("Temp/ui_shots");
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 — 방 시간표 (정예 우선 · 로컬 방 풀)");
        s_until = EditorApplication.timeSinceStartup + 300.0;
        EditorApplication.update -= Watch;
        EditorApplication.update += Watch;
    }

    private static void SetForce(bool on)
    {
        var f = typeof(RunSequencer).GetField("DebugForceDanger", Any);
        var p = typeof(RunSequencer).GetProperty("DebugForceDanger", Any);
        if (f != null) f.SetValue(null, on); else p?.SetValue(null, on);
    }

    private static void Check(string what, bool ok, string detail = "")
    {
        s_total++; if (ok) s_pass++;
        s_sb.AppendLine($"CHECK {what}: {ok}{(detail.Length > 0 ? "  — " + detail : "")}");
    }

    private static void Watch()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > s_until) { Finish("시간 초과"); return; }

        // ① 출구 배지
        if (!s_doorSeen && ExitCompassHud.Instance != null)
        {
            var entries = typeof(ExitCompassHud).GetField("_entries", Any)?.GetValue(ExitCompassHud.Instance) as IList;
            if (entries != null)
                foreach (var e in entries)
                {
                    var rect = e.GetType().GetField("Rect").GetValue(e) as RectTransform;
                    var texts = rect != null ? rect.GetComponentsInChildren<TMPro.TMP_Text>(true) : null;
                    if (texts == null || !texts.Any(t => t.text.Contains("위험한 문"))) continue;
                    s_doorSeen = true;
                    string sub = string.Join(" / ", texts.Select(t => t.text));
                    Check("① 출구 배지 「▲ 위험한 문」 · 보상 줄", sub.Contains("▲ 위험한 문") && sub.Contains("Epic") && sub.Contains("정예 둘"), sub);
                    Shot("F29H_door").Forget();
                    break;
                }
        }

        // ② 그 방 · ③ 보상
        var run = GameRunBootstrapper.Instance?.Run;
        if (run == null) return;
        bool danger = (bool)(typeof(GameRunSession).GetProperty("CurrentRoomDanger", Any)?.GetValue(run) ?? false);
        if (danger && !s_roomSeen)
        {
            s_roomSeen = true;
            Check("② 위험한 문 방 — 세션 CurrentRoomDanger · 정예방", run.CurrentRoomKind == RoomPlanKind.Elite, $"방 {run.CurrentRoomKind}");
        }
        var trig = Object.FindFirstObjectByType<ClearRewardTrigger>();
        if (s_roomSeen && danger && trig != null && trig != s_seenTrig)
        {
            s_seenTrig = trig;
            var list = typeof(ClearRewardTrigger).GetField("_rewards", Any)?.GetValue(trig) as IList;
            var rar = list?.Cast<object>().Select(o => (o.GetType().GetField("Item2")?.GetValue(o) as ItemSO)?.rarity).ToList();
            Check("③ 보상 후보 전부 Epic 이상", rar != null && rar.Count > 0 && rar.All(r => r >= ItemRarity.Epic), rar == null ? "-" : string.Join(", ", rar));
            Finish("위험한 문 방 보상까지");
        }
    }

    private static async UniTaskVoid Shot(string name)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null) return;
        File.WriteAllBytes(Path.Combine("Temp/ui_shots", name + ".png"), tex.EncodeToPNG());
        Object.Destroy(tex);
    }

    private static void Finish(string why)
    {
        EditorApplication.update -= Watch;
        SetForce(false);
        EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, s_prevEra);
        StoryProgress.RefreshDebugOverride();
        if (EditorApplication.isPlaying) EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
        s_sb.AppendLine($"끝({why}) · 문 봄 {s_doorSeen} · 방 들어감 {s_roomSeen}\n통과 {s_pass}/{s_total}");
        File.WriteAllText("Temp/fix1029_h.txt", s_sb.ToString());
        Debug.Log($"{Tag} 끝 — {s_pass}/{s_total}");
    }
}
#endif
