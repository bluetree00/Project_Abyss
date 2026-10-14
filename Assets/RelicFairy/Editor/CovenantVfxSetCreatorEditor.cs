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
    private const string Ssep1     = "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/";

    // key, 프리팹, 배율, 반경 맞춤 기준(0=맞추지 않음), 수명/기본 지속, 색 곱(알파 0 = 원래 색), 끌 자식(쉼표)
    private static readonly Color Orig = new(0f, 0f, 0f, 0f);
    private static readonly (string key, string path, float scale, float nativeRadius, float life, Color tint, string mute)[] Table =
    {
        // 공격 — 폭발은 1초에 퍼지는 반경(초신성 약 3.4m · 기폭 약 3.5m)을 판정 반경에 맞춘다(초신성은 절인 적 수에 따라 3.5~5.5m).
        // Spells Pack은 파티클 배율 모드가 Hierarchy가 아니지만, 배율 3에서 반경이 약 2.3~3배로 커지는 것을 쟀다(09-17 미리보기).
        // 10-11 톤 2차: 초신성(크림 = 서약 금 계열) · 기폭(태우고 피 흘리는 상태를 터뜨린다 = 불빛)은 빛깔이 뜻과 맞아 그대로 —
        // 대신 2초에 툭 잘리던 것은 재생기가 사그라들게 거둔다(ElementVfxPlayer.FadeTail).
        ("supernova",    Spells + "Explosion_Light_2_LWRP.prefab",                1.0f, 3.4f, 2.0f, Orig, null),
        ("detonate",     Spells + "Explosion _Fire_2_LWRP.prefab",                1.0f, 3.5f, 2.0f, Orig, null),
        // 대상 하나 기폭 — 옛 Hovl AOE Magic hit(빨강 · 누락 스크립트 경고)를 같은 기폭 폭발의 작은 판으로
        ("detonate_hit", Spells + "Explosion _Fire_2_LWRP.prefab",                0.35f, 0f,  1.0f, Orig, null),
        // 처형 — 단일 대상인데 Expansive Wave(5~10 m)가 광역으로 읽혔다 → 반(재생기가 입자 크기 모드 Hierarchy로 맞춘다)
        ("execute",      Spells + "Explosion_Arcane_LWRP.prefab",                 0.5f, 0f,   1.5f, Orig, null),
        // 제어 — 번개 폭발은 배율이 전부 따라간다(Hierarchy). 원래 반경 약 4.4m를 판정 반경에 맞춘다. 0.1초 9 m 섬광(Flash)은 끈다
        ("stasis",       HovlRpg + "Random effect prefabs/Chain explosion.prefab", 1.0f, 4.4f, 1.2f, Orig, "Flash"),
        // 경제
        // 10-11 톤 점검: 반복 길이 1초 프리팹이라 1.2초면 1.0초에 한 번 더 터지고 잘렸다 → 한 바퀴 안(0.95)
        ("harvest",      HovlRpg + "Random effect prefabs/Gold dot.prefab",       0.5f, 0f,   0.95f, Orig, null),
        // 생존 — 플레이어 몸에 붙는다(반경 약 1.5m로 줄여 캐릭터를 감싸게).
        // 피의 보호막 — 쿨 3초마다 검붉은 바닥 원 3.6 m → 작고 옅게(× 0.35 · 알파 0.6). 자원은 그대로(피 문양 = 뜻)
        ("bloodmark",    HovlMagic + "Magic circle blood.prefab",                 0.35f, 0f,  1.5f, new Color(1f, 1f, 1f, 0.6f), null),
        ("aegis",        HovlMagic + "Magic shield holy.prefab",                  0.6f, 0f,   0.5f, Orig, null),
        // 10-11 톤 점검: 결계 4초 내내 붙이면 반복 길이 1초라 네 번 번쩍였다 → 켜질 때 한 바퀴(0.95) · 남은 시간은 버프 칸
        ("ward",         HovlRpg + "Random effect prefabs/Mountains shield.prefab", 0.6f, 0f, 0.95f, Orig, null),
        // 마지막 숨결 — 옛 Lvl up은 레벨업으로 읽혔고 「모음」 · 「막음」 두 뜻에 같은 그림이었다.
        // 모음 = 흰 고리(Buff 6, 흰빛 원본) 옅은 금 · 작게 / 막음 = 성역 방패(1초 무적과 같은 뜻)
        // 10-11 2차 촬영: Buff 6 재질은 발광 3~8이라 옅은 금(1, 0.85, 0.55)이 흰빛으로 바랬다 → 진한 금
        ("lastbreath",      HovlRpg + "Prefabs/Magic buffs and hits/Buff 6.prefab", 0.4f, 0f, 1.0f, new Color(0.55f, 0.40f, 0.12f, 0.8f), null),
        ("lastbreath_save", HovlMagic + "Magic shield holy.prefab",                 0.6f, 0f, 1.0f, Orig, null),
        // 10-06 연출이 없던 4종 — 다른 시스템이 안 쓰는 것만(룬 속성 불 · 폭풍 · Debuff 1 · 리치 Aura_Dark · 칼 타격 VolumetricBlood 제외)
        // 격노 — 옛 Dragon punch는 대검 페이로드를 겸한 무기 이펙트(입자계 13 · Aura2 11 × 0.6 = 6.6 m)라 「플레이어 연출 살짝」 상한 위였다(10-11 톤 2차)
        ("fury",       HovlRpg + "Random effect prefabs/Buff orange circle.prefab", 0.5f,   0f, 1.0f, Orig, null),   // 나 — 발밑 주황 고리 한 번(날이 선다 · 입자계 4)
        ("momentum",   HovlRpg + "Prefabs/Magic buffs and hits/Buff 2.prefab",      0.6f,   0f, 1.2f, Orig, null),   // 나 — 발밑에서 먼지 고리가 차고 나간다(박차) — Fast wind는 게임 화면에서 거의 안 보였다(10-06 근접 실측)
        // 저주 — 옛 Debuff 2(입자계 21 · 4초 사슬)는 무거웠다 → 흰 고리(Buff 6) 보랏빛 · 새로 걸릴 때 한 번
        // 10-11 2차 촬영: 옅은 보라(0.7, 0.45, 1)가 흰 회색 작은 소용돌이로 보였다 → 진한 보라 · 몸 둘레가 읽히게 0.5 → 0.7
        ("curse",      HovlRpg + "Prefabs/Magic buffs and hits/Buff 6.prefab",       0.7f,   0f, 1.5f, new Color(0.42f, 0.16f, 0.78f, 1f), null),   // 적 — 몸에 보랏빛 고리(받는 피해 증가)
        ("hemorrhage", Ssep1 + "Effect_06_BloodFlood/Effect_06_BloodFlood.prefab",   0.028f, 0f, 1.2f, Orig, null),   // 적 — 발치에 번지는 피(원래 반경 ~3.4 m → 몸 둘레만, 10-06 근접 실측)
        // 루비 등급 발동 VFX(Buff 7)는 10-11 톤 2차에서 뺐다 — 정지 · 흔들림 · 펄스 · 소리가 이미 말한다
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
            e.FindPropertyRelative("tint").colorValue            = row.tint;
            e.FindPropertyRelative("mute").stringValue           = row.mute ?? string.Empty;
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
