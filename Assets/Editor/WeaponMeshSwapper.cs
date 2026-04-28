#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class WeaponMeshSwapper
{
    private const string LogPath = "Assets/_TempInspect/swap_log.txt";

    private struct WeaponSpec
    {
        public string weaponType;
        public int tier;
        public string fbxPath;
        public string materialPath;
        public bool isSkinned; // true for bow/crossbow

        public WeaponSpec(string weaponType, int tier, string fbxPath, string materialPath, bool isSkinned)
        {
            this.weaponType = weaponType;
            this.tier = tier;
            this.fbxPath = fbxPath;
            this.materialPath = materialPath;
            this.isSkinned = isSkinned;
        }
    }

    private static readonly WeaponSpec[] specs = new WeaponSpec[]
    {
        new WeaponSpec("Katana", 1,
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Swords/Meshes_Swords/Sword4_1.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Swords/Materials_Swords/Sword4_1_1.mat",
            false),
        new WeaponSpec("Katana", 2,
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Swords/Meshes_Swords/Sword4_2.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Swords/Materials_Swords/Sword4_2_2.mat",
            false),
        new WeaponSpec("Katana", 3,
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Swords/Meshes_Swords/Sword4_3.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Swords/Materials_Swords/Sword4_3_2.mat",
            false),
        new WeaponSpec("Greatsword", 1,
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Swords/Meshes_Swords/Sword1_1.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Swords/Materials_Swords/Sword1_1_1.mat",
            false),
        new WeaponSpec("Greatsword", 2,
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Swords/Meshes_Swords/Sword1_2.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Swords/Materials_Swords/Sword1_2_1.mat",
            false),
        new WeaponSpec("Greatsword", 3,
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Swords/Meshes_Swords/Sword1_3.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Swords/Materials_Swords/Sword1_3_1.mat",
            false),
        new WeaponSpec("Bow", 1,
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Bows/Meshes_Bows/Bow1_1.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Bows/Material_Bows/Bow1_1_1.mat",
            true),
        new WeaponSpec("Bow", 2,
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Bows/Meshes_Bows/Bow1_2.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Bows/Material_Bows/Bow1_2_2.mat",
            true),
        new WeaponSpec("Bow", 3,
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Bows/Meshes_Bows/Bow1_3.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Bows/Material_Bows/Bow1_3_1.mat",
            true),
        new WeaponSpec("Crossbow", 1,
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Crossbows/Meshes_Crossbows/Crossbow1_1.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Crossbows/Material_Crossbows/Crossbow1_1_3.mat",
            true),
        new WeaponSpec("Crossbow", 2,
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Crossbows/Meshes_Crossbows/Crossbow1_2.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Crossbows/Material_Crossbows/Crossbow1_2_2.mat",
            true),
        new WeaponSpec("Crossbow", 3,
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Crossbows/Meshes_Crossbows/Crossbow1_3.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Crossbows/Material_Crossbows/Crossbow1_3_2.mat",
            true),
    };

    [MenuItem("Tools/Weapon/Swap Visual Meshes (All 24 Prefabs)")]
    public static void SwapAllWeapons()
    {
        StringBuilder log = new StringBuilder();
        int success = 0, fail = 0;

        foreach (var spec in specs)
        {
            string weaponPrefab = string.Format("Assets/Abyss/Weapon/{0}/Prefabs/T{1}_{0}_Weapon.prefab", spec.weaponType, spec.tier);
            string displayPrefab = string.Format("Assets/Abyss/Weapon/{0}/Prefabs/T{1}_{0}_Display.prefab", spec.weaponType, spec.tier);

            log.AppendLine(string.Format("=== T{0}_{1} ({2}) ===", spec.tier, spec.weaponType, spec.isSkinned ? "skinned" : "static"));

            try
            {
                bool wOk = SwapWeaponPrefab(weaponPrefab, spec, log);
                if (wOk) success++; else fail++;
            }
            catch (Exception ex)
            {
                log.AppendLine("  Weapon prefab FAILED: " + ex.Message + "\n" + ex.StackTrace);
                fail++;
            }

            try
            {
                bool dOk = SwapDisplayPrefab(displayPrefab, spec, log);
                if (dOk) success++; else fail++;
            }
            catch (Exception ex)
            {
                log.AppendLine("  Display prefab FAILED: " + ex.Message + "\n" + ex.StackTrace);
                fail++;
            }

            log.AppendLine();
        }

        log.AppendLine(string.Format("\nTotal: success={0} fail={1}", success, fail));

        string dir = Path.GetDirectoryName(LogPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(LogPath, log.ToString());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Weapon swap log written: " + LogPath);
    }

    private static bool SwapWeaponPrefab(string prefabPath, WeaponSpec spec, StringBuilder log)
    {
        if (!File.Exists(prefabPath))
        {
            log.AppendLine("  [Weapon] Not found: " + prefabPath);
            return false;
        }

        GameObject prefabAsset = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            if (spec.isSkinned)
            {
                ReplaceSkinnedHierarchy(prefabAsset, spec, log, "Weapon");
            }
            else
            {
                ReplaceStaticMesh(prefabAsset, spec, log, "Weapon");
            }

            PrefabUtility.SaveAsPrefabAsset(prefabAsset, prefabPath);
            log.AppendLine("  [Weapon] Saved: " + prefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabAsset);
        }
    }

    private static bool SwapDisplayPrefab(string prefabPath, WeaponSpec spec, StringBuilder log)
    {
        if (!File.Exists(prefabPath))
        {
            log.AppendLine("  [Display] Not found: " + prefabPath);
            return false;
        }

        // Display prefab uses Weapon prefab as a nested prefab. Just resaving Display
        // after Weapon was changed will pick up the new visuals. But we still verify by
        // forcing a reimport.
        AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
        log.AppendLine("  [Display] Re-imported: " + prefabPath);
        return true;
    }

    private static void ReplaceStaticMesh(GameObject prefabRoot, WeaponSpec spec, StringBuilder log, string label)
    {
        MeshFilter mf = prefabRoot.GetComponent<MeshFilter>();
        MeshRenderer mr = prefabRoot.GetComponent<MeshRenderer>();
        if (mf == null || mr == null)
        {
            log.AppendLine(string.Format("  [{0}] no MeshFilter/MeshRenderer on root", label));
            return;
        }

        Mesh newMesh = ExtractMainMesh(spec.fbxPath);
        Material newMat = AssetDatabase.LoadAssetAtPath<Material>(spec.materialPath);
        if (newMesh == null)
        {
            log.AppendLine(string.Format("  [{0}] mesh load failed: {1}", label, spec.fbxPath));
            return;
        }
        if (newMat == null)
        {
            log.AppendLine(string.Format("  [{0}] material load failed: {1}", label, spec.materialPath));
            return;
        }

        mf.sharedMesh = newMesh;
        mr.sharedMaterials = new Material[] { newMat };
        log.AppendLine(string.Format("  [{0}] mesh={1} mat={2}", label, newMesh.name, newMat.name));
    }

    private static void ReplaceSkinnedHierarchy(GameObject prefabRoot, WeaponSpec spec, StringBuilder log, string label)
    {
        // Strategy:
        // 1. Find all SkinnedMeshRenderer + skeleton-related child GameObjects (by name match against fbx hierarchy).
        //    Easier: identify by checking which children come from the OLD fbx. We do the conservative approach
        //    of removing any child that has a SkinnedMeshRenderer or the known skeleton root names, then add
        //    the new fbx as a child.
        // 2. Remove old skeleton/skinned children but preserve other children (Root, Tip, particles, nested prefabs).
        // 3. Instantiate the new fbx as a child.

        // Identify the skinned mesh renderer to find its bones' root
        SkinnedMeshRenderer existingSmr = prefabRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Transform skeletonRoot = null;
        Transform meshChild = null;
        if (existingSmr != null)
        {
            meshChild = existingSmr.transform;
            // skeletonRoot: a sibling of meshChild whose name starts with bow_01 or crossbow_01 or contains the rig
            // Heuristic: it's a top-level child of prefabRoot whose name is not Root/Tip and not the smr's GameObject
            foreach (Transform child in prefabRoot.transform)
            {
                if (child == meshChild) continue;
                if (child.name == "Root" || child.name == "Tip") continue;
                // Particles/effects from Display — but Weapon prefabs typically don't have these
                if (child.GetComponent<ParticleSystem>() != null) continue;
                skeletonRoot = child;
                break;
            }
        }

        // Remove old skeleton root
        if (skeletonRoot != null)
        {
            log.AppendLine(string.Format("  [{0}] removing old skeleton: {1}", label, skeletonRoot.name));
            UnityEngine.Object.DestroyImmediate(skeletonRoot.gameObject, true);
        }
        // Remove old smr child
        if (meshChild != null)
        {
            log.AppendLine(string.Format("  [{0}] removing old smr child: {1}", label, meshChild.name));
            UnityEngine.Object.DestroyImmediate(meshChild.gameObject, true);
        }

        // Instantiate new fbx as child
        GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(spec.fbxPath);
        if (fbxAsset == null)
        {
            log.AppendLine(string.Format("  [{0}] fbx load failed: {1}", label, spec.fbxPath));
            return;
        }
        GameObject fbxInstance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset, prefabRoot.transform);
        // Unpack so its hierarchy is plain GameObjects (not a nested model prefab) — this matches the
        // existing structure where the fbx was already unpacked into the prefab.
        PrefabUtility.UnpackPrefabInstance(fbxInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        // Move children of fbxInstance up to prefabRoot, then delete fbxInstance wrapper, so the
        // hierarchy mirrors the original layout (skeleton + smr as siblings under prefabRoot).
        List<Transform> toReparent = new List<Transform>();
        foreach (Transform c in fbxInstance.transform) toReparent.Add(c);
        foreach (var c in toReparent) c.SetParent(prefabRoot.transform, false);
        UnityEngine.Object.DestroyImmediate(fbxInstance, true);

        // Apply material to new SkinnedMeshRenderer
        SkinnedMeshRenderer newSmr = prefabRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (newSmr != null)
        {
            Material newMat = AssetDatabase.LoadAssetAtPath<Material>(spec.materialPath);
            if (newMat == null)
            {
                log.AppendLine(string.Format("  [{0}] material load failed: {1}", label, spec.materialPath));
            }
            else
            {
                newSmr.sharedMaterials = new Material[] { newMat };
                log.AppendLine(string.Format("  [{0}] new smr={1} mesh={2} mat={3}",
                    label, newSmr.name, newSmr.sharedMesh != null ? newSmr.sharedMesh.name : "null", newMat.name));
            }
        }
        else
        {
            log.AppendLine(string.Format("  [{0}] no SkinnedMeshRenderer found after swap!", label));
        }
    }

    private static Mesh ExtractMainMesh(string fbxPath)
    {
        UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        Mesh result = null;
        foreach (var o in all)
        {
            Mesh m = o as Mesh;
            if (m == null) continue;
            // Skip null/empty
            if (m.vertexCount == 0) continue;
            // Pick the first one (each fbx has one main mesh)
            result = m;
            break;
        }
        return result;
    }
}
#endif
