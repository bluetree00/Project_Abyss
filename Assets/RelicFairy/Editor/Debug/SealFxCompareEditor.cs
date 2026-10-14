#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 10-10 사용자 「봉인 사슬은 괜찮은데 다른 이펙트가 봉인에 맞지 않게 어색하다 — 다른 리소스로」.
/// 보스 봉인 장면이 쓰는 봉인진(지금 SSEP 18 TimeField = 시계형) · 봉인 폭발(SSEP HolyRedemption) · 봉인 완성(SSEP 38 GloryBoundary)과
/// _Imported 후보를 같은 크기(봉인진 지름 10 m · 폭발 · 기둥 8 m) · 보스 봉인 빛깔(게임처럼 시작 색에 곱함) · 게임 후처리로 찍는다(편집 모드 · 미리보기 씬).
/// 결과: Temp/seal_fx_cmp/grid_sigil.png · grid_burst.png · 칸별 png · report.txt · 로그 「[SealFxCmp] 끝」.
/// </summary>
public static class SealFxCompareEditor
{
    private const string Dir = "Temp/seal_fx_cmp";
    private const int    W = 480, H = 320, Cols = 4;
    private const float  Dt = 1f / 30f, SigilDiameter = 10f, BurstSize = 8f;
    private const string Profile = "Assets/RelicFairy/Settings/GameVolumeProfile.asset";
    private const string Ssep  = "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/";
    private const string HovlL = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/Magic circles/Prefabs/Loop version/";
    private const string Hovl  = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/Magic circles/Prefabs/";
    private const string Aaa   = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/AAA Projectiles Vol 1/Prefabs/";

    // 숲 봉인 빛깔(연두 금) — 가장 진한 빛깔이라 곱했을 때 어두워지는지가 잘 보인다
    private static readonly Color SealTint = new Color(0.75f, 0.85f, 0.35f);

    // (이름, 경로, 찍는 시각(초))
    private static readonly (string name, string path, float at)[] Sigils =
    {
        ("S00_now_TimeField", Ssep + "EffectsSet_1(NotScriptBased)/Effects/Effect_18_TimeField/Effect_18_TimeField.prefab", 2f),
        ("S01_circle1",  HovlL + "Magic circle 1 loop.prefab", 2f),
        ("S02_circle2",  HovlL + "Magic circle 2 loop.prefab", 2f),
        ("S03_circle3",  HovlL + "Magic circle 3 loop.prefab", 2f),
        ("S04_circle4",  HovlL + "Magic circle 4 loop.prefab", 2f),
        ("S05_circle5",  HovlL + "Magic circle 5 loop.prefab", 2f),
        ("S06_circle9",  HovlL + "Magic circle 9 loop.prefab", 2f),
        ("S07_circle10", HovlL + "Magic circle 10 loop.prefab", 2f),
        ("S08_circle11", HovlL + "Magic circle 11 loop.prefab", 2f),
        ("S09_sun",      HovlL + "Magic circle sun loop.prefab", 2f),
        ("S10_suns",     HovlL + "Magic circle suns loop.prefab", 2f),
        ("S11_water",    HovlL + "Magic circle water loop.prefab", 2f),
    };

    // 2차(10-10): 사슬 섬광 후보 — 옛 HolyRedemption은 쓰러지는 순간 보스를 덮는 큰 금빛 공으로 보였다. + 새 봉인 완성(문양 한 번판)
    private static readonly (string name, string path, float at)[] Bursts =
    {
        ("B01_now_Burst_HolyRedemption", Ssep + "EffectsSet_1(NotScriptBased)/Effects/Effect_43_DarkChainSwamp/Effect_43_HolyRedemption.prefab", 0.5f),
        ("B02_novaOrange",  Aaa + "Hit 18 nova orange.prefab", 0.25f),
        ("B03_novaViolet",  Aaa + "Hit 17 nova violet.prefab", 0.25f),
        ("B04_cuteStar",    Aaa + "Hit 22 cute star.prefab", 0.25f),
        ("B05_circleBomb",  Aaa + "Hit 19 circle bomb.prefab", 0.25f),
        ("B06_HolyHit",     Ssep + "EffectsSet_2(ScriptBased)/Effects/Effect_05_HolyEmission/Effect_05_Parts/Effect_05_Hit.prefab", 0.3f),
        ("B07_sunSparks",   Hovl + "Magic circle sun sparks.prefab", 0.6f),
        ("B08_bossComplete", "Assets/RelicFairy/Characters/Monster/Monster/Boss/Boss Seal Complete.prefab", 0.6f),
    };

    [MenuItem("RelicFairy/Boss/Seal FX Candidates Compare (Edit)")]
    private static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[SealFxCmp] 편집 모드에서"); return; }
        Directory.CreateDirectory(Dir);
        var sb = new StringBuilder("보스 봉인 이펙트 후보 — 숲 봉인 빛깔을 시작 색에 곱함(게임과 같은 방식) · 게임 후처리\n");

        var scene = EditorSceneManager.NewPreviewScene();
        var cam = MakeCamera(scene);
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        var floorMat = new Material(unlit); floorMat.SetColor("_BaseColor", new Color(0.30f, 0.27f, 0.24f));
        var manMat   = new Material(unlit); manMat.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.15f));
        var floor = Prim(PrimitiveType.Plane, scene, floorMat, new Vector3(4f, 1f, 4f), Vector3.zero);
        var man   = Prim(PrimitiveType.Capsule, scene, manMat, Vector3.one, new Vector3(6.5f, 0.9f, -3f));   // 키 1.8 m = 플레이어

        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        sb.AppendLine("── 봉인진(지름 10 m로 맞춤 · 위에서 비스듬히)");
        Grid(scene, cam, rt, Sigils, SigilDiameter, false, new Vector3(0f, 13f, -12f), "grid_sigil.png", sb);
        sb.AppendLine("── 봉인 폭발 · 완성(가장 큰 변 8 m로 맞춤 · 옆에서 낮게)");
        Grid(scene, cam, rt, Bursts, BurstSize, true, new Vector3(0f, 5f, -16f), "grid_burst.png", sb);

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(floorMat);
        Object.DestroyImmediate(manMat);
        EditorSceneManager.ClosePreviewScene(scene);
        File.WriteAllText($"{Dir}/report.txt", sb.ToString());
        Debug.Log("[SealFxCmp] 끝\n" + sb);
    }

    private static void Grid(Scene scene, Camera cam, RenderTexture rt, (string name, string path, float at)[] list,
                             float size, bool byMax, Vector3 camOffset, string file, StringBuilder sb)
    {
        int rows = (list.Length + Cols - 1) / Cols;
        var grid = new Texture2D(W * Cols, H * rows, TextureFormat.RGB24, false);
        grid.SetPixels32(new Color32[W * Cols * H * rows]);
        var tile = new Texture2D(W, H, TextureFormat.RGB24, false);

        for (int i = 0; i < list.Length; i++)
        {
            var c = list[i];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(c.path);
            if (prefab == null) { sb.AppendLine($"{c.name}: 없음 {c.path}"); continue; }

            // 1차: 원래 크기로 굴려 크기를 재고, 2차: 맞춘 크기로 다시 굴려 찍는다
            float native = Measure(scene, prefab, c.at, 1f, byMax);
            float scale  = native > 0.01f ? size / native : 1f;
            var go = Spawn(scene, prefab, scale);
            Simulate(go, c.at);
            var b = Visible(go);

            cam.transform.position = camOffset + new Vector3(0f, byMax ? size * 0.3f : 0f, 0f);
            cam.transform.LookAt(new Vector3(0f, byMax ? size * 0.35f : 0f, 0f));
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tile.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tile.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes($"{Dir}/{c.name}.png", tile.EncodeToPNG());
            grid.SetPixels(i % Cols * W, (rows - 1 - i / Cols) * H, W, H, tile.GetPixels());
            sb.AppendLine($"{c.name}: 원래 {native:0.0} m → 배율 {scale:0.###} · 찍힌 범위 {b.size.x:0.0}×{b.size.y:0.0}×{b.size.z:0.0} · 입자계 {go.GetComponentsInChildren<ParticleSystem>(true).Length} · 셰이더 {Shaders(go)}");
            Object.DestroyImmediate(go);
        }
        grid.Apply();
        File.WriteAllBytes($"{Dir}/{file}", grid.EncodeToPNG());
        Object.DestroyImmediate(grid);
        Object.DestroyImmediate(tile);
    }

    private static float Measure(Scene scene, GameObject prefab, float at, float scale, bool byMax)
    {
        var go = Spawn(scene, prefab, scale);
        Simulate(go, at);
        var b = Visible(go);
        Object.DestroyImmediate(go);
        return byMax ? Mathf.Max(b.size.x, b.size.y, b.size.z) : Mathf.Max(b.size.x, b.size.z);
    }

    private static GameObject Spawn(Scene scene, GameObject prefab, float scale)
    {
        var go = Object.Instantiate(prefab);
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, 0f), prefab.transform.rotation);
        go.transform.localScale = prefab.transform.localScale * scale;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;   // LichVfxScaleCache.EnsureHierarchyScaling과 같게
            main.startColor = Mul(main.startColor, SealTint);         // LichVfxScaleCache.ApplyTint와 같게
        }
        return go;
    }

    private static void Simulate(GameObject go, float seconds)
    {
        var systems = go.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in systems) ps.Simulate(0f, false, true, false);
        for (float t = 0f; t < seconds; t += Dt)
            foreach (var ps in systems) ps.Simulate(Dt, false, false, false);
    }

    private static Camera MakeCamera(Scene scene)
    {
        var camGo = new GameObject("~Cam");
        SceneManager.MoveGameObjectToScene(camGo, scene);
        var cam = camGo.AddComponent<Camera>();
        cam.scene = scene;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.05f, 0.07f, 1f);
        cam.fieldOfView = 40f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 200f;
        cam.allowHDR = true;
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.volumeLayerMask = ~0;
        var volGo = new GameObject("~Volume");
        SceneManager.MoveGameObjectToScene(volGo, scene);
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 100f;
        vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Profile);
        return cam;
    }

    private static GameObject Prim(PrimitiveType type, Scene scene, Material mat, Vector3 scale, Vector3 pos)
    {
        var go = GameObject.CreatePrimitive(type);
        SceneManager.MoveGameObjectToScene(go, scene);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.transform.localScale = scale;
        go.transform.position = pos;
        return go;
    }

    /// <summary>
    /// 실제로 보이는 범위 — 살아 있는 입자 위치 ± 크기 + 입자 아닌 렌더러. 렌더러 bounds는 입자계가 「날 수 있는 최대」라
    /// Hovl 문양이 14 m로 재져 1.5 m로 찍혔다(1차).
    /// </summary>
    private static Bounds Visible(GameObject go)
    {
        bool any = false;
        var b = new Bounds(go.transform.position, Vector3.zero);
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
        {
            int n = ps.particleCount;
            if (n == 0) continue;
            var arr = new ParticleSystem.Particle[n];
            n = ps.GetParticles(arr);
            bool local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            float s = ps.transform.lossyScale.x;
            for (int k = 0; k < n; k++)
            {
                Vector3 pos = local ? ps.transform.TransformPoint(arr[k].position) : arr[k].position;
                float half = arr[k].GetCurrentSize(ps) * s * 0.5f;
                var pb = new Bounds(pos, Vector3.one * half * 2f);
                if (!any) { b = pb; any = true; } else b.Encapsulate(pb);
            }
        }
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    private static ParticleSystem.MinMaxGradient Mul(ParticleSystem.MinMaxGradient g, Color t)
        => g.mode == ParticleSystemGradientMode.Color ? new ParticleSystem.MinMaxGradient(g.color * t)
         : g.mode == ParticleSystemGradientMode.TwoColors ? new ParticleSystem.MinMaxGradient(g.colorMin * t, g.colorMax * t)
         : g;

    private static string Shaders(GameObject go)
    {
        var set = new System.Collections.Generic.SortedSet<string>();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            if (r.sharedMaterial != null && r.sharedMaterial.shader != null) set.Add(r.sharedMaterial.shader.name);
        return string.Join(", ", set);
    }
}
#endif
