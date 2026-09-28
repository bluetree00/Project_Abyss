using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// 스킬 아이콘 합성용 이펙트 캡처(편집 모드, 읽기 전용).
/// Temp/effect_capture_jobs.json의 이펙트 프리팹을 <b>미리보기 씬</b>에 띄워 파티클을 지정 시각까지 시뮬레이트하고,
/// 검은 배경 카메라로 찍어 Temp/effect_captures/*.png로 저장한다. 프로젝트 씬·에셋은 건드리지 않는다.
/// </summary>
public static class EffectIconCaptureEditor
{
    private const string JobPath = "Temp/effect_capture_jobs.json";
    private const string OutDir  = "Temp/effect_captures";
    private const int    Size    = 1024;

    [Serializable]
    private class JobFile { public Job[] jobs; }

    [Serializable]
    private class Job
    {
        public string  name;
        public string  prefab;
        public float[] times;
        public float[] pitches;   // 카메라 내려다보는 각(도). 90 = 정수리
        public float   yaw;
        public float   zoom = 1f; // 1 = 경계에 딱 맞춤, 작을수록 확대
        // 이펙트는 스스로 빛나서(가산 셰이더) 조명이 필요 없지만, 무기 모델 같은 <b>일반 메시</b>는
        // 조명이 없으면 새까맣게 찍힌다 → 무기 아이콘을 뽑을 때 켠다(09-21).
        public bool    light;
        // 큰 지형(베이스캠프 등)은 경계가 50m를 넘어 이펙트용 3m 틀로 잘린다 → 찍을 곳을 직접 준다(09-27).
        // radius > 0이면 center(프리팹 로컬 = 월드, 루트가 원점)·radius로 프레이밍한다.
        public float[] center;
        public float   radius;
    }

    [MenuItem("RelicFairy/Debug/스킬 아이콘용 이펙트 캡처")]
    private static void CaptureAll()
    {
        if (!File.Exists(JobPath)) { Debug.LogWarning("[EffectIconCapture] 작업 파일 없음: " + JobPath); return; }
        var file = JsonUtility.FromJson<JobFile>(File.ReadAllText(JobPath));
        Directory.CreateDirectory(OutDir);

        int shots = 0;
        foreach (var job in file.jobs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(job.prefab);
            if (prefab == null) { Debug.LogWarning("[EffectIconCapture] 프리팹 없음: " + job.prefab); continue; }
            shots += CaptureJob(job, prefab);
        }
        Debug.Log($"[EffectIconCapture] {shots}장 → {Path.GetFullPath(OutDir)}");
    }

    private static int CaptureJob(Job job, GameObject prefab)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        int shots = 0;
        try
        {
            var go = Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = Vector3.zero;
            // 이동 스크립트(투사체 등)가 편집 모드에서 돌지 않도록 — 모양만 본다.
            // 스크립트가 빠진 컴포넌트는 null로 나온다 — 건너뛴다.
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null) mb.enabled = false;

            var camGo = new GameObject("CaptureCam");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.scene           = scene;
            cam.cameraType      = CameraType.Game;
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView     = 30f;
            cam.nearClipPlane   = 0.05f;
            cam.farClipPlane    = job.radius > 0f ? Mathf.Max(500f, job.radius * 8f) : 500f;
            cam.targetTexture   = rt;

            if (job.light)
            {
                // 세 방향에서 약하게 — 한 방향만 쓰면 반대쪽 면이 통째로 검게 죽는다.
                AddLight(scene, new Vector3(35f, -35f, 0f), 1.1f);
                AddLight(scene, new Vector3(20f, 150f, 0f), 0.6f);
                AddLight(scene, new Vector3(-30f, 60f, 0f), 0.4f);
                RenderSettings.ambientLight = new Color(0.35f, 0.36f, 0.42f);
            }

            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            foreach (float t in job.times)
            {
                foreach (var ps in systems)
                {
                    // 최상위 파티클만 시뮬레이트(자식은 withChildren으로 함께 간다)
                    var parent = ps.transform.parent;
                    if (parent != null && parent.GetComponentInParent<ParticleSystem>(true) != null) continue;
                    ps.Simulate(Mathf.Max(0.001f, t), true, true, false);
                }

                Bounds b = job.radius > 0f && job.center != null && job.center.Length == 3
                    ? new Bounds(new Vector3(job.center[0], job.center[1], job.center[2]), Vector3.one * (job.radius / 0.866f))
                    : RendererBounds(go);
                foreach (float pitch in job.pitches)
                {
                    Frame(cam, b, pitch, job.yaw, job.zoom);
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    File.WriteAllBytes($"{OutDir}/{job.name}_t{t:0.00}_p{pitch:0}.png", tex.EncodeToPNG());
                    shots++;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[EffectIconCapture] {job.name} 실패: {e.Message}");
        }
        finally
        {
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        return shots;
    }

    /// <summary>미리보기 씬 전용 방향광 — 프로젝트 씬·라이팅 설정은 건드리지 않는다.</summary>
    private static void AddLight(Scene scene, Vector3 euler, float intensity)
    {
        var go = new GameObject("CaptureLight");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.rotation = Quaternion.Euler(euler);
        var l = go.AddComponent<Light>();
        l.type      = LightType.Directional;
        l.intensity = intensity;
        l.color     = Color.white;
    }

    private static Bounds RendererBounds(GameObject root)
    {
        bool any = false;
        var b = new Bounds(root.transform.position, Vector3.one);
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled || r.bounds.size.sqrMagnitude < 1e-6f) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        if (!any || b.extents.magnitude > 50f) b = new Bounds(root.transform.position, Vector3.one * 3f);
        return b;
    }

    private static void Frame(Camera cam, Bounds b, float pitch, float yaw, float zoom)
    {
        float radius = Mathf.Max(0.3f, b.extents.magnitude) * Mathf.Max(0.05f, zoom);
        float dist   = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        cam.transform.SetPositionAndRotation(b.center - rot * Vector3.forward * dist, rot);
    }
}
