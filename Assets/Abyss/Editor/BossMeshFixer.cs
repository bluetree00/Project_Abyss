using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// DragonBoss / ForestGuardian 프리팹의 SkinnedMeshRenderer에
/// Art 폴더 FBX에서 추출한 Mesh·Material을 연결한다.
/// </summary>
public static class BossMeshFixer
{
    [MenuItem("Abyss/Fix Boss Meshes")]
    public static void FixAll()
    {
        FixDragonBoss();
        FixForestGuardian();
        AssetDatabase.SaveAssets();
        Debug.Log("[BossMeshFixer] 완료.");
    }

    // ──────────────────────────────────────────────────────────────
    // DragonBoss
    // ──────────────────────────────────────────────────────────────
    static void FixDragonBoss()
    {
        const string fbxPath    = "Assets/Abyss/Characters/Monster/Monster/DragonBoss/Art/Unka Poly Art.FBX";
        const string prefabPath = "Assets/Abyss/Characters/Monster/Monster/DragonBoss/Prefab/DragonBoss.prefab";

        var subassets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        var allMeshes = subassets.OfType<Mesh>().ToArray();
        Debug.Log($"[BossMeshFixer] DragonBoss FBX 메쉬 목록: {string.Join(", ", allMeshes.Select(m => m.name))}");

        // 이름이 "Unka Poly Art"인 메쉬를 우선 선택, 없으면 가장 버텍스 수가 많은 메쉬
        var mesh = allMeshes.FirstOrDefault(m => m.name == "Unka Poly Art")
                   ?? allMeshes.OrderByDescending(m => m.vertexCount).FirstOrDefault();
        var mat  = subassets.OfType<Material>().FirstOrDefault(m => m.name != "Eyes")
                   ?? subassets.OfType<Material>().FirstOrDefault();

        if (mesh == null) { Debug.LogError("[BossMeshFixer] DragonBoss: FBX에서 Mesh를 찾을 수 없습니다."); return; }
        Debug.Log($"[BossMeshFixer] DragonBoss 선택된 mesh={mesh.name}, mat={mat?.name ?? "없음"}");

        using var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath);
        var smrs = scope.prefabContentsRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var smr in smrs)
        {
            if (smr.name != "Unka Poly Art") continue;
            smr.sharedMesh = mesh;
            if (mat != null) smr.sharedMaterials = new[] { mat };
            Debug.Log("[BossMeshFixer] DragonBoss SkinnedMeshRenderer 연결 완료.");
        }
    }

    // ──────────────────────────────────────────────────────────────
    // ForestGuardian
    // ──────────────────────────────────────────────────────────────
    static void FixForestGuardian()
    {
        const string fbxPath    = "Assets/Abyss/Characters/Monster/Monster/ForestGuardian/Art/Treant_Body.fbx";
        const string prefabPath = "Assets/Abyss/Characters/Monster/Monster/ForestGuardian/Prefab/ForestGuardian.prefab";
        const string matFolder  = "Assets/Abyss/Characters/Monster/Monster/ForestGuardian/Art/Materials";

        var subassets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        var mesh      = subassets.OfType<Mesh>().FirstOrDefault();
        if (mesh == null) { Debug.LogError("[BossMeshFixer] ForestGuardian: FBX에서 Mesh를 찾을 수 없습니다."); return; }
        Debug.Log($"[BossMeshFixer] ForestGuardian mesh={mesh.name}");

        var matA      = AssetDatabase.LoadAssetAtPath<Material>($"{matFolder}/TreantA.mat");
        var matALimbs = AssetDatabase.LoadAssetAtPath<Material>($"{matFolder}/TreantALimbs.mat");
        var matB      = AssetDatabase.LoadAssetAtPath<Material>($"{matFolder}/TreantB.mat");
        var matBLimbs = AssetDatabase.LoadAssetAtPath<Material>($"{matFolder}/TreantBLimbs.mat");

        using var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath);
        var root = scope.prefabContentsRoot;

        // ── SkinnedMeshRenderer 연결 ──────────────────────────────
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.name != "Treant") continue;
            smr.sharedMesh = mesh;
            if (matA != null && matALimbs != null)
                smr.sharedMaterials = new[] { matA, matALimbs };
            Debug.Log("[BossMeshFixer] ForestGuardian SkinnedMeshRenderer 연결 완료.");
        }

        Debug.Log("[BossMeshFixer] ForestGuardian SkinnedMeshRenderer 연결 완료 (Phase 머티리얼은 ForestGuardianMonster 구현 후 자동 연결됩니다).");
    }

    static void SetMaterialArray(SerializedProperty prop, Material m0, Material m1)
    {
        if (prop == null) return;

        int count = (m1 != null) ? 2 : (m0 != null ? 1 : 0);
        prop.arraySize = count;
        if (count > 0) prop.GetArrayElementAtIndex(0).objectReferenceValue = m0;
        if (count > 1) prop.GetArrayElementAtIndex(1).objectReferenceValue = m1;
    }
}
