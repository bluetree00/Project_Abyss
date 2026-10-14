#if UNITY_EDITOR
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 · 플레이 중] 입구 디졸브 문(10-09) — 카메라가 입구 안쪽으로 들어온 뒤에 나타나는지.
/// 테스트 허브에서 런 자동 실측을 걸고, 방에 들어설 때마다 0.5초 · 6초 뒤 입구 문(SealDoor)이 켜졌는지 · 카메라가 방 안인지 적는다.
/// 방 6개를 보면 자동 실측을 멈춘다. 결과 Temp/door_appear_probe.txt · 로그 「[DoorProbe] 끝」.
/// </summary>
public static class DoorAppearProbeEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const int RoomsToWatch = 6;

    private static readonly StringBuilder s_sb = new();
    private static int s_rooms;

    [MenuItem("RelicFairy/Debug/10-09 입구 문 나타남 확인 (테스트 허브, 플레이 중)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[DoorProbe] 플레이 모드에서만."); return; }
        s_sb.Clear().AppendLine("입구 디졸브 문 — 방 진입 뒤 0.5초 · 6초");
        s_rooms = 0;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (!msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        int room = ++s_rooms;
        if (room > RoomsToWatch) return;
        string tag = msg.Substring(msg.IndexOf("방 진입"));
        CheckAsync(room, tag).Forget();
    }

    private static async UniTaskVoid CheckAsync(int room, string tag)
    {
        await UniTask.Delay(500, DelayType.Realtime);
        Note(room, tag, "0.5초");
        await UniTask.Delay(5500, DelayType.Realtime);
        Note(room, tag, "6초");
        if (room < RoomsToWatch) return;

        Application.logMessageReceived -= OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
        System.IO.File.WriteAllText("Temp/door_appear_probe.txt", s_sb.ToString());
        Debug.Log("[DoorProbe] 끝 → Temp/door_appear_probe.txt");
    }

    private static void Note(int room, string tag, string when)
    {
        var rfc  = RunFlowController.Active;
        var lockGo = rfc != null ? typeof(RunFlowController).GetField("_entranceLock", Inst)?.GetValue(rfc) as GameObject : null;
        var door = lockGo != null ? lockGo.transform.Find("SealDoor") : null;
        var cam  = Camera.main;
        string inside = "?";
        if (lockGo != null && cam != null)
        {
            // 방 중심 쪽이 안 — 문에서 방 중심으로 가는 방향에 카메라가 얼마나 들어와 있나(m)
            var room0 = typeof(RunFlowController).GetField("_current", Inst)?.GetValue(rfc);
            var roomGo = room0?.GetType().GetField("roomGO", Inst)?.GetValue(room0) as GameObject;
            if (roomGo != null)
            {
                Vector3 inward = roomGo.transform.position - lockGo.transform.position;
                inward.y = 0f;
                float depth = Vector3.Dot(cam.transform.position - lockGo.transform.position, inward.normalized);
                inside = $"{depth:0.0} m";
            }
        }
        string line = $"방 {room} ({tag}) {when}: 입구 잠금 {(lockGo != null ? "있음" : "없음")} · 문 {(door == null ? "없음" : door.gameObject.activeInHierarchy ? "보임" : "숨김")} · 카메라 입구 안쪽 {inside}";
        s_sb.AppendLine(line);
        Debug.Log("[DoorProbe] " + line);
    }
}
#endif
