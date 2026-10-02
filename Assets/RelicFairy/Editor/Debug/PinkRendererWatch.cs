using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 핑크(마젠타) 렌더러 감시(에디터 전용, 10-01) — 플레이 중 2초마다 활성 렌더러를 훑어 「URP가 제대로 못 그리는」 재질을 잡는다.
///   · 재질 없음 · 셰이더 없음 · 오류 셰이더(Hidden/InternalErrorShader) · 이 플랫폼에서 지원 안 됨
///   · URP 패스가 하나도 없고 옛 LightMode(ForwardBase · Always · Vertex …)만 가진 셰이더 — URP는 이걸 마젠타로 그린다
///   · 디졸브 재질이 3번 연속(6초 이상) 남아 있음 — 방 입장 디졸브 재진입으로 원본을 잃은 경우(09 기록)
/// 새로 잡힐 때마다 한 줄 로그 + 화면 캡처(Logs/pink_watch/). 같은 렌더러 · 재질은 한 번만.
/// </summary>
public static class PinkRendererWatch
{
    private const string MenuPath = "RelicFairy/Debug/Pink Watch (Toggle, Play)";
    private const float  Interval = 2f;

    private static readonly HashSet<string> LegacyModes = new() { "ForwardBase", "ForwardAdd", "Always", "Vertex", "VertexLM", "VertexLMRGBM", "PrepassBase", "PrepassFinal", "Deferred", "ShadowCaster", "Meta", "MotionVectors" };
    private static readonly HashSet<string> UrpModes    = new() { "UniversalForward", "UniversalForwardOnly", "SRPDefaultUnlit", "Universal2D", "UniversalGBuffer" };
    private static readonly ShaderTagId LightMode = new("LightMode");

    private static CancellationTokenSource s_cts;
    private static readonly HashSet<string> s_reported = new();
    private static readonly Dictionary<int, int> s_dissolveSeen = new();
    private static readonly Dictionary<Shader, bool> s_legacyCache = new();

    [MenuItem(MenuPath)]
    public static void Toggle()
    {
        if (s_cts != null) { Stop(); return; }
        if (!Application.isPlaying) { Debug.LogWarning("[PinkWatch] 플레이 모드에서만."); return; }
        s_cts = new CancellationTokenSource();
        s_reported.Clear();
        s_dissolveSeen.Clear();
        WatchAsync(s_cts.Token).Forget();
        Debug.Log("[PinkWatch] 감시 시작 — 2초마다");
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, s_cts != null);
        return true;
    }

    private static void Stop()
    {
        s_cts?.Cancel();
        s_cts?.Dispose();
        s_cts = null;
        Debug.Log($"[PinkWatch] 감시 끝 — 잡힌 것 {s_reported.Count}");
    }

    private static async UniTaskVoid WatchAsync(CancellationToken ct)
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "pink_watch", DateTime.Now.ToString("MMdd_HHmmss"));
        try
        {
            while (Application.isPlaying)
            {
                int found = Scan();
                if (found > 0) await ShotAsync(dir, ct);
                await UniTask.Delay(TimeSpan.FromSeconds(Interval), DelayType.Realtime, cancellationToken: ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!Application.isPlaying && s_cts != null) { s_cts.Dispose(); s_cts = null; }
        }
    }

    private static int Scan()
    {
        int found = 0;
        string room = CurrentRoom();
        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!r.enabled || r is ParticleSystemRenderer psr && !psr.gameObject.activeInHierarchy) continue;
            var mats = r.sharedMaterials;
            bool dissolveNow = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                string why = Diagnose(m);
                if (m != null && m.name.IndexOf("Dissolve", StringComparison.OrdinalIgnoreCase) >= 0) dissolveNow = true;
                if (why == null) continue;
                if (Report(r, m, why, room)) found++;
            }

            int id = r.GetInstanceID();
            if (dissolveNow)
            {
                s_dissolveSeen.TryGetValue(id, out int n);
                s_dissolveSeen[id] = ++n;
                if (n == 3 && Report(r, mats.Length > 0 ? mats[0] : null, "디졸브 재질이 6초 넘게 남음(원본 재질 유실 의심)", room)) found++;
            }
            else s_dissolveSeen.Remove(id);
        }
        return found;
    }

    private static string Diagnose(Material m)
    {
        if (m == null) return "재질 없음";
        var s = m.shader;
        if (s == null) return "셰이더 없음";
        if (s.name == "Hidden/InternalErrorShader") return "오류 셰이더(컴파일 실패 · 누락)";
        if (!s.isSupported) return $"지원 안 되는 셰이더 {s.name}";
        if (IsLegacyOnly(s)) return $"URP 패스 없는 옛 셰이더 {s.name}";
        return null;
    }

    /// <summary>패스 중 URP가 그리는 것이 하나도 없고, 옛 LightMode만 있다 → URP는 마젠타로 그린다.</summary>
    private static bool IsLegacyOnly(Shader s)
    {
        if (s_legacyCache.TryGetValue(s, out bool v)) return v;
        bool anyUrp = false, anyLegacy = false;
        for (int p = 0; p < s.passCount; p++)
        {
            string mode = s.FindPassTagValue(p, LightMode).name;
            if (string.IsNullOrEmpty(mode) || UrpModes.Contains(mode)) { anyUrp = true; break; }
            if (LegacyModes.Contains(mode)) anyLegacy = true;
        }
        v = !anyUrp && anyLegacy;
        s_legacyCache[s] = v;
        return v;
    }

    private static bool Report(Renderer r, Material m, string why, string room)
    {
        string key = r.GetInstanceID() + "/" + (m != null ? m.GetInstanceID() : 0) + "/" + why;
        if (!s_reported.Add(key)) return false;

        string matPath = m != null ? AssetDatabase.GetAssetPath(m) : "";
        string src = PrefabSource(r.gameObject);
        Debug.LogWarning($"[PinkWatch] {why} — 방 {room} · {HierarchyPath(r.transform)} · 재질 {(m != null ? m.name : "-")} {(string.IsNullOrEmpty(matPath) ? "(런타임 재질)" : matPath)}{(string.IsNullOrEmpty(src) ? "" : " · 프리팹 " + src)} · 위치 {r.bounds.center}");
        return true;
    }

    private static string PrefabSource(GameObject go)
    {
        var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
        return src != null ? AssetDatabase.GetAssetPath(src) : "";
    }

    /// <summary>지금 서 있는 방의 pool_key — RunFlowController의 마지막 문 계획에서 읽는다(없으면 「-」).</summary>
    private static string CurrentRoom()
    {
        var flow = RunFlowController.Active;
        if (flow == null) return "-";
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        object plan  = typeof(RunFlowController).GetField("_lastPlan", F)?.GetValue(flow);
        object entry = plan?.GetType().GetField("entry", F)?.GetValue(plan);
        return entry?.GetType().GetField("pool_key", F)?.GetValue(entry) as string ?? "-";
    }

    private static string HierarchyPath(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    // ── 기둥 추적(10-01 · Ch1 상점/정제소 돌기둥 빈 재질) ──
    private static CancellationTokenSource s_traceCts;

    /// <summary>
    /// 방 이름에 refinery · shop이 든 방의 SM_StonePillar_01a LOD0 렌더러 재질 상태를 0.1초마다 보고, 바뀔 때만 한 줄(90초).
    /// 「무엇 → 빈 칸」으로 넘어가는 순간과 그 앞 재질 이름으로 누가 비웠는지 좁힌다.
    /// </summary>
    [MenuItem("RelicFairy/Debug/Pink Trace Pillars (Play)")]
    public static void TracePillars()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[PinkTrace] 플레이 모드에서만."); return; }
        s_traceCts?.Cancel();
        s_traceCts = new CancellationTokenSource();
        TraceAsync(s_traceCts.Token).Forget();
        Debug.Log("[PinkTrace] 기둥 추적 시작 — 90초");
    }

    private static async UniTaskVoid TraceAsync(CancellationToken ct)
    {
        // 렌더러마다 상태 이력(프레임 · 시각 · 재질 · 켜짐 · 디졸브 점유)을 모아 두고, 빈 칸이 되는 순간 그 이력을 통째로 찍는다
        var hist = new Dictionary<int, List<string>>();
        var last = new Dictionary<int, string>();
        var reported = new HashSet<int>();
        float end = Time.realtimeSinceStartup + 90f;
        try
        {
            while (Application.isPlaying && Time.realtimeSinceStartup < end)
            {
                foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (r.name != "SM_StonePillar_01a_LOD0") continue;
                    string path = HierarchyPath(r.transform);
                    if (path.IndexOf("refinery", StringComparison.OrdinalIgnoreCase) < 0 && path.IndexOf("shop", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var mats = r.sharedMaterials;
                    var sb = new StringBuilder();
                    sb.Append(r.enabled ? "켜짐" : "꺼짐").Append(DissolveEffect.IsDissolving(r) ? "·디졸브중" : "").Append(" [");
                    for (int i = 0; i < mats.Length; i++) sb.Append(i > 0 ? "," : "").Append(mats[i] == null ? "NULL" : mats[i].name + "#" + mats[i].GetInstanceID());
                    sb.Append(']');
                    string state = sb.ToString();
                    int id = r.GetInstanceID();
                    if (last.TryGetValue(id, out var prev) && prev == state) continue;
                    last[id] = state;
                    if (!hist.TryGetValue(id, out var h)) hist[id] = h = new List<string>();
                    h.Add($"f{Time.frameCount} t={Time.realtimeSinceStartup:0.000} {state}");
                    if (state.Contains("NULL") && reported.Add(id))
                        Debug.Log($"[PinkTrace] 빈 칸 — {path} || " + string.Join(" || ", h));
                }
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, ct);
            }
            Debug.Log($"[PinkTrace] 기둥 추적 끝 — 빈 칸 {reported.Count}개");
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>게임 화면 한 장(IMGUI 포함) — Logs/captures/. MCP 스크린샷과 달리 도메인 리로드를 부르지 않는다.</summary>
    [MenuItem("RelicFairy/Debug/Capture Game View (Play)")]
    public static void CaptureGameView()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Capture] 플레이 모드에서만."); return; }
        CaptureOnceAsync().Forget();
    }

    private static async UniTaskVoid CaptureOnceAsync()
    {
        try
        {
            string dir = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "Logs", "captures");
            await ShotAsync(dir, CancellationToken.None);
            Debug.Log($"[Capture] 저장 — {dir}");
        }
        catch (Exception e) { Debug.LogWarning($"[Capture] 실패 — {e.Message}"); }
    }

    private static async UniTask ShotAsync(string dir, CancellationToken ct)
    {
        var runner = UnityEngine.Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        Directory.CreateDirectory(dir);
        await UniTask.WaitForEndOfFrame(runner, ct);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(System.IO.Path.Combine(dir, $"{DateTime.Now:HHmmss_fff}_{CurrentRoom()}.png"), tex.EncodeToPNG());
        UnityEngine.Object.Destroy(tex);
    }
}
