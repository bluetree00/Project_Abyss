using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-01 서비스 방 NPC · 구조물 후보 렌더 — 씬을 건드리지 않고(PreviewRenderUtility 미리보기 씬) 후보마다 같은 구도로 찍는다.
/// 사용자 「콘텐츠 방마다 NPC가 동일 — 사람형일 필요 없이 컨셉에 맞게 NPC와 구조물」. 결과: Temp/npc_candidates/*.png + info.txt.
/// </summary>
public static class ServiceNpcCandidateRender
{
    private const string Out = "Temp/npc_candidates";
    private const string Imp = "Assets/RelicFairy/_Imported/";
    private const string Poly = Imp + "RPGMonsterBundlePolyart/";
    private const string Suri = Imp + "Suriyun/Animations/Animations_v3/";
    private const float  HammerScale = 0.45f;

    private struct Cand
    {
        public string label, path, material, clip;
        public float yaw, t;
        public Cand(string label, string path, string material = null, float yaw = 200f, string clip = null, float t = 0.3f)
        { this.label = label; this.path = path; this.material = material; this.yaw = yaw; this.clip = clip; this.t = t; }
    }

    private static readonly Cand[] Cands =
    {
        new("n00_M02_now",         Imp + "Suriyun/Characters/Castle Guard/Prefab/M02.prefab", null, 200f, Suri + "AnimSD@IdleA.fbx"),
        new("n02_turtle_idle",     Poly + "CommonStuffs/Prefab/Wave01/CharacterMaskTint/TurtleShellPAMaskTint.prefab", null, 200f,
                                   Poly + "RPGMonsterWave01Polyart/Animations/TurtleShell/IdleNormal_TurtleShell_Anim.fbx"),
        new("n03_turtle_defend",   Poly + "CommonStuffs/Prefab/Wave01/CharacterMaskTint/TurtleShellPAMaskTint.prefab", null, 200f,
                                   Poly + "RPGMonsterWave01Polyart/Animations/TurtleShell/Defend_TurtleShell_Anim.fbx", 0.6f),
        new("n04_turtle_victory",  Poly + "CommonStuffs/Prefab/Wave01/CharacterMaskTint/TurtleShellPAMaskTint.prefab", null, 200f,
                                   Poly + "RPGMonsterWave01Polyart/Animations/TurtleShell/Victory_TurtleShell_Anim.fbx", 0.5f),
        new("n05_mushroom_smile",  Poly + "CommonStuffs/Prefab/Wave03/CharacterMaskTint/MushroomSmilePAMaskTint.prefab", null, 200f,
                                   Poly + "RPGMonsterWave03Polyart/Animation/Mushroom/Mushroom_IdleNormalSmile.fbx"),
        new("n06_crystal_hovl",    Imp + "EffectSource/Hovl Studio/HSFiles/Models/HOVLCrystal.fbx", Imp + "EffectSource/Hovl Studio/HSFiles/Materials/EGACrystal.mat"),
        new("n07_crystal_piece",   Imp + "EffectSource/Hovl Studio/HSFiles/Models/HOVLCrystalPiese.fbx", Imp + "EffectSource/Hovl Studio/HSFiles/Materials/EGACrystal.mat"),
        new("n08_crystal_hs",      Imp + "EffectSource/Hovl Studio/HSFiles/Models/Crystal.fbx", Imp + "EffectSource/Hovl Studio/HSFiles/Materials/EGACrystal.mat"),
        new("n09_crystal_spells",  Imp + "Spells Pack/Particles/Models/Crystal.FBX"),
        new("n10_egg_lava",        Imp + "Malbers Animations/Dragons/Eggs/Models/Realistic Egg.FBX", Imp + "Malbers Animations/Dragons/Eggs/Materials/Realistic Lava Egg.mat"),
        new("n11_hammer_weapon",   Imp + "WeaponObject/Art/Weapons/Stylized/Hammers/Meshes_Hammers/Hammer1_3.fbx"),
        new("n12_hammer_spells",   Imp + "Spells Pack/Particles/Models/Hammer.FBX"),
        new("n13_skull_spells",    Imp + "Spells Pack/Particles/Models/Skull.FBX"),
        new("n14_tome_spells",     Imp + "Spells Pack/Particles/Models/Tome.FBX"),
        new("n15_potion_spells",   Imp + "Spells Pack/Particles/Models/Potion.FBX"),
        new("n16_altar_ancient",   Imp + "_AncientAltar/Meshes/SM_Altar.fbx"),
    };

    // 구조물 후보 — 이름으로 찾는다(팩마다 경로가 달라). 프리팹 우선.
    private static readonly string[] PropNames =
    {
        "SM_Gargoyle_1", "SM_Gargoyle_2", "SM_Interior_Deco_Gargoyle", "SM_Atlas_Statue", "SM_Big_Statue",
        "SM_Statue_1", "SM_Statue_2", "SM_Statue_3", "SM_Statue_4", "SM_Statue_01a", "SM_Statue_01b", "SM_Statue_01c",
        "SM_Tent", "SM_fountain", "SM_Box", "SM_barrell", "SM_banner", "SM_carpet", "SM_vase_1", "SM_vase_2", "SM_Wood_deco_Box025",
        "SM_Standtorch", "SM_torch", "SM_ground_torch", "SM_chandelier", "SM_Stone_1", "SM_Stone_3", "Flame", "FlameParticles",
        "SM_Globe_01a", "SM_OpenBook_01a", "SM_Bookpedestal_01a", "SM_Pedestal_01a", "SM_Pedestal_Wood_01a", "SM_Chains_01a",
        "SM_Skull_01a", "SM_SkullTop_01a", "SM_Map_01a", "SM_RolledMap_01a", "SM_InkBottle_01a", "SM_Side_Table_01a", "SM_Side_Table_02a",
        "SM_Shelf_2M_01a", "SM_Cabinet_2M_01b", "SM_Altar_01a", "SM_Altar_Top_01a", "SM_Altar_Table_01a", "SM_Dining_Table_01a",
        "SM_Chest_01a", "SM_Chest_02a", "SM_Candleabra_02a", "SM_Candleabra_04a", "SM_LargeCandleHolder_01b", "SM_Statue_Base_1",
        "SM_Big_Statue_Base", "SM_Merged_Books_01a", "SM_WoodDebris_01a", "SM_candle_holder",
    };

    [MenuItem("RelicFairy/Debug/서비스 NPC 후보 렌더 (편집 모드)")]
    private static void Run()
    {
        if (Application.isPlaying) { Debug.LogWarning("[NpcCandidates] 편집 모드에서만"); return; }
        Directory.CreateDirectory(Out);
        File.WriteAllText($"{Out}/info.txt", "");
        int ok = 0;
        foreach (var c in Cands)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(c.path);
            if (asset == null) { Debug.LogWarning($"[NpcCandidates] 없음: {c.path}"); continue; }
            if (Render(c.label, asset, c.path, c.material, c.yaw, c.clip, c.t, null)) ok++;
        }

        // 대장장이 변형 — M02에서 투구를 끄고 오른손 무기 자리에 망치
        var m02 = AssetDatabase.LoadAssetAtPath<GameObject>(Imp + "Suriyun/Characters/Castle Guard/Prefab/M02.prefab");
        var hammer = AssetDatabase.LoadAssetAtPath<GameObject>(Imp + "WeaponObject/Art/Weapons/Stylized/Hammers/Meshes_Hammers/Hammer1_3.fbx");
        if (m02 != null)
        {
            var hammerMat = AssetDatabase.LoadAssetAtPath<Material>(Imp + "WeaponObject/Art/Weapons/Stylized/Hammers/Materials_Hammers/Hammer1_3_1.mat");
            var bodyMat   = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("db6940c15197d0a4699926f8f4584a40"));
            System.Action<GameObject> smith = go =>
            {
                foreach (var n in new[] { "Helm_iron", "Spear_blackiron" })
                {
                    var t = FindDeep(go.transform, n);
                    if (t != null) t.gameObject.SetActive(false);
                }
                var head = FindDeep(go.transform, "Head");
                if (head != null && bodyMat != null && head.TryGetComponent<Renderer>(out var hr)) hr.sharedMaterial = bodyMat;   // 머리 재질 칸이 비어 분홍(d5 09-30 같은 처방)
                var hand = FindDeep(go.transform, "Sword_parentR");
                if (hand != null && hammer != null)
                {
                    var h = Object.Instantiate(hammer, hand, false);
                    h.transform.localScale = Vector3.one * HammerScale;
                    if (hammerMat != null) foreach (var r in h.GetComponentsInChildren<Renderer>()) r.sharedMaterial = hammerMat;
                }
            };
            if (Render("n01_M02_smith_idle", m02, "M02 smith", null, 200f, Suri + "AnimSD@IdleB.fbx", 0.3f, smith)) ok++;
            // 몸짓 띠 — 클립 하나를 다섯 순간(정규화 0.1~0.9)으로 나란히. 망치가 닿는 순간 · 끝 자세를 고른다.
            foreach (var c in new[] { "ATK0", "ATK1", "ATK2", "ATK3", "ATK4", "Aert", "Victory", "VictoryB", "Tired", "Rest", "IdleC", "Block", "Shoot" })
                if (Strip($"s_smith_{c}", m02, Suri + $"AnimSD@{c}.fbx", 160f, smith)) ok++;
            string gz = Imp + "Grruzam Powerful Sword Animation(Great Sword, Katana)/Animation/";
            foreach (var g in AssetDatabase.FindAssets("t:AnimationClip", new[] { gz.TrimEnd('/') }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (!p.Contains("7Combo_1") && !p.Contains("Jump_Attack_Combo_1") && !p.Contains("UpperAttack")) continue;
                if (Strip("s_smith_gz_" + Path.GetFileNameWithoutExtension(p).Replace("M_Big_Sword@", ""), m02, p, 160f, smith)) ok++;
            }
        }
        var turtle = AssetDatabase.LoadAssetAtPath<GameObject>(Poly + "CommonStuffs/Prefab/Wave01/CharacterMaskTint/TurtleShellPAMaskTint.prefab");
        if (turtle != null && false)
            foreach (var c in new[] { "IdleNormal", "SenseSomethingStart", "SenseSomethingRT", "Taunt", "Victory", "Defend", "Dizzy", "IdleBattle" })
                if (Strip($"s_turtle_{c}", turtle, Poly + $"RPGMonsterWave01Polyart/Animations/TurtleShell/{c}_TurtleShell_Anim.fbx", 160f, null)) ok++;
        var mush = AssetDatabase.LoadAssetAtPath<GameObject>(Poly + "CommonStuffs/Prefab/Wave03/CharacterMaskTint/MushroomSmilePAMaskTint.prefab");
        if (mush != null)
            foreach (var c in new[] { "IdleNormal", "SenseSomethingStart", "SenseSomethingMaintain", "Taunting", "Victory", "IdlePlantToBattle", "Dizzy", "GetHit", "walkFWD" })
                if (Strip($"s_mush_{c}", mush, Poly + $"RPGMonsterWave03Polyart/Animation/Mushroom/Mushroom_{c}Smile.fbx", 160f, null)) ok++;

        foreach (var name in PropNames)
        {
            string found = null;
            foreach (var g in AssetDatabase.FindAssets($"{name} t:GameObject", new[] { "Assets/RelicFairy/_Imported" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) != name) continue;
                if (found == null || p.EndsWith(".prefab")) found = p;
                if (p.EndsWith(".prefab")) break;
            }
            if (found == null) { Debug.LogWarning($"[NpcCandidates] 이름 없음: {name}"); continue; }
            if (Render("p_" + name, AssetDatabase.LoadAssetAtPath<GameObject>(found), found, null, 200f, null, 0f, null)) ok++;
        }
        Debug.Log($"[NpcCandidates] {ok}장 → {Out}");
    }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            var r = FindDeep(t.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    private static AnimationClip LoadClip(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
        Debug.LogWarning($"[NpcCandidates] 클립 없음: {path}");
        return null;
    }

    private static readonly float[] StripTimes = { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f };

    /// <summary>클립 하나를 다섯 순간으로 나란히(같은 구도) — 몸짓의 흐름과 정점을 본다.</summary>
    private static bool Strip(string label, GameObject asset, string clipPath, float yaw, System.Action<GameObject> tweak)
    {
        var clip = LoadClip(clipPath);
        if (clip == null) return false;
        const int px = 320;
        var sheet = new Texture2D(px * StripTimes.Length, px, TextureFormat.RGBA32, false);
        var pru = new PreviewRenderUtility();
        try
        {
            pru.camera.fieldOfView     = 30f;
            pru.camera.nearClipPlane   = 0.05f;
            pru.camera.farClipPlane    = 100f;
            pru.camera.clearFlags      = CameraClearFlags.SolidColor;
            pru.camera.backgroundColor = new Color(0.16f, 0.15f, 0.18f, 1f);
            pru.lights[0].intensity = 1.3f;
            pru.lights[0].transform.rotation = Quaternion.Euler(35f, -40f, 0f);
            pru.lights[1].intensity = 0.7f;
            pru.lights[1].transform.rotation = Quaternion.Euler(-20f, 140f, 0f);
            pru.ambientColor = new Color(0.35f, 0.34f, 0.38f, 1f);

            var go = pru.InstantiatePrefabInScene(asset);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            tweak?.Invoke(go);
            clip.SampleAnimation(go, 0f);
            var rends = go.GetComponentsInChildren<Renderer>(false);
            if (rends.Length == 0) return false;
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            float size = Mathf.Max(b.size.x, b.size.y, b.size.z, 0.2f) * 1.25f;   // 휘두르면 커진다 — 여유
            float dist = size * 0.5f / Mathf.Tan(pru.camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.3f;
            Vector3 camPos = b.center + new Vector3(0f, size * 0.35f, -dist);

            for (int i = 0; i < StripTimes.Length; i++)
            {
                clip.SampleAnimation(go, StripTimes[i] * clip.length);
                pru.BeginStaticPreview(new Rect(0, 0, px, px));
                pru.camera.transform.position = camPos;
                pru.camera.transform.LookAt(b.center);
                pru.camera.Render();
                var tex = pru.EndStaticPreview();
                var rt = RenderTexture.GetTemporary(px, px, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(tex, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                sheet.ReadPixels(new Rect(0, 0, px, px), px * i, 0);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(tex);
            }
            sheet.Apply();
            File.WriteAllBytes($"{Out}/{label}.png", sheet.EncodeToPNG());
            File.AppendAllText($"{Out}/info.txt", $"{label}\t클립 {clip.name} 길이 {clip.length:0.00}s · 반복 {clip.isLooping} · {clipPath}\n");
            return true;
        }
        catch (System.Exception e) { Debug.LogWarning($"[NpcCandidates] {label}: {e.Message}"); return false; }
        finally { pru.Cleanup(); Object.DestroyImmediate(sheet); }
    }

    private static bool Render(string label, GameObject asset, string path, string materialPath, float yaw,
                               string clipPath, float t, System.Action<GameObject> tweak)
    {
        var pru = new PreviewRenderUtility();
        try
        {
            pru.camera.fieldOfView     = 28f;
            pru.camera.nearClipPlane   = 0.05f;
            pru.camera.farClipPlane    = 300f;
            pru.camera.clearFlags      = CameraClearFlags.SolidColor;
            pru.camera.backgroundColor = new Color(0.16f, 0.15f, 0.18f, 1f);
            pru.lights[0].intensity = 1.3f;
            pru.lights[0].transform.rotation = Quaternion.Euler(35f, -40f, 0f);
            pru.lights[1].intensity = 0.7f;
            pru.lights[1].transform.rotation = Quaternion.Euler(-20f, 140f, 0f);
            pru.ambientColor = new Color(0.35f, 0.34f, 0.38f, 1f);

            var go = pru.InstantiatePrefabInScene(asset);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            tweak?.Invoke(go);

            var clip = LoadClip(clipPath);
            if (clip != null) clip.SampleAnimation(go, Mathf.Min(t, clip.length));

            if (!string.IsNullOrEmpty(materialPath))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (mat != null)
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    {
                        var arr = r.sharedMaterials;
                        for (int i = 0; i < arr.Length; i++) arr[i] = mat;
                        r.sharedMaterials = arr;   // 미리보기 사본만 — 에셋은 그대로
                    }
            }

            var rends = go.GetComponentsInChildren<Renderer>(false);
            if (rends.Length == 0) { Debug.LogWarning($"[NpcCandidates] 렌더러 없음: {label}"); return false; }
            var b = rends[0].bounds;
            foreach (var r in rends) if (!(r is ParticleSystemRenderer)) b.Encapsulate(r.bounds);
            float size = Mathf.Max(b.size.x, b.size.y, b.size.z, 0.2f);
            float dist = size * 0.5f / Mathf.Tan(pru.camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.3f;

            pru.BeginStaticPreview(new Rect(0, 0, 384, 384));
            pru.camera.transform.position = b.center + new Vector3(0f, size * 0.35f, -dist);
            pru.camera.transform.LookAt(b.center);
            pru.camera.Render();
            var tex = pru.EndStaticPreview();

            File.WriteAllBytes($"{Out}/{label}.png", tex.EncodeToPNG());
            int missing = 0, shaders = 0;
            string shaderNames = "";
            foreach (var r in rends)
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) { missing++; continue; }
                    if (shaders++ < 3) shaderNames += m.shader.name + ";";
                }
            File.AppendAllText($"{Out}/info.txt",
                $"{label}\t높이 {b.size.y:0.00} · 폭 {b.size.x:0.00} · 깊이 {b.size.z:0.00} · 바닥y {b.min.y:0.00} · 렌더러 {rends.Length} · 빈재질 {missing} · {shaderNames} · {path}\n");
            Object.DestroyImmediate(tex);
            return true;
        }
        catch (System.Exception e) { Debug.LogWarning($"[NpcCandidates] {label}: {e.Message}"); return false; }
        finally { pru.Cleanup(); }
    }
}
