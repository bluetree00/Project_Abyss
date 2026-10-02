#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>
/// 10-02 S4 「던전 공간」 실측 — 지금 화면에 하늘(허공)이 얼마나 보이는가.
/// 테스트 허브에서 챕터 N을 시작해 대기방 · 첫 방(일반) 또는 보스 대기방 · 보스방(보스)을 차례로 찍고, 자리마다
/// 카메라 높이 · 각도 · 벽 높이와 <b>허공 비율</b>(카메라 배경을 표식 색으로 지우고 다시 그려 남은 화소 = 아무 물체도 없는 곳)을 잰다.
/// 결과: Temp/dungeon_space_probe.txt(덧붙임) · Logs/dungeon_space/*.png(화면 · 허공 마스크). 로그 「[DungeonSpace] 끝」.
/// </summary>
public static class DungeonSpaceProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Root = "RelicFairy/Debug/10-02 던전 공간 실측/";
    private const string OutTxt = "Temp/dungeon_space_probe.txt";

    [MenuItem(Root + "Ch1 일반 (테스트 허브, 플레이 중)")] private static void N1() => Begin(1, false);
    [MenuItem(Root + "Ch2 일반 (테스트 허브, 플레이 중)")] private static void N2() => Begin(2, false);
    [MenuItem(Root + "Ch3 일반 (테스트 허브, 플레이 중)")] private static void N3() => Begin(3, false);
    [MenuItem(Root + "Ch4 일반 (테스트 허브, 플레이 중)")] private static void N4() => Begin(4, false);
    [MenuItem(Root + "Ch1 보스 (테스트 허브, 플레이 중)")] private static void B1() => Begin(1, true);
    [MenuItem(Root + "Ch2 보스 (테스트 허브, 플레이 중)")] private static void B2() => Begin(2, true);
    [MenuItem(Root + "Ch3 보스 (테스트 허브, 플레이 중)")] private static void B3() => Begin(3, true);
    [MenuItem(Root + "Ch4 보스 (테스트 허브, 플레이 중)")] private static void B4() => Begin(4, true);
    [MenuItem(Root + "지금 자리 (런 중)")] private static void Here()
    {
        if (!EditorApplication.isPlaying) return;
        HereAsync().Forget();
    }

    private static async UniTaskVoid HereAsync()
    {
        var sb = new StringBuilder();
        await MeasureAsync(sb, "지금");
        File.AppendAllText(OutTxt, sb.ToString());
        Debug.Log("[DungeonSpace] 끝");
    }

    private static void Begin(int chapter, bool boss)
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[DungeonSpace] 플레이 모드(테스트 허브)에서"); return; }
        RunAsync(chapter, boss).Forget();
    }

    private static async UniTaskVoid RunAsync(int chapter, bool boss)
    {
        var sb = new StringBuilder($"\n== Ch{chapter} {(boss ? "보스" : "일반")} · {StoryProgress.Era}\n");
        try
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher == null) { sb.AppendLine("테스트 허브가 아니다"); return; }
            typeof(TestHubLauncher).GetField("_chapter", Inst)?.SetValue(launcher, chapter);
            typeof(TestHubLauncher).GetField("_bossApproach", Inst)?.SetValue(launcher, boss);
            if (!launcher.TryLaunch()) { sb.AppendLine("런 시작 실패"); return; }

            if (!await WaitRunAsync(60f)) { sb.AppendLine("런 준비 대기 초과"); return; }
            await UniTask.Delay(boss ? 10000 : 4000, ignoreTimeScale: true);   // 보스 대기방은 들어오는 장면이 길다(Ch3 1차: 마법진 화면에서 찍힘)
            SkipDialogue();
            await UniTask.Delay(1500, ignoreTimeScale: true);
            await MeasureAsync(sb, boss ? $"Ch{chapter}_보스대기방" : $"Ch{chapter}_대기방");

            // 다음 방으로 — 대기방 문은 서약 신호로 열고 걸어 들어간 것처럼 옮긴다 · 보스 대기방은 열린 출구로
            if (!await AdvanceAsync()) { sb.AppendLine("다음 방으로 못 넘어감"); return; }
            await UniTask.Delay(6000, ignoreTimeScale: true);
            SkipDialogue();
            await UniTask.Delay(1500, ignoreTimeScale: true);
            await MeasureAsync(sb, boss ? $"Ch{chapter}_보스방" : $"Ch{chapter}_첫방");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e.Message); }
        finally
        {
            File.AppendAllText(OutTxt, sb.ToString());
            Debug.Log("[DungeonSpace] 끝");
        }
    }

    private static async UniTask<bool> WaitRunAsync(float seconds)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            await UniTask.Delay(500, ignoreTimeScale: true);
            var run = GameRunBootstrapper.Instance?.Run;
            if (run?.Player != null && Camera.main != null) return true;
        }
        return false;
    }

    private static async UniTask<bool> AdvanceAsync()
    {
        var run = GameRunBootstrapper.Instance?.Run;
        var player = run?.Player;
        if (player == null) return false;
        var before = player.transform.position;

        // 대기방 문(시작방 모드) — 서약 신호를 흉내 내 열고, 열림 연출이 끝난 뒤 문 안으로
        foreach (var gate in Object.FindObjectsByType<StartRoomGate>(FindObjectsSortMode.None))
        {
            if ((int)(typeof(StartRoomGate).GetField("_fromZoneIndex", Inst)?.GetValue(gate) ?? 0) != -1) continue;
            typeof(StartRoomGate).GetMethod("HandleCovenantAssembled", Inst)?.Invoke(gate, null);
            await UniTask.Delay(9000, ignoreTimeScale: true);
            if (gate != null && gate.TryGetComponent<Collider>(out var col)) Teleport(player, col.bounds);
            await UniTask.Delay(1000, ignoreTimeScale: true);
            return true;
        }
        // 그 밖의 방 — 열린 출구
        TestHubDebugMenu.EnterOpenExit();
        await UniTask.Delay(500, ignoreTimeScale: true);
        return player != null && (player.transform.position - before).sqrMagnitude > 1f;
    }

    private static void Teleport(PlayerController player, Bounds b)
    {
        var pos = new Vector3(b.center.x, b.min.y + 0.3f, b.center.z);
        player.transform.position = pos;
        if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
    }

    private static void SkipDialogue()
    {
        var m = typeof(TestHubDebugMenu).GetMethod("AdvanceDialogue", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < 6; i++) m?.Invoke(null, null);
    }

    /// <summary>한 자리 재기 — 화면에 실제로 그리는 카메라 · 벽 높이 · 허공 비율 + 사진 두 장.</summary>
    private static async UniTask MeasureAsync(StringBuilder sb, string label)
    {
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        var cam = DisplayCamera();
        if (cam == null || player == null) { sb.AppendLine($"{label}: 카메라 · 플레이어 없음"); return; }
        await UniTask.WaitForEndOfFrame();

        Vector3 rel = cam.transform.position - player.transform.position;
        float horiz = new Vector2(rel.x, rel.z).magnitude;
        float pitch = cam.transform.eulerAngles.x;

        int wallMask = LayerMask.GetMask("Wall");
        float wallTop = float.NaN;
        foreach (var c in Physics.OverlapSphere(player.transform.position, 25f, wallMask, QueryTriggerInteraction.Ignore))
            wallTop = float.IsNaN(wallTop) ? c.bounds.max.y : Mathf.Max(wallTop, c.bounds.max.y);

        string dir = Path.Combine("Logs", "dungeon_space");
        Directory.CreateDirectory(dir);
        string stamp = $"{DateTime.Now:HHmmss}_{label}";
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, stamp + "_화면.png"));
        await UniTask.Delay(300, ignoreTimeScale: true);

        var (all, top, mid) = await VoidRatioAsync(Path.Combine(dir, stamp + "_허공.png"));
        sb.AppendLine($"{label}: 허공 {all:P1} (위 1/3 {top:P1} · 가운데 1/3 {mid:P1}) · 카메라 {cam.name} 높이 {rel.y:0.0} m · 수평 {horiz:0.0} m · 각 {pitch:0}° · FOV {cam.fieldOfView:0} · " +
                      $"벽 윗면 {(float.IsNaN(wallTop) ? "없음" : (wallTop - player.transform.position.y).ToString("0.0") + " m")} · 하늘 {(RenderSettings.skybox != null ? RenderSettings.skybox.name : "없음")} · 배경 {cam.clearFlags}");
        sb.AppendLine($"    켜진 카메라: {DescribeCameras()}");
        sb.AppendLine($"    발밑: {DescribeFloor(player.transform.position)} · 빛줄기 {CountShafts()}");
    }

    /// <summary>발밑 바닥 오브젝트 · 재질 · 텍스처 + 지금 방을 지은 팔레트 — Ch2 대기방 바닥이 무늬 없는 판으로 보인 원인 조사(10-02).</summary>
    private static string DescribeFloor(Vector3 at)
    {
        var s = new StringBuilder();
        if (Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out var hit, 10f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore))
        {
            var t = hit.collider.transform;
            string path = t.name;
            for (int k = 0; k < 4 && t.parent != null; k++) { t = t.parent; path = t.name + "/" + path; }
            var r = hit.collider.GetComponentInParent<Renderer>() ?? hit.collider.GetComponentInChildren<Renderer>();
            var m = r != null ? r.sharedMaterial : null;
            var tex = m != null && m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
            s.Append($"{path} · 재질 {(m != null ? m.name : "없음")} · 셰이더 {(m != null ? m.shader.name : "-")} · 텍스처 {(tex != null ? tex.name : "없음")}");
        }
        else s.Append("바닥 없음");
        var f = typeof(GameRunBootstrapper).GetField("_currentMapPalette", BindingFlags.Instance | BindingFlags.NonPublic);
        var pal = f?.GetValue(GameRunBootstrapper.Instance) as UnityEngine.Object;
        s.Append($" · 맵 팔레트 {(pal != null ? pal.name : "없음")} · 테마 '{GameRunBootstrapper.Instance?.Run?.ActiveTheme}'");
        return s.ToString();
    }

    private static int CountShafts()
    {
        int n = 0;
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name == "Shaft" && t.parent != null && t.parent.name == "@LightShafts" && t.gameObject.activeInHierarchy) n++;
        return n;
    }

    /// <summary>화면에 마지막으로 그리는(깊이 가장 큰) 켜진 카메라 — Camera.main은 장면에 남은 다른 카메라일 수 있다(10-02 1차 실측).</summary>
    private static Camera DisplayCamera()
    {
        Camera best = null;
        foreach (var c in Camera.allCameras)
            if (c.targetTexture == null && (best == null || c.depth > best.depth)) best = c;
        return best;
    }

    private static string DescribeCameras()
    {
        var s = new StringBuilder();
        foreach (var c in Camera.allCameras)
            s.Append($"{c.name}(깊이 {c.depth:0} · {(c.targetTexture != null ? "RT" : "화면")} · {c.tag}) ");
        return s.ToString();
    }

    /// <summary>
    /// 실제 화면 출력에서 잰다 — 한 프레임 동안 하늘 · 안개 · 볼륨(후처리 · Buto) · 화면 UI를 끄고 화면 카메라 배경을 마젠타로 지운 뒤 캡처.
    /// 마젠타가 남은 화소 = 아무 물체도 없는 곳(하늘 · 허공).
    /// </summary>
    private static async UniTask<(float all, float top, float mid)> VoidRatioAsync(string maskPath)
    {
        var cams   = new List<(Camera c, CameraClearFlags f, Color bg, bool pp)>();
        var canvases = new List<Canvas>();
        var volumes  = new List<(UnityEngine.Rendering.Volume v, float w)>();
        var sky = RenderSettings.skybox;
        bool fog = RenderSettings.fog;
        Texture2D shot = null;
        try
        {
            foreach (var c in Camera.allCameras)
            {
                if (c.targetTexture != null) continue;
                var urp = c.GetUniversalAdditionalCameraData();
                cams.Add((c, c.clearFlags, c.backgroundColor, urp != null && urp.renderPostProcessing));
                c.clearFlags = CameraClearFlags.SolidColor;
                c.backgroundColor = Color.magenta;
                if (urp != null) urp.renderPostProcessing = false;
            }
            foreach (var cv in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (cv.isRootCanvas && cv.enabled && cv.renderMode == RenderMode.ScreenSpaceOverlay) { canvases.Add(cv); cv.enabled = false; }
            foreach (var v in Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None))
                { volumes.Add((v, v.weight)); v.weight = 0f; }
            RenderSettings.skybox = null;
            RenderSettings.fog = false;

            await UniTask.Yield(PlayerLoopTiming.Update);
            await UniTask.WaitForEndOfFrame();
            shot = ScreenCapture.CaptureScreenshotAsTexture();
        }
        finally
        {
            foreach (var (c, f, bg, pp) in cams)
            {
                if (c == null) continue;
                c.clearFlags = f; c.backgroundColor = bg;
                var urp = c.GetUniversalAdditionalCameraData();
                if (urp != null) urp.renderPostProcessing = pp;
            }
            foreach (var cv in canvases) if (cv != null) cv.enabled = true;
            foreach (var (v, w) in volumes) if (v != null) v.weight = w;
            RenderSettings.skybox = sky;
            RenderSettings.fog = fog;
        }
        if (shot == null) return (float.NaN, float.NaN, float.NaN);

        int W = shot.width, H = shot.height;
        var px = shot.GetPixels32();
        int voidAll = 0, voidTop = 0, voidMid = 0;
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            var c = px[y * W + x];
            if (!(c.r > 230 && c.g < 40 && c.b > 230)) continue;
            voidAll++;
            if (y >= H * 2 / 3) voidTop++;          // 텍스처 y는 아래에서 위로
            else if (y >= H / 3) voidMid++;
        }
        File.WriteAllBytes(maskPath, shot.EncodeToPNG());
        Object.DestroyImmediate(shot);
        float third = W * (H / 3f);
        return (voidAll / (float)(W * H), voidTop / third, voidMid / third);
    }
}
#endif
