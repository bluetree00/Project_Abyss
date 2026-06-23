#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// QuestDatabase / AchievementDatabase 를 GameData 그룹에 Addressable 엔트리로 보장(API 경유 → 라이브 설정 갱신).
/// 외부 YAML 편집이 인메모리 AddressableAssetSettings에 반영 안 되는 문제 복구용.
/// 메뉴: RelicFairy/Debug/Fix Quest Addressables
/// </summary>
public static class QuestAddressableFixEditor
{
    [MenuItem("RelicFairy/Debug/Fix Quest Addressables")]
    public static void FixQuestAddressables()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("[QAFIX] AddressableAssetSettings 없음"); return; }

        var group = settings.FindGroup("GameData");
        if (group == null) { Debug.LogError("[QAFIX] GameData 그룹 없음"); return; }

        Ensure(settings, group, "Assets/RelicFairy/Data/Quest/Generated/QuestDatabase.asset", "QuestDatabase");
        Ensure(settings, group, "Assets/RelicFairy/Data/Quest/Generated/AchievementDatabase.asset", "AchievementDatabase");

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, null, true, true);
        AssetDatabase.SaveAssets();

        // 현재 그룹의 모든 엔트리 덤프 (라이브 설정 상태 확인)
        foreach (var e in group.entries)
            Debug.Log($"[QAFIX] LIVE ENTRY {group.Name} | addr={e.address} | guid={e.guid} | type={e.MainAssetType}");
        Debug.Log("[QAFIX] done");
    }

    private static void Ensure(AddressableAssetSettings settings, AddressableAssetGroup group, string assetPath, string address)
    {
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid)) { Debug.LogError($"[QAFIX] 에셋 없음: {assetPath}"); return; }

        var entry = settings.CreateOrMoveEntry(guid, group, false, false);
        entry.SetAddress(address);
        Debug.Log($"[QAFIX] ensured addr={address} guid={guid} type={entry.MainAssetType}");
    }
}
#endif
