#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// CHAPTER_N_ROOM_POOL.csv(Ch1~4)를 Addressables에 "CHAPTER_N_ROOM_POOL" 키로 등록.
/// 서버(뒤끝) 미업로드 시 ZoneLayoutManager.LoadPoolAsync의 Addressables 폴백 경로로 사용.
/// 메뉴: RelicFairy/Addressables/Register Room Pool
/// </summary>
public static class RegisterRoomPoolAddressable
{
    private const int    ChapterCount = 4;
    private const string GroupName    = "ChartData";

    [MenuItem("RelicFairy/Addressables/Register Room Pool")]
    public static void Register()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[RegisterRoomPool] Addressable 설정을 찾을 수 없습니다.");
            return;
        }

        var group = settings.FindGroup(GroupName)
                    ?? settings.CreateGroup(GroupName, false, false, false, null);

        for (int n = 1; n <= ChapterCount; n++)
        {
            var csvPath = $"Assets/RelicFairy/Docs/CHAPTER_{n}_ROOM_POOL.csv";
            var addrKey = $"CHAPTER_{n}_ROOM_POOL";

            var guid = AssetDatabase.AssetPathToGUID(csvPath);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogWarning($"[RegisterRoomPool] CSV 없음 — 스킵: {csvPath}");
                continue;
            }

            var entry = settings.CreateOrMoveEntry(guid, group, false, false);
            entry.address = addrKey;
            Debug.Log($"[RegisterRoomPool] 등록: '{csvPath}' → key='{addrKey}'");
        }

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
    }
}
#endif
