using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 도구] VFX 후보 프리팹을 미리보기 전용 씬(PreviewRenderUtility)에 띄워 세 시점으로 렌더링하고,
/// 파티클이 실제로 퍼진 반경(살아 있는 파티클 위치 + 크기 절반)을 잰다. 활성 씬은 건드리지 않는다.
/// 카메라는 고정이다(화면 폭 약 ±3.9m) — 바닥의 흰 점은 반경 1m, 노란 점은 반경 3m 눈금.
/// 입력: Temp/vfx_candidates.txt (한 줄에 "프리팹 경로[|배율[|hier[|t=0.5;2;5]]]" — hier면 모든 파티클의 배율 모드를 Hierarchy로 바꿔 띄운다, t=는 찍는 시각 셋)
/// 출력: Temp/vfx_preview/NN_t.png · Temp/vfx_preview/index.json
/// </summary>
public static class CovenantVfxPreviewEditor
{
    private const int   Size = 256;
    private static readonly float[] Times = { 0.15f, 0.45f, 1.0f };
    private static readonly Vector3 CamPos    = new Vector3(0f, 7f, -10f);
    private static readonly Vector3 CamTarget = new Vector3(0f, 0.5f, 0f);

    [MenuItem("RelicFairy/Debug/VFX 후보 미리보기")]
    private static void Run()
    {
        string[] paths = File.ReadAllLines(Path.Combine("Temp", "vfx_candidates.txt"));
        string outDir = Path.Combine("Temp", "vfx_preview");
        Directory.CreateDirectory(outDir);

        var sb  = new StringBuilder("[");
        var pru = new PreviewRenderUtility();
        try
        {
            var cam = pru.camera;
            cam.fieldOfView     = 35f;
            cam.nearClipPlane   = 0.05f;
            cam.farClipPlane    = 300f;
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.10f, 0.12f, 1f);
            pru.lights[0].intensity          = 1.2f;
            pru.lights[0].transform.rotation = Quaternion.Euler(50f, 50f, 0f);
            cam.transform.position = CamPos;
            cam.transform.LookAt(CamTarget);
            AddRing(pru, 1f, 4, Color.white);
            AddRing(pru, 3f, 12, Color.yellow);

            for (int i = 0; i < paths.Length; i++)
            {
                string[] parts = paths[i].Trim().Split('|');
                string path = parts[0];
                if (path.Length == 0) continue;
                float scale = parts.Length > 1 ? float.Parse(parts[1], CultureInfo.InvariantCulture) : 1f;
                bool  hier  = parts.Length > 2 && parts[2] == "hier";
                float[] times = Times;
                if (parts.Length > 3 && parts[3].StartsWith("t="))
                {
                    string[] ts = parts[3].Substring(2).Split(';');
                    times = new float[ts.Length];
                    for (int k = 0; k < ts.Length; k++) times[k] = float.Parse(ts[k], CultureInfo.InvariantCulture);
                }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (i > 0) sb.Append(',');
                sb.Append("{\"i\":").Append(i + 1).Append(",\"path\":\"").Append(path.Replace("\\", "/")).Append('"')
                  .Append(",\"runScale\":").Append(F(scale)).Append(",\"forceHier\":").Append(hier ? "true" : "false");
                if (prefab == null) { sb.Append(",\"error\":\"load\"}"); continue; }

                var go = pru.InstantiatePrefabInScene(prefab);
                go.transform.position   = Vector3.zero;
                go.transform.localScale = prefab.transform.localScale * scale;   // ElementVfxPlayer.Spawn과 같은 방식
                var systems = go.GetComponentsInChildren<ParticleSystem>(true);
                if (hier)
                    foreach (var ps in systems) { var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Hierarchy; }

                float duration = 0f, lifetime = 0f;
                bool  loop = false;
                int   notHierarchy = 0;
                foreach (var ps in systems)
                {
                    var main = ps.main;
                    duration = Mathf.Max(duration, main.duration);
                    lifetime = Mathf.Max(lifetime, main.startLifetime.constantMax);
                    loop    |= main.loop;
                    if (main.scalingMode != ParticleSystemScalingMode.Hierarchy) notHierarchy++;
                }
                sb.Append(",\"ps\":").Append(systems.Length)
                  .Append(",\"duration\":").Append(F(duration))
                  .Append(",\"lifetime\":").Append(F(lifetime))
                  .Append(",\"loop\":").Append(loop ? "true" : "false")
                  .Append(",\"scale\":").Append(F(prefab.transform.localScale.x))
                  .Append(",\"notHierarchy\":").Append(notHierarchy)
                  .Append(",\"frames\":[");

                for (int k = 0; k < times.Length; k++)
                {
                    foreach (var ps in systems)
                        if (ps.transform.parent == null || ps.transform.parent.GetComponentInParent<ParticleSystem>() == null)
                            ps.Simulate(times[k], true, true, true);

                    ParticleExtent(systems, out float radius, out float top, out int alive);

                    pru.BeginStaticPreview(new Rect(0, 0, Size, Size));
                    pru.Render(true);
                    var tex = pru.EndStaticPreview();
                    File.WriteAllBytes(Path.Combine(outDir, $"{i + 1:00}_{k}.png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);

                    if (k > 0) sb.Append(',');
                    sb.Append("{\"t\":").Append(F(times[k]))
                      .Append(",\"radiusXZ\":").Append(F(radius))
                      .Append(",\"top\":").Append(F(top))
                      .Append(",\"alive\":").Append(alive).Append('}');
                }
                sb.Append("]}");
                Object.DestroyImmediate(go);
            }
        }
        finally
        {
            pru.Cleanup();
        }

        sb.Append(']');
        File.WriteAllText(Path.Combine(outDir, "index.json"), sb.ToString());
        Debug.Log("[VfxPreview] 완료 " + paths.Length);
    }

    /// <summary>살아 있는 파티클의 수평 최대 반경(크기 절반 포함)·최고 높이·개수.</summary>
    private static void ParticleExtent(ParticleSystem[] systems, out float radius, out float top, out int alive)
    {
        radius = 0f; top = 0f; alive = 0;
        foreach (var ps in systems)
        {
            int n = ps.particleCount;
            if (n == 0) continue;
            var buf = new ParticleSystem.Particle[n];
            n = ps.GetParticles(buf);
            bool local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = local ? ps.transform.TransformPoint(buf[i].position) : buf[i].position;
                float half = buf[i].GetCurrentSize(ps) * 0.5f;
                radius = Mathf.Max(radius, new Vector2(p.x, p.z).magnitude + half);
                top    = Mathf.Max(top, p.y + half);
            }
            alive += n;
        }
    }

    /// <summary>
    /// 바닥 눈금 — 반경 r 원 위에 작은 큐브 count개.
    /// 저장 안 되는 숨김 오브젝트로 만들어 곧바로 미리보기 씬으로 옮긴다(공유 중인 활성 씬을 더럽히지 않게).
    /// </summary>
    private static void AddRing(PreviewRenderUtility pru, float r, int count, Color color)
    {
        var mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var mat  = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { hideFlags = HideFlags.HideAndDontSave };
        mat.SetColor("_BaseColor", color);
        for (int i = 0; i < count; i++)
        {
            float a = i * Mathf.PI * 2f / count;
            var cube = EditorUtility.CreateGameObjectWithHideFlags("ring", HideFlags.HideAndDontSave,
                                                                   typeof(MeshFilter), typeof(MeshRenderer));
            cube.GetComponent<MeshFilter>().sharedMesh       = mesh;
            cube.GetComponent<MeshRenderer>().sharedMaterial = mat;
            cube.transform.position   = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            cube.transform.localScale = Vector3.one * 0.12f;
            pru.AddSingleGO(cube);
        }
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
