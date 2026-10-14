#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// [조사 · 편집 모드] 플레이어 의상 부위 렌더 비교(10-08 · 사용자 「플레이어를 더 가리는 의상 · 유물별 · 성장에 따라 덧입기」).
/// CombatGirls 모델은 부위가 나뉘어 있다 — 몸 3종(Body_Full · Body_Cloth · Body_Squire) · 갑옷(상의 · 치마 · 부츠 · 어깨 · 벨트) · 견습 기사 옷(Squire_Cloth).
/// 플레이어 프리팹을 <b>미리보기 장면</b>(열린 장면과 별개)에 띄워 ① 지금 모습 ② 부위 하나씩 앞 · 뒤로 찍는다.
/// 결과 Temp/costume/*.png + Temp/costume/parts.txt(부위 · 켜짐 · 재질).
/// </summary>
public static class CostumePartsRenderEditor
{
    private const string PlayerPrefab = "Assets/RelicFairy/Characters/Player/Gawain/Prefabs/PlayerCharacter.prefab";
    private const string OutDir = "Temp/costume";
    private static readonly string[] Parts =
    {
        "Body_Full", "Body_Cloth", "Body_Squire", "Squire_Cloth",
        "SwordAndShiled_Cloth_Top", "SwordAndShiled_Cloth_Skirt", "SwordAndShiled_Cloth_Boots", "SwordAndShiled_Clotth_Pauldrons", "Belt_Root",
    };

    [MenuItem("RelicFairy/Debug/의상/1 의상 부위 렌더 비교 (편집 모드)")]
    private static void RenderParts()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[의상] 편집 모드에서만"); return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
        if (prefab == null) { Debug.LogWarning("[의상] 플레이어 프리팹 없음"); return; }
        Directory.CreateDirectory(OutDir);

        var scene = EditorSceneManager.NewPreviewScene();
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        var camGo = new GameObject("~CostumeCam");
        var lightGo = new GameObject("~CostumeLight");
        SceneManager.MoveGameObjectToScene(camGo, scene);
        SceneManager.MoveGameObjectToScene(lightGo, scene);
        var report = new StringBuilder();
        try
        {
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            lightGo.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = scene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
            cam.fieldOfView = 28f;
            cam.nearClipPlane = 0.05f;

            // 부위 이름 → 트랜스폼(깊이 상관없이 첫 번째)
            var byName = new Dictionary<string, Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name)) byName[t.name] = t;

            var original = new Dictionary<string, bool>();
            foreach (var n in Parts)
            {
                if (!byName.TryGetValue(n, out var t)) { report.AppendLine($"{n}: 없음"); continue; }
                original[n] = t.gameObject.activeSelf;
                var r = t.GetComponentInChildren<Renderer>(true);
                string mats = r != null ? string.Join(", ", System.Array.ConvertAll(r.sharedMaterials, m => m != null ? m.name : "null")) : "렌더러 없음";
                report.AppendLine($"{n}: 지금 {(t.gameObject.activeSelf ? "켬" : "끔")} · 재질 {mats}");
            }

            // ① 지금 모습(앞 · 뒤 · 옆)
            Shoot(cam, go, "00_now");

            // ② 부위 하나씩 — 나머지 의상 부위는 끄고 그 부위만 켠다(얼굴 · 머리는 그대로)
            foreach (var n in Parts)
            {
                if (!byName.ContainsKey(n)) continue;
                foreach (var m in Parts) if (byName.TryGetValue(m, out var tm)) tm.gameObject.SetActive(m == n);
                Shoot(cam, go, "part_" + n, sideToo: false);
            }
            foreach (var kv in original) byName[kv.Key].gameObject.SetActive(kv.Value);
            File.WriteAllText(Path.Combine(OutDir, "parts.txt"), report.ToString());
            Debug.Log("[의상] 부위 렌더 끝 → " + OutDir + "\n" + report);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    // ② 겹쳐 입기 · 색 변형 — 견습 기사 옷 위에 갑옷 부위를 덧입었을 때 뚫림 · 갑옷 색 6종
    private static readonly (string name, string[] on)[] Combos =
    {
        ("c1_squire",               new[] { "Body_Squire", "Squire_Cloth" }),
        ("c2_squire_pauldron",      new[] { "Body_Squire", "Squire_Cloth", "SwordAndShiled_Clotth_Pauldrons" }),
        ("c3_squire_pauldron_boots",new[] { "Body_Squire", "Squire_Cloth", "SwordAndShiled_Clotth_Pauldrons", "SwordAndShiled_Cloth_Boots" }),
        ("c4_squire_top",           new[] { "Body_Squire", "Squire_Cloth", "SwordAndShiled_Cloth_Top", "SwordAndShiled_Clotth_Pauldrons" }),
        ("c5_squire_skirt",         new[] { "Body_Squire", "Squire_Cloth", "SwordAndShiled_Cloth_Skirt", "Belt_Root" }),
    };

    [MenuItem("RelicFairy/Debug/의상/2 겹쳐 입기 · 색 변형 렌더 (편집 모드)")]
    private static void RenderCombos()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[의상] 편집 모드에서만"); return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
        if (prefab == null) return;
        Directory.CreateDirectory(OutDir);
        var scene = EditorSceneManager.NewPreviewScene();
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        var camGo = new GameObject("~CostumeCam");
        var lightGo = new GameObject("~CostumeLight");
        SceneManager.MoveGameObjectToScene(camGo, scene);
        SceneManager.MoveGameObjectToScene(lightGo, scene);
        try
        {
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            lightGo.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = scene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
            cam.fieldOfView = 28f;
            cam.nearClipPlane = 0.05f;
            var byName = new Dictionary<string, Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name)) byName[t.name] = t;

            foreach (var (name, on) in Combos)
            {
                foreach (var n in Parts) if (byName.TryGetValue(n, out var t)) t.gameObject.SetActive(System.Array.IndexOf(on, n) >= 0);
                Shoot(cam, go, name);
            }

            // 갑옷 색 변형 — 지금 입은 갑옷(몸 Body_Cloth + 상의 · 치마 · 부츠 · 어깨)에 Shield_Sword_Cloth_01~06
            var armor = new[] { "SwordAndShiled_Cloth_Top", "SwordAndShiled_Cloth_Skirt", "SwordAndShiled_Cloth_Boots", "SwordAndShiled_Clotth_Pauldrons" };
            foreach (var n in Parts) if (byName.TryGetValue(n, out var t)) t.gameObject.SetActive(n == "Body_Cloth" || n == "Belt_Root" || System.Array.IndexOf(armor, n) >= 0);
            for (int i = 1; i <= 6; i++)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"Assets/CombatGirlsCharacterPack/CombatGirl_Shield/Materials/Cloth/Shield_Sword_Cloth_0{i}.mat");
                if (mat == null) continue;
                foreach (var n in armor)
                    if (byName.TryGetValue(n, out var t))
                        foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                        {
                            var arr = r.sharedMaterials;
                            for (int k = 0; k < arr.Length; k++) arr[k] = mat;
                            r.sharedMaterials = arr;   // 미리보기 장면의 사본에만 — 프리팹 · 에셋은 그대로
                        }
                Capture(cam, go.transform.position + Vector3.up * 0.95f + new Vector3(0f, 0.1f, 3.6f), go.transform.position + Vector3.up * 0.95f, $"color_0{i}");
            }
            Debug.Log("[의상] 겹쳐 입기 · 색 렌더 끝 → " + OutDir);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    // ③ 유물 의상 단계 — PlayerCostume.Wear(유물 의상, 0~3)로 입혀 앞 · 뒤 · 옆 + 머리색 후보(3단계 앞)
    [MenuItem("RelicFairy/Debug/의상/3 유물 의상 단계 렌더 (편집 모드)")]
    private static void RenderStages()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[의상] 편집 모드에서만"); return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
        if (prefab == null) return;
        Directory.CreateDirectory(OutDir);
        var scene = EditorSceneManager.NewPreviewScene();
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        var camGo = new GameObject("~CostumeCam");
        var lightGo = new GameObject("~CostumeLight");
        SceneManager.MoveGameObjectToScene(camGo, scene);
        SceneManager.MoveGameObjectToScene(lightGo, scene);
        try
        {
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            lightGo.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = scene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
            cam.fieldOfView = 28f;
            cam.nearClipPlane = 0.05f;
            var costume = go.GetComponent<PlayerCostume>();
            if (costume == null) { Debug.LogWarning("[의상] PlayerCostume 없음 — 설정 메뉴 먼저"); return; }
            Transform hair = null;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "SwordAndShiled_Hair") { hair = t; break; }
            var hairR = hair != null ? hair.GetComponent<Renderer>() : null;
            var hairOrig = hairR != null ? hairR.sharedMaterials : null;

            Shoot(cam, go, "s_none_0");
            foreach (var id in new[] { "Gawain", "Lancelot" })
            {
                var so = AssetDatabase.LoadAssetAtPath<RelicCostumeSO>($"Assets/RelicFairy/Characters/Player/Costume/Costume_{id}.asset");
                if (so == null) continue;
                for (int stage = 1; stage <= 3; stage++)
                {
                    costume.Wear(so, stage);
                    Shoot(cam, go, $"s_{id}_{stage}");
                }
                // 머리색 후보(D1) — 3단계 차림에 팩 머리 재질 01~05
                for (int h = 1; h <= 5 && hairR != null; h++)
                {
                    var hm = AssetDatabase.LoadAssetAtPath<Material>($"Assets/CombatGirlsCharacterPack/CombatGirl_Shield/Materials/Hair/Shield_Sword_Hair_0{h}.mat");
                    if (hm == null) continue;
                    var arr = hairR.sharedMaterials;
                    for (int k = 0; k < arr.Length; k++) if (hairOrig[k] != null && hairOrig[k].name.StartsWith("Shield_Sword_Hair")) arr[k] = hm;
                    hairR.sharedMaterials = arr;
                    Vector3 c = go.transform.position + Vector3.up * 1.25f;
                    Capture(cam, c + new Vector3(0.9f, 0.1f, 1.9f), c, $"h_{id}_{h}");
                }
                if (hairR != null) hairR.sharedMaterials = hairOrig;
            }
            File.WriteAllText(Path.Combine(OutDir, "hair.txt"), hairOrig == null ? "머리 없음" : string.Join(", ", System.Array.ConvertAll(hairOrig, m => m != null ? m.name : "null")));
            Debug.Log("[의상] 유물 단계 렌더 끝 → " + OutDir);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void Shoot(Camera cam, GameObject target, string name, bool sideToo = true)
    {
        Vector3 center = target.transform.position + Vector3.up * 0.85f;
        Capture(cam, center + new Vector3(0f, 0.1f, 3.6f), center, $"{name}_front");
        Capture(cam, center + new Vector3(0f, 0.1f, -3.6f), center, $"{name}_back");
        if (sideToo) Capture(cam, center + new Vector3(3.6f, 0.1f, 0f), center, $"{name}_side");
    }

    private static void Capture(Camera cam, Vector3 pos, Vector3 look, string name)
    {
        cam.transform.position = pos;
        cam.transform.LookAt(look);
        const int w = 512, h = 768;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = null;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }
}
#endif
