#if UNITY_EDITOR
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 10-30 귀환 자리 실측 — 사용자 「다시 스폰할 때 해금 제단 기준으로 카메라가 돌아가 있다 · 기존처럼 입구 기준 + 제단으로 유도하는 레벨디자인」.
/// 거점에 선 직후 카메라 방위 · 제단이 화면 어디에 있나(뷰포트) · 걸어서 거리(내비 경로)를 적고, 그대로 한 장 · 제단 「열 수 있음」을 켜고 한 장 찍는다.
/// 결과 Temp/return_spawn_1030.txt · Temp/ui_shots/RS30_*.png · 로그 「[RS30] 끝」. 거점(BaseCamp) 플레이 중에만.
/// </summary>
public static class ReturnSpawn1030ProbeEditor
{
    private const string Tag     = "[RS30]";
    private const string ShotDir = "Temp/ui_shots";

    [MenuItem("RelicFairy/Debug/10-30 귀환 자리 · 제단 보임 촬영 (거점 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-30 귀환 자리\n");
        var boot = BaseCampBootstrapper.Instance;
        for (int i = 0; i < 120 && (boot == null || !boot.IsReady || boot.Player == null); i++)
        {
            await UniTask.Delay(250, DelayType.Realtime);
            boot = BaseCampBootstrapper.Instance;
        }
        if (boot == null || boot.Player == null) { Finish(sb.AppendLine("플레이어 없음")); return; }
        await UniTask.Delay(1500, DelayType.Realtime);

        var cam    = Camera.main;
        var gcc    = GameCameraController.Instance;
        var player = boot.Player.transform;
        var altar  = Object.FindFirstObjectByType<WorldAwakeningAltar>();
        var crystal = Object.FindFirstObjectByType<MemoryAltarCrystal>();
        sb.AppendLine($"플레이어 {player.position} · 몸 방위 {player.eulerAngles.y:0} · 카메라 방위 {(gcc != null ? gcc.CurrentHeading.ToString("0") : "?")}");
        if (altar != null && cam != null)
        {
            Vector3 a = altar.transform.position;
            Vector3 v = cam.WorldToViewportPoint(a + Vector3.up * 1.5f);
            bool on = v.z > 0f && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f;
            Vector3 flat = a - player.position; flat.y = 0f;
            float bearing = Vector3.SignedAngle(Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up), flat, Vector3.up);
            sb.AppendLine($"제단 {a} · 직선 {flat.magnitude:0.0} m · 카메라 앞 기준 {bearing:0}° · 화면 {(on ? $"안 ({v.x:0.00},{v.y:0.00})" : $"밖 ({v.x:0.00},{v.y:0.00})")}");
            var path = new NavMeshPath();
            if (NavMesh.SamplePosition(player.position, out var ps, 2f, NavMesh.AllAreas) && NavMesh.SamplePosition(a, out var pa, 4f, NavMesh.AllAreas)
                && NavMesh.CalculatePath(ps.position, pa.position, NavMesh.AllAreas, path))
            {
                float len = 0f;
                for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                sb.AppendLine($"걸어서 {len:0.0} m · 꺾임 {path.corners.Length - 2} · 상태 {path.status}");
                for (int i = 0; i < path.corners.Length; i++) sb.AppendLine($"    {i}: {path.corners[i]}");
            }
            else sb.AppendLine("내비 경로 못 구함");
        }
        else sb.AppendLine($"제단 {altar != null} · 카메라 {cam != null}");

        Directory.CreateDirectory(ShotDir);
        var markers = GameObject.Find("BaseCamp_Sanctum")?.transform.Find("Markers");
        if (markers != null) sb.AppendLine($"Markers 위치 {markers.position} · 배율 {markers.lossyScale} · 회전 {markers.eulerAngles}");
        if (altar != null) RenderTop($"{ShotDir}/RS30_top.png", player.position, altar.transform.position, sb);
        ScreenCapture.CaptureScreenshot($"{ShotDir}/RS30_0_spawn.png");
        await UniTask.Delay(500, DelayType.Realtime);
        if (crystal != null)
        {
            crystal.DebugForceReady(true);
            await UniTask.Delay(1500, DelayType.Realtime);
            ScreenCapture.CaptureScreenshot($"{ShotDir}/RS30_1_ready.png");
            await UniTask.Delay(1000, DelayType.Realtime);
            ScreenCapture.CaptureScreenshot($"{ShotDir}/RS30_2_ready.png");
            await UniTask.Delay(300, DelayType.Realtime);
            if (altar != null) RenderTop($"{ShotDir}/RS30_top_ready.png", player.position, altar.transform.position, sb);
            var ps = crystal.transform.Find("~AltarTrail")?.GetComponent<ParticleSystem>();
            sb.AppendLine($"바닥 빛 길: {(ps != null ? $"입자 {ps.particleCount} · 재생 {ps.isPlaying} · 켜짐 {ps.gameObject.activeInHierarchy}" : "없음")} · 수정 위치 {crystal.transform.position} · 수정 켜짐 {crystal.isActiveAndEnabled}");
            const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var trail = typeof(MemoryAltarCrystal).GetField("_trail", Any)?.GetValue(crystal) as Vector3[];
            var core  = typeof(MemoryAltarCrystal).GetField("_core", Any)?.GetValue(crystal) as Renderer[];
            sb.AppendLine($"    점 {(trail != null ? trail.Length : -1)} · 첫 {(trail != null && trail.Length > 0 ? trail[0].ToString() : "-")} · 끝 {(trail != null && trail.Length > 0 ? trail[^1].ToString() : "-")}" +
                          $" · 닿음 {typeof(MemoryAltarCrystal).GetField("_trailReached", Any)?.GetValue(crystal)} · 보낸 {typeof(MemoryAltarCrystal).GetField("_trailEmitted", Any)?.GetValue(crystal)} · 가운데 수정 {(core != null ? core.Length : -1)}");
            if (ps != null)
            {
                var m = ps.main;
                ps.Emit(new ParticleSystem.EmitParams { position = player.position + Vector3.up, applyShapeToPosition = false }, 1);
                int c0 = ps.particleCount;
                await UniTask.Delay(200, DelayType.Realtime);
                sb.AppendLine($"    직접 쏨: 바로 {c0} · 0.2초 뒤 {ps.particleCount} · timeScale {Time.timeScale} · 수명 {m.startLifetime.constantMin:0.00}~{m.startLifetime.constantMax:0.00} · 최대 {m.maxParticles} · 재질 {ps.GetComponent<ParticleSystemRenderer>().sharedMaterial?.name ?? "없음"}");
                crystal.DebugForceReady(true);
                var counts = new StringBuilder();
                bool shot = false;
                for (int i = 0; i < 36; i++)
                {
                    int n = ps.particleCount;
                    counts.Append(n).Append(' ');
                    if (!shot && n >= 8) { ScreenCapture.CaptureScreenshot($"{ShotDir}/RS30_3_trail.png"); shot = true; }
                    await UniTask.Delay(100, DelayType.Realtime);
                }
                sb.AppendLine($"    0.1초마다 입자 수: {counts}· 촬영 {shot}");
            }
            crystal.DebugForceReady(false);
        }
        Finish(sb);
    }

    /// <summary>귀환 자리 ~ 제단을 위에서 — 화면 위 = 카메라 앞(+X · 성문 쪽), 오른쪽 = 게임 화면 오른쪽. 바닥 +3 m 위는 잘라 지붕을 뺀다. 귀환 자리 = 초록 · 제단 = 금색 점.</summary>
    private static void RenderTop(string path, Vector3 spawn, Vector3 altar, StringBuilder sb)
    {
        const int W = 1280, H = 960;
        const float Lift = 40f;
        var go = new GameObject("~RS30TopCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        var rt = new RenderTexture(W, H, 24);
        bool fog = RenderSettings.fog;
        try
        {
            Vector3 c = (spawn + altar) * 0.5f;
            float floorY = Mathf.Min(spawn.y, altar.y);
            go.transform.SetPositionAndRotation(new Vector3(c.x, floorY + Lift, c.z), Quaternion.LookRotation(Vector3.down, Vector3.right));
            cam.orthographic = true;
            cam.orthographicSize = 30f;
            cam.aspect = (float)W / H;
            cam.nearClipPlane = Lift - 3f;
            cam.farClipPlane = Lift + 10f;
            cam.cullingMask = ~LayerMask.GetMask("UI");
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f, 1f);
            cam.targetTexture = rt;
            RenderSettings.fog = false;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            RenderTexture.active = prev;
            Dot(tex, cam.WorldToScreenPoint(spawn), Color.green);
            Dot(tex, cam.WorldToScreenPoint(altar), new Color(1f, 0.8f, 0.3f));
            // 5 m 격자 눈금(월드 x · z가 5의 배수) — 자리 읽기용
            for (int gx = -40; gx <= 40; gx += 5)
            for (int gz = -40; gz <= 40; gz += 5)
            {
                var p = cam.WorldToScreenPoint(new Vector3(Mathf.Round(c.x / 5f) * 5f + gx, floorY, Mathf.Round(c.z / 5f) * 5f + gz));
                if (p.x >= 0 && p.x < W && p.y >= 0 && p.y < H) tex.SetPixel((int)p.x, (int)p.y, Color.white);
            }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            sb.AppendLine($"위에서 본 그림 {path} · 가운데 {c} · 한 화소 = {2f * cam.orthographicSize / H:0.000} m · 위 = +X · 오른쪽 = -Z");
        }
        finally
        {
            RenderSettings.fog = fog;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
        }
    }

    private static void Dot(Texture2D tex, Vector3 p, Color col)
    {
        for (int dx = -6; dx <= 6; dx++)
        for (int dy = -6; dy <= 6; dy++)
        {
            int x = (int)p.x + dx, y = (int)p.y + dy;
            if (x >= 0 && x < tex.width && y >= 0 && y < tex.height) tex.SetPixel(x, y, col);
        }
    }

    private static void Finish(StringBuilder sb)
    {
        File.WriteAllText("Temp/return_spawn_1030.txt", sb.ToString(), new UTF8Encoding(false));
        Debug.Log($"{Tag} 끝\n{sb}");
    }
}
#endif
