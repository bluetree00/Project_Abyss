#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 장비 개선 — 형 SO 2개(칼바람 · 무게)를 만들고 카타나 · 대검 T1~T3에 잇는다. 다시 돌려도 안전(있으면 값만 고친다).
/// .meta · GUID는 Unity가 만들어야 해서 에디터 메뉴로 만든다(NamelessSkillSetup과 같은 이유).
/// 기획: 바탕화면 기획/RelicFairy_장비개선_진화형능력·형단련_20261002.md §4
/// </summary>
public static class WeaponFormSetup
{
    private const string Dir        = "Assets/RelicFairy/Weapon/Forms";
    private const string WindPath   = Dir + "/Form_Windblade.asset";
    private const string WeightPath = Dir + "/Form_Weight.asset";

    [MenuItem("RelicFairy/Weapon/Setup Weapon Forms (형 2종)")]
    public static void Setup()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/RelicFairy/Weapon", "Forms");
        var wind   = LoadOrCreate(WindPath);
        var weight = LoadOrCreate(WeightPath);

        Fill(wind, "windblade", "칼바람", "풍절", "끊기지 않고 몰아치면, 마지막 타가 멀리 닿는다",
             new Color(0.62f, 0.80f, 0.95f), maxStacks: 5, atk: 1.5f, range: 8f, idle: 3f, new[]
             {
                 V("twin_wind",   2, "쌍풍",        "넓게",          "풍절이 두 갈래(±12°)로 나간다"),
                 V("return_wind", 2, "되감는 바람", "한 줄로 두 번", "풍절이 끝에서 되돌아오며 한 번 더 벤다"),
                 V("still_wind",  3, "무풍",        "회피가 공격",   "칼바람 5인 채 회피하면 지나간 길의 적을 벤다"),
                 V("storm_eye",   3, "폭풍의 눈",   "무리 속 연쇄",  "풍절에 맞은 적 하나당 칼바람 +1을 돌려받는다(최대 3)"),
             });
        Fill(weight, "weight", "무게", "내려찍기", "느리지만, 넷째 휘두름마다 확실한 한 방이 온다",
             new Color(0.95f, 0.62f, 0.32f), maxStacks: 4, atk: 0.6f, range: 3.5f, idle: 5f, new[]   // 10-07 예산 실측(단일 표적 · 유물 없음): 0.8 → Ⅰ +18% · 0.5 → +8%(확정 치명 +6% 포함) → 0.6. 범위 가치는 예산 밖
             {
                 V("fissure",    2, "균열",      "자리 장악",   "충격파 자리에 2초간 갈라진 땅이 남는다(0.5초마다 공격력 30%)"),
                 V("aftershock", 2, "여진",      "넓게",        "0.4초 뒤 더 큰 충격파(반경 ×1.4 · 피해 절반)가 한 번 더 퍼진다"),
                 V("heavy_hand", 3, "무거운 손", "스킬과 엮임", "무게 4일 때 E · R 적중이 치명타가 된다"),
                 V("crush",      3, "짓누름",    "몰아 잡기",   "경직된 적은 2초 동안 받는 피해 +12%"),
             });

        foreach (var t in new[] { "T1", "T2", "T3" })
        {
            Link($"Assets/RelicFairy/Weapon/Katana/Data/{t}_Katana.asset", wind);
            Link($"Assets/RelicFairy/Weapon/Greatsword/Data/{t}_Greatsword.asset", weight);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[WeaponFormSetup] 형 2종 생성 · 카타나 · 대검 T1~T3 연결 끝");
    }

    /// <summary>
    /// 발동 이펙트 · 가득 트레일 배정(10-07 실측 6 비교 3회): 칼바람 = Hovl Slash wave ×1.6(정면 초승달 검기 · 강철빛) ·
    /// 무게 = Hovl Hit 18 nova orange ×2(잉걸빛 충격파 — 퍼지는 돔 지름이 판정 반경 3.5 m와 같다 · ×3은 화면 반을 덮음 · 같은 모양 보라는 리치가 쓴다). 트레일 = Wind 1 / Fire 2.
    /// 이미 Addressables에 있는 에셋은 그 주소를 키로 쓴다 — 주소를 옮기면 기존 키가 깨진다(Hit 18 = 「NovaOrangeHit」).
    /// </summary>
    [MenuItem("RelicFairy/Weapon/Assign Weapon Form FX (형 연출)")]
    public static void AssignFx()
    {
        const string windVfxPath   = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio 2/Sword slash VFX/Prefabs/Slash wave.prefab";
        const string slamVfxPath   = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/AAA Projectiles Vol 1/Prefabs/Hit 18 nova orange.prefab";
        const string windTrailPath = "Assets/RelicFairy/_Imported/EffectSource/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Trail Prefabs/Wind 1.prefab";
        const string slamTrailPath = "Assets/RelicFairy/_Imported/EffectSource/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Trail Prefabs/Fire 2.prefab";
        Register(windVfxPath, "vfx_form_windcut");
        Register(slamVfxPath, "NovaOrangeHit");
        SetFx(WindPath, "vfx_form_windcut", 1.6f, windTrailPath);
        SetFx(WeightPath, "NovaOrangeHit", 2f, slamTrailPath);
        AssetDatabase.SaveAssets();
        Debug.Log("[WeaponFormSetup] 형 연출 배정 끝");
    }

    private static void Register(string path, string address)
    {
        var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
        string guid = AssetDatabase.AssetPathToGUID(path);
        var entry = settings.FindAssetEntry(guid)
                 ?? settings.CreateOrMoveEntry(guid, settings.FindGroup("Effects") ?? settings.DefaultGroup, false, false);
        if (entry.address == address) return;
        if (!string.IsNullOrEmpty(entry.address) && entry.address != path)
            Debug.LogWarning($"[WeaponFormSetup] 기존 주소 「{entry.address}」 → 「{address}」 — 옛 키를 쓰는 곳이 있으면 깨진다");
        entry.address = address;
        settings.SetDirty(UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
    }

    private static void SetFx(string formPath, string vfxKey, float vfxScale, string trailPath)
    {
        var so = AssetDatabase.LoadAssetAtPath<WeaponFormSO>(formPath);
        var o = new SerializedObject(so);
        o.FindProperty("triggerVfxKey").stringValue = vfxKey;
        o.FindProperty("triggerVfxScale").floatValue = vfxScale;
        o.FindProperty("fullTrailPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(trailPath);
        o.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
    }

    private static WeaponFormSO LoadOrCreate(string path)
    {
        var so = AssetDatabase.LoadAssetAtPath<WeaponFormSO>(path);
        if (so != null) return so;
        so = ScriptableObject.CreateInstance<WeaponFormSO>();
        AssetDatabase.CreateAsset(so, path);
        return so;
    }

    private static WeaponFormSO.VariantDef V(string id, int tier, string name, string tag, string line)
        => new() { id = id, tier = tier, displayName = name, traitTag = tag, line = line };

    private static void Fill(WeaponFormSO so, string id, string name, string trigger, string line, Color tint,
                             int maxStacks, float atk, float range, float idle, WeaponFormSO.VariantDef[] variants)
    {
        var o = new SerializedObject(so);
        o.FindProperty("formId").stringValue = id;
        o.FindProperty("displayName").stringValue = name;
        o.FindProperty("triggerName").stringValue = trigger;
        o.FindProperty("line").stringValue = line;
        o.FindProperty("tint").colorValue = tint;
        o.FindProperty("maxStacks").intValue = maxStacks;
        o.FindProperty("triggerAtkRatio").floatValue = atk;
        o.FindProperty("triggerRange").floatValue = range;
        o.FindProperty("idleSeconds").floatValue = idle;
        var costs = o.FindProperty("temperCosts");
        costs.arraySize = 2;
        costs.GetArrayElementAtIndex(0).intValue = 12;
        costs.GetArrayElementAtIndex(1).intValue = 20;
        var arr = o.FindProperty("variants");
        arr.arraySize = variants.Length;
        for (int i = 0; i < variants.Length; i++)
        {
            var e = arr.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("id").stringValue = variants[i].id;
            e.FindPropertyRelative("tier").intValue = variants[i].tier;
            e.FindPropertyRelative("displayName").stringValue = variants[i].displayName;
            e.FindPropertyRelative("traitTag").stringValue = variants[i].traitTag;
            e.FindPropertyRelative("line").stringValue = variants[i].line;
        }
        o.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
    }

    private static void Link(string weaponPath, WeaponFormSO form)
    {
        var w = AssetDatabase.LoadAssetAtPath<WeaponSO>(weaponPath);
        if (w == null) { Debug.LogWarning($"[WeaponFormSetup] 무기 없음 {weaponPath}"); return; }
        var o = new SerializedObject(w);
        o.FindProperty("form").objectReferenceValue = form;
        o.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(w);
    }
}
#endif
