#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-01 서비스 방 둘러보기 — 상점 · 재련소 · 정제소 방(챕터 1~4 룸 풀)을 하나씩 실제로 지어 들어가
/// NPC 무대(정면 · 위) · 플레이어가 다가간 게임 화면을 찍고, NPC가 카운터를 보는지 · 소품 자리를 기록한다.
/// 사용자 「콘텐츠 방마다 NPC가 동일 — 컨셉에 맞게 NPC와 구조물, CSV와 함께 확인」.
/// 로컬 룸 풀(Docs CSV)을 읽는다(끝나면 원래 설정으로). 결과: Temp/service_tour/&lt;태그&gt;/ · report.txt.
/// ⚠️ 테스트 허브 런에서만 — 지금 런의 방을 바꿔 가며 들어간다.
/// </summary>
public static class ServiceRoomTourProbeEditor
{
    private const BindingFlags NonPub = BindingFlags.Instance | BindingFlags.NonPublic;
    // 인수 파일(있으면): 1줄 = pool_key 쉼표 목록(비우면 16방 전부) · 2줄 = 진행 방향 0=북(회전 없음) 1=동 2=남 3=서
    private const string ArgsPath = "Temp/service_tour_args.txt";
    private const int StagePx = 960;

    [MenuItem("RelicFairy/Debug/10-01 서비스 방 둘러보기 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            Debug.LogWarning("[ServiceTour] 런에 들어간 뒤 실행해야 한다.");
            return;
        }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string tag = DateTime.Now.ToString("HHmmss");
        string dir = Path.Combine("Temp", "service_tour", tag);
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder($"서비스 방 둘러보기 {tag}\n");
        bool prevLocal = EditorPrefs.GetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, false);
        EditorPrefs.SetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, true);
        Invoke("HideTestHubGui");
        try
        {
            if (!await EnsureProcRunAsync()) { sb.AppendLine("절차 방 진행이 시작되지 않음(대기방 게이트)"); return; }
            var flow  = RunFlowController.Active;
            var enter = typeof(RunFlowController).GetMethod("EnterRoomAsync", NonPub);
            var cur   = typeof(RunFlowController).GetField("_current", NonPub);
            if (enter == null || cur == null) { sb.AppendLine("EnterRoomAsync/_current 없음"); return; }

            string[] args = File.Exists(ArgsPath) ? File.ReadAllLines(ArgsPath) : Array.Empty<string>();
            string filter = args.Length > 0 ? args[0].Trim() : "";
            var want = new HashSet<string>(filter.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            int heading = args.Length > 1 && int.TryParse(args[1].Trim(), out var h) ? Mathf.Clamp(h, 0, 3) : 0;
            sb.AppendLine($"필터 [{filter}] · 진행 방향 {(DoorEdge)heading}");

            for (int ch = 1; ch <= 4; ch++)
            {
                var pool = await Managers.ZoneLayout.LoadPoolAsync($"CHAPTER_{ch}_ROOM_POOL");
                if (pool == null) continue;
                foreach (var e in pool)
                {
                    if (!TryKind(e.category, out var kind)) continue;
                    if (want.Count > 0 && !want.Contains(e.pool_key)) continue;

                    var plan = new DoorPlan { kind = kind, entry = e };
                    var task = (UniTask)enter.Invoke(flow, new object[] { plan, (DoorEdge)heading, CancellationToken.None, 0 });
                    await UniTask.WhenAny(task, UniTask.Delay(20000, ignoreTimeScale: true));
                    await SkipDialogueAsync(4f);
                    await UniTask.Delay(1500, ignoreTimeScale: true);

                    var result = cur.GetValue(flow);
                    var room   = result?.GetType().GetField("roomGO")?.GetValue(result) as GameObject;
                    await RecordRoomAsync(sb, dir, e.pool_key, room);
                }
            }
        }
        catch (Exception ex) { sb.AppendLine("예외: " + ex); }
        finally
        {
            EditorPrefs.SetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, prevLocal);
            Invoke("RestoreTestHubGui");
            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString());
            Debug.Log($"[ServiceTour] 끝 → {dir}/report.txt");
        }
    }

    /// <summary>대기방이면 시작 게이트를 열고 지나간다 — 절차 진행 컨트롤러는 그 게이트를 지나야 생긴다(자동 실측과 같은 길).</summary>
    private static async UniTask<bool> EnsureProcRunAsync()
    {
        float end = Time.realtimeSinceStartup + 45f;
        float nextTry = 0f;
        while (RunFlowController.Active == null && Time.realtimeSinceStartup < end)
        {
            await SkipDialogueAsync(0.5f);
            if (Time.realtimeSinceStartup < nextTry) continue;
            nextTry = Time.realtimeSinceStartup + 8f;
            foreach (var gate in UnityEngine.Object.FindObjectsByType<StartRoomGate>(FindObjectsSortMode.None))
            {
                if ((int)typeof(StartRoomGate).GetField("_fromZoneIndex", NonPub).GetValue(gate) != -1) continue;
                if (!(bool)typeof(StartRoomGate).GetField("_covenantDone", NonPub).GetValue(gate))
                    typeof(StartRoomGate).GetMethod("HandleCovenantAssembled", NonPub).Invoke(gate, null);
                await UniTask.Delay(2500, ignoreTimeScale: true);   // 열림 연출
                var player = GameRunBootstrapper.Instance?.Run?.Player;
                if (player != null && gate.TryGetComponent<Collider>(out var col))
                {
                    Vector3 p = new Vector3(col.bounds.center.x, col.bounds.min.y + 0.3f, col.bounds.center.z);
                    player.transform.position = p;
                    if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = p; rb.linearVelocity = Vector3.zero; }
                }
                break;
            }
        }
        await UniTask.Delay(3000, ignoreTimeScale: true);   // 첫 방 입장 연출
        await SkipDialogueAsync(3f);
        return RunFlowController.Active != null;
    }

    private static bool TryKind(string category, out RoomPlanKind kind)
    {
        kind = RoomPlanKind.Shop;
        switch (category)
        {
            case "Shop":     kind = RoomPlanKind.Shop;     return true;
            case "Crucible": kind = RoomPlanKind.Crucible; return true;
            case "Refinery": kind = RoomPlanKind.Refinery; return true;
            default: return false;
        }
    }

    private static async UniTask RecordRoomAsync(StringBuilder sb, string dir, string key, GameObject room)
    {
        sb.AppendLine($"== {key}");
        if (room == null) { sb.AppendLine("  방 없음"); return; }

        var npc = room.GetComponentInChildren<ShopNpcInteraction>(true);
        if (npc == null) { sb.AppendLine("  NPC 없음"); return; }
        Transform npcT = npc.transform;
        Vector3 fwd = npcT.forward; fwd.y = 0f; fwd.Normalize();

        Transform counter = null;
        foreach (var t in room.GetComponentsInChildren<Transform>(true))
            if (t.name == "ShopCounter" || t.name == "CrucibleCounter" || t.name == "RefineryCounter") { counter = t; break; }

        sb.AppendLine($"  NPC {npcT.name} 위치 {npcT.position:F1} · 바라봄 {npcT.eulerAngles.y:0}°");
        if (counter != null)
        {
            Vector3 to = counter.position - npcT.position; to.y = 0f;
            float ang = Vector3.Angle(fwd, to);
            sb.AppendLine($"  카운터 {counter.position:F1} · NPC→카운터 {to.magnitude:0.0} m · 시선과 각 {ang:0}°{(ang > 60f ? " ★카운터를 안 봄" : "")}");
        }
        else sb.AppendLine("  카운터 없음");

        foreach (var a in room.GetComponentsInChildren<ServiceDecorAnchor>(true))
        {
            Vector3 d = a.transform.position - npcT.position;
            sb.AppendLine($"  앵커 {a.Kind} 상대 ({d.x:0.0}, {d.z:0.0})");
        }
        // 앵커 자리에 놓인 소품(방 직속 자식 중 프리팹 이름) — Place가 방 밑에 둔다
        for (int i = 0; i < room.transform.childCount; i++)
        {
            var c = room.transform.GetChild(i);
            if (!c.name.StartsWith("SM_") && !c.name.Contains("Magic circle") && !c.name.EndsWith("Counter")) continue;
            Vector3 d = c.position - npcT.position;
            sb.AppendLine($"  소품 {c.name} 상대 ({d.x:0.0}, {d.z:0.0})");
        }

        Vector3 look = npcT.position + Vector3.up * 0.2f;
        Render(Path.Combine(dir, key + "_stage.png"), look + fwd * 5.2f + Vector3.up * 4.2f, look, 46f, StagePx, StagePx * 9 / 16);
        Render(Path.Combine(dir, key + "_top.png"), look + Vector3.up * 16f + fwd * 0.01f, look, 60f, 720, 720);

        // 플레이어를 카운터 앞으로 — 다가갔을 때의 게임 화면(알아보는 반응)
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player != null)
        {
            Vector3 front = (counter != null ? counter.position : npcT.position) + fwd * 2.4f;
            if (Physics.Raycast(front + Vector3.up * 3f, Vector3.down, out var hit, 8f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore))
                front.y = hit.point.y + 0.2f;
            player.transform.position = front;
            if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = front; rb.linearVelocity = Vector3.zero; }
            await Shot($"Tour_{key}_near0", 350);
            await Shot($"Tour_{key}_near1", 1200);
            sb.AppendLine($"  다가간 뒤 NPC 바라봄 {npcT.eulerAngles.y:0}°");

            // 반응 몸짓 — 말 걸기 · 거래하고 나감 · 안 하고 나감(패널 없이 반응만 불러 무대 카메라로 찍는다)
            if (npc.TryGetComponent<ServiceNpcReactor>(out var reactor))
            {
                var anim = npc.GetComponentInChildren<Animator>();
                string State() => anim == null ? "-" : StateName(anim);
                reactor.BeginTalk();
                await UniTask.Delay(500, ignoreTimeScale: true);
                Render(Path.Combine(dir, key + "_r1_talk.png"), look + fwd * 4.2f + Vector3.up * 2.6f, look + Vector3.up * 0.6f, 40f, 640, 480);
                sb.AppendLine($"  반응 Talk → {State()}");
                reactor.EndTalk(true);
                await UniTask.Delay(450, ignoreTimeScale: true);
                Render(Path.Combine(dir, key + "_r2_thanks.png"), look + fwd * 4.2f + Vector3.up * 2.6f, look + Vector3.up * 0.6f, 40f, 640, 480);
                sb.AppendLine($"  반응 Thanks → {State()}");
                await UniTask.Delay(2200, ignoreTimeScale: true);
                reactor.BeginTalk();
                reactor.EndTalk(false);
                await UniTask.Delay(450, ignoreTimeScale: true);
                Render(Path.Combine(dir, key + "_r3_shrug.png"), look + fwd * 4.2f + Vector3.up * 2.6f, look + Vector3.up * 0.6f, 40f, 640, 480);
                sb.AppendLine($"  반응 Shrug → {State()}");
                // 멀어지면 제 일로 — 대장장이 망치질 · 버섯 두리번
                Vector3 far = front + fwd * 9f;
                player.transform.position = far;
                if (player.TryGetComponent<Rigidbody>(out var rb2)) { rb2.position = far; rb2.linearVelocity = Vector3.zero; }
                for (int i = 0; i < 4; i++)
                {
                    await UniTask.Delay(900, ignoreTimeScale: true);
                    Render(Path.Combine(dir, key + $"_r4_alone{i}.png"), look + fwd * 4.2f + Vector3.up * 2.6f, look + Vector3.up * 0.6f, 40f, 640, 480);
                    sb.AppendLine($"  혼자 {i} → {State()} · 가까움={reactor.IsNear}");
                }
            }
        }
    }

    private static string StateName(Animator a)
    {
        var info = a.GetCurrentAnimatorStateInfo(0);
        foreach (var n in new[] { "Idle", "Notice", "Talk", "Thanks", "Shrug", "Farewell", "Work" })
            if (info.shortNameHash == Animator.StringToHash(n)) return n + (a.IsInTransition(0) ? "(전이 중)" : "");
        return info.shortNameHash.ToString();
    }

    private static async UniTask SkipDialogueAsync(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            if (UnityEngine.Object.FindFirstObjectByType<UI_DialoguePopup>() != null) TestHubDebugMenu.AdvanceDialogue();
            await UniTask.Delay(250, ignoreTimeScale: true);
        }
    }

    private static void Render(string path, Vector3 pos, Vector3 target, float fov, int w, int h)
    {
        var go  = new GameObject("~ServiceTourCam");
        var cam = go.AddComponent<Camera>();
        var rt  = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        try
        {
            go.transform.position = pos;
            go.transform.LookAt(target);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.cullingMask = ~LayerMask.GetMask("UI");
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
        finally
        {
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    private static UniTask Shot(string name, int settleMs)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, settleMs }) : UniTask.CompletedTask;
    }

    private static void Invoke(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
}
#endif
