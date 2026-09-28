using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>
/// [데이터 적용 · 편집 모드] 의미가 다른 순간에 같은 이펙트를 쓰던 곳을 나눈다(09-25).
///
/// 리치 연출 때 사용자 지적: 「등장과 페이지 전환이 같은 리소스라 안 어울린다」 → 의미별로 이펙트를 나눈다.
/// 플레이어 쪽 같은 문제 두 건:
///  · 마무리 이펙트 <c>IasenFinishFlash</c>(Hovl Flash 5 red)를 성광베기·환영베기·무형일섬이 같이 썼다.
///    게다가 빨강은 색 규약상 「피격·즉시 회피」 색이다.
///    → 성광베기 = 금빛(Flash 18 nova orange), 환영베기 = 푸른빛(Flash 14 blue rapid). 무형일섬은 그대로(결정 대기).
///  · 원거리 파츠 폭발이 활 E 3단계 착탄(<c>BowETier3Hit</c>)을 빌려 썼다 → 화살비 폭발(Effect_17_Explosion).
///
/// 세 프리팹을 Addressables(Effects 그룹)에 새 주소로 등록하고, 스킬 데이터의 키를 바꾼다. 몇 번 돌려도 같은 결과.
/// 후보는 _Imported에서 렌더 비교로 골랐다(아무도 안 쓰는 것만 · GrabPass·자폭 스크립트 없음).
/// </summary>
public static class SkillVfxSeparationEditor
{
    private const string Group = "Effects";
    private const string Hovl  = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/AAA Projectiles Vol 1/Prefabs/";

    private static readonly (string address, string prefab)[] Entries =
    {
        ("HolySlashFinish",      Hovl + "Flash 18 nova orange.prefab"),
        ("PhantomDanceFinish",   Hovl + "Flash 14 blue rapid.prefab"),
        ("RangedPartsExplosion", "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_2(ScriptBased)/Effects/Effect_17_RainofArrow/Effect_17_Parts/Effect_17_Explosion.prefab"),
    };

    private static readonly (string asset, string field, string key)[] SkillKeys =
    {
        ("Assets/RelicFairy/Weapon/Katana/Data/KatanaBase/HolySlash.asset",    "slashEffectKey",  "HolySlashFinish"),
        ("Assets/RelicFairy/Weapon/Katana/Data/KatanaBase/PhantomDance.asset", "finishEffectKey", "PhantomDanceFinish"),
    };

    [MenuItem("RelicFairy/Debug/스킬 이펙트 의미별 분리 적용 (편집 모드)")]
    private static void Apply()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var group = settings != null ? settings.FindGroup(Group) : null;
        if (group == null) { Debug.LogWarning($"[이펙트분리] Addressables 그룹 '{Group}'이 없다 — 중단"); return; }

        foreach (var (address, prefab) in Entries)
        {
            string guid = AssetDatabase.AssetPathToGUID(prefab);
            if (string.IsNullOrEmpty(guid)) { Debug.LogWarning($"[이펙트분리] 프리팹 없음: {prefab}"); return; }
            var entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: false);
            entry.address = address;
            Debug.Log($"[이펙트분리] 등록 {address} ← {System.IO.Path.GetFileName(prefab)}");
        }
        settings.SetDirty(UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent.EntryMoved, null, true, true);

        foreach (var (asset, field, key) in SkillKeys)
        {
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(asset);
            if (so == null) { Debug.LogWarning($"[이펙트분리] 에셋 없음: {asset}"); continue; }
            var ser  = new SerializedObject(so);
            var prop = ser.FindProperty(field);
            if (prop == null) { Debug.LogWarning($"[이펙트분리] 필드 없음: {asset}.{field}"); continue; }
            string before = prop.stringValue;
            prop.stringValue = key;
            ser.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(so);
            Debug.Log($"[이펙트분리] {System.IO.Path.GetFileNameWithoutExtension(asset)}.{field}: {before} → {key}");
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[이펙트분리] 완료");
    }
}
