using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public static class AddKatanaHeavyToAddressables
{
    private static readonly (string fbxPath, string address)[] Entries =
    {
        (
            "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Defense/Root/HeavyCharge.FBX",
            "HeavyCharge"
        ),
        (
            "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/GhostSamurai_APose_Attack04_Inplace.FBX",
            "GroundHeavyAttack_01"
        ),
    };

    private const string GroupName = "WeaponAnimation";

    [MenuItem("Tools/Add Katana Heavy Clips to Addressables")]
    public static void Add()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[AddKatanaHeavy] AddressableAssetSettings를 찾을 수 없습니다.");
            return;
        }

        var group = settings.FindGroup(GroupName);
        if (group == null)
        {
            Debug.LogError($"[AddKatanaHeavy] 그룹 없음: {GroupName}");
            return;
        }

        int addedCount = 0;
        foreach (var (fbxPath, address) in Entries)
        {
            string guid = AssetDatabase.AssetPathToGUID(fbxPath);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogWarning($"[AddKatanaHeavy] GUID 없음: {fbxPath}");
                continue;
            }

            var existing = settings.FindAssetEntry(guid);
            if (existing != null)
            {
                Debug.Log($"[AddKatanaHeavy] 이미 등록됨: {address} (기존 address: {existing.address})");
                existing.address = address;
                continue;
            }

            var entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: true);
            entry.address = address;
            Debug.Log($"[AddKatanaHeavy] 등록 완료: {address} → {fbxPath}");
            addedCount++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[AddKatanaHeavy] 완료: {addedCount}개 신규 등록");
        EditorUtility.DisplayDialog("완료", $"{addedCount}개 클립을 Addressables에 등록했습니다.", "확인");
    }
}
