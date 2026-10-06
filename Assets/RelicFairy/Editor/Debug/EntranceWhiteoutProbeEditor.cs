#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 10-06 전투방 입구 화면 과노출 실측 — 입장 직후 게임 카메라 화면이 하얗게 번졌다(빛줄기를 고쳐도 그대로).
/// 카메라 둘레 광원 · 가까운 렌더러 · 후처리(블룸 · 노출)를 적고, A(그대로) · B(카메라 20 m 안 점/스포트 광원 끔) ·
/// C(카메라 5 m 안 렌더러 끔) 세 장을 찍어 범인을 가린다. 끈 것은 바로 되돌린다.
/// 결과: Temp/whiteout_probe.txt · Temp/ui_shots/White_{A|B|C}.png · 로그 「[Whiteout] 끝」.
/// </summary>
public static class EntranceWhiteoutProbeEditor
{
    [MenuItem("RelicFairy/Debug/10-06 입구 과노출 실측 (전투방 입장 직후, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) return;
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("입구 과노출 실측\n");
        var lights = new List<Light>();
        var rends  = new List<Renderer>();
        try
        {
            var cam = Camera.main;
            if (cam == null) { sb.AppendLine("메인 카메라 없음"); return; }
            Vector3 cp = cam.transform.position;
            sb.AppendLine($"카메라 {cp:F1} · 앞 {cam.transform.forward:F2} · HDR {cam.allowHDR}");

            // 광원 — 가까운 순
            var all = Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                            .Where(l => l.isActiveAndEnabled)
                            .OrderBy(l => (l.transform.position - cp).sqrMagnitude).ToList();
            sb.AppendLine($"켜진 광원 {all.Count}");
            foreach (var l in all.Take(14))
                sb.AppendLine($"  {Path(l.transform)} · {l.type} · 세기 {l.intensity:0.##} · 범위 {l.range:0.#} · 색 {l.color} · 거리 {(l.transform.position - cp).magnitude:0.0}");

            // 가까운 렌더러
            var near = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                             .Where(r => r.enabled && r.isVisible && !(r is ParticleSystemRenderer)
                                         && r.bounds.SqrDistance(cp) < 5f * 5f).ToList();
            sb.AppendLine($"카메라 5 m 안 렌더러 {near.Count}");
            foreach (var r in near.Take(14))
                sb.AppendLine($"  {Path(r.transform)} · {(r.sharedMaterial != null ? r.sharedMaterial.name + " / " + r.sharedMaterial.shader.name : "-")} · 거리 {Mathf.Sqrt(r.bounds.SqrDistance(cp)):0.0}");

            // 후처리
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (!v.isActiveAndEnabled || v.sharedProfile == null) continue;
                sb.Append($"볼륨 {Path(v.transform)} · 전역 {v.isGlobal} · 가중 {v.weight:0.##} · {v.sharedProfile.name}:");
                foreach (var c in v.sharedProfile.components)
                {
                    if (!c.active) continue;
                    sb.Append(' ').Append(c.GetType().Name);
                    foreach (var p in c.parameters)
                        if (p.overrideState && (p.GetType().Name.Contains("Float") || p.GetType().Name.Contains("Clamped")))
                        {
                            var val = p.GetType().GetProperty("value")?.GetValue(p);
                            sb.Append($"[{val}]");
                            break;
                        }
                }
                sb.AppendLine();
            }

            Directory.CreateDirectory("Temp/ui_shots");
            await Shot("Temp/ui_shots/White_A.png");

            lights = all.Where(l => l.type != LightType.Directional && (l.transform.position - cp).sqrMagnitude < 20f * 20f).ToList();
            foreach (var l in lights) l.enabled = false;
            await Shot("Temp/ui_shots/White_B.png");
            foreach (var l in lights) if (l != null) l.enabled = true;
            sb.AppendLine($"B: 점/스포트 광원 {lights.Count}개 끔");
            lights.Clear();

            rends = near;
            foreach (var r in rends) r.enabled = false;
            await Shot("Temp/ui_shots/White_C.png");
            foreach (var r in rends) if (r != null) r.enabled = true;
            sb.AppendLine($"C: 가까운 렌더러 {rends.Count}개 끔");
            rends.Clear();
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var l in lights) if (l != null) l.enabled = true;
            foreach (var r in rends) if (r != null) r.enabled = true;
            File.WriteAllText("Temp/whiteout_probe.txt", sb.ToString());
            Debug.Log("[Whiteout] 끝\n" + sb);
        }
    }

    private static async UniTask Shot(string path)
    {
        await UniTask.Delay(250, ignoreTimeScale: true);
        ScreenCapture.CaptureScreenshot(path);
        await UniTask.Delay(250, ignoreTimeScale: true);
    }

    private static string Path(Transform t)
    {
        var s = t.name;
        for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}
#endif
