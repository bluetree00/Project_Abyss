using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public static class RegisterBuff2
{
    private const string PrefabPath = "Assets/_ThirdParty/EffectSource/Hovl Studio/RPG VFX Bundle/Prefabs/Magic buffs and hits/Buff 2.prefab";
    private const string AddressKey = "Buff2";

    [MenuItem("Tools/Abyss/Register Buff2 Addressable")]
    public static void Execute()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("[RegisterBuff2] Addressable Settings 없음"); return; }

        var guid = AssetDatabase.AssetPathToGUID(PrefabPath);
        if (string.IsNullOrEmpty(guid)) { Debug.LogError($"[RegisterBuff2] 프리팹 없음: {PrefabPath}"); return; }

        var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup, readOnly: false, postEvent: false);
        entry.address = AddressKey;

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();

        Debug.Log($"[RegisterBuff2] 완료 — key='{AddressKey}'");
    }
}
