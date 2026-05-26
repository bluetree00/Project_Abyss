using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

/// <summary>
/// 아이템 효과 VFX 프리팹을 Addressable에 일괄 등록.
/// 메뉴: Tools/Item VFX/Register Addressables
/// </summary>
public static class RegisterItemVfxAddressable
{
    private static readonly (string path, string address)[] Entries =
    {
        ("Assets/RelicFairy/Shared/Item/VFX/item_snow_white_mirror/VFX_DamageReflect.prefab",   "VFX_DamageReflect"),
        ("Assets/RelicFairy/Shared/Item/VFX/item_aladdin_carpet/VFX_JumpLandingDamage.prefab",  "VFX_JumpLandingDamage"),
        ("Assets/RelicFairy/Shared/Item/VFX/item_black_wings/VFX_DeathNegateAura.prefab",       "VFX_DeathNegateAura"),
        ("Assets/RelicFairy/Shared/Item/VFX/item_excalibur_fragment/VFX_ExtraAttack.prefab",     "VFX_ExtraAttack"),
        ("Assets/RelicFairy/Shared/Item/VFX/item_ifrit_ring/VFX_FireExplosion.prefab",          "VFX_FireExplosion"),
        ("Assets/RelicFairy/Shared/Item/VFX/item_thor_hammer_fragment/VFX_LightningStrike.prefab", "VFX_LightningStrike"),
        ("Assets/RelicFairy/Shared/Item/SOdata/ItemSODatabase.asset",                           "ItemSODatabase"),
    };

    [MenuItem("RelicFairy/Addressables/Register Item VFX")]
    public static void Register()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            UnityEngine.Debug.LogError("[RegisterItemVfx] AddressableAssetSettings not found.");
            return;
        }

        var group = settings.DefaultGroup;
        int count = 0;

        foreach (var (path, address) in Entries)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                UnityEngine.Debug.LogWarning($"[RegisterItemVfx] Asset not found: {path}");
                continue;
            }

            var entry = settings.CreateOrMoveEntry(guid, group, false, false);
            entry.address = address;
            count++;
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
        AssetDatabase.SaveAssets();
        UnityEngine.Debug.Log($"[RegisterItemVfx] {count}개 VFX Addressable 등록 완료.");
    }
}
