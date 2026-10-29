#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-29 K 「던전 입구 · 통로 천장이 안 흐려진다」 고침 실측 — 자동 진행하며 가림(CameraOcclusionFader) 목록을 본다.
/// ① 대기방(던전 입구): 입구 게이트 복도 천장(Corridor_C_) · 문틀이 목록에 들었나 ② 챕터 둘째 전투방부터(quickEntry): 출구 · 입구 잠금 문틀이 남았나 ·
/// 방 안 통로 천장(꺼져 있던 것 포함)이 다 들었나. 결과 Temp/fix1029_k.txt · 로그 「[Fix29K] 끝」.
/// </summary>
public static class Fix1029CeilingProbeEditor
{
    private const string Tag = "[Fix29K]";
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly string[] CeilNames = { "Ceiling_", "Corridor_C_", "Chamber_C_", "Stair_C_" };

    private static double s_until, s_waitAt = -1;
    private static int s_stage, s_rooms;
    private static StringBuilder s_sb;
    private static Object s_lastRoom;

    [MenuItem("RelicFairy/Debug/10-29 K 천장 걷힘 실측 — 자동 진행 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에"); return; }
        s_sb = new StringBuilder("10-29 K 천장 걷힘\n");
        s_stage = 0; s_rooms = 0; s_waitAt = -1; s_lastRoom = null;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
        s_until = EditorApplication.timeSinceStartup + 240.0;
        EditorApplication.update -= Watch;
        EditorApplication.update += Watch;
    }

    private static void Watch()
    {
        double now = EditorApplication.timeSinceStartup;
        if (!EditorApplication.isPlaying || now > s_until) { Finish("시간 초과"); return; }

        if (s_stage == 0)
        {
            var gate = Object.FindFirstObjectByType<StartRoomGate>();
            if (gate == null || GameRunBootstrapper.Instance == null) return;
            if (s_waitAt < 0) { s_waitAt = now + 2.5; return; }
            if (now < s_waitAt) return;
            Report("① 대기방(던전 입구)", gate.transform.parent != null ? gate.transform.parent : gate.transform);
            s_stage = 1;
            return;
        }

        var flow = RunFlowController.Active;
        if (flow == null) return;
        var cur = typeof(RunFlowController).GetField("_current", Any)?.GetValue(flow);
        var roomGO = cur?.GetType().GetField("roomGO")?.GetValue(cur) as GameObject;
        bool cleared = (bool)(typeof(RunFlowController).GetField("_currentRoomCleared", Any)?.GetValue(flow) ?? false);
        if (roomGO == null || !cleared || roomGO == s_lastRoom) { s_waitAt = -1; return; }
        // 즉사 자동 진행은 방 모으기(RoomRevealed)보다 먼저 깰 수 있다 — 깬 뒤 2.5초를 기다려 잰다
        if (s_waitAt < 0) { s_waitAt = now + 2.5; return; }
        if (now < s_waitAt) return;
        s_waitAt = -1;
        s_lastRoom = roomGO;
        s_rooms++;
        Report($"② 방 {s_rooms} {roomGO.name}", roomGO.transform, flow.transform);
        if (s_rooms >= 3) Finish("3방");
    }

    private static void Report(string title, Transform area, Transform gatesRoot = null)
    {
        var fader = typeof(CameraOcclusionFader).GetField("s_active", Any)?.GetValue(null);
        var ceiling = (typeof(CameraOcclusionFader).GetField("_ceiling", Any)?.GetValue(fader) as IList)?.Cast<Object>().ToList();
        var overhead = (typeof(CameraOcclusionFader).GetField("_overhead", Any)?.GetValue(fader) as IList)?.Cast<Object>().ToList();
        if (ceiling == null || overhead == null) { s_sb.AppendLine($"{title}: 가림 없음"); return; }

        var ceilRs = area.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.gameObject.activeInHierarchy && HasCeilName(r.transform, area)).ToList();
        int ceilIn = ceilRs.Count(r => ceiling.Contains(r));
        int ceilOff = ceilRs.Count(r => !r.enabled);
        var frames = (gatesRoot != null ? gatesRoot : area).GetComponentsInChildren<Transform>(true).Where(t => t.name == "GateFrame").ToList();
        var frameRs = frames.SelectMany(f => f.GetComponentsInChildren<MeshRenderer>(true)).ToList();
        int frameIn = frameRs.Count(r => overhead.Contains(r) || ceiling.Contains(r));
        s_sb.AppendLine($"{title}: 천장 렌더러 {ceilRs.Count}개 중 목록 {ceilIn}(그때 꺼진 것 {ceilOff}) · 문틀 {frames.Count}개 렌더러 {frameRs.Count} 중 목록 {frameIn} · 가림 목록 천장 {ceiling.Count} · 떠 있는 것 {overhead.Count}");
    }

    private static bool HasCeilName(Transform t, Transform root)
    {
        for (; t != null && t != root; t = t.parent)
            foreach (var p in CeilNames) if (t.name.StartsWith(p, System.StringComparison.Ordinal)) return true;
        return false;
    }

    private static void Finish(string why)
    {
        EditorApplication.update -= Watch;
        if (EditorApplication.isPlaying) EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
        s_sb.AppendLine($"끝({why})");
        File.WriteAllText("Temp/fix1029_k.txt", s_sb.ToString());
        Debug.Log($"{Tag} 끝\n{s_sb}");
    }
}
#endif
