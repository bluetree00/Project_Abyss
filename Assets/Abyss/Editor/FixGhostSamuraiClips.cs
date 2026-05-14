using UnityEditor;
using UnityEngine;

public static class FixGhostSamuraiClips
{
    private static readonly string[] TargetClips =
    {
        "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Defense/Root/HeavyCharge.FBX",
        "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/GhostSamurai_APose_Attack04_Inplace.FBX",
    };

    [MenuItem("Tools/Fix GhostSamurai Katana Clips (Humanoid CreateFromThis)")]
    public static void Fix()
    {
        int fixedCount = 0;
        foreach (string path in TargetClips)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[FixGhostSamurai] 파일 없음: {path}");
                continue;
            }

            if (importer.animationType == ModelImporterAnimationType.Human
                && importer.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel)
            {
                Debug.Log($"[FixGhostSamurai] 이미 Human+CreateFromThis: {path}");
                continue;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
            Debug.Log($"[FixGhostSamurai] Humanoid+CreateFromThis 변경 완료: {path}");
            fixedCount++;
        }

        Debug.Log($"[FixGhostSamurai] 완료: {fixedCount}/{TargetClips.Length}개 변경");
        EditorUtility.DisplayDialog("Fix 완료", $"{fixedCount}개 클립을 Humanoid(CreateFromThis)로 변경했습니다.", "확인");
    }
}
