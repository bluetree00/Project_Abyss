using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public static class RegisterWispVfxAddressable
{
    private const string VfxPath = "Assets/_ThirdParty/EffectSource/Hovl Studio/3D Lasers Pack/Prefabs/Laser beam 2 electro.prefab";
    private const string AddressKey = "wisp-vfx";

    [MenuItem("Tools/Abyss/Register Wisp VFX Addressable")]
    public static void Register()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[RegisterWispVfx] Addressable Settings not found.");
            return;
        }

        string guid = AssetDatabase.AssetPathToGUID(VfxPath);
        if (string.IsNullOrEmpty(guid))
        {
            Debug.LogError($"[RegisterWispVfx] Asset not found: {VfxPath}");
            return;
        }

        var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
        entry.address = AddressKey;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();

        Debug.Log($"[RegisterWispVfx] '{AddressKey}' 등록 완료 → {VfxPath}");
    }
}
