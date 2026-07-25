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

        // AddressablesPlayModeFixEditor가 활성 빌더를 매 refresh마다 FastMode(0)로 되돌린다.
        // 그 상태로 BuildPlayerContent를 부르면 0초 만에 실패하므로, 빌드 직전에 PackedMode 빌더를
        // 직접 선택한다. 이 메서드는 동기 실행이라 도중 도메인 리로드/복귀가 끼어들지 않는다.
        int packedIndex = -1;
        for (int i = 0; i < settings.DataBuilders.Count; i++)
        {
            var b = settings.DataBuilders[i] as UnityEditor.AddressableAssets.Build.DataBuilders.BuildScriptPackedMode;
            if (b != null) { packedIndex = i; break; }
        }
        if (packedIndex < 0)
        {
            Debug.LogError("[AddressablesBuildRunner] BuildScriptPackedMode 빌더를 찾을 수 없습니다.");
            return;
        }
        if (settings.ActivePlayerDataBuilderIndex != packedIndex)
        {
            settings.ActivePlayerDataBuilderIndex = packedIndex;
            Debug.Log($"[AddressablesBuildRunner] 빌드용 활성 빌더 → PackedMode(index {packedIndex})로 강제.");
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
