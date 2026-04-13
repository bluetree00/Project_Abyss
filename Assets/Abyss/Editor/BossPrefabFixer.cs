#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class BossPrefabFixer
{
    [MenuItem("Abyss/Fix/1 Print FBX Mesh Info")]
    static void PrintFbxInfo()
    {
        PrintMeshes(
            "Assets/_ThirdParty/Malbers Animations/Dragons/4 - Unka the Dragon/Model/Unka Poly Art.FBX",
            "Unka Poly Art");
        PrintMeshes(
            "Assets/_ThirdParty/Magic Pig Games (Infinity PBR)/Characters/Treant/Model/Treant_Body.fbx",
            "Treant_Body");
    }

    static void PrintMeshes(string path, string label)
    {
        var all = AssetDatabase.LoadAllAssetsAtPath(path);
        Debug.Log($"=== {label} sub-assets ({all.Length}) ===");
        foreach (var a in all)
        {
            if (a is Mesh m)
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m, out string guid, out long localId);
                Debug.Log($"  Mesh '{m.name}'  fileID={localId}  guid={guid}");
            }
        }
    }

    // ─────────────────────────── DragonBoss ────────────────────────────

    [MenuItem("Abyss/Fix/2 Fix DragonBoss Prefab")]
    static void FixDragonBoss()
    {
        const string prefabPath =
            "Assets/Abyss/Characters/Monster/Monster/DragonBoss/Prefab/DragonBoss.prefab";
        const string fbxPath =
            "Assets/_ThirdParty/Malbers Animations/Dragons/4 - Unka the Dragon/Model/Unka Poly Art.FBX";
        const string matGreenPath =
            "Assets/_ThirdParty/Malbers Animations/Dragons/4 - Unka the Dragon/Materials/Poly Art/Unka Poly Art Green.mat";

        Mesh bodyMesh = null, eyesMesh = null;
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
        {
            if (!(a is Mesh m)) continue;
            string n = m.name.ToLower();
            if (n.Contains("eye"))                             eyesMesh = m;
            else if (n.Contains("unka") || n.Contains("body")) bodyMesh = m;
        }
        // fallback: 첫 번째 Mesh
        if (bodyMesh == null)
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (a is Mesh m) { bodyMesh = m; break; }

        if (bodyMesh == null) { Debug.LogError("[Fix] Unka body mesh 없음"); return; }
        Debug.Log($"[Fix] bodyMesh='{bodyMesh.name}' eyesMesh='{(eyesMesh?.name ?? "none")}'");

        var greenMat = AssetDatabase.LoadAssetAtPath<Material>(matGreenPath);
        if (greenMat == null)
            Debug.LogWarning("[Fix] Unka Poly Art Green.mat 없음 → 머터리얼 변경 생략");

        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        bool changed = false;

        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            string n = smr.gameObject.name.ToLower();
            if (n.Contains("eye") && eyesMesh != null)
            {
                smr.sharedMesh = eyesMesh;
                Debug.Log($"[Fix] Eyes SMR '{smr.gameObject.name}' mesh 설정");
                changed = true;
            }
            else if (!n.Contains("eye"))
            {
                smr.sharedMesh = bodyMesh;
                if (greenMat != null) smr.sharedMaterials = new[] { greenMat };
                Debug.Log($"[Fix] Body SMR '{smr.gameObject.name}' mesh+mat 설정");
                changed = true;
            }
        }

        if (changed)
        {
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log("[Fix] DragonBoss.prefab 저장 완료");
        }
        else Debug.LogWarning("[Fix] DragonBoss: 변경 대상 SMR 없음");

        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();
    }

    // ────────────────────────── ForestGuardian ─────────────────────────

    [MenuItem("Abyss/Fix/3 Fix ForestGuardian Prefab")]
    static void FixForestGuardian()
    {
        const string prefabPath =
            "Assets/Abyss/Characters/Monster/Monster/ForestGuardian/Prefab/ForestGuardian.prefab";
        const string fbxPath =
            "Assets/_ThirdParty/Magic Pig Games (Infinity PBR)/Characters/Treant/Model/Treant_Body.fbx";
        const string matBase =
            "Assets/_ThirdParty/Magic Pig Games (Infinity PBR)/Characters/Treant/Materials/";

        Mesh treantMesh = null;
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (a is Mesh m) { treantMesh = m; break; }

        if (treantMesh == null) { Debug.LogError("[Fix] Treant_Body mesh 없음"); return; }
        Debug.Log($"[Fix] treantMesh='{treantMesh.name}'");

        var matA      = AssetDatabase.LoadAssetAtPath<Material>(matBase + "TreantA.mat");
        var matALimbs = AssetDatabase.LoadAssetAtPath<Material>(matBase + "TreantALimbs.mat");
        var matD      = AssetDatabase.LoadAssetAtPath<Material>(matBase + "TreantD.mat");
        var matDLimbs = AssetDatabase.LoadAssetAtPath<Material>(matBase + "TreantDLimbs.mat");

        if (!matA || !matALimbs) Debug.LogWarning("[Fix] TreantA 머터리얼 없음");

        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        bool changed = false;

        // SMR 메시 + Phase1 머터리얼
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.sharedMesh = treantMesh;
            if (matA && matALimbs) smr.sharedMaterials = new[] { matA, matALimbs };
            Debug.Log($"[Fix] Treant SMR '{smr.gameObject.name}' 설정");
            changed = true;
            break;
        }

        // 데모 컴포넌트 제거
        string[] removeTypes =
        {
            "InfinityPBR.Demo.TreantDemo",
            "InfinityPBR.BlendShapesManager",
            "InfinityPBR.BlendShapesPresetManager",
            "InfinityPBR.GotHitDirection",
        };
        foreach (var typeName in removeTypes)
        {
            var t = System.Type.GetType(typeName + ", Assembly-CSharp")
                 ?? System.Type.GetType(typeName);
            if (t == null) { Debug.Log($"[Fix] 타입 없음(이미 제거됨?): {typeName}"); continue; }
            foreach (var comp in root.GetComponentsInChildren(t, true))
            {
                Object.DestroyImmediate(comp);
                Debug.Log($"[Fix] 제거: {typeName}");
                changed = true;
            }
        }

        // ForestGuardianMonster _phase1/_phase2Materials reflection 설정
        foreach (var mb in root.GetComponents<MonoBehaviour>())
        {
            var type = mb.GetType();
            if (type.Name != "ForestGuardianMonster") continue;

            var f1 = type.GetField("_phase1Materials",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var f2 = type.GetField("_phase2Materials",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (f1 != null && matA && matALimbs)
            {
                f1.SetValue(mb, new Material[] { matA, matALimbs });
                Debug.Log("[Fix] _phase1Materials = TreantA + TreantALimbs");
                changed = true;
            }
            if (f2 != null && matD && matDLimbs)
            {
                f2.SetValue(mb, new Material[] { matD, matDLimbs });
                Debug.Log("[Fix] _phase2Materials = TreantD + TreantDLimbs");
                changed = true;
            }
            break;
        }

        if (changed)
        {
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log("[Fix] ForestGuardian.prefab 저장 완료");
        }
        else Debug.LogWarning("[Fix] ForestGuardian: 변경 대상 없음");

        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();
    }
}
#endif
