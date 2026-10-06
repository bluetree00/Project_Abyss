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
/// _Imported 등급 표시 후보(10-05: Vefects 「Item Pickup VFX URP」 등급별 4종)를 지금 것과 같은 각도에서 찍는다(편집 모드 · 미리보기 씬).
/// 결과: Temp/rewardfx_cmp/{후보}_{등급}_{게임|옆}.png · report.txt · 로그 「[RewardFxCmp] 끝」.
/// </summary>
public static class RewardFxCompareEditor
{
    private const string Dir = "Temp/rewardfx_cmp";
    private const int    W = 480, H = 480;
    private const string Ssep = "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/";
    private const string Vef  = "Assets/RelicFairy/_Imported/EffectSource/Vefects/Item Pickup VFX URP/VFX/Particles/";

    private enum Tint { Multiply, Recolor, Raw }

    // (이름, 경로, 크기, 색 입히기, 이 등급에서만(null = 전부))
    // 10-05 2차: Hovl 지도 표지는 탈락(10-03 — 질감 없는 납작한 판). Vefects 등급 팩은 원래 빛깔 그대로(Raw) 제 등급에서만 찍는다.
    private static readonly (string name, string path, float scale, Tint tint, ItemRarity? only)[] Candidates =
    {
        ("V_Common",   Vef + "VFX_Item_Common.prefab",    1f, Tint.Raw, ItemRarity.Common),
        ("V_Rare",     Vef + "VFX_Item_Rare.prefab",      1f, Tint.Raw, ItemRarity.Rare),
        ("V_Epic",     Vef + "VFX_Item_Epic.prefab",      1f, Tint.Raw, ItemRarity.Epic),
        ("V_Legend",   Vef + "VFX_Item_Legendary.prefab", 1f, Tint.Raw, ItemRarity.Legendary),
        ("E_NowGlyph", Ssep + "Effect_18_TimeField/Effect_18_TimeField.prefab",         0.2f * 0.35f, Tint.Multiply, null),
        ("F_NowBeacon",Ssep + "Effect_38_GloryBoundary/Effect_38_GloryBoundary.prefab", 0.2f * 0.6f,  Tint.Raw, ItemRarity.Legendary),
    };

    private static readonly (string name, ItemRarity rarity)[] Grades =
    {
        ("Common", ItemRarity.Common), ("Rare", ItemRarity.Rare), ("Epic", ItemRarity.Epic), ("Legend", ItemRarity.Legendary),
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
                if (c.only.HasValue && c.only.Value != g.rarity) continue;
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
                if (c.tint == Tint.Recolor) RunFxRecolor.Apply(go, tint);
                else if (c.tint == Tint.Multiply)
                    foreach (var ps in systems) { var m = ps.main; m.startColor = Mul(m.startColor, tint); }

                foreach (var ps in systems) ps.Simulate(1.6f, false, true, true);
                var b = Bounds(go);
                if (c.only.HasValue || g.rarity == ItemRarity.Rare)
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
