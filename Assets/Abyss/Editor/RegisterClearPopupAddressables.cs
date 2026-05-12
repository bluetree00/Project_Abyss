
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public static class RegisterClearPopupAddressables
{
    [MenuItem("Tools/Abyss/Register Clear Popup Addressables")]
    public static void Register()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[RegisterClearPopupAddressables] Addressable Settings not found.");
            return;
        }

        string[] guids =
        {
            "aae8f0f888f36fb4294032a50f519abe", // UI_ClearReward
            "3defb73d0de15bf438a5b9bfda84e670", // UI_ClearResult
        };
        string[] addresses =
        {
            "UI/Popup/UI_ClearReward",
            "UI/Popup/UI_ClearResult",
        };

        // Weapons 그룹 찾기 (없으면 첫 번째 그룹 사용)
        var group = settings.FindGroup("Weapons") ?? settings.groups[0];

        for (int i = 0; i < guids.Length; i++)
        {
            var existing = settings.FindAssetEntry(guids[i]);
            if (existing != null)
            {
                existing.address = addresses[i];
                Debug.Log($"[RegisterClearPopupAddressables] Updated address: {addresses[i]}");
            }
            else
            {
                var entry = settings.CreateOrMoveEntry(guids[i], group);
                entry.address = addresses[i];
                Debug.Log($"[RegisterClearPopupAddressables] Registered: {addresses[i]}");
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[RegisterClearPopupAddressables] Done.");
    }
}
#endif
