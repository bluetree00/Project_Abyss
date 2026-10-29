#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-26 사용자 「카메라를 넓힌 상태에서 챕터2 보스의 메테오 가이드라인이 안 보이는 문제」 — 화룡 전투 중(전투 시뮬과 같이)
/// 카메라 거리 · 높이를 최대 · 기본으로 번갈아 두고, 메테오 예고 칸(…MeteorWarn · FireballWarn)이 뜰 때마다 사진 한 장과
/// 칸 렌더러 상태(켜짐 · 보임 · 알파 · 순서 · 카메라 거리)와 그 칸을 덮는 반투명 렌더러(정렬 거리)를 남긴다.
/// 결과: Logs/boss_fight_sim/meteor_guide_view_*/ · 로그 「[MeteorGuideView] 끝」.
/// </summary>
public static class MeteorGuideViewProbeEditor
{
    private const int EventsPerSetting = 3;

    [MenuItem("RelicFairy/Boss/10-26 화룡 메테오 예고 — 카메라 거리별 (Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[MeteorGuideView] 플레이 모드(화룡 전투)에서"); return; }
        RunAsync(-1).Forget();
    }

    // 빌드(Standalone 기본)는 품질 1 「Balanced」(lodBias 1) — 에디터 2 「High Fidelity」(lodBias 2)에선 재현이 안 됐다(10-26 ca 단서)
    [MenuItem("RelicFairy/Boss/10-26 화룡 메테오 예고 — 카메라 거리별 · 빌드 품질 (Play)")]
    private static void RunBuildQuality()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[MeteorGuideView] 플레이 모드(화룡 전투)에서"); return; }
        RunAsync(1).Forget();
    }

    // 10-26 ca 보충 「카메라가 확장되었을 때」 — 화룡 공중 선회 중 탑다운(맵 전체가 들어오는 높이 · 바로 아래로)에서 흩뿌림 예고를 본다.
    // 지수제곱 안개(Ch2 0.018)는 카메라 거리 10 m에선 3 %지만 탑다운 높이에선 바닥을 거의 안개색으로 덮는다 → 안개 켜고 · 끄고 사진과 화면 픽셀 대비.
    [MenuItem("RelicFairy/Boss/10-26 화룡 메테오 예고 — 탑다운 카메라 (Play)")]
    private static void RunTopDown()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[MeteorGuideView] 플레이 모드(화룡 전투)에서"); return; }
        RunTopDownAsync().Forget();
    }

    private static async UniTaskVoid RunTopDownAsync()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim", $"meteor_guide_topdown_{DateTime.Now:MMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("화룡 메테오 예고 — 탑다운 카메라(공중 선회와 같은 ActivateDragonTopDownView)\n");
        var camCtl = GameCameraController.Instance;
        bool fog0 = RenderSettings.fog;
        try
        {
            if (camCtl == null) { sb.AppendLine("GameCameraController 없음"); return; }
            var floor = new Vector2(RelicFairy.Monster.DragonBossRoomContext.Width * RelicFairy.Monster.DragonBossRoomContext.CellSize,
                                    RelicFairy.Monster.DragonBossRoomContext.Height * RelicFairy.Monster.DragonBossRoomContext.CellSize);
            camCtl.ActivateDragonTopDownView(RelicFairy.Monster.DragonBossRoomContext.WorldCenter, floor);
            await UniTask.Delay(2000, DelayType.Realtime);
            var cam = Camera.main;
            sb.AppendLine($"바닥 {floor.x}×{floor.y} m · 가운데 {RelicFairy.Monster.DragonBossRoomContext.WorldCenter} · 카메라 {cam.transform.position} 각 {cam.transform.eulerAngles} · FOV {cam.fieldOfView:0.0} · 화면비 {cam.aspect:0.00} · 근 {cam.nearClipPlane} 먼 {cam.farClipPlane:0}");
            sb.AppendLine($"안개 {(RenderSettings.fog ? "켬" : "끔")} · {RenderSettings.fogMode} · 밀도 {RenderSettings.fogDensity} · 색 #{ColorUtility.ToHtmlStringRGB(RenderSettings.fogColor)}");
            int events = 0;
            float until = Time.realtimeSinceStartup + 150f;
            while (events < EventsPerSetting && Time.realtimeSinceStartup < until)
            {
                var tiles = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                    .Where(r => r.gameObject.activeInHierarchy && r.enabled && r.name == "PassiveMeteorWarn" && r.sharedMaterial != null && r.sharedMaterial.color.a > 0.2f)
                    .ToList();
                if (tiles.Count == 0) { await UniTask.Delay(50, DelayType.Realtime); continue; }
                cam = Camera.main;
                // 화룡 패턴이 끝나며 탑다운을 풀었으면 다시 올린다
                if (cam.transform.eulerAngles.x < 85f)
                {
                    camCtl.ActivateDragonTopDownView(RelicFairy.Monster.DragonBossRoomContext.WorldCenter, floor);
                    await UniTask.Delay(1500, DelayType.Realtime);
                    continue;
                }
                var tilePos = tiles.Select(r => r.bounds.center).ToList();
                var samples = tiles.Take(6).Select(r =>
                {
                    Vector3 c = r.bounds.center;
                    // 둘레 칸 중 예고가 없는 바닥 한 점
                    Vector3 bg = c;
                    foreach (var o in new[] { new Vector3(2.5f, 0, 0), new Vector3(-2.5f, 0, 0), new Vector3(0, 0, 2.5f), new Vector3(0, 0, -2.5f), new Vector3(4f, 0, 4f), new Vector3(-4f, 0, -4f) })
                        if (tilePos.All(p => Vector3.Distance(p, c + o) > 0.9f)) { bg = c + o; break; }
                    return (r, c, bg, a: r.sharedMaterial.color.a);
                }).ToList();
                // 0 = 그대로 · 1 = 유니티 안개 끔 · 2 = 유니티 안개 + 부토 볼류메트릭 안개 끔(볼륨 profile 복제본만 — 에셋은 안 건드린다)
                var shots = new Texture2D[3];
                string[] tags = { "그대로", "안개끔", "안개부토끔" };
                for (int k = 0; k < 3; k++)
                {
                    RenderSettings.fog = k == 0 && fog0;
                    if (k == 2) SetButo(false);
                    await UniTask.WaitForEndOfFrame(camCtl);
                    if (k > 0) await UniTask.WaitForEndOfFrame(camCtl);   // 끈 뒤 한 프레임 더
                    shots[k] = ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(Path.Combine(dir, $"탑다운_{events}_{tags[k]}.png"), shots[k].EncodeToPNG());
                }
                RenderSettings.fog = fog0;
                SetButo(true);
                float d = Vector3.Distance(cam.transform.position, samples[0].c);
                float f = RenderSettings.fogMode == FogMode.ExponentialSquared ? Mathf.Exp(-Mathf.Pow(RenderSettings.fogDensity * d, 2f))
                        : RenderSettings.fogMode == FogMode.Exponential ? Mathf.Exp(-RenderSettings.fogDensity * d)
                        : Mathf.Clamp01((RenderSettings.fogEndDistance - d) / Mathf.Max(0.01f, RenderSettings.fogEndDistance - RenderSettings.fogStartDistance));
                var alphas = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                    .Where(r => r.enabled && r.name == "PassiveMeteorWarn" && r.sharedMaterial != null).Select(r => r.sharedMaterial.color.a).ToList();
                sb.AppendLine($"  {events}: 흩뿌림 칸 알파 가장 옅은 {alphas.DefaultIfEmpty(0f).Min():0.00} · 가장 진한 {alphas.DefaultIfEmpty(0f).Max():0.00} · 탑다운 {camCtl.IsDragonTopDownActive}");
                sb.AppendLine($"  {events}: 예고 칸 {tiles.Count} · 카메라 거리 {d:0.0} m · 안개 남김(1 = 맑음) {f:0.00} → 바닥이 안개색 {(1f - f) * 100f:0}%");
                foreach (var (r, c, bg, a) in samples)
                {
                    Vector3 sp = cam.WorldToScreenPoint(c), sbg = cam.WorldToScreenPoint(bg);
                    if (sp.z <= 0 || sp.x < 0 || sp.y < 0 || sp.x >= shots[0].width || sp.y >= shots[0].height) continue;
                    string Px(Texture2D t, Vector3 p) { var col = t.GetPixel((int)p.x, (int)p.y); return $"#{ColorUtility.ToHtmlStringRGB(col)}"; }
                    float Diff(Texture2D t) { var x = t.GetPixel((int)sp.x, (int)sp.y); var y = t.GetPixel((int)sbg.x, (int)sbg.y); return Mathf.Abs(x.r - y.r) + Mathf.Abs(x.g - y.g) + Mathf.Abs(x.b - y.b); }
                    sb.AppendLine($"      칸 {c} 알파 {a:0.00} · " + string.Join(" · ", Enumerable.Range(0, 3).Select(k => $"{tags[k]}: 칸 {Px(shots[k], sp)} 바닥 {Px(shots[k], sbg)} 차이 {Diff(shots[k]):0.00}")));
                }
                foreach (var t in shots) UnityEngine.Object.Destroy(t);
                events++;
                await UniTask.Delay(2500, DelayType.Realtime);
            }
            if (events == 0) sb.AppendLine("150초 안에 흩뿌림 예고가 안 떴다");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            RenderSettings.fog = fog0;
            SetButo(true);
            camCtl?.DeactivateDragonTopDownView(0.3f);
            File.WriteAllText(Path.Combine(dir, "view.txt"), sb.ToString(), Encoding.UTF8);
            Debug.Log($"[MeteorGuideView] 끝 — {dir}\n{sb}");
        }
    }

    private static void SetButo(bool on)
    {
        foreach (var v in UnityEngine.Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None))
        {
            if (v.sharedProfile == null) continue;
            foreach (var c in v.profile.components)   // profile = 이 볼륨만의 런타임 복제본
                if (c.GetType().Name == "ButoVolumetricFog") c.active = on;
        }
    }

    private static async UniTaskVoid RunAsync(int quality)
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim", $"meteor_guide_view_{DateTime.Now:MMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("화룡 메테오 예고 — 카메라 거리별\n");
        float d0 = CameraUserSettings.Distance, h0 = CameraUserSettings.Height;
        int q0 = QualitySettings.GetQualityLevel();
        try
        {
            if (quality >= 0) QualitySettings.SetQualityLevel(quality, true);
            sb.AppendLine($"품질 {QualitySettings.GetQualityLevel()} 「{QualitySettings.names[QualitySettings.GetQualityLevel()]}」 · lodBias {QualitySettings.lodBias} · maximumLODLevel {QualitySettings.maximumLODLevel}");
            foreach (var (label, d, h) in new[] { ("최대", CameraUserSettings.MaxDistance, CameraUserSettings.MaxHeight), ("기본", 1f, 1f) })
            {
                CameraUserSettings.SetDistance(d);
                CameraUserSettings.SetHeight(h);
                sb.AppendLine($"■ 카메라 {label} — 거리 {d} · 높이 {h}");
                await UniTask.Delay(1500, DelayType.Realtime);
                int events = 0;
                float until = Time.realtimeSinceStartup + 150f;
                while (events < EventsPerSetting && Time.realtimeSinceStartup < until)
                {
                    var tiles = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                        .Where(r => r.gameObject.activeInHierarchy && r.enabled && r.name.EndsWith("Warn") && r.sharedMaterial != null && r.sharedMaterial.color.a > 0.15f)
                        .ToList();
                    if (tiles.Count == 0) { await UniTask.Delay(100, DelayType.Realtime); continue; }
                    var cam = Camera.main;
                    if (cam == null) { await UniTask.Delay(100, DelayType.Realtime); continue; }
                    // 시야 안 칸 중 그려진 것 — 그려지지 않은 칸은 무엇에 가렸는지 광선으로
                    var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                    var inView = tiles.Where(r => GeometryUtility.TestPlanesAABB(planes, r.bounds)).ToList();
                    if (inView.Count == 0) { await UniTask.Delay(100, DelayType.Realtime); continue; }
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);   // 이 프레임 그리기 결과(isVisible)를 읽는다
                    var hidden = inView.Where(r => r != null && !r.isVisible).ToList();
                    string shot = $"{label}_{events}.png";
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, shot));
                    sb.AppendLine($"  {events}: 시야 안 칸 {inView.Count}/{tiles.Count} · 그려짐 {inView.Count - hidden.Count} · 안 그려짐 {hidden.Count} · 카메라 먼 면 {cam.farClipPlane:0} · 가림 컬링 {cam.useOcclusionCulling} · 사진 {shot}");
                    foreach (var r in hidden.Take(3))
                    {
                        Vector3 c = r.bounds.center, from = cam.transform.position;
                        var block = Physics.RaycastAll(from, (c - from).normalized, Vector3.Distance(from, c) - 0.05f, ~0, QueryTriggerInteraction.Ignore)
                                           .OrderBy(h => h.distance).Select(h => $"{h.collider.name}({LayerMask.LayerToName(h.collider.gameObject.layer)}) {h.distance:0.0} m").Take(3);
                        sb.AppendLine($"      안 그려진 {r.name} {c} · 카메라 거리 {Vector3.Distance(from, c):0.0} · 막는 것 {string.Join(" · ", block.DefaultIfEmpty("없음"))} · 층 {LayerMask.LayerToName(r.gameObject.layer)} · 컬링 마스크 포함 {(cam.cullingMask & (1 << r.gameObject.layer)) != 0}");
                    }
                    // 깊이로 가림 — 칸 아래 바닥 LODGroup의 단계별 윗면 높이 · 지금 그려지는 단계(isVisible은 절두체만 본다)
                    foreach (var r in inView.Take(4))
                    {
                        Vector3 c = r.bounds.center;
                        var groups = UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None)
                            .Where(g =>
                            {
                                var lods = g.GetLODs();
                                return lods.Length > 0 && lods[0].renderers.Any(x => x != null && x.bounds.min.x <= c.x && x.bounds.max.x >= c.x && x.bounds.min.z <= c.z && x.bounds.max.z >= c.z && x.bounds.max.y < c.y + 1f);
                            })
                            .Take(2);
                        foreach (var g in groups)
                        {
                            var lods = g.GetLODs();
                            string tops = string.Join(" · ", lods.Select((l, i) =>
                            {
                                var rs = l.renderers.Where(x => x != null).ToList();
                                float top = rs.Count == 0 ? float.NaN : rs.Max(x => x.bounds.max.y);
                                bool drawn = rs.Any(x => x.isVisible);
                                return $"LOD{i} 위 {top:0.00}{(drawn ? "(그려짐)" : "")}";
                            }));
                            sb.AppendLine($"      칸 {r.name} 높이 {r.bounds.max.y:0.00} · 아래 바닥 {g.name} — {tops}");
                        }
                    }
                    var t = inView[0];
                    var tc = t.bounds.center;
                    sb.AppendLine($"  {events}: 칸 {tiles.Count}개 · {t.name} {tc} · 알파 {t.sharedMaterial.color.a:0.00} · 보임 {t.isVisible} · 순서 {t.sharedMaterial.renderQueue}/{t.sortingOrder} · 카메라 {cam?.transform.position} 거리 {(cam != null ? Vector3.Distance(cam.transform.position, tc) : -1f):0.0} · 사진 {shot}");
                    if (cam != null)
                    {
                        float tileDist = Vector3.Distance(cam.transform.position, tc);
                        var covers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                            .Where(r => r != t && r.enabled && r.gameObject.activeInHierarchy && r.isVisible && r.sharedMaterial != null
                                        && r.sharedMaterial.renderQueue >= 2450 && !(r is ParticleSystemRenderer) && !r.name.EndsWith("Warn")
                                        && r.bounds.min.x <= tc.x && r.bounds.max.x >= tc.x && r.bounds.min.z <= tc.z && r.bounds.max.z >= tc.z
                                        && r.bounds.max.y >= tc.y - 0.5f)
                            .Select(r => (r, dist: Vector3.Distance(cam.transform.position, r.bounds.center)))
                            .OrderBy(x => x.dist).Take(8);
                        foreach (var (r, dist) in covers)
                            sb.AppendLine($"      덮는 반투명 {r.name}({r.sharedMaterial.shader.name} · 순서 {r.sharedMaterial.renderQueue}/{r.sortingOrder}) 중심 거리 {dist:0.0}{(dist < tileDist ? " < 칸 → 칸 뒤에 그림(위에 덮음)" : "")} · 범위 {r.bounds.min:F1}~{r.bounds.max:F1}");
                    }
                    events++;
                    await UniTask.Delay(2500, DelayType.Realtime);
                }
                if (events == 0) sb.AppendLine("  150초 안에 예고 칸이 안 떴다");
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            CameraUserSettings.SetDistance(d0);
            CameraUserSettings.SetHeight(h0);
            if (quality >= 0) QualitySettings.SetQualityLevel(q0, true);
            File.WriteAllText(Path.Combine(dir, "view.txt"), sb.ToString(), Encoding.UTF8);
            Debug.Log($"[MeteorGuideView] 끝 — {dir}\n{sb}");
        }
    }
}
#endif
