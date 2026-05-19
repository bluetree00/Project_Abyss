using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public static class AddGroundHeavyAttackAddressable
{
    private const string GroupName = "WeaponAnimation";
    private const string AssetGuid = "a40902f57abb8e94c9c2ededb8df4b47"; // GhostSamurai_APose_Attack04_Inplace.FBX
    private const string Address = "GroundHeavyAttack_01";

    [MenuItem("Tools/Add GroundHeavyAttack_01 Addressable")]
    public static void Add()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[AddGroundHeavyAttack] Addressable Settings 없음.");
            return;
        }

        var group = settings.FindGroup(GroupName);
        if (group == null)
        {
            Debug.LogError($"[AddGroundHeavyAttack] 그룹 '{GroupName}' 없음.");
            return;
        }

        var existing = settings.FindAssetEntry(AssetGuid);
        if (existing != null)
        {
            Debug.Log($"[AddGroundHeavyAttack] 이미 등록됨: address='{existing.address}'");
            return;
        }

        var entry = settings.CreateOrMoveEntry(AssetGuid, group, readOnly: false, postEvent: true);
        entry.address = Address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryAdded, entry, true);
        AssetDatabase.SaveAssets();

        Debug.Log($"[AddGroundHeavyAttack] 등록 완료: '{Address}' → {AssetGuid}");
    }
}
