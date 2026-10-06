#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 10-05 챕터 방 봉인 문 후보 비교(사용자 10-02 「문도 제대로 된 오브젝트로 — 지금은 얇은 판이 움직이는 느낌」).
/// 실측(10-03): 지금 문은 개구부에 가로 · 세로를 따로 맞춰 늘린다 — 숲 뿌리(SM_roots_1)는 배율 (2.39, 10.45, 1)로 누운 뿌리가 세로로 10배 늘어난 얇은 띠였다.
/// 표준 개구부(6.5 × 8 m) + 지금 문틀(GateFrame_Forest)에 후보를 세워 「옛 방식(따로 늘림)」과 「새 규칙(SealDoorFit — 런타임과 같은 코드)」으로 찍는다.
/// 결과: Temp/door_cmp/{후보}_{Old|Fit}_{front|side}.png · report.txt · 로그 「[DoorCmp] 끝」. 편집 모드 · 미리보기 씬(씬 안 건드림).
/// </summary>
public static class SealDoorCompareEditor
{
    private const string Dir    = "Temp/door_cmp";
    private const int    W = 640, H = 480;
    private const float  OpenW  = 6.5f;   // 전투방 개구부(실측 SealDoor 월드 크기)
    private const float  OpenH  = 8f;
    private const string Gothic = "Assets/RelicFairy/_Imported/Gothic_Interior/Environment/Asset/Prefabs/";
    private const string Frame  = "Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/GateFrame_Forest.prefab";

    private static readonly (string name, string path)[] Candidates =
    {
        ("Roots_now",        "Assets/RelicFairy/_Imported/LeartesStudios/FantasyCastle/~HDRP/Art/Prefabs/SM_roots_1.prefab"),
        ("ArchWallDoor_01a", Gothic + "SM_ArchWallDoor_01a.prefab"),
        ("Door_01a",         Gothic + "SM_Door_01a.prefab"),
        ("Door_03a",         Gothic + "SM_Door_03a.prefab"),
        ("Door_03b",         Gothic + "SM_Door_03b.prefab"),
        ("BarMetalDoor",     Gothic + "SM_BarMetalDoor_01a.prefab"),
        ("BarMetalFrameDoor",Gothic + "SM_BarMetalFrameDoor_01a.prefab"),
        ("Door_Broken_01a",  Gothic + "SM_Door_Broken_01a.prefab"),
    };

    [MenuItem("RelicFairy/Debug/10-05 봉인 문 후보 비교 (편집 모드)")]
    private static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[DoorCmp] 편집 모드에서"); return; }
        Directory.CreateDirectory(Dir);
        var sb = new StringBuilder($"봉인 문 후보 비교 — 개구부 {OpenW} × {OpenH} m\n");

        var scene = EditorSceneManager.NewPreviewScene();
        var cam = NewIn<Camera>(scene, "~Cam");
        cam.scene = scene;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.09f, 0.10f, 0.12f, 1f);
        cam.fieldOfView = 50f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 200f;
        var light = NewIn<Light>(scene, "~Light");
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.transform.eulerAngles = new Vector3(40f, -35f, 0f);

        // 바닥 · 양옆 벽 · 상인방 — 개구부만 남긴다
        Block(scene, new Vector3(0f, -0.05f, 0f), new Vector3(30f, 0.1f, 30f));
        Block(scene, new Vector3(-(OpenW * 0.5f + 2f), 5f, 0f), new Vector3(4f, 10f, 1.5f));
        Block(scene, new Vector3( (OpenW * 0.5f + 2f), 5f, 0f), new Vector3(4f, 10f, 1.5f));
        Block(scene, new Vector3(0f, OpenH + 1f, 0f), new Vector3(OpenW, 2f, 1.5f));
        var framePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Frame);
        if (framePrefab != null)
        {
            var frame = (GameObject)PrefabUtility.InstantiatePrefab(framePrefab, scene);
            float k = Mathf.Min(OpenW / 20f, OpenH / 12f);
            frame.transform.position = new Vector3(0f, OpenH * 0.5f, 0f);
            frame.transform.localScale = Vector3.one * k;
        }

        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        int shots = 0;
        foreach (var c in Candidates)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(c.path);
            if (prefab == null) { sb.AppendLine($"{c.name}: 없음 {c.path}"); continue; }
            foreach (bool fit in new[] { false, true })
            {
                GameObject root;
                if (fit)
                {
                    // 런타임과 같은 규칙(SealDoorFit) — 돌려 세움 · 높이에 맞춘 균등 배율 · 좁으면 쌍문
                    root = new GameObject("SealDoor");
                    SceneManager.MoveGameObjectToScene(root, scene);
                    root.transform.position = new Vector3(0f, OpenH * 0.5f, 0f);
                    if (!SealDoorFit.Place(root.transform, prefab, OpenW, OpenH, out var lb, out int leaves))
                    {
                        Object.DestroyImmediate(root);
                        sb.AppendLine($"{c.name} 새 규칙: 메시 없음");
                        continue;
                    }
                    sb.AppendLine($"{c.name} 새 규칙: 문짝 {leaves} · 월드 {lb.size:F2}" +
                                  (lb.size.x < OpenW - 0.3f ? $" · 양옆 틈 {(OpenW - lb.size.x) * 0.5f:F2} m" : "") +
                                  (lb.size.x > OpenW + 0.3f ? $" · 벽에 묻힘 {(lb.size.x - OpenW) * 0.5f:F2} m" : ""));
                }
                else
                {
                    root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    root.transform.rotation = Quaternion.identity;
                    root.transform.localScale = Vector3.one;
                    if (!TryBounds(root, out var b)) { Object.DestroyImmediate(root); sb.AppendLine($"{c.name}: 렌더러 없음"); break; }
                    var s = new Vector3(OpenW / Mathf.Max(0.01f, b.size.x), OpenH / Mathf.Max(0.01f, b.size.y), 1f);   // 지금(옛) 방식
                    root.transform.localScale = s;
                    root.transform.position = new Vector3(-b.center.x * s.x, OpenH * 0.5f - b.center.y * s.y, -0.25f);
                    sb.AppendLine($"{c.name} 옛 방식: 메시 {b.size:F2} · 배율 {s:F2}");
                }

                string tag = $"{Dir}/{c.name}_{(fit ? "Fit" : "Old")}";
                Shot(cam, rt, new Vector3(0f, 4.5f, -11f), new Vector3(0f, 4f, 0f), tag + "_front.png");
                Shot(cam, rt, new Vector3(7f, 6.5f, -8f), new Vector3(0f, 3.5f, 0f), tag + "_side.png");
                shots += 2;
                Object.DestroyImmediate(root);
            }
        }
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        EditorSceneManager.ClosePreviewScene(scene);
        sb.AppendLine($"찍음 {shots}");
        File.WriteAllText($"{Dir}/report.txt", sb.ToString());
        Debug.Log("[DoorCmp] 끝\n" + sb);
    }

    private static T NewIn<T>(Scene scene, string name) where T : Component
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        return go.AddComponent<T>();
    }

    private static void Block(Scene scene, Vector3 center, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.position = center;
        go.transform.localScale = size;
    }

    private static bool TryBounds(GameObject go, out Bounds b)
    {
        b = default;
        bool has = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        if (has) b.center -= go.transform.position;   // 스케일 1 · 회전 0 상태라 월드 = 로컬
        return has;
    }

    private static void Shot(Camera cam, RenderTexture rt, Vector3 pos, Vector3 look, string path)
    {
        cam.transform.position = pos;
        cam.transform.LookAt(look);
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
#endif
