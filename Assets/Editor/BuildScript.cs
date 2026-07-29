using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public static class BuildScript
{
    [MenuItem("Build/1. Build Addressables")]
    public static void BuildAddressables()
    {
        // 프로필을 Default로 변경 (로컬 번들 사용)
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings != null)
        {
            var profileId = settings.profileSettings.GetProfileId("Default");
            if (!string.IsNullOrEmpty(profileId))
            {
                settings.activeProfileId = profileId;
                Debug.Log($"[Build] Addressables 프로필 → Default");
            }
        }

        AddressableAssetSettings.BuildPlayerContent();
        Debug.Log("[Build] Addressables 빌드 완료");
    }


    [MenuItem("Build/Build Windows (Steam)")]
    public static void BuildWindows()
    {
        string outputPath = "C:/Users/u/Desktop/RelicFairyBuild/RelicFairy.exe";

        // 씬은 Build Settings(EditorBuildSettings)에서 그대로 읽는다.
        // 예전엔 여기에 경로를 하드코딩해 뒀다가 씬이 이동·삭제되며 전부 유효하지 않게 됐다
        // (Logo/Login/StageMap/GameScene). 목록을 한 곳(Build Settings)에만 두면 다시 어긋나지 않는다.
        var scenes = System.Array.ConvertAll(
            System.Array.FindAll(EditorBuildSettings.scenes, s => s.enabled),
            s => s.path);
        if (scenes.Length == 0)
        {
            Debug.LogError("[Build] Build Settings에 활성 씬이 없다 — 빌드 중단");
            return;
        }

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            // 배포용이므로 개발 빌드로 뽑지 않는다 — Development는 디버거·프로파일러를 열고
            // 산출물 옆에 DoNotShip 심볼 폴더를 만든다(docs/build-distribution-checklist.md).
            options = BuildOptions.None
        };

        Debug.Log($"[Build] 씬 {scenes.Length}개로 빌드 시작 → {outputPath}");
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[Build] 결과: {report.summary.result} | 경로: {outputPath}");
    }
}
