using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 10-22 대검 · 카타나 스킬 개편 리소스 등록(장비 형 실측 78 · 79에서 고름).
///   땅 균열(Infinity PBR ShatteredGround) → Effects 「GreatswordGroundCrack」 — 최후의 일격 착지
///   늦게 갈라짐(SpecialSkills MassiveSlash의 베기 한 조각) → Effects 「KatanaDelayedCut」 — 환영베기 X자.
///     MassiveSlash 본체는 스크립트(MultipleObjectsMake)가 Start에서만 조각을 만들어 풀 재사용이 안 된다 — 조각을 직접 쓴다.
///   뛰어들며 베기(GhostSamurai Dodge_Attack_F) → _ThirdParty(저장소 밖)에서 _Imported로 옮기고 WeaponAnimation 「Katana_DodgeSlash」 — 성광베기.
/// </summary>
public static class SkillOverhaulSetup
{
    private const string CrackPath = "Assets/RelicFairy/_Imported/EffectSource/Magic Pig Games (Infinity PBR)/Shared Files/Magic Spells & Particles/Particle Components/PComponent ShatteredGround.prefab";
    private const string CutPath = "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_2(ScriptBased)/Effects/Effect_06_MassiveSlash/Effect_06_Parts/Effect_06_Slash.prefab";
    private const string DodgeFrom = "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Dodge/Inplace/GhostSamurai_APose_Dodge_Attack_F_Inplace.FBX";
    private const string DodgeDir = "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/APose/Dodge/Inplace";
    private const string DodgeTo = DodgeDir + "/GhostSamurai_APose_Dodge_Attack_F_Inplace.FBX";
    private const string GreatHitPath = "Assets/RelicFairy/_Imported/EffectSource/Travis Game Assets/Hit Impact Effects/Prefabs/Hits/Hit_04.prefab";   // 10-22 B4 대검 푸른 타격

    [MenuItem("RelicFairy/Weapon/대검 · 카타나 스킬 개편 리소스 등록 (10-22)")]
    private static void Register()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogWarning("[SkillOverhaul] 어드레서블 설정 없음"); return; }

        if (AssetDatabase.LoadAssetAtPath<Object>(DodgeTo) == null)
        {
            EnsureFolder(DodgeDir);
            string err = AssetDatabase.MoveAsset(DodgeFrom, DodgeTo);
            if (!string.IsNullOrEmpty(err)) { Debug.LogWarning($"[SkillOverhaul] 클립 옮기기 실패: {err}"); return; }
        }

        bool ok = Add(settings, "Effects", CrackPath, "GreatswordGroundCrack")
                & Add(settings, "Effects", CutPath, "KatanaDelayedCut")
                & Add(settings, "WeaponAnimation", DodgeTo, "Katana_DodgeSlash")
                & Add(settings, "Effects", GreatHitPath, "GreatswordHitImpact");
        AssetDatabase.SaveAssets();
        Debug.Log($"[SkillOverhaul] 등록 {(ok ? "끝" : "일부 실패")}");
    }

    private static bool Add(AddressableAssetSettings settings, string groupName, string path, string address)
    {
        var group = settings.FindGroup(groupName);
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (group == null || string.IsNullOrEmpty(guid)) { Debug.LogWarning($"[SkillOverhaul] {address}: 그룹 {group != null} · 에셋 {!string.IsNullOrEmpty(guid)} ({path})"); return false; }
        foreach (var g in settings.groups)
            if (g != null)
                foreach (var x in g.entries)
                    if (x.address == address && x.guid != guid) { Debug.LogWarning($"[SkillOverhaul] 주소 충돌 {address}: {x.AssetPath}"); return false; }
        var e = settings.CreateOrMoveEntry(guid, group, false, false);
        e.address = address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, e, true);
        Debug.Log($"[SkillOverhaul] {e.address} · {e.parentGroup.Name} · {e.AssetPath}");
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
