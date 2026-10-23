#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-16 빌드 피드백 실측 — 테스트 허브에서 플레이 중에 돌린다(한 판에 6장).
/// ① 대기방 문: 개구부 실제 폭(옆 벽까지 광선) · 문 · 문틀 · 상인방 · 통로 안쪽 폭(문 기준 좌표) + 방 쪽에서 본 사진 FB_1_Gate_chN
/// ② 요정의 샘: 방 가운데 · 시작 자리와의 거리 + [F] 확인 창 FB_2_Spring ③ ESC → 게임 종료 확인 FB_3_EscQuit
/// ④ 로비로 넘어가 FB_4_Lobby(타이틀 아랫변 ↔ 첫 메뉴 글자 윗변 간격) ⑤ 로비 Exit 확인 FB_5_LobbyQuit ⑥ 세이브 슬롯 FB_6_SaveSlot.
/// 인수 Temp/fb1016_args.txt 1줄 = 챕터(기본 1). 결과 Temp/fb1016_probe.txt · Temp/ui_shots/FB_*.png · 로그 「[FB1016] 끝」. 끝나면 플레이를 멈출 것(로비에 남는다).
/// </summary>
public static class BuildFeedback1016ProbeEditor
{
    private const string Tag     = "[FB1016]";
    private const string ArgsTxt = "Temp/fb1016_args.txt";
    private const string OutTxt  = "Temp/fb1016_probe.txt";
    private const string ShotDir = "Temp/ui_shots";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// 10-16 로비 레거시 점검(바탕화면 구현설계/빌드피드백_20261016/로비_구성요소_레거시점검.md)의 정리 1~5를 LobbyRoot 프리팹에서 걷는다(편집 모드).
    /// MenuPanel/Btn_Awakening(+Txt_Awakening — 빌드의 @UIRoot 인스턴스에선 이미 지워져 있었다) · DarkOverlay(늘 투명) ·
    /// Panel_SaveSlot/Panel_Main/Txt_Title(빈 글) · ConfirmRoot의 꺼진 Txt_BtnConfirm · Txt_BtnCancel · SlotCard_0~2의 꺼진 LobbyButtonHover.
    /// 없는 것은 건너뛴다(두 번 돌려도 된다). 결과 Temp/fb1016_lobby_cleanup.txt · 로그 「[FB1016] 로비 정리 끝」.
    /// </summary>
    [MenuItem("RelicFairy/Debug/10-17 로비 레거시 정리 (편집 모드 · LobbyRoot 프리팹)")]
    private static void CleanLobbyLegacy()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 편집 모드에서만 동작한다."); return; }
        const string path = "Assets/RelicFairy/UI/Scene/ScenePrefabs/LobbyRoot.prefab";
        var sb   = new StringBuilder("10-17 로비 레거시 정리\n");
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int n = 0;
            void Kill(string p, bool onlyInactive = false)
            {
                var t = root.transform.Find(p);
                if (t == null || (onlyInactive && t.gameObject.activeSelf)) { sb.AppendLine($"건너뜀 {p}{(t != null ? " (켜져 있음)" : " (없음)")}"); return; }
                Object.DestroyImmediate(t.gameObject);
                sb.AppendLine($"지움 {p}"); n++;
            }
            Kill("MenuPanel/Btn_Awakening");
            Kill("DarkOverlay");
            Kill("Panel_SaveSlot/Panel_Main/Txt_Title");
            Kill("Panel_SaveSlot/ConfirmRoot/Btn_Confirm/Txt_BtnConfirm", onlyInactive: true);
            Kill("Panel_SaveSlot/ConfirmRoot/Btn_Cancel/Txt_BtnCancel", onlyInactive: true);
            for (int i = 0; i < 3; i++)
            {
                string p = $"Panel_SaveSlot/Panel_Main/Panel_Slots/SlotCard_{i}";
                var card = root.transform.Find(p);
                if (card != null && card.TryGetComponent<LobbyButtonHover>(out var hover) && !hover.enabled)
                {
                    Object.DestroyImmediate(hover);
                    sb.AppendLine($"지움 {p} · 꺼진 LobbyButtonHover"); n++;
                }
                else sb.AppendLine($"건너뜀 {p} · LobbyButtonHover");
            }
            if (n > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
            sb.AppendLine($"지운 것 {n}개{(n > 0 ? " · 저장" : "")}");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        File.WriteAllText("Temp/fb1016_lobby_cleanup.txt", sb.ToString());
        Debug.Log($"{Tag} 로비 정리 끝\n{sb}");
    }

    [MenuItem("RelicFairy/Debug/10-16 빌드 피드백 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중(테스트 허브)에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-16 빌드 피드백 실측\n");
        Directory.CreateDirectory(ShotDir);
        try
        {
            int ch = 1;
            if (File.Exists(ArgsTxt) && int.TryParse(File.ReadAllText(ArgsTxt).Trim(), out var a)) ch = Mathf.Clamp(a, 1, 4);
            var gate = await LaunchAsync(ch, sb);
            if (gate != null)
            {
                await UniTask.Delay(2500, ignoreTimeScale: true);   // 석문 · 문틀 · 통로가 첫 Update 뒤에 선다
                MeasureGate(gate, ch, sb);
                await SpringAsync(sb);
                await EscQuitAsync(sb);
            }
            await LobbyAsync(sb);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    /// <summary>
    /// 거점 던전 입장 포탈(PortalVortex) — 편집 모드(Build/BaseCamp 씬을 연 채)에서 켜진 입자계를 4초 돌려 보고 앞 · 뒤 12 m에서 찍는다
    /// (FB_7_Portal_front · back). 씬 값은 바꾸지 않는다(Simulate 뒤 Clear). 결과 Temp/fb1016_portal.txt · 로그 「[FB1016] 포탈 끝」.
    /// </summary>
    [MenuItem("RelicFairy/Debug/10-16 거점 포탈 촬영 (편집 모드 · Build/BaseCamp 씬)")]
    private static void RunPortal()
    {
        var sb = new StringBuilder("10-16 거점 포탈\n");
        Directory.CreateDirectory(ShotDir);
        var vortex = GameObject.Find("PortalVortex");
        var systems = vortex != null ? vortex.GetComponentsInChildren<ParticleSystem>(false) : new ParticleSystem[0];
        try
        {
            if (EditorApplication.isPlaying || vortex == null) { sb.AppendLine($"편집 모드 · 거점 씬에서 누를 것(포탈 {vortex != null})"); return; }
            int alive = 0;
            foreach (var ps in systems) { ps.Simulate(4f, false, true, false); alive += ps.particleCount; }
            sb.AppendLine($"켜진 입자계 {systems.Length}개({string.Join(", ", System.Array.ConvertAll(systems, p => p.name))}) · 4초 뒤 입자 {alive} · 배율 {vortex.transform.lossyScale.x:0.###}");
            var v = vortex.transform.position;
            foreach (var (name, sign) in new[] { ("front", 1f), ("back", -1f) })
            {
                var fwd = Vector3.ProjectOnPlane(vortex.transform.forward * sign, Vector3.up).normalized;
                var cam = v + fwd * 12f;
                if (Physics.Raycast(cam + Vector3.up * 30f, Vector3.down, out var hit, 80f, ~0, QueryTriggerInteraction.Ignore)) cam.y = hit.point.y;
                cam.y += 3.5f;
                RenderFrom(Path.Combine(ShotDir, $"FB_7_Portal_{name}.png"), cam, v, 50f);
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var ps in systems) { ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Clear(false); }
            File.WriteAllText("Temp/fb1016_portal.txt", sb.ToString());
            Debug.Log($"{Tag} 포탈 끝\n{sb}");
        }
    }

    /// <summary>
    /// 플레이어 클립 자세 — 받는 몸짓 후보(Test_Sword_ChangeAction · APose2Crouch)를 0 · 25 · 50 · 75 · 100%에서 한 장(FB_Clip_Poses).
    /// 편집 모드 · 미리보기 씬에서 그린다(열린 씬은 건드리지 않는다). 로그 「[FB1016] 클립 끝」.
    /// </summary>
    [MenuItem("RelicFairy/Debug/10-16 클립 자세 촬영 (편집 모드)")]
    private static void RunClipPoses()
    {
        const int W = 256, H = 384;
        string[] clips =
        {
            "Assets/RelicFairy/Animations/Player/Test_01/Test_Sword_ChangeAction.FBX",
            "Assets/RelicFairy/Characters/Player/Knight/Animations/GhostSamurai_APose2Crouch_Inplace.FBX",
        };
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RelicFairy/Characters/Player/Gawain/Prefabs/PlayerCharacter.prefab");
        if (EditorApplication.isPlaying || prefab == null) { Debug.LogWarning($"{Tag} 클립 끝 — 편집 모드에서 · 플레이어 프리팹 {prefab != null}"); return; }
        Directory.CreateDirectory(ShotDir);
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var sheet = new Texture2D(W * 5, H * clips.Length, TextureFormat.RGBA32, false);
        var rt    = new RenderTexture(W, H, 24);
        var sb    = new StringBuilder();
        try
        {
            var go = Object.Instantiate(prefab);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var anim = go.GetComponentInChildren<Animator>();
            var lightGo = new GameObject("~Light", typeof(Light));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, scene);
            lightGo.GetComponent<Light>().type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            var camGo = new GameObject("~Cam", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.GetComponent<Camera>();
            cam.scene = scene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.32f, 0.33f, 0.36f, 1f);
            cam.fieldOfView = 30f;
            cam.targetTexture = rt;
            camGo.transform.position = new Vector3(1.6f, 1.15f, 3.2f);
            camGo.transform.LookAt(new Vector3(0f, 0.95f, 0f));

            AnimationMode.StartAnimationMode();
            for (int c = 0; c < clips.Length; c++)
            {
                AnimationClip clip = null;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(clips[c]))
                    if (o is AnimationClip ac && !ac.name.StartsWith("__preview__")) { clip = ac; break; }
                if (clip == null || anim == null) { sb.AppendLine($"클립 없음 {clips[c]}"); continue; }
                sb.AppendLine($"{clip.name} 길이 {clip.length:0.00}초 · 반복 {clip.isLooping} · 휴머노이드 {clip.humanMotion}");
                for (int i = 0; i < 5; i++)
                {
                    AnimationMode.BeginSampling();
                    // 첫 줄(받는 몸짓 후보)은 움직임이 앞 0.5초에 몰려 있어 0 · 0.12 · 0.25 · 0.37 · 0.5초로 촘촘히
                    float at = c == 0 ? Mathf.Min(clip.length, 0.125f * i) : clip.length * i / 4f;
                    AnimationMode.SampleAnimationClip(anim.gameObject, clip, at);
                    AnimationMode.EndSampling();
                    cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, W, H), i * W, (clips.Length - 1 - c) * H);
                    RenderTexture.active = prev;
                }
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(ShotDir, "FB_Clip_Poses.png"), sheet.EncodeToPNG());
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            AnimationMode.StopAnimationMode();
            Object.DestroyImmediate(sheet);
            rt.Release();
            Object.DestroyImmediate(rt);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            Debug.Log($"{Tag} 클립 끝\n{sb}");
        }
    }

    /// <summary>
    /// 무형검 받기 — 인트로 연출이 「S4 검을 받다」 컷에 들어오면 0.2초마다 8장(FB_Sword_0~7). 인트로 실측(IntroCine)과 함께 돌린다.
    /// 로그 「[FB1016] 검 끝」.
    /// </summary>
    [MenuItem("RelicFairy/Debug/10-16 무형검 받기 촬영 (인트로, 플레이 중)")]
    private static void RunSwordShots()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만 동작한다."); return; }
        SwordShotsAsync().Forget();
    }

    private static async UniTaskVoid SwordShotsAsync()
    {
        Directory.CreateDirectory(ShotDir);
        int n = 0;
        float until = Time.realtimeSinceStartup + 150f;
        while (IntroMordredDirector.CurrentShot != "S4 검을 받다" && Time.realtimeSinceStartup < until)
            await UniTask.Delay(50, ignoreTimeScale: true);
        if (IntroMordredDirector.CurrentShot == "S4 검을 받다")
            for (; n < 8; n++)
            {
                await ShotAsync($"FB_Sword_{n}");
                await UniTask.Delay(200, ignoreTimeScale: true);
            }
        Debug.Log($"{Tag} 검 끝 — {n}장");
    }

    // ── 런 시작 → 대기방 ─────────────────────────────────────
    private static async UniTask<StartRoomGate> LaunchAsync(int ch, StringBuilder sb)
    {
        var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { sb.AppendLine("테스트 허브가 아니다"); return null; }
        typeof(TestHubLauncher).GetField("_chapter", Any)?.SetValue(launcher, ch);
        typeof(TestHubLauncher).GetField("_bossApproach", Any)?.SetValue(launcher, false);
        if (!launcher.TryLaunch()) { sb.AppendLine("런 시작 실패"); return null; }
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 60f)
        {
            await UniTask.Delay(500, ignoreTimeScale: true);
            foreach (var g in Object.FindObjectsByType<StartRoomGate>(FindObjectsSortMode.None))
                if ((int)(typeof(StartRoomGate).GetField("_fromZoneIndex", Any)?.GetValue(g) ?? 0) == -1
                    && GameRunBootstrapper.Instance?.Run?.Player != null)
                    return g;
        }
        sb.AppendLine("대기방 문을 못 찾음(60초)");
        return null;
    }

    // ── ① 대기방 문 ─────────────────────────────────────────
    private static void MeasureGate(StartRoomGate gate, int ch, StringBuilder sb)
    {
        var t = gate.transform;
        float gw = (float)(typeof(StartRoomGate).GetField("_gateWidth", Any)?.GetValue(gate) ?? 0f);
        float gh = (float)(typeof(StartRoomGate).GetField("_gateHeight", Any)?.GetValue(gate) ?? 0f);
        sb.AppendLine($"[1 대기방 문 — 챕터 {ch}]  문 칸 {gw:0.00} × {gh:0.00} m");

        // 개구부 실제 폭 — 문 평면 높이 1.5 m에서 옆으로 광선, 문 자신(기둥 프리미티브 · 문틀)은 뺀다
        float left = Side(t, -1f), right = Side(t, +1f);
        sb.AppendLine($"  개구부(옆 벽까지) 왼 {left:0.00} · 오른 {right:0.00} → 폭 {left + right:0.00} m");

        var door = t.Find("StartSealDoor");
        if (door != null && LocalBounds(t, door, out var db))
            sb.AppendLine($"  문 x {db.min.x:0.00}~{db.max.x:0.00}(폭 {db.size.x:0.00}) · 높이 {db.min.y:0.00}~{db.max.y:0.00} · 가로 늘림 ×{door.localScale.x:0.00}");
        else sb.AppendLine("  문(StartSealDoor) 없음");

        var frame = t.Find("GateFrame");
        if (frame != null && LocalBounds(t, frame, out var fb))
        {
            sb.AppendLine($"  문틀 x {fb.min.x:0.00}~{fb.max.x:0.00}(폭 {fb.size.x:0.00}) · 높이 ~{fb.max.y:0.00} · 배율 {frame.localScale.x:0.000}");
            var lintel = frame.Find("Lintel");
            if (lintel != null && LocalBounds(t, lintel, out var lb))
                sb.AppendLine($"  상인방 x {lb.min.x:0.00}~{lb.max.x:0.00}(폭 {lb.size.x:0.00}) · 아랫면 {lb.min.y:0.00} · 윗면 {lb.max.y:0.00}");
        }
        else sb.AppendLine("  문틀(GateFrame) 없음");

        var corridor = t.Find("StartGateCorridor");
        float side = 1f;
        if (corridor != null && LocalBounds(t, corridor, out var cb))
        {
            side = Mathf.Sign(cb.center.z == 0f ? 1f : cb.center.z);
            float inL = -99f, inR = 99f;
            foreach (var r in corridor.GetComponentsInChildren<Renderer>())
            {
                var b = ToLocal(t, r.bounds);
                if (b.max.y < 1f || b.min.y > 3f) continue;                 // 벽 높이대만
                if (Mathf.Abs(b.center.z) > 8f) continue;                    // 문 가까운 통로만
                if (b.max.x < 0f) inL = Mathf.Max(inL, b.max.x);
                else if (b.min.x > 0f) inR = Mathf.Min(inR, b.min.x);
            }
            sb.AppendLine($"  통로 안쪽 폭(문 앞 8 m) {(inR < 99f && inL > -99f ? (inR - inL).ToString("0.00") : "?")} m (왼 {inL:0.00} · 오른 {inR:0.00})");
        }

        Render(Path.Combine(ShotDir, $"FB_1_Gate_ch{ch}.png"), t, side);
    }

    private static float Side(Transform gate, float dir)
    {
        var o = gate.position + Vector3.up * 1.5f;
        var hits = Physics.RaycastAll(o, gate.right * dir, 30f, ~0, QueryTriggerInteraction.Ignore);
        float best = 30f;
        foreach (var h in hits)
            if (!h.transform.IsChildOf(gate) && h.distance < best) best = h.distance;
        return best;
    }

    private static bool LocalBounds(Transform root, Transform part, out Bounds b)
    {
        b = default;
        bool any = false;
        foreach (var r in part.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            var lb = ToLocal(root, r.bounds);
            if (!any) { b = lb; any = true; } else b.Encapsulate(lb);
        }
        return any;
    }

    private static Bounds ToLocal(Transform root, Bounds w)
    {
        var b = new Bounds(root.InverseTransformPoint(w.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            var c = w.center + Vector3.Scale(w.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            b.Encapsulate(root.InverseTransformPoint(c));
        }
        return b;
    }

    /// <summary>방 쪽(통로 반대)에서 문을 정면으로 — 1280×720 · 화각 55.</summary>
    private static void Render(string path, Transform gate, float corridorSide)
        => RenderFrom(path, gate.TransformPoint(new Vector3(0f, 2.2f, -corridorSide * 10f)), gate.TransformPoint(new Vector3(0f, 2.8f, 0f)), 55f);

    private static void RenderFrom(string path, Vector3 from, Vector3 at, float fov)
    {
        const int W = 1280, H = 720;
        var go = new GameObject("~FB1016GateCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        var rt = new RenderTexture(W, H, 24);
        try
        {
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
            cam.fieldOfView = fov;
            cam.aspect = (float)W / H;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.cullingMask = ~LayerMask.GetMask("UI");
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        finally
        {
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
        }
    }

    // ── ② 요정의 샘 ─────────────────────────────────────────
    private static async UniTask SpringAsync(StringBuilder sb)
    {
        sb.AppendLine("[2 요정의 샘]");
        var spring = Object.FindFirstObjectByType<ChapterSpring>();
        if (spring == null) { sb.AppendLine("  샘 없음"); return; }
        var boot   = GameRunBootstrapper.Instance;
        var center = (Vector3?)typeof(GameRunBootstrapper).GetField("_waitingRoomCenter", Any)?.GetValue(boot);
        var player = boot?.Run?.Player != null ? boot.Run.Player.transform.position : Vector3.zero;
        var p = spring.transform.position;
        sb.AppendLine($"  샘 {p} · 방 가운데 {(center.HasValue ? center.Value.ToString() : "?")} · 가운데까지 {(center.HasValue ? Flat(p - center.Value).ToString("0.00") : "?")} m · 시작 자리까지 {Flat(p - player):0.00} m");

        // 가득 상태(대기방 첫 진입 그대로) — 닫기 하나짜리 알림 창
        typeof(ChapterSpring).GetMethod("OpenPrompt", Any)?.Invoke(spring, null);
        await UniTask.Delay(600, ignoreTimeScale: true);
        sb.AppendLine($"  가득일 때 [F] 창 열림 {UI_ConfirmDialog.IsOpen}");
        await ShotAsync("FB_2_Spring_full");
        UI_ConfirmDialog.CloseIfOpen();
        await UniTask.Delay(400, ignoreTimeScale: true);

        // 다친 상태 — 체력 55% · 포션 하나 씀 → 마신다 / 닫기
        var stats = boot?.Run?.Player?.RuntimeStats;
        var state = boot?.Run?.PlayerState;
        if (stats != null) stats.SetHp(Mathf.RoundToInt(stats.MaxHp * 0.55f));
        state?.TryConsumePotion();
        typeof(ChapterSpring).GetMethod("OpenPrompt", Any)?.Invoke(spring, null);
        await UniTask.Delay(600, ignoreTimeScale: true);
        sb.AppendLine($"  다쳤을 때 [F] 창 열림 {UI_ConfirmDialog.IsOpen} · 시간 {Time.timeScale:0.##}(창이 열린 동안 0)");
        await ShotAsync("FB_2_Spring");
        UI_ConfirmDialog.CloseIfOpen();
        await UniTask.Delay(400, ignoreTimeScale: true);
        sb.AppendLine($"  닫은 뒤 시간 {Time.timeScale:0.##}");
    }

    private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    // ── ③ ESC → 게임 종료 확인 ───────────────────────────────
    private static async UniTask EscQuitAsync(StringBuilder sb)
    {
        sb.AppendLine("[3 ESC 게임 종료]");
        var esc = Object.FindFirstObjectByType<UI_EscMenu>(FindObjectsInactive.Include);
        if (esc == null) { sb.AppendLine("  UI_EscMenu 없음"); return; }
        try
        {
            esc.Open();
            await UniTask.Delay(400, ignoreTimeScale: true);
            typeof(UI_EscMenu).GetMethod("OnQuit", Any)?.Invoke(esc, null);
            await UniTask.Delay(600, ignoreTimeScale: true);
            sb.AppendLine($"  확인 창 열림 {UI_ConfirmDialog.IsOpen}(곧바로 꺼지면 안 된다)");
            await ShotAsync("FB_3_EscQuit");
        }
        finally
        {
            UI_ConfirmDialog.CloseIfOpen();
            esc.Close();
            await UniTask.Delay(400, ignoreTimeScale: true);
        }
    }

    // ── ④~⑥ 로비 ────────────────────────────────────────────
    private static async UniTask LobbyAsync(StringBuilder sb)
    {
        sb.AppendLine("[4 로비]");
        if (Object.FindFirstObjectByType<UI_Lobby>() == null)
        {
            AppBootstrapper.Instance?.RequestLoad(Define.Scene.Lobby);
            for (int i = 0; i < 300 && Object.FindFirstObjectByType<UI_Lobby>() == null; i++)
                await UniTask.Delay(100, ignoreTimeScale: true);
            await UniTask.Delay(5000, ignoreTimeScale: true);   // 로딩 · 타이틀 페이드가 걷히도록
        }
        var lobby = Object.FindFirstObjectByType<UI_Lobby>();
        if (lobby == null) { sb.AppendLine("  로비(UI_Lobby)를 못 찾음"); return; }
        await ShotAsync("FB_4_Lobby");

        var title = lobby.transform.Find("TitleImage") as RectTransform;
        var start = lobby.transform.Find("MenuPanel/Btn_StartRun") as RectTransform;
        if (title != null && start != null)
        {
            float titleBottom = Corner(title, 0).y;
            // 첫 메뉴 글자 윗변 = 칸 윗변 − 그림 위 여백(Start off 1440×720에서 글자 위 225 px)
            float startTop = Corner(start, 1).y - start.rect.height * (225f / 720f) * Scale(start);
            sb.AppendLine($"  타이틀 아랫변 ↔ Start 글자 윗변 간격 {titleBottom - startTop:0}px(화면) — 양수면 떨어져 있다");
        }
        foreach (var n in new[] { "Btn_StartRun", "Btn_Settings", "Btn_Exit" })
            if (lobby.transform.Find("MenuPanel/" + n) is RectTransform r)
                sb.AppendLine($"  {n} 칸 {r.rect.width:0}×{r.rect.height:0}");

        sb.AppendLine("[5 로비 Exit]");
        typeof(UI_Lobby).GetMethod("OnClickExit", Any)?.Invoke(lobby, null);
        await UniTask.Delay(600, ignoreTimeScale: true);
        sb.AppendLine($"  확인 창 열림 {UI_ConfirmDialog.IsOpen}");
        await ShotAsync("FB_5_LobbyQuit");
        UI_ConfirmDialog.CloseIfOpen();
        await UniTask.Delay(400, ignoreTimeScale: true);

        sb.AppendLine("[6 세이브 슬롯]");
        typeof(UI_Lobby).GetMethod("OnClickStartRun", Any)?.Invoke(lobby, null);
        await UniTask.Delay(900, ignoreTimeScale: true);
        await ShotAsync("FB_6_SaveSlot");
        foreach (var card in Object.FindObjectsByType<UI_SaveSlotCard>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            var lines = new StringBuilder();
            foreach (var f in new[] { "nameText", "progressText", "retryText", "savedAtText" })
                if (typeof(UI_SaveSlotCard).GetField(f, Any)?.GetValue(card) is TMP_Text tx && tx.gameObject.activeInHierarchy)
                    lines.Append($" | {tx.fontSize:0} 「{tx.text.Replace("\n", " / ")}」");
            sb.AppendLine($"  {card.name}{lines}");
        }
    }

    private static Vector3 Corner(RectTransform r, int i)
    {
        var c = new Vector3[4];
        r.GetWorldCorners(c);   // 0 왼아래 · 1 왼위 · 2 오른위 · 3 오른아래 (오버레이 캔버스 = 화면 좌표)
        return c[i];
    }

    private static float Scale(RectTransform r) => r.lossyScale.y;

    // ── 부품 ────────────────────────────────────────────────
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
