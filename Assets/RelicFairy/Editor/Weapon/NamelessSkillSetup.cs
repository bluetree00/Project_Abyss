#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 무형검 스킬 2종(E 무형참 · R 무형일섬)의 데이터를 만들고 배선한다.
/// 메뉴: RelicFairy/Weapon/Setup Nameless Skills — 재실행 안전(있으면 값만 갱신).
///
/// 왜 도구인가 — 새 .asset은 GUID가 있어야 다른 에셋이 참조할 수 있고, .meta는 Unity가 만든다(직접 생성 금지).
/// 그래서 생성·참조·어드레서블 등록을 한 번에 에디터 API로 처리한다.
///
/// 만드는 것:
///  · NamelessBase/NamelessBeam.asset   (FanArrowBehaviorSO — 검기 1발, +5부터 관통)
///  · NamelessBase/NamelessIasen.asset  (IasenSlashBehaviorSO — 기존 IasenSlash.asset 복제 + 2단계 필드)
///  · Nameless_ESkill.asset / Nameless_RSkill.asset (SkillSO) → T0_Nameless.skillE / skillQ(=R 슬롯)
///  · 어드레서블(Effects 그룹): NamelessBeam · NamelessBeamMuzzle · NamelessBeamHit · NamelessCut
/// </summary>
public static class NamelessSkillSetup
{
    private const string DataDir    = "Assets/RelicFairy/Weapon/Nameless/Data";
    private const string BaseDir    = DataDir + "/NamelessBase";
    private const string WeaponPath = DataDir + "/T0_Nameless.asset";
    private const string IasenSrc   = "Assets/RelicFairy/Shared/Characters/Skill/Behaviors/IasenSlash.asset";

    // 아이콘은 기존 카타나 E / 화염베기 아이콘을 임시로 빌린다(아트 후속).
    private const string IconEGuid = "5932011b449ac234197ea8c6600fb8fe";
    private const string IconRGuid = "ea5d9aa3689141f4da41f072b5dd4d64";

    private const string EffectsGroup = "Effects";
    private const string Beam = "Assets/RelicFairy/_Imported/EffectSource/MasterStylizedProjectiles/Projectiles/YellowSwordBeam/Prefabs";
    private const string Hovl = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio 2/Sword slash VFX/Prefabs";

    private static readonly (string key, string path)[] Vfx =
    {
        ("NamelessBeam",       $"{Beam}/Par_YellowSwordBeam.prefab"),
        ("NamelessBeamMuzzle", $"{Beam}/Par_YellowSwordBeam_Muzzle.prefab"),
        ("NamelessBeamHit",    $"{Beam}/Par_YellowSwordBeam_Hit.prefab"),
        ("NamelessCut",        $"{Hovl}/Spatial section.prefab"),
    };

    [MenuItem("RelicFairy/Weapon/Setup Nameless Skills")]
    public static void Setup()
    {
        var weapon = AssetDatabase.LoadAssetAtPath<MainWeaponSO>(WeaponPath);
        if (weapon == null) { Debug.LogError($"[무형검 셋업] 무기 없음: {WeaponPath}"); return; }

        // 1) 행동 SO
        var beam = LoadOrCreate<FanArrowBehaviorSO>($"{BaseDir}/NamelessBeam.asset");
        beam.arrowCount         = 1;
        beam.spreadAngle        = 0f;
        beam.arrowKey           = "Basic_Arrow_01";
        beam.baseDamagePerArrow = 30f;
        beam.fireDelay          = 0.35f;   // ESkill_01(Attack02_2, 1.12s)의 휘두름에 맞춘다
        beam.forwardOffset      = 1f;
        beam.upOffset           = 1f;
        beam.pierceMaxCount     = 5;
        beam.explodeRadius      = 2f;
        beam.explodeDamageRatio = 0.5f;
        beam.explodeEffectKey   = "NamelessBeamHit";
        beam.explodeEffectScale = 1f;
        beam.projectileEffectKey   = "NamelessBeam";
        beam.projectileEffectScale = 1f;
        beam.tier2ExtraMuzzleKey   = "NamelessBeamMuzzle";
        beam.tier2ExtraMuzzleScale = 1.3f;
        beam.tier3ExtraHitKey      = "NamelessBeamHit";
        beam.tier3ExtraHitScale    = 1.5f;
        beam.muzzleEffectKey       = "NamelessBeamMuzzle";
        beam.muzzleEffectScale     = 1f;
        beam.endDelay              = 0.5f;
        beam.animationOverride     = "";          // ESkill_01 상태(베이스 클립)
        EditorUtility.SetDirty(beam);

        var iasen = LoadOrCreate<IasenSlashBehaviorSO>($"{BaseDir}/NamelessIasen.asset", copyFrom: IasenSrc);
        iasen.animationOverride      = "QSkill_02";   // QSkill_01은 유물 Q가 덮어쓴다 — QSkill_02(모션 = QSkill_Iasen)로 분리
        iasen.tier2ExtraSlashCount   = 2;
        iasen.tier2FinishEffectKey   = "NamelessCut";
        iasen.tier2FinishEffectScale = 1f;
        EditorUtility.SetDirty(iasen);

        // 2) 스킬 SO
        var e = LoadOrCreate<SkillSO>($"{DataDir}/Nameless_ESkill.asset");
        e.skillName   = "무형참";
        e.description = "형태 없는 검기를 전방으로 날린다. 강화 +5부터 적을 꿰뚫는다.";
        e.icon        = LoadSprite(IconEGuid);
        e.cooldown    = 4f;
        e.behavior    = beam;
        EditorUtility.SetDirty(e);

        var r = LoadOrCreate<SkillSO>($"{DataDir}/Nameless_RSkill.asset");
        r.skillName   = "무형일섬";
        r.description = "형태를 지우고 8m를 꿰뚫어 지나간 뒤, 스쳐 간 적을 연이어 벤다. 강화 +5부터 연참이 늘고 공간을 가른다.";
        r.icon        = LoadSprite(IconRGuid);
        r.cooldown    = 8f;
        r.behavior    = iasen;
        EditorUtility.SetDirty(r);

        // 3) 무기 배선 — skillQ 필드가 R 슬롯(ActSkillState.GetSkillSO)
        weapon.skillE = e;
        weapon.skillQ = r;
        EditorUtility.SetDirty(weapon);

        // 4) 어드레서블
        int ok = RegisterVfx();

        AssetDatabase.SaveAssets();
        Debug.Log($"[무형검 셋업] 완료 — E={e.skillName} R={r.skillName}, VFX 등록 {ok}/{Vfx.Length}");
    }

    private static T LoadOrCreate<T>(string path, string copyFrom = null) where T : ScriptableObject
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) return existing;

        if (!string.IsNullOrEmpty(copyFrom) && AssetDatabase.LoadAssetAtPath<T>(copyFrom) != null)
        {
            if (!AssetDatabase.CopyAsset(copyFrom, path))
                Debug.LogWarning($"[무형검 셋업] 복제 실패, 새로 만든다: {copyFrom} → {path}");
            else
                return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        var so = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(so, path);
        return so;
    }

    private static Sprite LoadSprite(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path)) { Debug.LogWarning($"[무형검 셋업] 아이콘 없음: {guid}"); return null; }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static int RegisterVfx()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("[무형검 셋업] AddressableAssetSettings 없음"); return 0; }
        var group = settings.FindGroup(EffectsGroup) ?? settings.DefaultGroup;

        int ok = 0;
        foreach (var (key, path) in Vfx)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) { Debug.LogWarning($"[무형검 셋업] VFX 없음: {path}"); continue; }
            var entry = settings.CreateOrMoveEntry(guid, group, false, false);
            entry.address = key;
            ok++;
        }
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
        return ok;
    }
}
#endif
