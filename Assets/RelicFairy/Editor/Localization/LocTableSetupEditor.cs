#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 11-02 Q11 L1 — 문자열 표(Data/Localization/STRINGS.csv)를 Addressable 「STRINGS」로 등록한다(<see cref="Loc.PreloadAsync"/>가 읽는다).
/// 표를 처음 만들거나 옮겼을 때 한 번. 이미 등록돼 있으면 주소만 맞춘다.
/// </summary>
public static class LocTableSetupEditor
{
    private const string TablePath = "Assets/RelicFairy/Data/Localization/STRINGS.csv";
    private const string Address   = "STRINGS";

    [MenuItem("RelicFairy/Localization/STRINGS 표 Addressable 등록")]
    private static void Register()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        string guid = AssetDatabase.AssetPathToGUID(TablePath);
        if (settings == null || string.IsNullOrEmpty(guid)) { Debug.LogWarning($"[Loc] 등록 못 함 — 설정 {settings != null} · 표 {TablePath} {(!string.IsNullOrEmpty(guid))}"); return; }
        var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup, false, false);
        entry.address = Address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Loc] STRINGS 등록 — {settings.DefaultGroup.Name} / {Address}");
    }
}
#endif
