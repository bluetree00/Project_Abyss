using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public static class RegisterEffectAddressables
{
    private static readonly (string path, string key)[] Entries =
    {
        ("Assets/_ThirdParty/EffectSource/Hovl Studio/RPG VFX Bundle/Prefabs/Magic buffs and hits/Soft blue buff.prefab", "VFX_LandEffect"),
        ("Assets/_ThirdParty/EffectSource/Hovl Studio/RPG VFX Bundle/Random effect prefabs/Gold dot.prefab",              "GoldDot"),
    };

    [MenuItem("Tools/Abyss/Register Effect Addressables")]
    public static void Register()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("[RegisterEffects] Settings not found."); return; }

        foreach (var (path, key) in Entries)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) { Debug.LogError($"[RegisterEffects] Not found: {path}"); continue; }

            var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
            entry.address = key;
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
            Debug.Log($"[RegisterEffects] '{key}' 등록 완료");
        }

        AssetDatabase.SaveAssets();
    }
}
