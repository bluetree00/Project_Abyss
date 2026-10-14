#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-11 인트로 의자 「멀리서 뜨고 가까이 가면 붙는」 원인 가르기 — 같은 의자를 같은 각도로
/// ① 가까이(6 m) ② 멀리(25 m) ③ 멀리 + LOD0 강제(lodBias 50) ④ 멀리 + 그림자 끔 으로 찍는다.
/// ③에서 붙으면 LOD(바닥 · 의자 단계), ③에서도 뜨고 ④에서 차이가 사라지면 그림자 단계(캐스케이드 바이어스)다.
/// 인트로 아레나가 떠 있는 플레이 중에 실행(인트로 실측 도구로 아레나 장면까지 간 뒤). 메인 카메라는 건드리지 않고 임시 카메라로 RT에 그린다.
/// 결과: Temp/ui_shots/Bench_*.png · Temp/bench_capture.txt · 로그 「[BenchCap] 끝」.
/// </summary>
public static class IntroBenchFloatCaptureEditor
{
    [MenuItem("RelicFairy/Debug/10-11 인트로 의자 거리별 촬영 (플레이 중 · 아레나가 뜬 뒤)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BenchCap] 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("인트로 의자 거리별 촬영\n");
        Camera cam = null; RenderTexture rt = null;
        float lodBias = QualitySettings.lodBias;
        var light = RenderSettings.sun;
        var shadows = light != null ? light.shadows : LightShadows.None;
        try
        {
            var main = Camera.main;
            var groups = Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None)
                .Where(g => g.gameObject.activeInHierarchy && (g.name.Contains("Bench") || g.name.Contains("Chair"))).ToArray();
            sb.AppendLine($"의자류 LODGroup {groups.Length}개 · 메인 카메라 {(main != null ? main.name : "없음")} · 해 {(light != null ? light.name : "없음")}");
            if (groups.Length == 0 || main == null) return;
            // 메인 카메라 화면 안에 보이는 의자 중 8~80 m 거리 것 4개 — 사용자가 「멀리서」 보는 그 시점
            var planes = GeometryUtility.CalculateFrustumPlanes(main);
            Vector3 eye = main.transform.position;
            var picks = groups.Select(g => (g, b: Bounds(g)))
                .Where(x => GeometryUtility.TestPlanesAABB(planes, x.b))
                .Select(x => (x.g, x.b, d: Vector3.Distance(eye, x.b.center)))
                .Where(x => x.d > 8f && x.d < 80f)
                .Where(x => !Physics.Linecast(eye, x.b.center + Vector3.up * 0.3f, ~0, QueryTriggerInteraction.Ignore) || true)
                .OrderBy(x => x.d).Take(4).ToArray();
            sb.AppendLine($"메인 카메라 {eye:F1} · 화면 안 의자(8~80 m) {picks.Length}개");

            rt = new RenderTexture(2560, 1440, 24);
            cam = new GameObject("BenchCapCam").AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.targetTexture = rt;
            cam.enabled = false;

            int k = 0;
            foreach (var (g, b, d) in picks)
            {
                k++;
                Vector3 target = b.center;
                Vector3 dir = (target - eye).normalized;
                sb.AppendLine($"■ {k} {g.name} · 거리 {d:0.0} m · 바닥 y {b.min.y:0.00} · 현재 LOD 화면 높이 {ScreenHeight(main, g):0.000}");
                await Shot(cam, rt, eye, target, $"Bench{k}_far");
                QualitySettings.lodBias = 50f;
                await Shot(cam, rt, eye, target, $"Bench{k}_far_lod0");
                QualitySettings.lodBias = lodBias;
                if (light != null) light.shadows = LightShadows.None;
                await Shot(cam, rt, eye, target, $"Bench{k}_far_noshadow");
                if (light != null) light.shadows = shadows;
                await Shot(cam, rt, target - dir * 5f, target, $"Bench{k}_near");
            }
            sb.AppendLine("찍음: _near · _far · _far_lod0 · _far_noshadow");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            QualitySettings.lodBias = lodBias;
            if (light != null) light.shadows = shadows;
            if (cam != null) Object.Destroy(cam.gameObject);
            if (rt != null) rt.Release();
            File.WriteAllText("Temp/bench_capture.txt", sb.ToString());
            Debug.Log("[BenchCap] 끝\n" + sb);
        }
    }

    private static Bounds Bounds(LODGroup g)
    {
        var rs = g.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>LODGroup이 화면에서 차지하는 상대 높이(LOD 고르는 값의 근사).</summary>
    private static float ScreenHeight(Camera c, LODGroup g)
    {
        float d = Vector3.Distance(c.transform.position, g.transform.TransformPoint(g.localReferencePoint));
        float size = g.size * Mathf.Max(g.transform.lossyScale.x, g.transform.lossyScale.y, g.transform.lossyScale.z);
        return size / (2f * d * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad)) * QualitySettings.lodBias;
    }

    private static async UniTask Shot(Camera cam, RenderTexture rt, Vector3 from, Vector3 target, string name)
    {
        cam.transform.position = from;
        cam.transform.rotation = Quaternion.LookRotation(target - from);
        await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        Directory.CreateDirectory("Temp/ui_shots");
        File.WriteAllBytes($"Temp/ui_shots/{name}.png", tex.EncodeToPNG());
        Object.Destroy(tex);
    }
}
#endif
