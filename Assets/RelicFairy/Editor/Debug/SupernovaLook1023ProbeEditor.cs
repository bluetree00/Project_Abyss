#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-23 사용자 「서약의 초신성 폭발 이펙트가 너무 크다」 — 플레이어 앞에 판정 반경 고리(흰 선)를 깔고 초신성 VFX를 같은 길(CovenantFxService.Burst)로 터뜨려 찍는다.
/// 판정 3.5 m(기본) · 5.5 m(적신 적이 많을 때 상한). 입자마다 거리로 보이는 양 90% 도달 · 알파 0.2 이상 최대 반경을 잰다.
/// 이름표 = Temp/nova23_tag.txt. 결과 Temp/nova23.txt · Temp/ui_shots/NOVA23_*.png · 로그 「[Nova23] 끝」.
/// </summary>
public static class SupernovaLook1023ProbeEditor
{
    private const string Tag = "[Nova23]";

    [MenuItem("RelicFairy/Debug/10-23 서약 초신성 이펙트 크기 촬영 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string tag = File.Exists("Temp/nova23_tag.txt") ? File.ReadAllText("Temp/nova23_tag.txt").Trim() : "shot";
        var sb = new StringBuilder($"[{tag}] 서약 초신성 이펙트 크기\n");
        var hub = Object.FindFirstObjectByType<TestHubLauncher>();
        bool hubWas = hub != null && hub.enabled;
        if (hub != null) hub.enabled = false;
        try
        {
            CovenantFxService.Preload();
            await Wait(1.5f);
            var cam = Camera.main;
            var player = Object.FindFirstObjectByType<PlayerController>();
            Vector3 fwd = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 at = (player != null ? player.transform.position : Vector3.zero) + fwd * 7f;

            foreach (float radius in new[] { 3.5f, 5.5f })
            {
                var ring = MakeRing(at, radius);
                var before = new HashSet<ParticleSystem>(Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None));
                CovenantFxService.Burst("supernova", at, radius);
                float maxReach = 0f, maxEdge = 0f;
                foreach (float t in new[] { 0.15f, 0.35f, 0.6f })
                {
                    await Wait(t - (t == 0.15f ? 0f : (t == 0.35f ? 0.15f : 0.35f)));
                    var (reach, edge) = MeasureNew(before, at);
                    maxReach = Mathf.Max(maxReach, reach); maxEdge = Mathf.Max(maxEdge, edge);
                    sb.AppendLine($"  판정 {radius} m · {t:0.00}초 — 보이는 양 90% {reach:0.0} m · 최대 {edge:0.0} m");
                    if (t < 0.5f) await Shot($"NOVA23_{tag}_{radius:0.0}_{t:0.00}");
                }
                sb.AppendLine($"  ▶ 판정 {radius} m — 구간 최대: 90% {maxReach:0.0} m({maxReach / radius:0.00}배) · 최대 {maxEdge:0.0} m({maxEdge / radius:0.00}배)");
                Object.Destroy(ring);
                await Wait(2.2f);
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (hub != null) hub.enabled = hubWas;
            File.AppendAllText("Temp/nova23.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }

    /// <summary>새로 켜진 입자계의 입자만 — 거리 가중(알파 × 크기²) 90% 도달 · 알파 0.2 이상 입자의 가장 먼 가장자리.</summary>
    private static (float reach, float edge) MeasureNew(HashSet<ParticleSystem> before, Vector3 origin)
    {
        var buf = new ParticleSystem.Particle[512];
        var w = new List<Vector2>();
        float total = 0f, edge = 0f;
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
        {
            if (before.Contains(ps) && !ps.isPlaying) continue;
            if ((ps.transform.position - origin).sqrMagnitude > 30f * 30f) continue;
            if (!ps.TryGetComponent(out ParticleSystemRenderer pr) || !pr.enabled) continue;
            int n = Mathf.Min(ps.particleCount, buf.Length);
            if (n == 0) continue;
            n = ps.GetParticles(buf, n);
            bool local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            float s = Mathf.Abs(ps.transform.lossyScale.x);
            for (int k = 0; k < n; k++)
            {
                Vector3 p = local ? ps.transform.TransformPoint(buf[k].position) : buf[k].position;
                float size = buf[k].GetCurrentSize(ps) * s;
                float a = buf[k].GetCurrentColor(ps).a / 255f;
                Vector3 d = p - origin; d.y = 0f;
                float r = d.magnitude + size * 0.5f;
                float wt = a * size * size;
                total += wt; w.Add(new Vector2(r, wt));
                if (a >= 0.2f) edge = Mathf.Max(edge, r);
            }
        }
        if (total <= 0f) return (0f, edge);
        w.Sort((x, y) => x.x.CompareTo(y.x));
        float acc = 0f;
        foreach (var v in w) { acc += v.y; if (acc >= total * 0.9f) return (v.x, edge); }
        return (w[w.Count - 1].x, edge);
    }

    private static GameObject MakeRing(Vector3 at, float radius)
    {
        var go = new GameObject("Nova23_Ring");
        var lr = go.AddComponent<LineRenderer>();
        lr.loop = true; lr.positionCount = 64; lr.widthMultiplier = 0.06f; lr.useWorldSpace = true;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = Color.white;
        for (int i = 0; i < 64; i++)
        {
            float a = i / 64f * Mathf.PI * 2f;
            lr.SetPosition(i, at + new Vector3(Mathf.Cos(a) * radius, 0.08f, Mathf.Sin(a) * radius));
        }
        return go;
    }

    private static UniTask Wait(float s) => UniTask.Delay((int)(Mathf.Max(0f, s) * 1000), DelayType.Realtime);

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
