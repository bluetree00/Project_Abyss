#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 10-06 서약 효과 이펙트 고르기(사용자 10-02 「서약 때 사용하는 리소스 이펙트도 제대로 선택해서」).
/// 연출이 하나도 없는 효과 4종 — 격노(fury · 나) · 박차(momentum · 나) · 저주(curse · 적) · 출혈(hemorrhage · 적) — 의 후보를
/// 플레이어 크기 기둥(나) · 적 기둥(적)에 붙여 0.5초 · 1.2초에 찍는다(편집 모드 · 미리보기 씬).
/// 후보는 다른 시스템이 이미 쓰는 프리팹을 뺐다(룬 속성 · 리치 · 기사 · 플레이어 타격과 겹치지 않게 — 10-06 사용처 조사).
/// 결과: Temp/covfx_cmp/{역할}_{후보}_{0.5|1.2}.png · report.txt · 로그 「[CovFxCmp] 끝」.
/// </summary>
public static class CovenantFxCompareEditor
{
    private const string Dir  = "Temp/covfx_cmp";
    private const int    W = 480, H = 480;
    private const string Rpg  = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/RPG VFX Bundle/Prefabs/Magic buffs and hits/";
    private const string Aura = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/Auras pack 3/Prefabs/";
    private const string Sp   = "Assets/RelicFairy/_Imported/EffectSource/Spells Pack/LWRP(URP)/Particles_LWRP/Prefabs/";
    private const string Ss1  = "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/";
    private const string Ss2  = "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_2(ScriptBased)/Effects/";

    // (역할, 후보 이름, 경로, 배율, 적에게?)
    private static readonly (string role, string name, string path, float scale, bool onEnemy)[] Candidates =
    {
        ("fury",       "Buff1",        Rpg + "Buff 1.prefab",       0.6f, false),
        ("fury",       "Buff3",        Rpg + "Buff 3.prefab",       0.6f, false),
        ("fury",       "Buff4",        Rpg + "Buff 4.prefab",       0.6f, false),
        ("fury",       "Buff6",        Rpg + "Buff 6.prefab",       0.6f, false),
        ("fury",       "DragonPunch",  Rpg + "Dragon punch.prefab", 0.6f, false),
        ("momentum",   "FastWind",     Aura + "Fast wind.prefab",   0.7f, false),
        ("momentum",   "Buff2",        Rpg + "Buff 2.prefab",       0.6f, false),
        ("momentum",   "YellowFlash",  Rpg + "Yellow Flash.prefab", 0.6f, false),
        ("curse",      "Debuff2",      Rpg + "Debuff 2.prefab",     0.6f, true),
        ("curse",      "BuffDark",     Sp + "Buffs/Buff_Dark_LWRP.prefab", 1f, true),
        ("hemorrhage", "SlashHit",     Ss2 + "Effect_11_IntangibleSlash/Effect_11_Parts/Effect_11_SlashHit.prefab", 0.15f, true),
        ("hemorrhage", "BloodBoom",    Ss1 + "Effect_14_MadnessBloodBoom/Effect_14_MadnessBloodBoom.prefab",         0.1f,  true),
        ("hemorrhage", "BloodFlood",   Ss1 + "Effect_06_BloodFlood/Effect_06_BloodFlood.prefab",                     0.06f, true),
    };

    [MenuItem("RelicFairy/Debug/10-06 서약 이펙트 후보 비교 (편집 모드)")]
    private static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[CovFxCmp] 편집 모드에서"); return; }
        Directory.CreateDirectory(Dir);
        var sb = new StringBuilder("서약 이펙트 후보 비교\n");

        var scene = EditorSceneManager.NewPreviewScene();
        var camGo = new GameObject("~Cam");
        SceneManager.MoveGameObjectToScene(camGo, scene);
        var cam = camGo.AddComponent<Camera>();
        cam.scene = scene;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.10f, 0.12f, 0.13f, 1f);
        cam.fieldOfView = 40f;
        cam.nearClipPlane = 0.05f;
        var lgo = new GameObject("~Light");
        SceneManager.MoveGameObjectToScene(lgo, scene);
        var light = lgo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lgo.transform.eulerAngles = new Vector3(50f, -30f, 0f);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        SceneManager.MoveGameObjectToScene(floor, scene);
        floor.transform.localScale = new Vector3(2f, 1f, 2f);
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);   // 플레이어 · 적 크기(1.8 m)
        SceneManager.MoveGameObjectToScene(body, scene);
        body.transform.position = new Vector3(0f, 0.9f, 0f);

        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        int shots = 0;
        foreach (var c in Candidates)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(c.path);
            if (prefab == null) { sb.AppendLine($"{c.role}/{c.name}: 없음 {c.path}"); continue; }
            foreach (float t in new[] { 0.5f, 1.2f })
            {
                var go = Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.position = c.onEnemy ? new Vector3(0f, 1f, 0f) : Vector3.zero;   // 적: 몸 가운데 · 나: 발밑(Attach와 같은 자리)
                go.transform.localScale = prefab.transform.localScale * c.scale;
                var systems = go.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in systems) { var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Hierarchy; }
                foreach (var ps in systems) ps.Simulate(t, false, true, true);
                if (t < 1f)
                {
                    var b = new Bounds(go.transform.position, Vector3.zero);
                    foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                    sb.AppendLine($"{c.role}/{c.name}: 입자계 {systems.Length} · 크기 {b.size:F1}");
                }
                cam.transform.position = new Vector3(0f, 4.2f, -5.2f);   // 게임 카메라와 비슷한 내려다보기
                cam.transform.LookAt(new Vector3(0f, 1f, 0f));
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes($"{Dir}/{c.role}_{c.name}_{t:0.0}.png", tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(go);
                shots++;
            }
        }
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        EditorSceneManager.ClosePreviewScene(scene);
        sb.AppendLine($"찍음 {shots}");
        File.WriteAllText($"{Dir}/report.txt", sb.ToString());
        Debug.Log("[CovFxCmp] 끝\n" + sb);
    }
}
#endif
