#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 몬스터 발밑 그림자 재질을 어드레서블에 등록한다(10-30 빌드 Player.log 「셰이더 'RelicFairy/MonsterGroundShadow'를 찾을 수 없습니다」).
/// <see cref="MonsterGroundShadow"/>는 주소 <c>Mat_MonsterGroundShadow</c>로 재질을 먼저 읽고, 없으면 Shader.Find로 넘어간다 —
/// 재질이 어드레서블에 없어 빌드에선 늘 Shader.Find였고, 셰이더를 참조하는 것이 없어 빌드에서 빠졌다(그림자 없음).
/// 재질을 번들에 넣으면 셰이더도 의존으로 따라 들어간다.
/// 메뉴: RelicFairy/Monster/10-30 몬스터 발밑 그림자 재질 어드레서블 등록 (Edit)
/// </summary>
public static class MonsterGroundShadowAddressableRegistrar
{
    private const string MaterialPath = "Assets/RelicFairy/Art/Materials/Mat_MonsterGroundShadow.mat";
    private const string Address      = "Mat_MonsterGroundShadow";   // MonsterGroundShadow.MaterialKey

    [MenuItem("RelicFairy/Monster/10-30 몬스터 발밑 그림자 재질 어드레서블 등록 (Edit)")]
    public static void Register()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("[그림자 재질 등록] AddressableAssetSettings 없음"); return; }

        string guid = AssetDatabase.AssetPathToGUID(MaterialPath);
        if (string.IsNullOrEmpty(guid)) { Debug.LogError($"[그림자 재질 등록] 에셋 없음: {MaterialPath}"); return; }

        var group = settings.FindGroup("Monsters") ?? settings.DefaultGroup;
        var entry = settings.CreateOrMoveEntry(guid, group, false, false);
        entry.address = Address;

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[그림자 재질 등록] 완료 — '{Address}' → 그룹 '{group.Name}' · 셰이더 {AssetDatabase.LoadAssetAtPath<Material>(MaterialPath)?.shader?.name}");
    }
}
#endif
