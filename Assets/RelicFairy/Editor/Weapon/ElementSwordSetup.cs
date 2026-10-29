using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 10-26 대검 R 「속성 검」 리소스 등록(설계 기획/RelicFairy_대검R_속성검버프_설계_20261026.md §7 · 실측 97 ⑩-a에서 고름).
///   검 뽑기(GhostSamurai Equip — 아무 무기도 안 쓰는 클립) → _ThirdParty(저장소 밖)에서 _Imported로 옮기고 WeaponAnimation 「GreatswordDraw」.
///   대검 애니 칸: QSkill_01(R 첫 상태) = 검 뽑기 · QSkill_02(원본 클립 QSkill_Iasen) = 대검 3타 휘두르기(GreatswordAttack_03).
///   동작 SO(ElementSwordBehaviorSO)를 만들어 R 스킬(Greatsword_QSkill)에 물리고 이름 · 설명을 바꾼다.
/// </summary>
public static class ElementSwordSetup
{
    private const string DrawFrom = "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/Common/Unarm&Equip/Inplace/GhostSamurai_APose_Equip01_Inplace.FBX";
    private const string DrawDir  = "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/Greatsword";
    private const string DrawTo   = DrawDir + "/GhostSamurai_APose_Equip01_Inplace.FBX";
    private const string DrawKey  = "GreatswordDraw", SwingKey = "GreatswordAttack_03";
    private const string SkillPath    = "Assets/RelicFairy/Weapon/Greatsword/Data/Greatsword_QSkill.asset";
    private const string ESkillPath   = "Assets/RelicFairy/Weapon/Greatsword/Data/Greatsword_ESkill.asset";
    private const string BehaviorPath = "Assets/RelicFairy/Weapon/Greatsword/Data/Greatsword_ElementSwordBehavior.asset";
    private const string AnimSetPath  = "Assets/RelicFairy/Weapon/Greatsword/Data/GreatswordBase/GreatswordAnimation.asset";
    private const string DrawBase = "QSkill_01", SwingBase = "QSkill_Iasen";
    private const string RegistryPath = "Assets/RelicFairy/Systems/Combat/ElementVfxRegistry.asset";
    private const string SpellMats = "Assets/RelicFairy/_Imported/EffectSource/Spells Pack/LWRP(URP)/Particles_LWRP/Materials/";

    private const string Trails = "Assets/RelicFairy/_Imported/EffectSource/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Trail Prefabs/";

    // 속성 검 칼 궤적(10-26 사용자 「속성별 트레일 · 리소스 찾아서」) — INab Weapon Trails(플레이어 트레일과 같은 팩)
    private static readonly (RuneElement element, string path)[] BladeTrails =
    {
        (RuneElement.Fire,     Trails + "Fire 1.prefab"),
        (RuneElement.Ice,      Trails + "Water 1.prefab"),
        (RuneElement.Electric, Trails + "Energy 1.prefab"),
        (RuneElement.Grass,    Trails + "Poison 1.prefab"),
        (RuneElement.Light,    Trails + "Holy 1.prefab"),
        (RuneElement.Dark,     Trails + "Dark 1.prefab"),
    };

    // 칼날 입자 재질(실측 97 ⑨ 10-26) — Spells Pack URP 입자 · 색은 BladeWrapFx가 속성 팔레트로 칠한다
    private static readonly (RuneElement element, string path)[] BladeMaterials =
    {
        (RuneElement.Fire,     SpellMats + "FireTrail_LWRP.mat"),
        (RuneElement.Ice,      SpellMats + "Glow_LWRP.mat"),
        (RuneElement.Electric, SpellMats + "Glow_LWRP.mat"),
        (RuneElement.Grass,    SpellMats + "Smoke_LWRP.mat"),
        (RuneElement.Light,    SpellMats + "Glow_LWRP.mat"),
        (RuneElement.Dark,     SpellMats + "Smoke_LWRP.mat"),
    };

    [MenuItem("RelicFairy/Weapon/대검 R 속성 검 리소스 등록 (10-26)")]
    private static void Register()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogWarning("[ElementSword] 어드레서블 설정 없음"); return; }

        if (AssetDatabase.LoadAssetAtPath<Object>(DrawTo) == null)
        {
            EnsureFolder(DrawDir);
            string err = AssetDatabase.MoveAsset(DrawFrom, DrawTo);
            if (!string.IsNullOrEmpty(err)) { Debug.LogWarning($"[ElementSword] 클립 옮기기 실패: {err}"); return; }
        }
        bool ok = Add(settings, "WeaponAnimation", DrawTo, DrawKey);

        var behavior = AssetDatabase.LoadAssetAtPath<ElementSwordBehaviorSO>(BehaviorPath);
        if (behavior == null)
        {
            behavior = ScriptableObject.CreateInstance<ElementSwordBehaviorSO>();
            AssetDatabase.CreateAsset(behavior, BehaviorPath);
        }

        var skill = AssetDatabase.LoadAssetAtPath<SkillSO>(SkillPath);
        if (skill != null)
        {
            var so = new SerializedObject(skill);
            so.FindProperty("behavior").objectReferenceValue = behavior;
            so.FindProperty("skillName").stringValue = "속성 검";
            so.FindProperty("description").stringValue =
                "검을 뽑아 속성 검을 들고 크게 휘두른다(둘레 공격력의 400%). 속성 = 룬판에 가장 많은 속성(없으면 불). " +
                "속성 검 동안 칼이 커지고 평타 범위 ×1.3 · 휘두른 자리에 속성 자국 — 6초 + 재련할 때마다 0.5초.";
            so.FindProperty("_tier2Change").stringValue = "속성 검 휘두름 공격력의 400% · 속성 자국";
            so.FindProperty("_tier3Change").stringValue = "속성 검 동안 평타 범위 ×1.3 → ×1.45";
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(skill);
        }
        else { Debug.LogWarning("[ElementSword] R 스킬 자산 없음: " + SkillPath); ok = false; }

        var eSkill = AssetDatabase.LoadAssetAtPath<SkillSO>(ESkillPath);   // E도 R에 맞게(10-26 사용자) — 속성 검 동안 범위 · 속성 자국
        if (eSkill != null)
        {
            var so = new SerializedObject(eSkill);
            so.FindProperty("description").stringValue =
                "칼을 끌었다가 한 바퀴 크게 벤다(둘레 3.5m · 공격력의 310%). 속성 검(R) 동안 범위 ×1.3 · 벤 둘레에 속성 자국.";
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(eSkill);
        }
        else { Debug.LogWarning("[ElementSword] E 스킬 자산 없음: " + ESkillPath); ok = false; }

        ok &= SetClips();
        ok &= SetBladeMaterials();
        AssetDatabase.SaveAssets();
        Debug.Log($"[ElementSword] 등록 {(ok ? "끝" : "일부 실패")}");
    }

    /// <summary>대검 애니 칸 — R 첫 상태 = 검 뽑기 · 둘째 상태 = 3타 휘두르기(없으면 R 칸 바로 뒤에 새로).</summary>
    private static bool SetClips()
    {
        var set = AssetDatabase.LoadAssetAtPath<WeaponAnimationSetSO>(AnimSetPath);
        if (set == null) { Debug.LogWarning("[ElementSword] 애니 묶음 없음: " + AnimSetPath); return false; }
        var so = new SerializedObject(set);
        var groups = so.FindProperty("animGroups");
        bool draw = false, swing = false;
        for (int g = 0; g < groups.arraySize; g++)
        {
            var maps = groups.GetArrayElementAtIndex(g).FindPropertyRelative("clipMappings");
            int drawAt = -1;
            for (int i = 0; i < maps.arraySize; i++)
            {
                var m = maps.GetArrayElementAtIndex(i);
                string baseName = m.FindPropertyRelative("baseClipName").stringValue;
                if (baseName == DrawBase) { m.FindPropertyRelative("addressableKey").stringValue = DrawKey; draw = true; drawAt = i; }
                else if (baseName == SwingBase) { m.FindPropertyRelative("addressableKey").stringValue = SwingKey; swing = true; }
            }
            if (drawAt >= 0 && !swing)
            {
                maps.InsertArrayElementAtIndex(drawAt);   // 뽑기 칸을 복사해 바로 뒤에 둔다(같은 행동 종류)
                var m = maps.GetArrayElementAtIndex(drawAt + 1);
                m.FindPropertyRelative("baseClipName").stringValue = SwingBase;
                m.FindPropertyRelative("addressableKey").stringValue = SwingKey;
                m.FindPropertyRelative("comboIndex").intValue = 1;
                swing = true;
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(set);
        Debug.Log($"[ElementSword] 애니 칸 — 뽑기 {draw} · 휘두르기 {swing}");
        return draw && swing;
    }

    /// <summary>속성 이펙트 목록의 칼날 입자 재질 칸을 채운다.</summary>
    private static bool SetBladeMaterials()
    {
        var registry = AssetDatabase.LoadAssetAtPath<ElementVfxRegistry>(RegistryPath);
        if (registry == null) { Debug.LogWarning("[ElementSword] 속성 이펙트 목록 없음: " + RegistryPath); return false; }
        var so = new SerializedObject(registry);
        var entries = so.FindProperty("entries");
        int set = 0;
        foreach (var (element, path) in BladeMaterials)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { Debug.LogWarning("[ElementSword] 재질 없음: " + path); continue; }
            for (int i = 0; i < entries.arraySize; i++)
            {
                var e = entries.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("element").enumValueIndex != (int)element) continue;
                e.FindPropertyRelative("bladeMaterial").objectReferenceValue = mat;
                set++;
            }
        }
        int trails = 0;
        foreach (var (element, path) in BladeTrails)
        {
            var trail = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (trail == null) { Debug.LogWarning("[ElementSword] 트레일 없음: " + path); continue; }
            for (int i = 0; i < entries.arraySize; i++)
            {
                var e = entries.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("element").enumValueIndex != (int)element) continue;
                e.FindPropertyRelative("bladeTrail").objectReferenceValue = trail;
                trails++;
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(registry);
        Debug.Log($"[ElementSword] 칼날 재질 {set}칸 · 트레일 {trails}칸");
        return set == BladeMaterials.Length && trails == BladeTrails.Length;
    }

    private static bool Add(AddressableAssetSettings settings, string groupName, string path, string address)
    {
        var group = settings.FindGroup(groupName);
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (group == null || string.IsNullOrEmpty(guid)) { Debug.LogWarning($"[ElementSword] {address}: 그룹 {group != null} · 에셋 {!string.IsNullOrEmpty(guid)} ({path})"); return false; }
        foreach (var g in settings.groups)
            if (g != null)
                foreach (var x in g.entries)
                    if (x.address == address && x.guid != guid) { Debug.LogWarning($"[ElementSword] 주소 충돌 {address}: {x.AssetPath}"); return false; }
        var e = settings.CreateOrMoveEntry(guid, group, false, false);
        e.address = address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, e, true);
        Debug.Log($"[ElementSword] {e.address} · {e.parentGroup.Name} · {e.AssetPath}");
        return true;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int cut = path.LastIndexOf('/');
        string parent = path.Substring(0, cut);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(cut + 1));
    }
}
