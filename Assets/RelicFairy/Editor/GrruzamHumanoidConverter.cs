#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 일회용 변환기: Grruzam Powerful Sword 애니메이션 클립을 Humanoid로 변환한다.
/// T-pose 베이스 모델로 아바타를 생성한 뒤, 대검/카타나 클립을 CopyFromOther로 그 아바타에 맞춘다.
/// (Generic 리그라 CombatGirl(Humanoid)에 리타게팅되지 않던 문제 해결)
/// 메뉴: Tools/RelicFairy/Convert Grruzam To Humanoid
/// </summary>
public static class GrruzamHumanoidConverter
{
    private const string Root  = "Assets/_ThirdParty/Grruzam Powerful Sword Animation(Great Sword, Katana)";
    private const string TPose = Root + "/Modeling/Modeling_T-Pose_Grrrru_Man(recommend).FBX";

    // 사용 중인 클립은 LFS 정리 때 _Imported 로 이동됨(원본 _ThirdParty 는 미사용 보관분만 남음).
    // 재변환 대상은 실제 사용 위치(_Imported)를 가리켜야 한다.
    private const string ImportedRoot = "Assets/RelicFairy/_Imported/Grruzam Powerful Sword Animation(Great Sword, Katana)";

    private static readonly string[] ClipFolders =
    {
        Root + "/Animation/M_Big_Sword",
        Root + "/Animation/M_Katana_Blade",
    };

    // 대검에 실제 사용하는 클립(전부 _Imported 로 이동됨). CopyFromOther(T-pose 아바타)로 재변환.
    private static readonly string[] GreatswordUsed =
    {
        ImportedRoot + "/Animation/M_Big_Sword/2_Attacks/2__7Combos/M_Big_Sword@Attack_7Combo_1.FBX",
        ImportedRoot + "/Animation/M_Big_Sword/2_Attacks/2__7Combos/M_Big_Sword@Attack_7Combo_2.FBX",
        ImportedRoot + "/Animation/M_Big_Sword/2_Attacks/2__7Combos/M_Big_Sword@Attack_7Combo_3.FBX",
        ImportedRoot + "/Animation/M_Big_Sword/2_Attacks/4__Jump_Attack/M_Big_Sword@Jump_Attack_Combo_1_ZeroHeight.FBX",
        ImportedRoot + "/Animation/M_Big_Sword/2_Attacks/4__Jump_Attack/M_Big_Sword@Jump_Attack_Combo_2_ZeroHeight.FBX",
        ImportedRoot + "/Animation/M_Big_Sword/2_Attacks/4__Jump_Attack/M_Big_Sword@Jump_Attack_Combo_3_ZeroHeight.FBX",
        ImportedRoot + "/Animation/M_Big_Sword/2_Attacks/5__Upper_Attack/M_Big_Sword@UpperAttack_ZeroHeight.FBX",
        ImportedRoot + "/Animation/M_Big_Sword/5_Revenges/Guard_Revenges/M_Big_Sword@Revenge_Guard_Loop.FBX",
        ImportedRoot + "/Animation/M_Big_Sword/5_Revenges/Guard_Revenges/M_Big_Sword@Revenge_Guard_Attack.FBX",
        ImportedRoot + "/Animation/M_Big_Sword/5_Revenges/Guard_Revenges/M_Big_Sword@Revenge_Guard_Accept.FBX",
        ImportedRoot + "/Animation/M_Big_Sword/3_Skills/M_Big_Sword@Skill_C.FBX",
    };

    // 올바른 본 매핑: T-pose 아바타로 CopyFromOther (CreateFromThisModel은 비-T포즈라 본이 틀어짐).
    [MenuItem("Tools/RelicFairy/Reconvert Greatsword Used (CopyFromTPose)")]
    public static void ReconvertGreatswordUsedCopy()
    {
        var baseImp = AssetImporter.GetAtPath(TPose) as ModelImporter;
        if (baseImp == null) { Debug.LogError("[GrruzamFix] T-pose 모델 없음: " + TPose); return; }
        // T-pose 아바타: Humanoid + 자체 생성 + Translation DoF 활성화.
        // (원본이 UE 스켈레톤이라 척추·쇄골 translation이 버려져 상체 포즈가 캐릭터와 어긋남 → Translation DoF로 보존해 리타게팅 품질 개선)
        bool baseDirty = false;
        if (baseImp.animationType != ModelImporterAnimationType.Human)
        { baseImp.animationType = ModelImporterAnimationType.Human; baseDirty = true; }
        if (baseImp.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        { baseImp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; baseDirty = true; }
        var hd = baseImp.humanDescription;
        if (!hd.hasTranslationDoF) { hd.hasTranslationDoF = true; baseImp.humanDescription = hd; baseDirty = true; }
        if (baseDirty) baseImp.SaveAndReimport();
        Debug.Log($"[GrruzamFix] T-pose hasTranslationDoF={baseImp.humanDescription.hasTranslationDoF}");
        Avatar src = null;
        foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(TPose))
            if (o is Avatar a) { src = a; break; }
        if (src == null) { Debug.LogError("[GrruzamFix] T-pose 아바타 없음"); return; }
        Debug.Log($"[GrruzamFix] T-pose avatar isValid={src.isValid} isHuman={src.isHuman} name={src.name}");

        int ok = 0, fail = 0;
        foreach (var path in GreatswordUsed)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) { Debug.LogWarning("[GrruzamFix] 임포터 없음: " + path); fail++; continue; }
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup   = ModelImporterAvatarSetup.CopyFromOther;
            imp.sourceAvatar  = src;
            imp.SaveAndReimport();
            int takes = imp.importedTakeInfos != null ? imp.importedTakeInfos.Length : 0;
            if (takes > 0) ok++;
            else { fail++; Debug.LogWarning("[GrruzamFix] CopyFromOther 후 클립 없음: " + System.IO.Path.GetFileName(path)); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[GrruzamFix] CopyFromTPose 재변환: ok={ok}, fail={fail} / {GreatswordUsed.Length}");
    }

    [MenuItem("Tools/RelicFairy/Reconvert Greatsword Used (CreateFromThisModel)")]
    public static void ReconvertGreatswordUsed()
    {
        int ok = 0, fail = 0;
        foreach (var path in GreatswordUsed)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) { Debug.LogWarning("[GrruzamFix] 임포터 없음: " + path); fail++; continue; }
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.sourceAvatar  = null;
            imp.SaveAndReimport();

            int takes = imp.importedTakeInfos != null ? imp.importedTakeInfos.Length : 0;
            if (takes > 0) ok++; else { fail++; Debug.LogWarning("[GrruzamFix] 여전히 클립 없음: " + path); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[GrruzamFix] 재변환: ok={ok}, fail={fail} / {GreatswordUsed.Length}");
    }

    [MenuItem("Tools/RelicFairy/Convert Grruzam To Humanoid")]
    public static void Convert()
    {
        // 1) T-pose 베이스 → Humanoid (Create From This Model) → 아바타 생성
        var baseImp = AssetImporter.GetAtPath(TPose) as ModelImporter;
        if (baseImp == null) { Debug.LogError("[Grruzam] T-pose 모델 없음: " + TPose); return; }
        baseImp.animationType = ModelImporterAnimationType.Human;
        baseImp.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
        baseImp.SaveAndReimport();

        Avatar src = null;
        foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(TPose))
            if (o is Avatar a) { src = a; break; }
        if (src == null) { Debug.LogError("[Grruzam] T-pose 아바타 생성 실패"); return; }

        // 2) 대검/카타나 클립 → Humanoid (Copy From Other = T-pose 아바타)
        int total = 0, changed = 0;
        foreach (var g in AssetDatabase.FindAssets("t:Model", ClipFolders))
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            if (!path.ToLower().EndsWith(".fbx")) continue;
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) continue;
            total++;
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup   = ModelImporterAvatarSetup.CopyFromOther;
            imp.sourceAvatar  = src;
            imp.SaveAndReimport();
            changed++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Grruzam] Humanoid 변환 완료: base=1, clips total={total} changed={changed}, avatar={src.name}");
    }
}
#endif
