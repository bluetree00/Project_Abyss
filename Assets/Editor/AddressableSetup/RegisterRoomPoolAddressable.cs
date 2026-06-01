#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// CHAPTER_1_ROOM_POOL.csv를 Addressables에 "CHAPTER_1_ROOM_POOL" 키로 등록.
/// 서버(뒤끝) 미업로드 시 ZoneLayoutManager.LoadPoolAsync의 Addressables 폴백 경로로 사용.
/// 메뉴: RelicFairy/Addressables/Register Room Pool
/// </summary>
public static class RegisterRoomPoolAddressable
{
    private const string CsvPath   = "Assets/RelicFairy/Docs/CHAPTER_1_ROOM_POOL.csv";
    private const string AddrKey   = "CHAPTER_1_ROOM_POOL";
    private const string GroupName = "ChartData";

    [MenuItem("RelicFairy/Addressables/Register Room Pool")]
    public static void Register()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[RegisterRoomPool] Addressable 설정을 찾을 수 없습니다.");
            return;
        }

        var guid = AssetDatabase.AssetPathToGUID(CsvPath);
        if (string.IsNullOrEmpty(guid))
        {
            Debug.LogError($"[RegisterRoomPool] CSV 파일을 찾을 수 없습니다: {CsvPath}");
            return;
        }

        var existing = settings.FindAssetEntry(guid);
        if (existing != null)
        {
            existing.address = AddrKey;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"[RegisterRoomPool] 기존 항목 키 갱신: {AddrKey}");
            return;
        }

        var group = settings.FindGroup(GroupName)
                    ?? settings.CreateGroup(GroupName, false, false, false, null);

        var entry = settings.CreateOrMoveEntry(guid, group, false, false);
        entry.address = AddrKey;

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        Debug.Log($"[RegisterRoomPool] 등록 완료: '{CsvPath}' → key='{AddrKey}' (group='{GroupName}')");
    }
}
#endif
