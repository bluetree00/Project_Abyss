using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Build;

public static class AddressablesBuildRunner
{
    [MenuItem("RelicFairy/Addressables/Build Now")]
    public static void BuildAddressables()
    {
        AddressableAssetSettings.CleanPlayerContent(
            AddressableAssetSettingsDefaultObject.Settings.ActivePlayerDataBuilder);
        AddressableAssetSettings.BuildPlayerContent();
        UnityEngine.Debug.Log("[AddressablesBuildRunner] Addressables build complete.");
    }
}
