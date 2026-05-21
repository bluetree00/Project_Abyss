using UnityEditor;
using UnityEngine;

public static class SetEntranceVfx
{
    private const string EntrancePath = "Assets/Abyss/Shared/Characters/PlayerData/Entrance/DefaultPlayerEntrance.asset";
    private const string VfxPath = "Assets/_ThirdParty/EffectSource/Hovl Studio/RPG VFX Bundle/Prefabs/Magic buffs and hits/Soft blue buff.prefab";

    [MenuItem("Tools/Abyss/Set Entrance VFX")]
    public static void Set()
    {
        var so = AssetDatabase.LoadAssetAtPath<DefaultPlayerEntranceSO>(EntrancePath);
        if (so == null) { Debug.LogError("[SetEntranceVfx] DefaultPlayerEntrance.asset not found."); return; }

        var vfx = AssetDatabase.LoadAssetAtPath<GameObject>(VfxPath);
        if (vfx == null) { Debug.LogError("[SetEntranceVfx] Soft blue buff.prefab not found."); return; }

        var serialized = new SerializedObject(so);
        serialized.FindProperty("vfxPrefab").objectReferenceValue = vfx;
        serialized.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();

        Debug.Log("[SetEntranceVfx] vfxPrefab = Soft blue buff 설정 완료");
    }
}
