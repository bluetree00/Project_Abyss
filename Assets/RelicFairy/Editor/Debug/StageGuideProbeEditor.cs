#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 스테이지 표식 실측 — 막힌 경계 결계 띠(<see cref="RoomBoundaryTracer"/> · <see cref="ArcaneEdge"/>)와 적 나침반(<see cref="EnemyCompassHud"/>).
/// 「런 구조 자동 실측」이 방을 넘기는 동안 같이 켜 두고 쓴다(이 메뉴를 <b>먼저</b> 켜고 자동 실측을 시작할 것).
///   · 경계: 방이 지어질 때마다(로그 「막힌 경계 표시」) 위에서 본 그림 + 게임 화면 + 테두리 옆에 선 화면을 남긴다.
///   · 나침반(몬스터): 다음 전투방이 지어지면 자동 실측을 멈추고(몬스터가 살아 있어야 한다), 몬스터에서 가장 먼 테두리로 옮겨 찍는다.
///   · 나침반(보스): 보스방이 지어지면 자동 실측을 멈추고 입구를 밟아 보스를 깨운 뒤, 보스에서 가장 먼 테두리로 옮겨 찍는다.
///   · 출구 라벨: 출구가 열리면(출구 배지가 생기면) 자동 실측을 멈추고, 게이트 앞 2 m · 12 m에서 라벨 크기·알파와 배지 위치를 잰다.
/// 결과: Logs/stage_guide/ (그림 + report.txt). 세이브·씬은 건드리지 않는다.
/// </summary>
public static class StageGuideProbeEditor
{
    private const string Root      = "RelicFairy/Debug/스테이지 표식 실측/";
    private const string AutoStop  = "RelicFairy/Debug/런 구조 자동 실측/중지·기록";
    private const string BuiltLog  = "[GameRunBootstrapper] 막힌 경계 표시 — ";
    private const string OutDir    = "Logs/stage_guide";
    private const int    TopPixels = 1400;
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private enum Mode { Off, Boundary, CompassMonster, CompassBoss, ExitLabel }

    private static Mode   s_mode;
    private static string s_room;          // 지금 다루는 방(GameObject 이름)
    private static int    s_shot;
    private static bool   s_compassArmed;  // 나침반 모드 — 대상 방이 지어져 자동 실측을 멈춘 뒤
    private static double s_deadline, s_nextDialogue;
    private static readonly List<(double at, Action act)> s_steps = new();

    [MenuItem(Root + "경계 — 방마다 캡처 (켜기·끄기)")]
    private static void ToggleBoundary() => Arm(s_mode == Mode.Boundary ? Mode.Off : Mode.Boundary);

    [MenuItem(Root + "나침반 — 다음 전투방에서 멈추고 캡처")]
    private static void ArmCompassMonster() => Arm(Mode.CompassMonster);

    [MenuItem(Root + "나침반 — 보스방에서 멈추고 캡처")]
    private static void ArmCompassBoss() => Arm(Mode.CompassBoss);

    [MenuItem(Root + "출구 라벨 — 출구가 열리면 멈추고 캡처")]
    private static void ArmExitLabel() => Arm(Mode.ExitLabel);

    private static void Arm(Mode mode)
    {
        s_mode = mode;
        s_steps.Clear();
        s_compassArmed = false;
        s_exitCaught = false;
        s_room = null;
        Application.logMessageReceived -= OnLog;
        EditorApplication.update -= Tick;
        if (mode == Mode.Off) { Debug.Log("[StageGuideProbe] 끔"); return; }
        Directory.CreateDirectory(OutDir);
        Application.logMessageReceived += OnLog;
        EditorApplication.update += Tick;
        Debug.Log($"[StageGuideProbe] 켬 — {mode}");
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Log || !message.StartsWith(BuiltLog, StringComparison.Ordinal)) return;
        string name = message.Substring(BuiltLog.Length);
        int cut = name.IndexOf(" 선 ", StringComparison.Ordinal);
        if (cut > 0) name = name.Substring(0, cut);
        var room = GameObject.Find(name);
        if (room == null) return;

        switch (s_mode)
        {
            case Mode.Boundary:
                s_room = name;
                s_steps.Clear();
                After(4.0, () => CaptureBoundary(name));
                break;
            case Mode.CompassMonster:
                if (s_compassArmed || room.GetComponentInChildren<BossSpawner>(true) != null
                                   || room.GetComponentInChildren<MonsterSpawner>(true) == null) break;
                BeginCompass(name);
                break;
            case Mode.CompassBoss:
                if (s_compassArmed || room.GetComponentInChildren<BossSpawner>(true) == null) break;
                BeginCompass(name);
                After(5.0, TestHubDebugMenu.StepIntoBossEntrance);
                After(5.8, TestHubDebugMenu.StepPastBossEntrance);
                break;
        }
    }

    private static void Tick()
    {
        if (!Application.isPlaying) { Arm(Mode.Off); return; }
        double now = EditorApplication.timeSinceStartup;

        for (int i = 0; i < s_steps.Count; i++)
        {
            if (now < s_steps[i].at) continue;
            var act = s_steps[i].act;
            s_steps.RemoveAt(i);
            try { act(); }
            catch (Exception e) { Debug.LogWarning($"[StageGuideProbe] 단계 실패: {e.Message}"); }
            return;
        }

        if (s_mode == Mode.ExitLabel) { TickExitLabel(); return; }
        if (!s_compassArmed) return;
        // 자동 실측을 멈췄으니 대사는 여기서 넘긴다
        if (now >= s_nextDialogue)
        {
            s_nextDialogue = now + 1.0;
            if (UnityEngine.Object.FindFirstObjectByType<UI_DialoguePopup>() != null) TestHubDebugMenu.AdvanceDialogue();
        }
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null) return;
        SetInvincible(player);
        MonsterBase.FindCompassTargets(player.transform.position, out var nearest, out _, out var boss);
        var target = s_mode == Mode.CompassBoss ? boss : nearest;
        if (target == null)
        {
            if (now > s_deadline) { Report($"{s_room} — {s_mode}: 대상이 끝내 안 나왔다"); s_compassArmed = false; }
            return;
        }

        // 대상이 나왔다 — 나온 그대로 한 장, 가장 먼 테두리로 옮겨 한 장
        s_compassArmed = false;
        string tag = s_mode == Mode.CompassBoss ? "boss" : "monster";
        After(s_mode == Mode.CompassBoss ? 4.0 : 1.2, () => Shoot(tag + "_near", target));
        After(s_mode == Mode.CompassBoss ? 4.6 : 1.8, () => MoveToFarthestEdge(target));
        After(s_mode == Mode.CompassBoss ? 6.6 : 3.8, () => Shoot(tag + "_far", target));
        // 카메라 정면 쪽 끝으로 — 대상이 카메라 뒤(화면 아래 밖)에 놓인다
        After(s_mode == Mode.CompassBoss ? 7.2 : 4.4, () => MoveToFarthestEdge(target, alongCamera: true));
        After(s_mode == Mode.CompassBoss ? 9.2 : 6.4, () => Shoot(tag + "_behind", target));
        After(s_mode == Mode.CompassBoss ? 10.0 : 7.2, () => Debug.Log($"[StageGuideProbe] 나침반 끝 — {s_mode}"));
    }

    private static void After(double seconds, Action act) => s_steps.Add((EditorApplication.timeSinceStartup + seconds, act));

    private static void BeginCompass(string name)
    {
        s_room = name;
        s_compassArmed = true;
        s_deadline = EditorApplication.timeSinceStartup + 40.0;
        EditorApplication.ExecuteMenuItem(AutoStop);   // 자동 실측이 몬스터를 바로 잡지 않게
        Report($"{name} — {s_mode}: 자동 실측 멈춤, 대상 기다리는 중");
    }

    // ── 경계 ──

    private static void CaptureBoundary(string name)
    {
        var room = GameObject.Find(name);
        if (room == null) { Report($"{name} — 방이 이미 사라졌다(캡처 생략)"); return; }
        var lines = room.GetComponentsInChildren<LineRenderer>(false);
        var bounds = new Bounds();
        bool any = false;
        float total = 0f;
        int points = 0, edgeCount = 0;
        Vector3 longestMid = Vector3.zero, longestIn = Vector3.zero;
        float longest = 0f;
        var buf = new List<Vector3>();
        foreach (var lr in lines)
        {
            if (lr.transform.parent == null || !lr.transform.parent.name.StartsWith("BoundaryEdge_", StringComparison.Ordinal)) continue;
            edgeCount++;
            buf.Clear();
            for (int i = 0; i < lr.positionCount; i++) buf.Add(lr.GetPosition(i));
            points += buf.Count;
            float len = 0f;
            for (int i = 0; i < buf.Count; i++)
            {
                if (any) bounds.Encapsulate(buf[i]); else { bounds = new Bounds(buf[i], Vector3.zero); any = true; }
                if (i > 0) len += Vector3.Distance(buf[i - 1], buf[i]);
            }
            total += len;
            if (len > longest && buf.Count >= 2)
            {
                longest = len;
                int m = buf.Count / 2;
                Vector3 t = (buf[Mathf.Min(m + 1, buf.Count - 1)] - buf[Mathf.Max(m - 1, 0)]).normalized;
                longestMid = buf[m];
                longestIn = new Vector3(-t.z, 0f, t.x);   // 진행 방향의 왼쪽 = 안쪽
            }
        }
        if (!any) { Report($"{name} — 경계 선 없음"); return; }

        string stem = $"{DateTime.Now:HHmmss}_{name}";
        bounds.Expand(new Vector3(6f, 0f, 6f));
        RenderTop($"{OutDir}/{stem}_top.png", bounds);
        ScreenCapture.CaptureScreenshot($"{OutDir}/{stem}_game.png");
        Report($"{name} — 선 {edgeCount}개 · {total:0.0} m · 점 {points}개 · 범위 {bounds.size.x - 6f:0.0}×{bounds.size.z - 6f:0.0} m");

        // 테두리 옆에 서서 한 장 — 가장 긴 선의 가운데에서 안쪽 2.5 m
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null) return;
        After(0.5, () => { SetInvincible(player); MovePlayer(player, longestMid + longestIn * 2.5f); });
        After(2.0, () => ScreenCapture.CaptureScreenshot($"{OutDir}/{stem}_edge.png"));
    }

    private static void RenderTop(string path, Bounds b)
    {
        float size = Mathf.Max(b.size.x, b.size.z);
        var go = new GameObject("~StageGuideTopCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        var rt = new RenderTexture(TopPixels, TopPixels, 24);
        bool fog = RenderSettings.fog;
        try
        {
            go.transform.SetPositionAndRotation(new Vector3(b.center.x, b.center.y + 80f, b.center.z), Quaternion.Euler(90f, 0f, 0f));
            cam.orthographic = true;
            cam.orthographicSize = size * 0.5f;
            cam.aspect = 1f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 200f;
            cam.cullingMask = ~LayerMask.GetMask("UI");
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f, 1f);
            cam.targetTexture = rt;
            RenderSettings.fog = false;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(TopPixels, TopPixels, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, TopPixels, TopPixels), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
        finally
        {
            RenderSettings.fog = fog;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    // ── 나침반 ──

    /// <summary>대상에서 가장 먼 테두리(<paramref name="alongCamera"/>면 대상에서 카메라 정면 쪽으로 가장 먼 테두리)의 안쪽 4 m로.</summary>
    private static void MoveToFarthestEdge(MonsterBase target, bool alongCamera = false)
    {
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        var room = s_room != null ? GameObject.Find(s_room) : null;
        if (player == null || room == null || target == null) return;

        Vector3 center = Vector3.zero, best = player.transform.position;
        int count = 0;
        float bestDist = float.MinValue;
        Vector3 camFwd = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
        camFwd.y = 0f;
        camFwd.Normalize();
        foreach (var lr in room.GetComponentsInChildren<LineRenderer>(false))
        {
            if (lr.transform.parent == null || !lr.transform.parent.name.StartsWith("BoundaryEdge_", StringComparison.Ordinal)) continue;
            for (int i = 0; i < lr.positionCount; i++)
            {
                Vector3 p = lr.GetPosition(i);
                center += p; count++;
                float d = alongCamera ? Vector3.Dot(p - target.transform.position, camFwd) : (p - target.transform.position).sqrMagnitude;
                if (d > bestDist) { bestDist = d; best = p; }
            }
        }
        if (count == 0) { Report($"{s_room} — 경계 선이 없어 옮기지 못했다"); return; }
        center /= count;
        Vector3 inward = center - best; inward.y = 0f;
        MovePlayer(player, best + inward.normalized * 4f);   // 벽 틈(구석 홈)에 박히면 카메라가 벽에 가린다 — 넉넉히 안쪽
    }

    private static void Shoot(string tag, MonsterBase target)
    {
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        var hud = UnityEngine.Object.FindFirstObjectByType<EnemyCompassHud>();
        string stem = $"{DateTime.Now:HHmmss}_{s_room}_{tag}";
        ScreenCapture.CaptureScreenshot($"{OutDir}/{stem}.png");
        if (player == null || hud == null || target == null) { Report($"{stem} — 나침반 없음(hud={hud != null})"); return; }

        var t = typeof(EnemyCompassHud);
        float arrow = (float)t.GetField("_arrowAlpha", Inst).GetValue(hud);
        float boss = (float)t.GetField("_bossAlpha", Inst).GetValue(hud);
        var image = t.GetField("_bossImage", Inst).GetValue(hud) as UnityEngine.UI.RawImage;
        // 찍는 순간의 「가장 가까운 몬스터」 — 옮긴 뒤엔 처음 대상과 다를 수 있다(쫓아온 몬스터 등)
        MonsterBase.FindCompassTargets(player.transform.position, out var nearest, out _, out _);
        var focus = s_mode == Mode.CompassBoss ? target : nearest != null ? nearest : target;
        Vector3 flat = focus.transform.position - player.transform.position; flat.y = 0f;
        var cam = Camera.main;
        Vector3 vp = cam != null ? cam.WorldToViewportPoint(focus.transform.position) : Vector3.zero;
        Report($"{stem} — 대상 {focus.name}({focus.Grade}) 거리 {flat.magnitude:0.0} m · 화면 좌표 ({vp.x:0.00}, {vp.y:0.00}, z {vp.z:0.0}) · " +
               $"화살표 알파 {arrow:0.00} · 보스 배지 알파 {boss:0.00} · 보스 아이콘 {(image != null && image.texture != null ? image.texture.name : "없음")} · " +
               $"UI 막힘 {UIInputGate.Blocked}");
    }

    // ── 출구 라벨 ──

    private static bool s_exitCaught;

    /// <summary>출구 배지가 생긴 프레임에 자동 실측을 멈춘다(자동 실측은 0.25초 간격이라 이쪽이 먼저 본다).</summary>
    private static void TickExitLabel()
    {
        if (s_exitCaught) return;
        var hud = ExitCompassHud.Instance;
        if (hud == null) return;
        var entries = typeof(ExitCompassHud).GetField("_entries", Inst)?.GetValue(hud) as System.Collections.IList;
        if (entries == null || entries.Count == 0) return;
        var gateT = entries[0].GetType().GetField("Target")?.GetValue(entries[0]) as Transform;
        if (gateT == null) return;
        var label = UnityEngine.Object.FindFirstObjectByType<GateLabelScreenCap>();   // 배지 출구엔 없어야 한다(10-01)

        s_exitCaught = true;
        EditorApplication.ExecuteMenuItem(AutoStop);
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null) return;
        Vector3 gate = gateT.position;
        Vector3 inward = player.transform.position - gate; inward.y = 0f;
        inward = inward.sqrMagnitude > 0.01f ? inward.normalized : -gateT.forward;
        s_room = gateT.name;
        Report($"출구 라벨 — 출구 {entries.Count}개 · 월드 라벨 {UnityEngine.Object.FindObjectsByType<GateLabelScreenCap>(FindObjectsSortMode.None).Length}개 · 자동 실측 멈춤");
        After(1.0, () => { SetInvincible(player); MovePlayer(player, gate + inward * 2f); });
        After(3.0, () => ShootExit("exit_near", label, entries));
        After(3.4, () => MovePlayer(player, gate + inward * 12f));
        After(5.4, () => ShootExit("exit_far", label, entries));
        After(6.0, () => Debug.Log("[StageGuideProbe] 출구 라벨 끝"));
    }

    private static void ShootExit(string tag, GateLabelScreenCap label, System.Collections.IList entries)
    {
        string stem = $"{DateTime.Now:HHmmss}_{tag}";
        ScreenCapture.CaptureScreenshot($"{OutDir}/{stem}.png");
        if (label == null) { Report($"{stem} — 월드 라벨 없음 · 배지 {DescribeBadges(entries)}"); return; }
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        var group = label.GetComponent<CanvasGroup>();
        Vector3 d = label.transform.position - (player != null ? player.transform.position : label.transform.position); d.y = 0f;
        float lineWorld = (float)typeof(GateLabelScreenCap).GetField("_lineWorld", Inst).GetValue(label) * label.transform.localScale.x
                          / Mathf.Max(0.0001f, (float)typeof(GateLabelScreenCap).GetField("_baseScale", Inst).GetValue(label));
        float linePx = -1f;
        var cam = Camera.main;
        if (cam != null)
        {
            float depth = Vector3.Dot(label.transform.position - cam.transform.position, cam.transform.forward);
            if (depth > 0.1f) linePx = lineWorld * 1080f / (2f * depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
        }
        Report($"{stem} — 게이트까지 {d.magnitude:0.0} m · 라벨 1행 {linePx:0}px(상한 72) · 알파 {(group != null ? group.alpha : -1f):0.00} · 배율 {label.transform.localScale.x:0.0000} · 배지 {DescribeBadges(entries)}");
    }

    /// <summary>배지마다 화면 안/밖 · x · 위 가장자리에서 판 위쪽 끝까지(1080 기준 px).</summary>
    private static string DescribeBadges(System.Collections.IList entries)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var e in entries)
        {
            var rect = e.GetType().GetField("Rect")?.GetValue(e) as RectTransform;
            var arrow = e.GetType().GetField("Arrow")?.GetValue(e) as UnityEngine.UI.Graphic;
            if (rect == null || !rect.gameObject.activeInHierarchy) continue;
            float scale = Screen.height / 1080f;
            float topGap = (Screen.height - (rect.position.y + rect.sizeDelta.y * 0.5f * rect.lossyScale.y)) / scale;
            sb.Append($"[{(arrow != null && arrow.gameObject.activeSelf ? "화면 밖" : "화면 안")} x {rect.position.x / scale:0} · 위 여백 {topGap:0}px] ");
        }
        return sb.ToString();
    }

    // ── 공통 ──

    private static void SetInvincible(PlayerController player)
        => typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(player, true);

    private static void MovePlayer(PlayerController player, Vector3 pos)
    {
        if (Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore))
            pos.y = hit.point.y + 0.1f;
        player.transform.position = pos;
        if (player.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.position = pos;
            rb.linearVelocity = Vector3.zero;
        }
    }

    private static void Report(string line)
    {
        Debug.Log($"[StageGuideProbe] {line}");
        File.AppendAllText($"{OutDir}/report.txt", $"{DateTime.Now:HH:mm:ss} {line}\n");
    }
}
#endif
