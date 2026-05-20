#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// CHAPTER1_ZONE_LAYOUT.csv를 Addressables에 "CHAPTER1_ZONE_LAYOUT" 키로 등록.
/// 메뉴: Abyss/Setup/Register Zone Layout Addressable
/// </summary>
public static class RegisterZoneLayoutAddressable
{
    private const string CsvPath  = "Assets/Abyss/Docs/CHAPTER1_ZONE_LAYOUT.csv";
    private const string AddrKey  = "CHAPTER1_ZONE_LAYOUT";
    private const string GroupName = "ChartData";

    [MenuItem("Abyss/Setup/Register Zone Layout Addressable")]
    public static void Register()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[RegisterZoneLayout] Addressable 설정을 찾을 수 없습니다.");
            return;
        }

        var guid = AssetDatabase.AssetPathToGUID(CsvPath);
        if (string.IsNullOrEmpty(guid))
        {
            Debug.LogError($"[RegisterZoneLayout] CSV 파일을 찾을 수 없습니다: {CsvPath}");
            return;
        }

        // 이미 등록된 경우 스킵
        var existing = settings.FindAssetEntry(guid);
        if (existing != null)
        {
            existing.address = AddrKey;
            Debug.Log($"[RegisterZoneLayout] 기존 항목 키 갱신: {AddrKey}");
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return;
        }

        // 그룹 확보 (없으면 생성)
        var group = settings.FindGroup(GroupName)
                    ?? settings.CreateGroup(GroupName, false, false, false, null);

        var entry = settings.CreateOrMoveEntry(guid, group, false, false);
        entry.address = AddrKey;

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        Debug.Log($"[RegisterZoneLayout] 등록 완료: '{CsvPath}' → key='{AddrKey}' (group='{GroupName}')");
    }
}
#endif
