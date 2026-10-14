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
/// 10-08 사용자 「새끼 용 얼음 탄이 안 보인다 — 알맞은 새 이펙트를 찾거나 없으면 크기를 고친다」.
/// 지금 새끼 용 탄 3종(얼음 · 번개 · 불)과 _Imported 얼음 탄 후보를 게임 카메라 각도 · 화룡 아레나 바닥색에서 날아가는 중에 찍는다(편집 모드 · 미리보기 씬).
/// 이동은 <see cref="DragonBreathShot"/>과 같게 — 뿌리 입자 startSpeed로 날린다. 입자 속도가 없는 후보는 지금 얼음 탄 속도(20 m/s)로 날린다(바꿔도 속도는 그대로 둔다).
/// 게임 후처리(GameVolumeProfile — 블룸 · 톤매핑)를 켜고 찍는다(1차: 후처리 없이 찍으니 전부 작고 어둡게 나왔다).
/// 결과: Temp/whelp_breath_cmp/grid.png(4열) · 칸별 png · report.txt · 로그 「[WhelpBreathCmp] 끝」.
/// </summary>
public static class WhelpBreathCompareEditor
{
    private const string Dir = "Temp/whelp_breath_cmp";
    private const int    W = 640, H = 360, Cols = 4;
    private const float  FlyTime = 0.3f, Dt = 1f / 60f, IceSpeed = 20f, ShotHeight = 1f;
    private const string Profile = "Assets/RelicFairy/Settings/GameVolumeProfile.asset";
    private const string Now    = "Assets/RelicFairy/Characters/Monster/Monster/DragonBoss/Art/Effects/";
    private const string Hovl   = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/AAA Projectiles Vol 1/Prefabs/Projectiles(Particle collision)/";
    private const string Spells = "Assets/RelicFairy/_Imported/EffectSource/Spells Pack/LWRP(URP)/Particles_LWRP/Prefabs/Projectiles/Projectiles/";
    private const string Msp    = "Assets/RelicFairy/_Imported/EffectSource/MasterStylizedProjectiles/Projectiles/SmallIceBullet/Prefabs/";

    private static readonly (string name, string path)[] Candidates =
    {
        ("A_now_ice",     Now    + "Projectile 6 blue fire.prefab"),
        ("B_now_thunder", Now    + "Projectile 2 electro.prefab"),
        ("C_now_fire",    Now    + "Projectile 3 black fire.prefab"),
        ("D_crystal26",   Hovl   + "Projectile 26 blue crystal.prefab"),
        ("E_rapid14",     Hovl   + "Projectile 14 blue rapid.prefab"),
        ("F_water9",      Hovl   + "Projectile 9 water.prefab"),
        ("G_spellIce1",   Spells + "Projectile_Ice_LWRP.prefab"),
        ("H_spellIce2",   Spells + "Projectile_Ice_2_LWRP.prefab"),
        ("I_spellIce3",   Spells + "Projectile_Ice_3_LWRP.prefab"),
        ("J_smallIce",    Msp    + "SmallIceBullet.prefab"),
    };

    [MenuItem("RelicFairy/Boss/Dragon/Whelp Breath Candidates Compare (Edit)")]
    private static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[WhelpBreathCmp] 편집 모드에서"); return; }
        Directory.CreateDirectory(Dir);
        var sb = new StringBuilder("새끼 용 탄 후보 비교 — 게임 카메라 각도 · 0.4초 비행 · 왼쪽 → 오른쪽\n");

        var scene = EditorSceneManager.NewPreviewScene();
        var camGo = new GameObject("~Cam");
        SceneManager.MoveGameObjectToScene(camGo, scene);
        var cam = camGo.AddComponent<Camera>();
        cam.scene = scene;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.08f, 0.10f, 1f);
        cam.fieldOfView = 22f;   // 게임 카메라 거리에서 확대(칸 하나 = 화면 일부)
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 200f;
        cam.allowHDR = true;
        var camData = cam.GetUniversalAdditionalCameraData();
        camData.renderPostProcessing = true;
        camData.volumeLayerMask = ~0;
        var volGo = new GameObject("~Volume");
        SceneManager.MoveGameObjectToScene(volGo, scene);
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 100f;   // 열린 씬 볼륨보다 위
        vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Profile);

        // 화룡 아레나 바닥 — 실측 사진에서 뽑은 그늘 · 밝은 돌 색(뒤쪽 반 = 그늘 · 앞쪽 반 = 밝은 돌)
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        var darkMat = new Material(unlit); darkMat.SetColor("_BaseColor", new Color(0.35f, 0.30f, 0.27f));
        var litMat  = new Material(unlit); litMat.SetColor("_BaseColor", new Color(0.67f, 0.55f, 0.41f));
        var manMat  = new Material(unlit); manMat.SetColor("_BaseColor", new Color(0.15f, 0.15f, 0.18f));
        var darkFloor = Prim(PrimitiveType.Plane, scene, darkMat);
        var litFloor  = Prim(PrimitiveType.Plane, scene, litMat);
        var man       = Prim(PrimitiveType.Capsule, scene, manMat);   // 키 1.8 m = 플레이어 키

        var rt   = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        var tile = new Texture2D(W, H, TextureFormat.RGB24, false);
        int rows = (Candidates.Length + Cols - 1) / Cols;
        var grid = new Texture2D(W * Cols, H * rows, TextureFormat.RGB24, false);
        var blank = new Color32[W * Cols * H * rows];
        grid.SetPixels32(blank);
        cam.targetTexture = rt;

        for (int i = 0; i < Candidates.Length; i++)
        {
            var c = Candidates[i];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(c.path);
            if (prefab == null) { sb.AppendLine($"{c.name}: 없음 {c.path}"); continue; }
            var go = Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetPositionAndRotation(new Vector3(0f, ShotHeight, 0f), Quaternion.LookRotation(Vector3.right));

            var root = go.GetComponent<ParticleSystem>() ?? go.GetComponentInChildren<ParticleSystem>(true);
            float speed = IceSpeed;
            if (root != null)
            {
                var curve = root.main.startSpeed;
                float ps = curve.mode == ParticleSystemCurveMode.TwoConstants ? (curve.constantMin + curve.constantMax) * 0.5f : curve.constant;
                if (ps > 0.1f) speed = ps;
            }
            // DragonBreathShot처럼 몸(GO)을 매 프레임 옮기며 입자를 굴린다(월드 공간 꼬리)
            for (float t = 0f; t < FlyTime; t += Dt)
            {
                go.transform.position += Vector3.right * speed * Dt;
                root?.Simulate(Dt, true, t == 0f, false);
            }
            Vector3 p = go.transform.position;
            darkFloor.transform.position = new Vector3(p.x, 0f, p.z + 5f - 0.01f);
            litFloor.transform.position  = new Vector3(p.x, 0f, p.z - 5f);
            man.transform.position       = new Vector3(p.x + 2.5f, 0.9f, p.z - 1.2f);

            var b = Bounds(go);
            sb.AppendLine($"{c.name}: 속도 {speed:0.#} m/s · 보이는 범위 {b.size.x:0.0}×{b.size.y:0.0}×{b.size.z:0.0} m · 셰이더 {Shaders(go)}");

            // 게임 카메라 — 뒤 · 위 비스듬히(약 50도 · 14 m)
            cam.transform.position = p + new Vector3(0f, 10.5f, -9f);
            cam.transform.LookAt(p);
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tile.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tile.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes($"{Dir}/{c.name}.png", tile.EncodeToPNG());
            grid.SetPixels(i % Cols * W, (rows - 1 - i / Cols) * H, W, H, tile.GetPixels());
            Object.DestroyImmediate(go);
        }
        grid.Apply();
        File.WriteAllBytes($"{Dir}/grid.png", grid.EncodeToPNG());

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tile);
        Object.DestroyImmediate(grid);
        Object.DestroyImmediate(darkMat);
        Object.DestroyImmediate(litMat);
        Object.DestroyImmediate(manMat);
        EditorSceneManager.ClosePreviewScene(scene);
        File.WriteAllText($"{Dir}/report.txt", sb.ToString());
        Debug.Log("[WhelpBreathCmp] 끝\n" + sb);
    }

    private static GameObject Prim(PrimitiveType type, Scene scene, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        SceneManager.MoveGameObjectToScene(go, scene);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.transform.localScale = type == PrimitiveType.Plane ? new Vector3(3f, 1f, 1f) : Vector3.one;
        return go;
    }

    private static Bounds Bounds(GameObject go)
    {
        var b = new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        return b;
    }

    private static string Shaders(GameObject go)
    {
        var set = new System.Collections.Generic.SortedSet<string>();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            if (r.sharedMaterial != null && r.sharedMaterial.shader != null) set.Add(r.sharedMaterial.shader.name);
        return string.Join(", ", set);
    }
}
#endif
