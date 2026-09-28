#if UNITY_EDITOR
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// [실측 도구] 리치 이펙트 후보를 미리보기 전용 씬(PreviewRenderUtility)에 띄워 시간별로 렌더링한다. 활성 씬은 건드리지 않는다.
/// 리치 이펙트는 4~20 m라 카메라를 이펙트 크기(파티클·렌더러 범위)에 맞춰 자동으로 뺀다.
/// 바닥 눈금: 흰 점 = 반경 1 m, 노랑 = 5 m, 빨강 = 10 m.
/// 입력: Temp/lich_vfx_candidates.txt (한 줄에 「이름|프리팹 경로」)
/// 출력: Temp/lich_vfx_preview/NN_k.png · index.json (펼친 반경·높이·파티클 수·루프 여부)
/// </summary>
public static class LichVfxPreviewEditor
{
    private const int   Size = 320;
    private static readonly float[] Times = { 0.3f, 0.9f, 1.8f };
    private const string InputFile = "Temp/lich_vfx_candidates.txt";
    private const string OutDir    = "Temp/lich_vfx_preview";

    [MenuItem("RelicFairy/Boss/Lich/VFX Candidate Preview")]
    private static void Run()
    {
        if (!File.Exists(InputFile)) { Debug.LogWarning($"[LichVfxPreview] 입력 없음 — {InputFile}"); return; }
        string[] lines = File.ReadAllLines(InputFile);
        Directory.CreateDirectory(OutDir);

        var sb  = new StringBuilder("[");
        var pru = new PreviewRenderUtility();
        try
        {
            var cam = pru.camera;
            cam.fieldOfView     = 40f;
            cam.nearClipPlane   = 0.05f;
            cam.farClipPlane    = 500f;
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.08f, 0.10f, 1f);
            pru.lights[0].intensity          = 1.2f;
            pru.lights[0].transform.rotation = Quaternion.Euler(50f, 50f, 0f);
            AddRing(pru, 1f, 6, Color.white);
            AddRing(pru, 5f, 16, Color.yellow);
            AddRing(pru, 10f, 24, Color.red);

            int n = 0;
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int bar = line.IndexOf('|');
                string label = bar > 0 ? line.Substring(0, bar).Trim() : Path.GetFileNameWithoutExtension(line);
                string path  = bar > 0 ? line.Substring(bar + 1).Trim() : line;
                n++;

                if (n > 1) sb.Append(',');
                sb.Append("{\"i\":").Append(n).Append(",\"label\":\"").Append(label)
                  .Append("\",\"path\":\"").Append(path.Replace("\\", "/")).Append('"');

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.Append(",\"error\":\"load\"}"); continue; }

                var go = pru.InstantiatePrefabInScene(prefab);
                go.transform.position = Vector3.zero;
                var systems = go.GetComponentsInChildren<ParticleSystem>(true);
                var graphs  = go.GetComponentsInChildren<VisualEffect>(true);

                bool loop = false;
                foreach (var ps in systems) loop |= ps.main.loop;
                sb.Append(",\"ps\":").Append(systems.Length).Append(",\"vfxGraph\":").Append(graphs.Length)
                  .Append(",\"loop\":").Append(loop ? "true" : "false").Append(",\"frames\":[");

                for (int k = 0; k < Times.Length; k++)
                {
                    foreach (var ps in systems)
                        if (ps.transform.parent == null || ps.transform.parent.GetComponentInParent<ParticleSystem>() == null)
                            ps.Simulate(Times[k], true, true, true);
                    foreach (var vfx in graphs)
                    {
                        vfx.Reinit();
                        vfx.Simulate(1f / 60f, (uint)Mathf.RoundToInt(Times[k] * 60f));
                    }

                    Extent(go, systems, out float radius, out float top, out int alive);
                    Frame(cam, Mathf.Max(radius, top * 0.8f));

                    pru.BeginStaticPreview(new Rect(0, 0, Size, Size));
                    pru.Render(true);
                    var tex = pru.EndStaticPreview();
                    File.WriteAllBytes(Path.Combine(OutDir, $"{n:00}_{k}.png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);

                    if (k > 0) sb.Append(',');
                    sb.Append("{\"t\":").Append(F(Times[k]))
                      .Append(",\"radius\":").Append(F(radius))
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
        File.WriteAllText(Path.Combine(OutDir, "index.json"), sb.ToString());
        Debug.Log($"[LichVfxPreview] 완료 — {OutDir}");
    }

    /// <summary>이펙트 크기에 맞춰 카메라를 뺀다 — 35° 내려다보는 각, 반경의 약 2.6배 거리.</summary>
    private static void Frame(Camera cam, float extent)
    {
        float r    = Mathf.Clamp(extent, 1.5f, 40f);
        float dist = r * 2.6f + 2f;
        var   dir  = Quaternion.Euler(35f, 0f, 0f) * Vector3.back;
        cam.transform.position = new Vector3(0f, r * 0.25f, 0f) + dir * dist;
        cam.transform.LookAt(new Vector3(0f, r * 0.25f, 0f));
    }

    /// <summary>살아 있는 파티클과 (파티클이 아닌) 렌더러 범위를 합친 수평 반경·최고 높이·파티클 수.</summary>
    private static void Extent(GameObject go, ParticleSystem[] systems, out float radius, out float top, out int alive)
    {
        radius = 0f; top = 0f; alive = 0;
        foreach (var ps in systems)
        {
            int count = ps.particleCount;
            if (count == 0) continue;
            var buf = new ParticleSystem.Particle[count];
            count = ps.GetParticles(buf);
            bool local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = local ? ps.transform.TransformPoint(buf[i].position) : buf[i].position;
                float half = buf[i].GetCurrentSize(ps) * 0.5f;
                radius = Mathf.Max(radius, new Vector2(p.x, p.z).magnitude + half);
                top    = Mathf.Max(top, p.y + half);
            }
            alive += count;
        }
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            // 메시 파티클은 입자 크기(GetCurrentSize)가 메시 크기를 반영하지 않는다 — 렌더러 범위로 잰다.
            if (!r.enabled || (r is ParticleSystemRenderer pr && pr.renderMode != ParticleSystemRenderMode.Mesh)) continue;
            var b = r.bounds;
            radius = Mathf.Max(radius, new Vector2(Mathf.Max(Mathf.Abs(b.min.x), Mathf.Abs(b.max.x)),
                                                   Mathf.Max(Mathf.Abs(b.min.z), Mathf.Abs(b.max.z))).magnitude);
            top    = Mathf.Max(top, b.max.y);
        }
    }

    /// <summary>바닥 눈금 — 저장 안 되는 숨김 오브젝트로 만들어 미리보기 씬으로만 옮긴다.</summary>
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
            cube.transform.localScale = Vector3.one * (0.12f + r * 0.02f);
            pru.AddSingleGO(cube);
        }
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
#endif
