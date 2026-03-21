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
        string outputPath = "C:/Users/u/Desktop/AbyssBuild/Abyss.exe";

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[]
            {
                "Assets/Abyss/Scenes/Logo.unity",
                "Assets/Abyss/Scenes/Login.unity",
                "Assets/Abyss/Scenes/Lobby.unity",
                "Assets/Abyss/Scenes/StageMap.unity",
                "Assets/Abyss/Scenes/GameScene.unity",
            },
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        };

        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[Build] 결과: {report.summary.result} | 경로: {outputPath}");
    }
}
