#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 10-02 사용자 「룬 보상 등급 표기 이펙트가 봉인 이펙트랑 겹친다 · 빛기둥 표기 이펙트를 찾아봐」.
/// 지금 보상 문양(SSEP 18 TimeField = 리치 봉인진) · 표지 기둥(SSEP 38 GloryBoundary = 리치 봉인 완성)과
/// _Imported 빛기둥 후보(Hovl 지도 표지 팩)를 등급색(레어 · 에픽 · 전설)으로 칠해 같은 각도에서 찍는다(편집 모드 · 미리보기 씬).
/// 결과: Temp/rewardfx_cmp/{후보}_{등급}_{게임|옆}.png · report.txt · 로그 「[RewardFxCmp] 끝」.
/// </summary>
public static class RewardFxCompareEditor
{
    private const string Dir = "Temp/rewardfx_cmp";
    private const int    W = 480, H = 480;
    private const string Hovl = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/Map track markers VFX/Prefabs/";
    private const string Ssep = "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/";

    // (이름, 경로, 크기, 재채색 = 빛깔 갈아 끼우기 · 아니면 곱하기)
    private static readonly (string name, string path, float scale, bool recolor)[] Candidates =
    {
        ("A_Pillar",   Hovl + "Marker 4 Pillar Loop.prefab",  1f,   true),
        ("B_Circle",   Hovl + "Marker 5 Circle Loop.prefab",  1f,   true),
        ("C_Zone",     Hovl + "Marker 3 Zone Loop.prefab",    1f,   true),
        ("D_Pointer",  Hovl + "Marker 2 Pointer Loop.prefab", 1f,   true),
        ("E_NowGlyph", Ssep + "Effect_18_TimeField/Effect_18_TimeField.prefab",       0.2f * 0.35f, false),
        ("F_NowBeacon",Ssep + "Effect_38_GloryBoundary/Effect_38_GloryBoundary.prefab", 0.2f * 0.6f, false),
    };

    private static readonly (string name, ItemRarity rarity)[] Grades =
    {
        ("Rare", ItemRarity.Rare), ("Epic", ItemRarity.Epic), ("Legend", ItemRarity.Legendary),
    };

    [MenuItem("RelicFairy/Debug/10-02 보상 등급 빛기둥 후보 비교 (편집 모드)")]
    private static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[RewardFxCmp] 편집 모드에서"); return; }
        Directory.CreateDirectory(Dir);
        var sb = new StringBuilder("보상 등급 빛기둥 후보 비교\n");

        var scene = EditorSceneManager.NewPreviewScene();
        var camGo = new GameObject("~Cam");
        SceneManager.MoveGameObjectToScene(camGo, scene);
        var cam = camGo.AddComponent<Camera>();
        cam.scene = scene;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.08f, 0.10f, 1f);
        cam.fieldOfView = 40f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 200f;
        var lg = new GameObject("~Light");
        SceneManager.MoveGameObjectToScene(lg, scene);
        var light = lg.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lg.transform.eulerAngles = new Vector3(50f, -30f, 0f);

        // 바닥 · 키 기준(1.8 m 기둥 = 플레이어 키)
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        SceneManager.MoveGameObjectToScene(floor, scene);
        floor.transform.localScale = new Vector3(3f, 1f, 3f);
        var man = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        SceneManager.MoveGameObjectToScene(man, scene);
        man.transform.position = new Vector3(1.6f, 0.9f, 0f);

        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        int shots = 0;
        foreach (var c in Candidates)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(c.path);
            if (prefab == null) { sb.AppendLine($"{c.name}: 없음 {c.path}"); continue; }
            foreach (var g in Grades)
            {
                var go = Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.position = new Vector3(0f, 0.05f, 0f);
                go.transform.localScale = prefab.transform.localScale * c.scale;
                var systems = go.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in systems)
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }
                // RunFx와 같게 — 굴절층(Distortion · ShockWave 셰이더)은 끈다(10-01 「평면 조각으로 깨진다」의 후보 원인)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var sh = r.sharedMaterial != null ? r.sharedMaterial.shader : null;
                    if (sh != null && (sh.name.IndexOf("Distortion", System.StringComparison.OrdinalIgnoreCase) >= 0
                                    || sh.name.IndexOf("ShockWave", System.StringComparison.OrdinalIgnoreCase) >= 0))
                        r.enabled = false;
                }
                Color tint = RewardObjectPresenter.LightColor(g.rarity);
                if (c.recolor) RunFxRecolor.Apply(go, tint);
                else if (c.name != "F_NowBeacon")
                    foreach (var ps in systems) { var m = ps.main; m.startColor = Mul(m.startColor, tint); }

                foreach (var ps in systems) ps.Simulate(1.6f, false, true, true);
                var b = Bounds(go);
                if (shots == 0 || g.rarity == ItemRarity.Rare)
                    sb.AppendLine($"{c.name}: 입자계 {systems.Length} · 크기 {b.size} · 중심 {b.center}");

                // 게임 카메라(뒤 · 위 비스듬히) · 옆(높이 확인)
                Shot(cam, rt, new Vector3(0f, 9f, -8f), new Vector3(0f, 0.8f, 0f), $"{Dir}/{c.name}_{g.name}_game.png");
                Shot(cam, rt, new Vector3(0f, 2.2f, -9f), new Vector3(0f, 2f, 0f), $"{Dir}/{c.name}_{g.name}_side.png");
                shots += 2;
                Object.DestroyImmediate(go);
            }
        }
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        EditorSceneManager.ClosePreviewScene(scene);
        sb.AppendLine($"찍음 {shots}");
        File.WriteAllText($"{Dir}/report.txt", sb.ToString());
        Debug.Log("[RewardFxCmp] 끝\n" + sb);
    }

    private static Bounds Bounds(GameObject go)
    {
        var b = new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        return b;
    }

    private static ParticleSystem.MinMaxGradient Mul(ParticleSystem.MinMaxGradient g, Color t)
        => g.mode == ParticleSystemGradientMode.Color ? new ParticleSystem.MinMaxGradient(g.color * t)
         : g.mode == ParticleSystemGradientMode.TwoColors ? new ParticleSystem.MinMaxGradient(g.colorMin * t, g.colorMax * t)
         : g;

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
