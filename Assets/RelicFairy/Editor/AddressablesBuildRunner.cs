using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Build;
using UnityEngine;

public static class AddressablesBuildRunner
{
    [MenuItem("RelicFairy/Addressables/Build Now")]
    public static void BuildAddressables()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[AddressablesBuildRunner] AddressableAssetSettings를 찾을 수 없어 빌드를 중단합니다.");
            return;
        }

        AddressableAssetSettings.CleanPlayerContent(settings.ActivePlayerDataBuilder);
        AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

        // 결과를 검사하지 않으면 실패해도 '완료' 로그가 찍혀 원인을 놓친다.
        // 활성 빌더가 PackedMode가 아니면 여기서 0초 만에 실패하므로 빌더 이름을 함께 남긴다.
        if (!string.IsNullOrEmpty(result.Error))
        {
            Debug.LogError(
                $"[AddressablesBuildRunner] 빌드 실패: {result.Error}\n" +
                $"활성 빌더: {settings.ActivePlayerDataBuilder?.Name} " +
                "(플레이어 콘텐츠 빌드는 'Default Build Script'(BuildScriptPackedMode)여야 합니다)");
            return;
        }

        Debug.Log(
            $"[AddressablesBuildRunner] 빌드 완료 — {result.LocationCount}개 로케이션, " +
            $"{result.Duration:0.0}초\n출력: {result.OutputPath}");
    }
}
