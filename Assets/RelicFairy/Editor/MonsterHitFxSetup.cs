#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 몬스터 피격 · 사망 이펙트 주소 등록(10-08 사용자 「피격 이펙트를 공격 종류에 어느 정도 맞춘다 · 사망 이펙트를 더 간단하면서 손맛 있게」).
/// 후보는 _Imported에서 게임 카메라로 렌더 비교해 골랐다(RelicFairy/Debug/몬스터 공격 예고/7 · 8).
/// 메뉴: RelicFairy/Setup/Register Monster Hit · Death Fx (다시 눌러도 같은 결과). 이미 다른 주소가 붙은 에셋은 건드리지 않고 알린다.
/// </summary>
public static class MonsterHitFxSetup
{
    private const string GroupName = "Effects";
    private const string Hovl1     = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/AAA Projectiles Vol 1/Prefabs/";

    // 주소 · 프리팹 · 메모
    private static readonly (string address, string path, string note)[] Entries =
    {
        ("GawainSolarHit", Hovl1 + "Hit 16 fire.prefab", "가웨인 Q 착탄 피격 — 태양 불꽃(공용 노란 베기 HitEffect_02 대신)"),
        // 사망 후보 7종 비교(몬스터 공격 예고/8): 지금 DieEffect_01 = 크고 어지러운 초록 줄기 · 보라 노바 = 흐린 연기 · 원형 폭탄 · 연기 = 안 보임 ·
        // 검은 불 = 탁함 · 귀여운 별 = 분홍 별(톤 안 맞음) → 푸른 수정 = 흰 핵 섬광 + 날카로운 불티, 0.3초 안에 사라짐(「간단하면서 손맛」)
        ("MonsterDeathBurst", Hovl1 + "Hit 26 blue crystal.prefab", "일반 몬스터 처치 섬광 — 몸 가운데 · 히트스톱 동안 실시간 재생"),
    };

    /// <summary>
    /// 일반 몬스터 공격 범위 예고 켜기(10-11 사용자 「공격 전 범위 예고가 나오지 않는다」).
    /// 07-10에 전 패턴 disableWarning=1(빨간 격자가 시끄러워서)이었다 — 표시를 얇은 바깥 테두리로 바꾼 뒤 다시 켠다.
    /// 일반 몬스터 폴더(Characters/Monster/Monster, 보스 제외)의 PatternAttackOverrideSO만. 원거리 패턴은 코드가 알아서 건너뛴다.
    /// </summary>
    [MenuItem("RelicFairy/Setup/Monster Attack Warning — 얇은 외곽선 켜기 (10-11)")]
    public static void EnableAttackWarnings()
    {
        int assets = 0, patterns = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:PatternAttackOverrideSO", new[] { "Assets/RelicFairy/Characters/Monster/Monster" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("Boss")) continue;
            var so = AssetDatabase.LoadAssetAtPath<RelicFairy.Monster.PatternAttackOverrideSO>(path);
            if (so == null) continue;
            var o = new SerializedObject(so);
            var arr = o.FindProperty("patterns");
            for (int i = 0; i < arr.arraySize; i++)
            {
                var dw = arr.GetArrayElementAtIndex(i).FindPropertyRelative("disableWarning");
                if (dw != null && dw.boolValue) { dw.boolValue = false; patterns++; }
            }
            o.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(so);
            assets++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[몬스터 공격 예고] 범위 외곽선 켬 — 설정 {assets}개 · 패턴 {patterns}개");
    }

    /// <summary>
    /// 공격 이펙트 없는 일반 몬스터 11종(10-11 사용자 「몬스터 공격이 제대로 된 공격 이펙트를 쓰지 않는다」).
    /// 새 이펙트를 들이지 않고 이미 다른 몬스터가 쓰는 묶음에서 공격 종류대로 고른다(이펙트 톤 절제 — 같은 계열).
    ///  찌르기 · 물기 · 박치기 = Prick 2(벌 · 거미 · 망령 · 살라만더) · 할퀴기 = Sword Slash 12(해골 등).
    ///  사이클롭스는 원거리 사격(RangedShot)이라 넣지 않는다 — 투사체가 예고다(10-11 1회차에 바닥 가시를 잘못 넣어 되돌림).
    /// 크기는 몸집대로(작은 슬라임 · 버섯 · 달팽이 0.8). 이미 이펙트가 있는 패턴은 건드리지 않는다.
    /// </summary>
    private const string MonsterRoot = "Assets/RelicFairy/Characters/Monster/Monster/";
    private static readonly (string monster, string fxPath, float scale)[] AttackFx =
    {
        ("Cactus",        MonsterRoot + "BattleBee/Effect/Prick 2.prefab", 1f),
        ("StingRay",      MonsterRoot + "BattleBee/Effect/Prick 2.prefab", 1f),
        ("DemonKing",     MonsterRoot + "Skeleton/Effect/Sword Slash 12.prefab", 1.2f),
    };

    // 10-12 몸통 · 물기 시전 이펙트 빼기 — 83 몬스터 전수조사 §2-1 「빼기」(사용자 「버섯 폭발 이펙트 부적절」).
    // (몬스터 폴더, 패턴 이름 — null이면 그 몬스터의 모든 패턴)
    private static readonly (string monster, string pattern)[] StripCastFx =
    {
        ("Slime", null), ("MushroomAngry", null), ("MushroomSmile", null), ("Snail", null), ("ChestMonster", null),
        ("WormMonster", null), ("MonsterPlant", null),
        ("Spider", "Bite"), ("Spider", "BodyBash"), ("Specter", "Bite"), ("Specter", "Lunge"),
        ("Salamander", "Bite"), ("Salamander", "ChargeBite"), ("Orc", "Spin"),
    };

    [MenuItem("RelicFairy/Setup/Monster Attack Fx — 몸통 · 물기 시전 이펙트 빼기 (10-12)")]
    public static void StripBodyBiteCastFx()
    {
        int cleared = 0;
        foreach (var (monster, pattern) in StripCastFx)
            foreach (var guid in AssetDatabase.FindAssets("t:PatternAttackOverrideSO", new[] { MonsterRoot + monster }))
            {
                var so = AssetDatabase.LoadAssetAtPath<RelicFairy.Monster.PatternAttackOverrideSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (so == null) continue;
                var o = new SerializedObject(so);
                var arr = o.FindProperty("patterns");
                for (int i = 0; i < arr.arraySize; i++)
                {
                    var pat = arr.GetArrayElementAtIndex(i);
                    string name = pat.FindPropertyRelative("name")?.stringValue;
                    if (pattern != null && name != pattern) continue;
                    var cast = pat.FindPropertyRelative("castVfxPrefab");
                    if (cast == null || cast.objectReferenceValue == null) continue;
                    Debug.Log($"[몬스터 공격 이펙트] {monster} · {name} — {cast.objectReferenceValue.name} 뺌(몸통 · 물기)");
                    cast.objectReferenceValue = null;
                    cleared++;
                }
                o.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(so);
            }
        AssetDatabase.SaveAssets();
        Debug.Log($"[몬스터 공격 이펙트] 몸통 · 물기 시전 이펙트 뺌 {cleared}개");
    }

    [MenuItem("RelicFairy/Setup/Monster Attack Fx — 이펙트 없는 11종 (10-11)")]
    public static void AssignMissingAttackFx()
    {
        int set = 0;
        foreach (var (monster, fxPath, scale) in AttackFx)
        {
            var fx = AssetDatabase.LoadAssetAtPath<GameObject>(fxPath);
            if (fx == null) { Debug.LogWarning("[몬스터 공격 이펙트] 이펙트 없음: " + fxPath); continue; }
            foreach (var guid in AssetDatabase.FindAssets("t:PatternAttackOverrideSO", new[] { MonsterRoot + monster }))
            {
                var so = AssetDatabase.LoadAssetAtPath<RelicFairy.Monster.PatternAttackOverrideSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (so == null) continue;
                var o = new SerializedObject(so);
                var arr = o.FindProperty("patterns");
                for (int i = 0; i < arr.arraySize; i++)
                {
                    var pat = arr.GetArrayElementAtIndex(i);
                    var cast = pat.FindPropertyRelative("castVfxPrefab");
                    if (cast == null || cast.objectReferenceValue != null) continue;   // 이미 있는 것은 두기
                    cast.objectReferenceValue = fx;
                    pat.FindPropertyRelative("castVfxScale").floatValue = scale;
                    set++;
                    Debug.Log($"[몬스터 공격 이펙트] {monster} · {pat.FindPropertyRelative("name")?.stringValue} ← {System.IO.Path.GetFileNameWithoutExtension(fxPath)} ×{scale}");
                }
                o.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(so);
            }
        }
        // 1회차에 잘못 넣은 사이클롭스(원거리) 바닥 가시를 되돌린다
        var spikes = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterRoot + "Golem/Effect/Spikes attack.prefab");
        foreach (var guid in AssetDatabase.FindAssets("t:PatternAttackOverrideSO", new[] { MonsterRoot + "Cyclops" }))
        {
            var so = AssetDatabase.LoadAssetAtPath<RelicFairy.Monster.PatternAttackOverrideSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (so == null) continue;
            var o = new SerializedObject(so);
            var arr = o.FindProperty("patterns");
            for (int i = 0; i < arr.arraySize; i++)
            {
                var cast = arr.GetArrayElementAtIndex(i).FindPropertyRelative("castVfxPrefab");
                if (cast != null && spikes != null && cast.objectReferenceValue == spikes) { cast.objectReferenceValue = null; Debug.Log("[몬스터 공격 이펙트] 사이클롭스 원거리 패턴 — 바닥 가시 되돌림"); }
            }
            o.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(so);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[몬스터 공격 이펙트] 넣음 {set}개");
    }

    // 사망 설정이 없어 코드 기본값(옛 DieEffect_01 · 발밑 · 멈칫 중 정지 · 1.5초 + 0.6초)으로 죽던 7종 — 22종과 같은 규칙으로 채운다(10-14).
    private static readonly string[] DeathFxMissing = { "Cactus", "ChestMonster", "CrabMonster", "MushroomSmile", "NagaWizard", "Snail", "WormMonster" };

    [MenuItem("RelicFairy/Setup/Monster Death Fx — 설정 없는 7종 채우기 (10-14)")]
    public static void FillMissingDeathFx()
    {
        int set = 0;
        foreach (var name in DeathFxMissing)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{MonsterRoot}{name}/SO/{name}Config.asset");
            if (cfg == null) { Debug.LogWarning($"[처치 이펙트] 설정 없음 — {name}"); continue; }
            var so = new SerializedObject(cfg);
            float vh = so.FindProperty("visualHeightMeters").floatValue;
            if (vh <= 0f) { Debug.LogWarning($"[처치 이펙트] 기본 신장 0 — {name}(건너뜀)"); continue; }
            var d = so.FindProperty("death");
            d.FindPropertyRelative("deathVfxKey").stringValue     = "MonsterDeathBurst";
            d.FindPropertyRelative("deathVfxScale").floatValue    = Mathf.Round(1.5f * vh / 1.7f * 100f) / 100f;   // 22종 규칙: × 1.5 × 키 / 1.7
            d.FindPropertyRelative("deathVfxHeight").floatValue   = Mathf.Round(vh * 0.5f * 100f) / 100f;          // 몸 가운데
            d.FindPropertyRelative("deathVfxUnscaled").boolValue  = true;                                          // 막타 멈칫 동안에도 터지게
            d.FindPropertyRelative("despawnDelay").floatValue     = 0.5f;
            d.FindPropertyRelative("dissolveDuration").floatValue = 0.35f;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(cfg);
            set++;
            Debug.Log($"[처치 이펙트] {name} — 키 {vh} → 섬광 ×{1.5f * vh / 1.7f:0.00} · 높이 {vh * 0.5f:0.00}");
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[처치 이펙트] 채움 {set}/{DeathFxMissing.Length}");
    }

    [MenuItem("RelicFairy/Setup/Register Monster Hit · Death Fx")]
    public static void Register()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("[몬스터 피격 이펙트] AddressableAssetSettings 없음"); return; }
        var group = settings.FindGroup(GroupName) ?? settings.DefaultGroup;

        int ok = 0;
        foreach (var (address, path, note) in Entries)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) { Debug.LogWarning($"[몬스터 피격 이펙트] 에셋 없음 — {path}"); continue; }
            var entry = settings.FindAssetEntry(guid);
            if (entry != null && entry.address != address)
            {
                Debug.LogWarning($"[몬스터 피격 이펙트] 이미 다른 주소 「{entry.address}」 — {path} (건너뜀)");
                continue;
            }
            entry ??= settings.CreateOrMoveEntry(guid, group, false, false);
            entry.address = address;
            ok++;
            Debug.Log($"[몬스터 피격 이펙트] {address} → {path} · {note}");
        }
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[몬스터 피격 이펙트] 등록 {ok}/{Entries.Length} · 그룹 {group.Name}");
    }
}
#endif
