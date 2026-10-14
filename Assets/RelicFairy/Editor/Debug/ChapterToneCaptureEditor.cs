#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using OccaSoftware.Buto.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>
/// 10-12 챕터 톤 촬영(83 · 씬 톤 · 렌더링) — 사용자 「챕터마다의 톤을 좀더 살려야해 · 해당 챕터만의 포그나 분위기 배경」.
/// 테스트 허브(Scenes/Test/BaseCamp_Test)에서 챕터 N을 대기방부터 시작해 <b>대기방 · 첫 일반방</b>을 같은 게임 카메라로 한 장씩 찍고,
/// 런을 접고 허브로 돌아와 다음 챕터로 간다(4챕터 × 2 = 8장). 전후 비교용 — 고치기 전에 한 번, 반입 뒤 한 번.
/// <para>찍는 법: 실제 화면 출력(<see cref="ScreenCapture"/>)에서 화면 UI(오버레이 캔버스)만 한 프레임 끄고 찍는다 — 하늘 · 안개 · 볼륨 · 비네트는 게임 그대로.
/// MCP 스크린샷은 도메인 리로드로 런을 깨므로 쓰지 않는다. 결과는 <c>Assets/</c> 밖(Temp/ui_shots)에만 쓴다.</para>
/// <para>결과: Temp/ui_shots/TONE_ChN_{wait|room}_{시기}_{시각}.png + 같은 이름 .txt(그 순간 실제 적용된 RenderSettings · 직사광 · 카메라 ·
/// 켜진 볼륨 · 볼륨 스택 최종값 · Buto · 방 조명). 로그 「[ChapterTone] 끝」.</para>
/// <para>시기 비교는 「시기 비교 — Ch1 첫 방 해방기 1장 · 악몽 1장」: 에디터 시기 오버라이드를 해방기 → 악몽 모드로 바꿔 가며 Ch1 첫 방만 찍고,
/// 끝나면 오버라이드를 원래 값으로 되돌린다. txt에 「[@EraTone]」 줄(시기 런타임 볼륨이 실제로 덮은 값)이 남는다.</para>
/// </summary>
public static class ChapterToneCaptureEditor
{
    // ── Constants ─────────────────────────────────────────────────
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Root          = "RelicFairy/Debug/10-12 챕터 톤 촬영/";
    private const string ShotDir       = "Temp/ui_shots";
    private const float  HubWaitSec    = 45f;
    private const float  RunWaitSec    = 60f;
    private const int    ArriveMs      = 5000;   // 대기방 도착 연출 · 탑 벽 · 입장 안개(1.6초)가 걷힐 때까지
    private const int    NextRoomMs    = 6000;   // 다음 방 디졸브 · 입장 안개가 걷힐 때까지
    private const int    SettleMs      = 1500;   // 대사 넘긴 뒤 · Buto 시간 누적이 자리 잡을 때까지
    private const float  RoomLightReach = 30f;
    // StoryProgress 에디터 시기 오버라이드 값 — TestHubLauncher.EraValues와 같다(1 = 해방기 · 2 = 악몽 모드)
    private const int    EraOverrideLiberated = 1;
    private const int    EraOverrideNightmare = 2;

    // ── Static ────────────────────────────────────────────────────
    private static CancellationTokenSource s_cts;

    // ── Menu ──────────────────────────────────────────────────────
    [MenuItem(Root + "Ch1-4 대기방 · 첫 방 8장 (테스트 허브, 플레이 중)")] private static void All() => Begin(new[] { 1, 2, 3, 4 });
    [MenuItem(Root + "Ch1만 2장 (테스트 허브, 플레이 중)")] private static void Ch1() => Begin(new[] { 1 });
    [MenuItem(Root + "Ch2만 2장 (테스트 허브, 플레이 중)")] private static void Ch2() => Begin(new[] { 2 });
    [MenuItem(Root + "Ch3만 2장 (테스트 허브, 플레이 중)")] private static void Ch3() => Begin(new[] { 3 });
    [MenuItem(Root + "Ch4만 2장 (테스트 허브, 플레이 중)")] private static void Ch4() => Begin(new[] { 4 });

    [MenuItem(Root + "시기 비교 — Ch1 첫 방 해방기 1장 · 악몽 1장 (테스트 허브, 플레이 중)")]
    private static void EraPair()
    {
        if (!CanBegin()) return;
        Restart();
        RunAsync(new[] { 1 }, new[] { EraOverrideLiberated, EraOverrideNightmare }, false, s_cts.Token).Forget();
    }

    [MenuItem(Root + "지금 자리 1장 (런 중 — 보스방 등)")]
    private static void Here()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[ChapterTone] 플레이 모드에서만"); return; }
        Restart();
        HereAsync(s_cts.Token).Forget();
    }

    [MenuItem(Root + "멈춤")]
    private static void Stop()
    {
        s_cts?.Cancel();
        Debug.Log("[ChapterTone] 멈춤");
    }

    // ── Private Methods ───────────────────────────────────────────
    private static void Begin(int[] chapters)
    {
        if (!CanBegin()) return;
        Restart();
        RunAsync(chapters, null, true, s_cts.Token).Forget();
    }

    private static bool CanBegin()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[ChapterTone] 테스트 허브를 플레이한 뒤 누른다"); return false; }
        if (Object.FindFirstObjectByType<TestHubLauncher>() == null) { Debug.LogWarning("[ChapterTone] 테스트 허브 씬이 아니다(BaseCamp_Test)"); return false; }
        return true;
    }

    /// <summary>에디터 시기 오버라이드(EditorPrefs · 이야기 기록은 안 건드림) — 다음 런 시작 때 DungeonSpaceDirector가 읽는다.</summary>
    private static void SetEraOverride(int value)
    {
        EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, value);
        StoryProgress.RefreshDebugOverride();
    }

    private static void Restart()
    {
        s_cts?.Cancel();
        s_cts?.Dispose();
        s_cts = new CancellationTokenSource();
        Directory.CreateDirectory(ShotDir);
    }

    private static async UniTaskVoid HereAsync(CancellationToken ct)
    {
        try
        {
            var chapter = GameRunBootstrapper.Instance?.Run?.CurrentChapter;
            await ShootAsync(chapter != null ? (int)chapter.Value : 0, "here", ct);
        }
        catch (OperationCanceledException) { }
        finally { Debug.Log("[ChapterTone] 끝"); }
    }

    /// <summary>eras가 있으면 그 시기마다 chapters를 한 바퀴씩 — 끝나면(취소여도) 시기 오버라이드를 원래 값으로.</summary>
    private static async UniTaskVoid RunAsync(int[] chapters, int[] eras, bool shootWait, CancellationToken ct)
    {
        int shots = 0;
        int eraBefore = StoryProgress.DebugNightmareOverride;
        try
        {
            for (int e = 0; e < (eras?.Length ?? 1); e++)
            {
                if (eras != null) SetEraOverride(eras[e]);
                foreach (int ch in chapters)
                {
                    if (!await WaitHubAsync(ct)) { Debug.LogWarning($"[ChapterTone] Ch{ch} — 허브 대기 초과"); return; }
                    if (!await LaunchAsync(ch, ct)) { Debug.LogWarning($"[ChapterTone] Ch{ch} — 런 시작 실패"); return; }
                    if (!await WaitRunAsync(ct)) { Debug.LogWarning($"[ChapterTone] Ch{ch} — 런 준비 대기 초과"); return; }

                    await UniTask.Delay(ArriveMs, ignoreTimeScale: true, cancellationToken: ct);
                    SkipDialogue();
                    await UniTask.Delay(SettleMs, ignoreTimeScale: true, cancellationToken: ct);
                    if (shootWait && await ShootAsync(ch, "wait", ct)) shots++;

                    if (await AdvanceAsync(ct))
                    {
                        await UniTask.Delay(NextRoomMs, ignoreTimeScale: true, cancellationToken: ct);
                        SkipDialogue();
                        await UniTask.Delay(SettleMs, ignoreTimeScale: true, cancellationToken: ct);
                        if (await ShootAsync(ch, "room", ct)) shots++;
                    }
                    else Debug.LogWarning($"[ChapterTone] Ch{ch} — 첫 방으로 못 넘어감(대기방만)");

                    ReturnToHub();
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (eras != null) SetEraOverride(eraBefore);
            Debug.Log($"[ChapterTone] 끝 — {shots}장 · {Path.GetFullPath(ShotDir)}{(eras != null ? $" · 시기 오버라이드 {eraBefore}로 되돌림" : "")}");
        }
    }

    private static async UniTask<bool> WaitHubAsync(CancellationToken ct)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < HubWaitSec)
        {
            var app = AppBootstrapper.Instance;
            if (app != null && app.IsReady && Object.FindFirstObjectByType<TestHubLauncher>() != null) return true;
            await UniTask.Delay(500, ignoreTimeScale: true, cancellationToken: ct);
        }
        return false;
    }

    /// <summary>허브 런처의 챕터 · 시작점(대기방부터)을 맞추고 시작 — 초기화 중이면 「무시」되므로 몇 번 다시 누른다.</summary>
    private static async UniTask<bool> LaunchAsync(int chapter, CancellationToken ct)
    {
        for (int i = 0; i < 10; i++)
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher != null)
            {
                launcher.SelectChapter(chapter);
                typeof(TestHubLauncher).GetField("_bossApproach", Inst)?.SetValue(launcher, false);
                if (launcher.TryLaunch()) return true;
            }
            await UniTask.Delay(1000, ignoreTimeScale: true, cancellationToken: ct);
        }
        return false;
    }

    private static async UniTask<bool> WaitRunAsync(CancellationToken ct)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < RunWaitSec)
        {
            await UniTask.Delay(500, ignoreTimeScale: true, cancellationToken: ct);
            if (GameRunBootstrapper.Instance?.Run?.Player != null && DisplayCamera() != null) return true;
        }
        return false;
    }

    /// <summary>대기방 문을 서약 신호로 열고 문 안으로 옮긴다(DungeonSpaceProbe와 같은 길) — 대기방이 아니면 열린 출구로.</summary>
    private static async UniTask<bool> AdvanceAsync(CancellationToken ct)
    {
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null) return false;
        Vector3 before = player.transform.position;

        foreach (var gate in Object.FindObjectsByType<StartRoomGate>(FindObjectsSortMode.None))
        {
            if ((int)(typeof(StartRoomGate).GetField("_fromZoneIndex", Inst)?.GetValue(gate) ?? 0) != -1) continue;
            typeof(StartRoomGate).GetMethod("HandleCovenantAssembled", Inst)?.Invoke(gate, null);
            await UniTask.Delay(9000, ignoreTimeScale: true, cancellationToken: ct);
            if (gate != null && player != null && gate.TryGetComponent<Collider>(out var col)) Teleport(player, col.bounds);
            await UniTask.Delay(1000, ignoreTimeScale: true, cancellationToken: ct);
            return player != null && (player.transform.position - before).sqrMagnitude > 1f;
        }
        TestHubDebugMenu.EnterOpenExit();
        await UniTask.Delay(500, ignoreTimeScale: true, cancellationToken: ct);
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
        for (int i = 0; i < 6; i++)
            if (Object.FindFirstObjectByType<UI_DialoguePopup>() != null) TestHubDebugMenu.AdvanceDialogue();
    }

    /// <summary>런을 정산 없이 접고 허브로 — 촬영용이라 메타 정산 · 결과창을 건너뛴다(로컬 런 세이브 · 로드아웃만 비운다).</summary>
    private static void ReturnToHub()
    {
        AppBootstrapper.Instance?.EndRun();
        if (!TestRunRequest.TryReturnToHub()) Debug.LogWarning("[ChapterTone] 허브 복귀 실패 — 테스트 요청이 비었다");
    }

    /// <summary>한 장 — 오버레이 UI만 한 프레임 끄고 실제 화면 출력을 찍는다 + 같은 이름 .txt에 그 순간 값.</summary>
    private static async UniTask<bool> ShootAsync(int chapter, string spot, CancellationToken ct)
    {
        var cam = DisplayCamera();
        var runner = Object.FindFirstObjectByType<Managers>();
        if (cam == null || runner == null) { Debug.LogWarning($"[ChapterTone] Ch{chapter} {spot} — 카메라 · Managers 없음"); return false; }

        string stem = Path.Combine(ShotDir, $"TONE_Ch{chapter}_{spot}_{StoryProgress.Era}_{DateTime.Now:HHmmss}");
        string report = Describe(cam, chapter, spot);   // 끄기 전 상태로 적는다

        var hidden = new System.Collections.Generic.List<Canvas>();
        Texture2D shot = null;
        try
        {
            foreach (var cv in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (cv.isRootCanvas && cv.enabled && cv.renderMode == RenderMode.ScreenSpaceOverlay) { hidden.Add(cv); cv.enabled = false; }
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await UniTask.WaitForEndOfFrame(runner, ct);
            shot = ScreenCapture.CaptureScreenshotAsTexture();
        }
        finally
        {
            foreach (var cv in hidden) if (cv != null) cv.enabled = true;
        }
        if (shot == null) return false;
        File.WriteAllBytes(stem + ".png", shot.EncodeToPNG());
        Object.Destroy(shot);
        File.WriteAllText(stem + ".txt", report, new UTF8Encoding(false));
        Debug.Log($"[ChapterTone] {Path.GetFileName(stem)}.png");
        return true;
    }

    /// <summary>화면에 마지막으로 그리는(깊이 가장 큰) 켜진 화면 카메라 — Camera.main은 다른 카메라일 수 있다(10-02 실측).</summary>
    private static Camera DisplayCamera()
    {
        Camera best = null;
        foreach (var c in Camera.allCameras)
            if (c.targetTexture == null && (best == null || c.depth > best.depth)) best = c;
        return best;
    }

    // ── 기록 ──────────────────────────────────────────────────────
    private static string Describe(Camera cam, int chapter, string spot)
    {
        var s = new StringBuilder();
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        Vector3 p = player != null ? player.transform.position : Vector3.zero;
        s.AppendLine($"Ch{chapter} {spot} · 씬 {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} · 시기 {StoryProgress.Era} · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        s.AppendLine($"품질 {QualitySettings.names[QualitySettings.GetQualityLevel()]} · 파이프라인 {(GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "-")} · 화면 {Screen.width}x{Screen.height}");
        s.AppendLine($"플레이어 {p:F1} · 팔레트 테마 '{GameRunBootstrapper.Instance?.Run?.ActiveTheme}'");

        // RenderSettings
        s.AppendLine($"[안개] {(RenderSettings.fog ? "켬" : "끔")} · {RenderSettings.fogMode} · 색 {Hex(RenderSettings.fogColor)} · 밀도 {RenderSettings.fogDensity:0.0000} · 선형 {RenderSettings.fogStartDistance:0}~{RenderSettings.fogEndDistance:0}");
        s.AppendLine($"[환경광] {RenderSettings.ambientMode} · {Hex(RenderSettings.ambientLight)} · 세기 {RenderSettings.ambientIntensity:0.00} · 하늘 {(RenderSettings.skybox != null ? RenderSettings.skybox.name : "없음")} · sun {(RenderSettings.sun != null ? RenderSettings.sun.name : "없음")}");
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional && l.isActiveAndEnabled)
                s.AppendLine($"[직사광] {l.name} · 색 {Hex(l.color)}{(l.useColorTemperature ? $" × {l.colorTemperature:0}K" : "")} · 세기 {l.intensity:0.00} · 각 {l.transform.eulerAngles:F0} · 그림자 {l.shadows} {l.shadowStrength:0.00}");

        // 카메라
        Vector3 rel = cam.transform.position - p;
        var urp = cam.GetUniversalAdditionalCameraData();
        s.AppendLine($"[카메라] {cam.name} · 배경 {cam.clearFlags} {Hex(cam.backgroundColor)} · FOV {cam.fieldOfView:0} · 각 {cam.transform.eulerAngles.x:0}° · 높이 {rel.y:0.0} m · 수평 {new Vector2(rel.x, rel.z).magnitude:0.0} m · 후처리 {(urp != null && urp.renderPostProcessing)}");

        // 켜진 볼륨
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            if (v.isActiveAndEnabled)
                s.AppendLine($"[볼륨] {v.name} · 프로파일 {(v.sharedProfile != null ? v.sharedProfile.name : "(런타임)")} · 우선 {v.priority:0} · 무게 {v.weight:0.00} · {(v.isGlobal ? "전역" : "국소")}");
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            if (v.name == "@EraTone" && v.sharedProfile != null)
                s.AppendLine($"[@EraTone] {DescribeOverrides(v.sharedProfile)}");

        // 볼륨 스택 최종값(카메라가 실제로 쓰는 값)
        var stack = urp != null && urp.volumeStack != null ? urp.volumeStack : VolumeManager.instance.stack;
        var ca  = stack.GetComponent<ColorAdjustments>();
        var wb  = stack.GetComponent<WhiteBalance>();
        var smh = stack.GetComponent<ShadowsMidtonesHighlights>();
        var st  = stack.GetComponent<SplitToning>();
        var tm  = stack.GetComponent<Tonemapping>();
        var bl  = stack.GetComponent<Bloom>();
        var vg  = stack.GetComponent<Vignette>();
        var bf  = stack.GetComponent<ButoVolumetricFog>();
        if (ca != null)  s.AppendLine($"[색 보정] 노출 {ca.postExposure.value:0.00} · 대비 {ca.contrast.value:0} · 필터 {Hex(ca.colorFilter.value)} · 채도 {ca.saturation.value:0} · 색상 {ca.hueShift.value:0}");
        if (wb != null)  s.AppendLine($"[화이트밸런스] 온도 {wb.temperature.value:0} · 틴트 {wb.tint.value:0}");
        if (smh != null) s.AppendLine($"[SMH] 그림자 {smh.shadows.value:F2} · 중간 {smh.midtones.value:F2} · 밝은 {smh.highlights.value:F2}");
        if (st != null)  s.AppendLine($"[스플릿] 그림자 {Hex(st.shadows.value)} · 밝은 {Hex(st.highlights.value)} · 균형 {st.balance.value:0}");
        if (tm != null)  s.AppendLine($"[톤매핑] {tm.mode.value}");
        if (bl != null)  s.AppendLine($"[블룸] 문턱 {bl.threshold.value:0.00} · 세기 {bl.intensity.value:0.00} · 퍼짐 {bl.scatter.value:0.00}");
        if (vg != null)  s.AppendLine($"[비네트] {vg.intensity.value:0.00} · 색 {Hex(vg.color.value)}");
        if (bf != null)  s.AppendLine($"[Buto] 켬 {bf.active} · 밀도 {bf.fogDensity.value:0.00} · 빛 {bf.lightIntensity.value:0.00} · 빛깔 {Hex(bf.litColor.value)} · 빛깔 영향 {bf.colorInfluence.value:0.00} · 발광 {Hex(bf.emitColor.value)} · 기준 높이 {bf.baseHeight.value:0} · 감쇠 {bf.attenuationBoundarySize.value:0}");

        // 방 조명(점광원) · 분위기 입자
        int n = 0; Color sum = Color.black;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Point && l.isActiveAndEnabled && (l.transform.position - p).sqrMagnitude < RoomLightReach * RoomLightReach) { n++; sum += l.color; }
        s.AppendLine($"[방 조명] 점광원 {n}개(반경 {RoomLightReach:0} m) · 평균 색 {(n > 0 ? Hex(sum / n) : "-")}");
        var air = GameObject.Find("~ChapterAir");
        s.AppendLine($"[분위기 입자] {(air != null ? air.GetComponentsInChildren<ParticleSystem>().Length + "개 계 · 살아 있는 입자 " + AliveParticles(air) : "없음")} · 탑 벽 {(GameObject.Find("@DungeonShell") != null ? "있음" : "없음")}");
        return s.ToString();
    }

    /// <summary>프로파일이 실제로 덮는 값(overrideState 켜진 칸)만 — 「컴포넌트.칸=값(켬/끔)」.</summary>
    private static string DescribeOverrides(VolumeProfile profile)
    {
        var s = new StringBuilder();
        foreach (var comp in profile.components)
        {
            if (comp == null) continue;
            s.Append($"{comp.GetType().Name}(active {comp.active}): ");
            foreach (var f in comp.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!typeof(VolumeParameter).IsAssignableFrom(f.FieldType)) continue;
                var p = (VolumeParameter)f.GetValue(comp);
                if (p == null || !p.overrideState) continue;
                object val = p.GetType().GetProperty("value")?.GetValue(p);
                s.Append($"{f.Name}={(val is Color c ? Hex(c) : val is float x ? x.ToString("0.000") : val)} ");
            }
            s.Append("· ");
        }
        return s.Length > 0 ? s.ToString() : "(덮는 칸 없음)";
    }

    private static int AliveParticles(GameObject root)
    {
        int n = 0;
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>()) n += ps.particleCount;
        return n;
    }

    private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c) + $"({c.r:0.000},{c.g:0.000},{c.b:0.000})";
}
#endif
