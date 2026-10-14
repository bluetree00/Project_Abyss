#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 유물 데이터(RelicClassSO)를 Addressables 「Characters」 그룹에 SO 이름 주소로 등록한다(10-08) — 다시 돌려도 같은 결과.
/// 런 저장은 relicKey = 유물 SO 이름(RunProgressManager)이고 이어하기는 그 이름을 주소로 불러온다(AppBootstrapper.RestoreRun).
/// 그런데 두 유물 에셋에 주소가 없어 앱을 다시 켠 뒤 이어하기에서 유물이 비고, @GameRun의 debugDefaultRelic(가웨인)로 바뀌었다
/// — 랜슬롯 런이 가웨인 능력치 · 패시브 · Q · 의상으로 이어지고 랜슬롯 기억 조각이 가웨인에 붙었다.
/// </summary>
public static class RelicClassAddressSetup
{
    private const string RelicDir = "Assets/RelicFairy/Systems/Relic/Data";

    [MenuItem("RelicFairy/Setup/Register Relic Class Addresses")]
    public static void Apply()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogWarning("[RelicClassAddress] Addressables 설정 없음"); return; }
        var group = settings.FindGroup("Characters") ?? settings.DefaultGroup;
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:RelicClassSO", new[] { RelicDir }))
        {
            var so = AssetDatabase.LoadAssetAtPath<RelicClassSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (so == null) continue;
            var entry = settings.FindAssetEntry(guid) ?? settings.CreateOrMoveEntry(guid, group, false, false);
            if (entry.address != so.name)
            {
                entry.address = so.name;   // 저장되는 relicKey와 같은 이름
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
            }
            n++;
            Debug.Log($"[RelicClassAddress] {so.name} → 그룹 {entry.parentGroup.Name} · 주소 {entry.address}");
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[RelicClassAddress] 끝 — 유물 {n}개");
    }
}
#endif
