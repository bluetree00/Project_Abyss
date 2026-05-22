using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class FixMonsterHPBarSprite
{
    private const string PrefabPath = "Assets/Abyss/UI/WorldSpace/MonsterHPBar.prefab";

    [MenuItem("Tools/Abyss/Fix MonsterHPBar Sprite")]
    public static void Execute()
    {
        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefabAsset == null) { Debug.LogError($"[FixMonsterHPBarSprite] 프리팹 없음: {PrefabPath}"); return; }

        var sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        if (sprite == null) { Debug.LogError("[FixMonsterHPBarSprite] 빌트인 UISprite 로드 실패"); return; }

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        int fixed_count = 0;

        foreach (var img in root.GetComponentsInChildren<Image>(true))
        {
            if (img.name == "HpFill" || img.name == "GhostFill")
            {
                img.sprite = sprite;
                img.type   = Image.Type.Filled;
                Debug.Log($"[FixMonsterHPBarSprite] {img.name} 스프라이트 할당 완료");
                fixed_count++;
            }
        }

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();

        Debug.Log($"[FixMonsterHPBarSprite] 완료 — {fixed_count}개 Image 수정");
    }
}
