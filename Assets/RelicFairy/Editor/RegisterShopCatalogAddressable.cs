#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

/// <summary>
/// 상점 카탈로그(ShopCatalogSO) 에셋을 Addressable에 일괄 등록하는 에디터 유틸.
/// 메뉴: Abyss/Shop/Register Shop Catalog Addressables
/// </summary>
public static class RegisterShopCatalogAddressable
{
    private static readonly (string path, string address)[] Catalogs =
    {
        ("Assets/Abyss/Systems/Stage/Shop/Catalog/ShopCatalog_Default.asset", "ShopCatalog_Default"),
    };

    [MenuItem("Abyss/Shop/Register Shop Catalog Addressables")]
    public static void Execute()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            UnityEngine.Debug.LogError("[RegisterShopCatalog] Addressable Settings not found.");
            return;
        }

        var defaultGroup = settings.DefaultGroup;

        foreach (var (path, address) in Catalogs)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                UnityEngine.Debug.LogWarning($"[RegisterShopCatalog] 에셋 없음: {path}");
                continue;
            }

            var entry = settings.FindAssetEntry(guid);
            if (entry == null)
                entry = settings.CreateOrMoveEntry(guid, defaultGroup, readOnly: false, postEvent: false);

            entry.address = address;
            UnityEngine.Debug.Log($"[RegisterShopCatalog] 등록: {address} → {path}");
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
        AssetDatabase.SaveAssets();
        UnityEngine.Debug.Log("[RegisterShopCatalog] 완료.");
    }
}
#endif
