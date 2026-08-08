#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// CHAPTER_N_ZONE_LAYOUT.csv를 Addressables에 "CHAPTER_N_ZONE_LAYOUT" 키로 등록(챕터 1~4).
/// 메뉴: RelicFairy/Addressables/Register Zone Layout
///
/// 키 규약: 런타임(ZoneLayoutManager.LoadAsync)은 layoutKey.ToUpper()로 주소를 찾고,
/// layoutKey는 "chapter_{N}_zone_layout" 이다 → 주소는 반드시 <b>언더스코어 포함</b> CHAPTER_1_ZONE_LAYOUT.
/// (예전 이 툴은 "CHAPTER1_ZONE_LAYOUT" / 같은 이름의 CSV 경로를 상수로 갖고 있어
///  실제 파일도 못 찾고 런타임 주소와도 어긋나 아무것도 등록되지 않았다.)
/// 그룹은 이미 등록돼 있던 Ch1과 같은 StageData를 쓴다.
/// </summary>
public static class RegisterZoneLayoutAddressable
{
    private const string CsvPathFormat = "Assets/RelicFairy/Docs/CHAPTER_{0}_ZONE_LAYOUT.csv";
    private const string AddrKeyFormat = "CHAPTER_{0}_ZONE_LAYOUT";
    private const string GroupName     = "StageData";
    private const int    MaxChapter    = 4;

    [MenuItem("RelicFairy/Addressables/Register Zone Layout")]
    public static void Register()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[RegisterZoneLayout] Addressable 설정을 찾을 수 없습니다.");
            return;
        }

        var group = settings.FindGroup(GroupName)
                    ?? settings.CreateGroup(GroupName, false, false, false, null);

        int registered = 0;
        for (int ch = 1; ch <= MaxChapter; ch++)
        {
            string csvPath = string.Format(CsvPathFormat, ch);
            string addrKey = string.Format(AddrKeyFormat, ch);

            var guid = AssetDatabase.AssetPathToGUID(csvPath);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogWarning($"[RegisterZoneLayout] CSV 없음 — 건너뜀: {csvPath}");
                continue;
            }

            var existing = settings.FindAssetEntry(guid);
            if (existing != null)
            {
                if (existing.address != addrKey)
                {
                    existing.address = addrKey;
                    Debug.Log($"[RegisterZoneLayout] 기존 항목 키 갱신: {addrKey}");
                }
                registered++;
                continue;
            }

            var entry = settings.CreateOrMoveEntry(guid, group, false, false);
            entry.address = addrKey;
            registered++;
            Debug.Log($"[RegisterZoneLayout] 등록: '{csvPath}' → key='{addrKey}' (group='{GroupName}')");
        }

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log($"[RegisterZoneLayout] 완료 — {registered}/{MaxChapter}개 챕터 존 레이아웃 등록됨.");
    }
}
#endif
