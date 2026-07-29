#if UNITY_EDITOR
using INab.VFXAssets;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 데모씬과 동일한 방식으로 Lich 프리팹에 CharacterEffect를 사전 설정한다.
/// Tools/RelicFairy/Setup Lich Character Effect 메뉴에서 실행.
///
/// 데모 방식:
///   meshRenderer = 캐릭터 SkinnedMeshRenderer 할당
///   effectPrefab = 이펙트 프리팹 할당
///   _InstantiateEffectPrefab() 실행 → instantiatedEffectPrefab/vfxComponent/vfxBinder 저장
/// </summary>
public static class Setup_LichCharacterEffect
{
    private const string LichPrefabPath =
        "Assets/RelicFairy/Characters/Monster/Boss_Monster/Lich/Lich.prefab";

    [MenuItem("Tools/RelicFairy/Clear Lich Aura Effect Instance")]
    public static void ClearEffectInstance()
    {
        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(LichPrefabPath);
        if (prefabAsset == null) { Debug.LogError("Lich 프리팹을 찾지 못했습니다."); return; }

        using var scope = new PrefabUtility.EditPrefabContentsScope(LichPrefabPath);
        var root = scope.prefabContentsRoot;

        foreach (var ce in root.GetComponentsInChildren<CharacterEffect>(true))
        {
            if (ce.instantiatedEffectPrefab != null)
            {
                Object.DestroyImmediate(ce.instantiatedEffectPrefab);
                ce.instantiatedEffectPrefab = null;
            }
            ce.vfxComponent = null;
            ce.vfxBinder = null;
            EditorUtility.SetDirty(ce);
            Debug.Log($"[LichCESetup] 이펙트 인스턴스 제거: {ce.gameObject.name}");
        }

        Debug.Log("[LichCESetup] 완료.");
    }

    [MenuItem("Tools/RelicFairy/Setup Lich Character Effect")]
    public static void Run() => RunWithEffect(null);

    [MenuItem("Tools/RelicFairy/Setup Lich Character Effect (Arcane Fire)")]
    public static void RunArcane() => RunWithEffect("Assets/_ThirdParty/INab Studio/Vfx Assets/Character Effects/Effect Prefabs/Arcane Fire.prefab");

    [MenuItem("Tools/RelicFairy/Setup Lich Character Effect (Mana)")]
    public static void RunMana() => RunWithEffect("Assets/_ThirdParty/INab Studio/Vfx Assets/Character Effects/Effect Prefabs/Mana.prefab");

    [MenuItem("Tools/RelicFairy/Setup Lich Character Effect (Cursed Mana)")]
    public static void RunCursed() => RunWithEffect("Assets/_ThirdParty/INab Studio/Vfx Assets/Character Effects/Effect Prefabs/Cursed Mana.prefab");

    private static void RunWithEffect(string overridePrefabPath)
    {
        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(LichPrefabPath);
        if (prefabAsset == null) { Debug.LogError("Lich 프리팹을 찾지 못했습니다."); return; }

        GameObject overridePrefab = null;
        if (!string.IsNullOrEmpty(overridePrefabPath))
        {
            overridePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(overridePrefabPath);
            if (overridePrefab == null)
            {
                Debug.LogError($"[LichCESetup] 이펙트 프리팹 없음: {overridePrefabPath}"); return;
            }
        }

        using var scope = new PrefabUtility.EditPrefabContentsScope(LichPrefabPath);
        var root = scope.prefabContentsRoot;

        // ── 1. 전신 SkinnedMeshRenderer 탐색
        SkinnedMeshRenderer chestSMR = null;
        foreach (var name in new[] { "Shirt", "Chest", "Spine" })
        {
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.name == name) { chestSMR = smr; break; }
            if (chestSMR != null) break;
        }
        if (chestSMR == null) { Debug.LogError("[LichCESetup] 전신 SMR를 찾지 못했습니다."); return; }
        Debug.Log("[LichCESetup] 바디 SMR: " + chestSMR.transform.GetPath());

        // ── 2. CharacterEffect 설정 (VFX_Aura_Void)
        var effects = root.GetComponentsInChildren<CharacterEffect>(true);
        if (effects.Length == 0) { Debug.LogError("[LichCESetup] CharacterEffect 없음"); return; }

        foreach (var ce in effects)
        {
            ce.meshRenderer = chestSMR;

            if (overridePrefab != null)
                ce.effectPrefab = overridePrefab;

            if (ce.effectPrefab == null)
            {
                Debug.LogWarning($"[LichCESetup] effectPrefab null — 건너뜀 ({ce.gameObject.name})");
                continue;
            }

            if (ce.instantiatedEffectPrefab != null)
            {
                Object.DestroyImmediate(ce.instantiatedEffectPrefab);
                ce.instantiatedEffectPrefab = null;
            }

            ce._InstantiateEffectPrefab(autoStartEffect: false);

            if (ce.vfxComponent != null)
                ce.vfxComponent.initialEventName = "OnPlay";

            EditorUtility.SetDirty(ce);
            Debug.Log($"[LichCESetup] 완료: {ce.gameObject.name} → {ce.effectPrefab.name}");
        }

        Debug.Log("[LichCESetup] Lich 프리팹 설정 완료.");
    }
}

static class TransformExtensions
{
    public static string GetPath(this Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}
#endif
