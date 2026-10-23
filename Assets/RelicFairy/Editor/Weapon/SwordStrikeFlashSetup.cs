using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 10-19 ⑫ 검 섬광(Travis Hit_02 — 장비 형 실측 73에서 고름)을 Effects 그룹에 주소 SwordStrikeFlash로 넣는다.
/// CombatDamage가 검류 피격(SwordHitImpact = 피) 위에 겹쳐 띄운다.
/// </summary>
public static class SwordStrikeFlashSetup
{
    private const string Address = "SwordStrikeFlash";
    private const string PrefabPath = "Assets/RelicFairy/_Imported/EffectSource/Travis Game Assets/Hit Impact Effects/Prefabs/Hits/Hit_02.prefab";

    [MenuItem("RelicFairy/Weapon/검 섬광 주소 등록 — SwordStrikeFlash (10-19 ⑫)")]
    private static void Register()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var group = settings != null ? settings.FindGroup("Effects") : null;
        string guid = AssetDatabase.AssetPathToGUID(PrefabPath);
        if (group == null || string.IsNullOrEmpty(guid)) { Debug.LogWarning($"[SwordStrikeFlash] 그룹 {group != null} · 프리팹 {!string.IsNullOrEmpty(guid)}"); return; }
        foreach (var g in settings.groups)
            if (g != null)
                foreach (var x in g.entries)
                    if (x.address == Address && x.guid != guid) { Debug.LogWarning($"[SwordStrikeFlash] 주소 충돌: {x.AssetPath}"); return; }
        var e = settings.CreateOrMoveEntry(guid, group, false, false);
        e.address = Address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, e, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SwordStrikeFlash] 등록 — {e.address} · {e.parentGroup.Name} · {e.AssetPath}");
    }
}
