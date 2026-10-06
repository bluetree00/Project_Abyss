using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 서약 효과 VFX 세트(<see cref="CovenantVfxSet"/>)를 만들거나 갱신하고 Addressable("CovenantVfxSet", Effects 그룹)로 등록한다.
/// 프리팹은 전부 _Imported/EffectSource에서 골랐다 — 후보를 렌더링해 비교한 결과(구현설계 문서 §7).
/// 다시 실행하면 항목을 아래 표로 덮어쓴다(인스펙터에서 바꾼 값도 되돌아간다).
/// </summary>
public static class CovenantVfxSetCreatorEditor
{
    private const string AssetPath = "Assets/RelicFairy/Systems/Covenant/Assembly/CovenantVfxSet.asset";
    private const string GroupName = "Effects";
    private const string Spells    = "Assets/RelicFairy/_Imported/EffectSource/Spells Pack/LWRP(URP)/Particles_LWRP/Prefabs/Projectiles/Explosion/";
    private const string HovlRpg   = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/RPG VFX Bundle/";
    private const string HovlMagic = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/Magic circles/Prefabs/";
    private const string HovlAoe   = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/AOE Magic spells Vol.1/Prefabs/";
    private const string Ssep1     = "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/";

    // key, 프리팹, 배율, 반경 맞춤 기준(0=맞추지 않음), 수명/기본 지속
    private static readonly (string key, string path, float scale, float nativeRadius, float life)[] Table =
    {
        // 공격 — 폭발은 1초에 퍼지는 반경(초신성 약 3.4m · 기폭 약 3.5m)을 판정 반경에 맞춘다(초신성은 절인 적 수에 따라 3.5~5.5m).
        // Spells Pack은 파티클 배율 모드가 Hierarchy가 아니지만, 배율 3에서 반경이 약 2.3~3배로 커지는 것을 쟀다(09-17 미리보기).
        ("supernova",    Spells + "Explosion_Light_2_LWRP.prefab",                1.0f, 3.4f, 2.0f),
        ("detonate",     Spells + "Explosion _Fire_2_LWRP.prefab",                1.0f, 3.5f, 2.0f),
        ("detonate_hit", HovlAoe + "Magic hit.prefab",                           0.5f, 0f,   1.0f),
        ("execute",      Spells + "Explosion_Arcane_LWRP.prefab",                 1.0f, 0f,   1.5f),
        // 제어 — 번개 폭발은 배율이 전부 따라간다(Hierarchy). 원래 반경 약 4.4m를 판정 반경에 맞춘다.
        ("stasis",       HovlRpg + "Random effect prefabs/Chain explosion.prefab", 1.0f, 4.4f, 1.2f),
        // 경제
        ("harvest",      HovlRpg + "Random effect prefabs/Gold dot.prefab",       0.5f, 0f,   1.2f),
        // 생존 — 플레이어 몸에 붙는다(반경 약 1.5m로 줄여 캐릭터를 감싸게).
        ("bloodmark",    HovlMagic + "Magic circle blood.prefab",                 0.6f, 0f,   1.5f),
        ("aegis",        HovlMagic + "Magic shield holy.prefab",                  0.6f, 0f,   0.5f),
        ("ward",         HovlRpg + "Random effect prefabs/Mountains shield.prefab", 0.6f, 0f, 4.0f),
        ("lastbreath",   HovlRpg + "Prefabs/Magic buffs and hits/Lvl up.prefab",  0.5f, 0f,   1.5f),
        // 10-06 연출이 없던 4종 — 다른 시스템이 안 쓰는 것만(룬 속성 불 · 폭풍 · Debuff 1 · 리치 Aura_Dark · 칼 타격 VolumetricBlood 제외)
        ("fury",       HovlRpg + "Prefabs/Magic buffs and hits/Dragon punch.prefab", 0.6f,   0f, 1.0f),   // 나 — 몸에서 주황 불꽃이 터진다(날이 선다)
        ("momentum",   HovlRpg + "Prefabs/Magic buffs and hits/Buff 2.prefab",      0.6f,   0f, 1.2f),   // 나 — 발밑에서 먼지 고리가 차고 나간다(박차) — Fast wind는 게임 화면에서 거의 안 보였다(10-06 근접 실측)
        ("curse",      HovlRpg + "Prefabs/Magic buffs and hits/Debuff 2.prefab",     0.6f,   0f, 1.5f),   // 적 — 몸을 감는 문양 고리(받는 피해 증가 동안)
        ("hemorrhage", Ssep1 + "Effect_06_BloodFlood/Effect_06_BloodFlood.prefab",   0.028f, 0f, 1.2f),   // 적 — 발치에 번지는 피(원래 반경 ~3.4 m → 몸 둘레만, 10-06 근접 실측)
        // 루비 등급 발동
        (CovenantFxService.RubyKey, HovlRpg + "Prefabs/Magic buffs and hits/Buff 7.prefab", 1.5f, 0f, 1.0f),
    };

    [MenuItem("RelicFairy/Covenant/서약 VFX 세트 만들기·갱신")]
    private static void CreateOrUpdate()
    {
        var set = AssetDatabase.LoadAssetAtPath<CovenantVfxSet>(AssetPath);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<CovenantVfxSet>();
            AssetDatabase.CreateAsset(set, AssetPath);
        }

        var so      = new SerializedObject(set);
        var entries = so.FindProperty("entries");
        entries.arraySize = Table.Length;
        int missing = 0;
        for (int i = 0; i < Table.Length; i++)
        {
            var row    = Table[i];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(row.path);
            if (prefab == null) { missing++; Debug.LogError($"[CovenantVfxSet] 프리팹 없음: {row.path}"); }

            var e = entries.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("key").stringValue            = row.key;
            e.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            e.FindPropertyRelative("scale").floatValue           = row.scale;
            e.FindPropertyRelative("nativeRadius").floatValue    = row.nativeRadius;
            e.FindPropertyRelative("life").floatValue            = row.life;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();

        RegisterAddressable();
        Debug.Log($"[CovenantVfxSet] 갱신 완료 — 항목 {Table.Length} · 누락 {missing}");
    }

    private static void RegisterAddressable()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogWarning("[CovenantVfxSet] Addressable Settings 없음 — 수동 등록 필요"); return; }

        var group = settings.FindGroup(GroupName) ?? settings.DefaultGroup;
        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(AssetPath), group, false, false);
        entry.address = CovenantFxService.SetKey;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[CovenantVfxSet] Addressable 등록: {entry.address} ({group.Name})");
    }
}
