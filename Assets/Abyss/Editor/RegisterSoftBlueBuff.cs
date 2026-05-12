using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public static class RegisterSoftBlueBuff
{
    private const string PrefabPath = "Assets/_ThirdParty/EffectSource/Hovl Studio/RPG VFX Bundle/Prefabs/Magic buffs and hits/Soft blue buff.prefab";
    private const string AddressKey = "SoftBlueBuff";

    [MenuItem("Tools/Abyss/Register SoftBlueBuff Addressable")]
    public static void Execute()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("[RegisterSoftBlueBuff] Addressable Settings 없음"); return; }

        var guid = AssetDatabase.AssetPathToGUID(PrefabPath);
        if (string.IsNullOrEmpty(guid)) { Debug.LogError($"[RegisterSoftBlueBuff] 프리팹 없음: {PrefabPath}"); return; }

        var group = settings.DefaultGroup;
        var entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: false);
        entry.address = AddressKey;

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();

        Debug.Log($"[RegisterSoftBlueBuff] 완료 — '{PrefabPath}' → key='{AddressKey}'");
    }
}
