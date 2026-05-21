using UnityEditor;
using UnityEngine;

public static class FixGhostSamuraiClips
{
    private static readonly string[] TargetClips =
    {
        "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Defense/Root/HeavyCharge.FBX",
        "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/GhostSamurai_APose_Attack04_Inplace.FBX",
    };

    // 같은 리그를 쓰는 작동 중인 FBX — humanDescription 복사 소스
    private const string HumanDescSourcePath =
        "Assets/Abyss/Animations/Player/Test_01/Attack/NormalAttack_1.FBX";

    [MenuItem("Tools/Fix GhostSamurai Katana Clips (Copy humanDescription)")]
    public static void Fix()
    {
        var srcImporter = AssetImporter.GetAtPath(HumanDescSourcePath) as ModelImporter;
        if (srcImporter == null)
        {
            Debug.LogError($"[FixGhostSamurai] 소스 FBX 없음: {HumanDescSourcePath}");
            return;
        }

        HumanDescription humanDesc = srcImporter.humanDescription;
        Debug.Log($"[FixGhostSamurai] humanDescription 로드 완료: human={humanDesc.human?.Length ?? 0}개 뼈, skeleton={humanDesc.skeleton?.Length ?? 0}개");

        int fixedCount = 0;
        foreach (string path in TargetClips)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[FixGhostSamurai] 파일 없음: {path}");
                continue;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.humanDescription = humanDesc;
            importer.SaveAndReimport();
            Debug.Log($"[FixGhostSamurai] humanDescription 적용: {path}");
            fixedCount++;
        }

        Debug.Log($"[FixGhostSamurai] 완료: {fixedCount}/{TargetClips.Length}개 변경");
        EditorUtility.DisplayDialog("Fix 완료", $"{fixedCount}개 클립에 GhostSamurai humanDescription 적용 완료", "확인");
    }
}
