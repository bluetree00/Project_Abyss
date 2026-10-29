#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-29 설계 §5 거점 「기록의 문」 확인 — 오버라이드 「Ended (Nightmare Off)」로 거점(Build/BaseCamp)에서 플레이.
/// 드러남 기다림 → 멀리서 한 장 · 곁(안내) 한 장 → [F] 대신 리플렉션으로 열기:
///   ① 악몽 안 깸 = 바로 해방기 방 → 돌아가는 문 → 제자리 · 안내 다시
///   ② 악몽 깸(메모리에만 표시 · 끝나면 되돌림) = 두 장 창 → 바깥(안 들어감) → 다시 열어 「악몽 기록」 → 악몽 방 → 제자리
/// 사진 Temp/record_door/ · 로그 「[RecordDoorProbe] 끝」.
/// </summary>
public static class RecordDoorProbeEditor
{
    private const string Dir = "Temp/record_door";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-29 §5 기록의 문 확인 (거점 · Ended, Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[RecordDoorProbe] 거점에서 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        Directory.CreateDirectory(Dir);
        var sb = new StringBuilder("기록의 문\n");
        string nmKey = StoryProgress.Rec.SeenPrefix + "NightmareClear";
        bool flagged = false;
        try
        {
            await UniTask.Delay(3000, DelayType.Realtime);
            for (float until = Time.realtimeSinceStartup + 60f; Time.realtimeSinceStartup < until && BaseCampBootstrapper.Instance?.Player == null;)
                await UniTask.Delay(300, DelayType.Realtime);
            await UniTask.Delay(4000, DelayType.Realtime);
            float calm = -1f;
            for (float until = Time.realtimeSinceStartup + 60f; Time.realtimeSinceStartup < until;)
            {
                bool talking = UnityEngine.Object.FindFirstObjectByType<UI_DialoguePopup>() != null;
                if (talking) { TestHubDebugMenu.AdvanceDialogue(); calm = -1f; }
                else if (Time.timeScale > 0f) { if (calm < 0f) calm = Time.realtimeSinceStartup; else if (Time.realtimeSinceStartup - calm > 2f) break; }
                await UniTask.Delay(300, DelayType.Realtime);
            }
            var player = BaseCampBootstrapper.Instance?.Player ?? UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            var door = UnityEngine.Object.FindFirstObjectByType<BaseCampRecordDoor>(FindObjectsInactive.Include);
            sb.AppendLine($"엔딩 {StoryProgress.HasEnded} · 악몽 깸 {RankingFlow.HasClearedNightmare} · 문 {(door != null)} · 플레이어 {(player != null)}");
            if (door == null || player == null) return;

            var readyField = typeof(BaseCampRecordDoor).GetField("_ready", Inst);
            var busyField  = typeof(BaseCampRecordDoor).GetField("_busy", Inst);
            for (float until = Time.realtimeSinceStartup + 30f; Time.realtimeSinceStartup < until && !(bool)readyField.GetValue(door);)
                await UniTask.Delay(250, DelayType.Realtime);
            var gate = BaseCampFxDirector.Instance != null ? (Transform)typeof(BaseCampFxDirector).GetField("gateFront", Inst).GetValue(BaseCampFxDirector.Instance) : null;
            sb.AppendLine($"드러남 {readyField.GetValue(door)} · 자리 {door.transform.position} · 성문 앞에서 {(gate != null ? Vector3.Distance(Flat(gate.position), Flat(door.transform.position)) : -1f):0.0} m");

            Vector3 fwd = door.transform.forward;   // 받침은 성문 반대(광장 쪽)를 본다
            Place(player, door.transform.position + fwd * 5.5f, -fwd);
            await UniTask.Delay(1500, DelayType.Realtime);
            sb.AppendLine($"멀리 — 이름표 {Label(door)} · 안내 {Prompt(door)}");
            ScreenCapture.CaptureScreenshot($"{Dir}/0_far.png"); await UniTask.Delay(500, DelayType.Realtime);

            Place(player, door.transform.position + fwd * 1.8f, -fwd);
            await UniTask.Delay(1200, DelayType.Realtime);
            Vector3 home = player.transform.position;
            sb.AppendLine($"곁 — 이름표 {Label(door)} · 안내 {Prompt(door)}");
            ScreenCapture.CaptureScreenshot($"{Dir}/1_near.png"); await UniTask.Delay(500, DelayType.Realtime);

            // ① 악몽 안 깸 — 바로 해방기 방
            if (!RankingFlow.HasClearedNightmare)
            {
                Open(door, player);
                await UniTask.Delay(2600, DelayType.Realtime);
                sb.AppendLine($"[lib] 창 {RankingRoomChoice.IsOpen} · 들름 {RankingHall.IsVisiting} · 홀 {HallTitle()} · 비석 창 {RankingBoardOverlay.IsOpen}");
                ScreenCapture.CaptureScreenshot($"{Dir}/2_lib_hall.png"); await UniTask.Delay(500, DelayType.Realtime);
                await ReturnAsync(player);
                sb.AppendLine($"[lib] 돌아옴 — 들름 {RankingHall.IsVisiting} · 제자리까지 {Vector3.Distance(player.transform.position, home):0.0} m · 바쁨 {busyField.GetValue(door)} · 안내 {Prompt(door)}");
                ScreenCapture.CaptureScreenshot($"{Dir}/3_lib_back.png"); await UniTask.Delay(500, DelayType.Realtime);
            }

            // ② 악몽 깸 — 메모리에만 표시(저장 안 함) · 끝나면 되돌린다
            var data = BackendGameData.Instance?.Data;
            if (!RankingFlow.HasClearedNightmare && data != null) { data.SetRecordMax(nmKey, 1); flagged = true; }
            sb.AppendLine($"악몽 깸 표시 {RankingFlow.HasClearedNightmare}");

            Open(door, player);
            await UniTask.Delay(700, DelayType.Realtime);
            sb.AppendLine($"[pick] 창 {RankingRoomChoice.IsOpen} · 들름 {RankingHall.IsVisiting}");
            ScreenCapture.CaptureScreenshot($"{Dir}/4_choice.png"); await UniTask.Delay(500, DelayType.Realtime);
            Click("@NightmareGateChoice", "Veil"); await UniTask.Delay(900, DelayType.Realtime);
            sb.AppendLine($"[cancel] 창 {RankingRoomChoice.IsOpen} · 들름 {RankingHall.IsVisiting} · 바쁨 {busyField.GetValue(door)} · 시간 {Time.timeScale:0.##}");

            Open(door, player);
            await UniTask.Delay(700, DelayType.Realtime);
            Click("@NightmareGateChoice", "Card_악몽 기록");
            await UniTask.Delay(2800, DelayType.Realtime);
            sb.AppendLine($"[nm] 창 {RankingRoomChoice.IsOpen} · 들름 {RankingHall.IsVisiting} · 홀 {HallTitle()} · 비석 창 {RankingBoardOverlay.IsOpen}");
            ScreenCapture.CaptureScreenshot($"{Dir}/5_nm_hall.png"); await UniTask.Delay(500, DelayType.Realtime);
            await ReturnAsync(player);
            sb.AppendLine($"[nm] 돌아옴 — 들름 {RankingHall.IsVisiting} · 제자리까지 {Vector3.Distance(player.transform.position, home):0.0} m · 바쁨 {busyField.GetValue(door)} · 안내 {Prompt(door)} · 시간 {Time.timeScale:0.##}");
            ScreenCapture.CaptureScreenshot($"{Dir}/6_nm_back.png"); await UniTask.Delay(500, DelayType.Realtime);
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (flagged) BackendGameData.Instance?.Data?.AddRecord(nmKey, -1);
            sb.AppendLine($"되돌림 — 악몽 깸 {RankingFlow.HasClearedNightmare}");
            File.WriteAllText($"{Dir}/view.txt", sb.ToString(), Encoding.UTF8);
            Debug.Log($"[RecordDoorProbe] 끝\n{sb}");
        }
    }

    private static void Open(BaseCampRecordDoor door, PlayerController player)
        => typeof(BaseCampRecordDoor).GetMethod("VisitAsync", Inst).Invoke(door, new object[] { player, door.GetCancellationTokenOnDestroy() });

    private static async UniTask ReturnAsync(PlayerController player)
    {
        Click("@RankingBoard", "Veil"); await UniTask.Delay(1000, DelayType.Realtime);
        var hall = UnityEngine.Object.FindFirstObjectByType<RankingHall>();
        var trig = hall != null ? hall.transform.Find("ReturnDoor/Trigger") : null;
        if (trig != null) Place(player, trig.position, player.transform.forward);
        for (float until = Time.realtimeSinceStartup + 8f; Time.realtimeSinceStartup < until && RankingHall.IsVisiting;)
            await UniTask.Delay(200, DelayType.Realtime);
        await UniTask.Delay(1200, DelayType.Realtime);
    }

    private static string HallTitle()
    {
        var hall = UnityEngine.Object.FindFirstObjectByType<RankingHall>();
        var t = hall != null ? typeof(RankingHall).GetField("_title", Inst).GetValue(hall) as TextMeshPro : null;
        return t != null ? t.text : "-";
    }

    private static bool Label(BaseCampRecordDoor d)
    {
        var l = d.transform.Find("RecordLabel");
        return l != null && l.TryGetComponent<TextMeshPro>(out var t) && t.enabled;
    }

    private static bool Prompt(BaseCampRecordDoor d)
    {
        var p = d.transform.Find("InteractPrompt");
        return p != null && p.gameObject.activeSelf;
    }

    private static void Click(string root, string name)
    {
        var r = GameObject.Find(root);
        if (r == null) { Debug.LogWarning($"[RecordDoorProbe] {root} 없음"); return; }
        foreach (var b in r.GetComponentsInChildren<Button>(true))
            if (b.name == name) { b.onClick.Invoke(); return; }
        Debug.LogWarning($"[RecordDoorProbe] {root}/{name} 버튼 없음");
    }

    private static void Place(PlayerController p, Vector3 at, Vector3 face)
    {
        if (Physics.Raycast(at + Vector3.up * 6f, Vector3.down, out var hit, 20f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore)) at.y = hit.point.y + 0.05f;
        var rot = Quaternion.LookRotation(Flat(face).sqrMagnitude > 0.01f ? Flat(face).normalized : Vector3.forward);
        p.transform.SetPositionAndRotation(at, rot);
        if (p.Rigid != null) { p.Rigid.position = at; p.Rigid.rotation = rot; p.Rigid.linearVelocity = Vector3.zero; }
        GameCameraController.Instance?.HandToGameplayCamera(p.transform, alignHeadingToTarget: true);
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
#endif
