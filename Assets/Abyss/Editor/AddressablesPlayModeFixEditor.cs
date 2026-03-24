#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.AddressableAssets;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

/// <summary>
/// 에디터 플레이 모드를 FastMode(AssetDatabase)로 강제 설정합니다.
/// WeaponAnimation 등 Addressables 그룹 변경 후 재빌드 없이 바로 동작하게 합니다.
/// </summary>
[InitializeOnLoad]
public static class AddressablesPlayModeFixEditor
{
    // FastMode = index 0 (Use Asset Database - 빌드 없이 AssetDB에서 직접 로드)
    private const int FastModeIndex = 0;

    static AddressablesPlayModeFixEditor()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return;

        if (settings.ActivePlayerDataBuilderIndex != FastModeIndex)
        {
            settings.ActivePlayerDataBuilderIndex = FastModeIndex;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("[AddressablesFix] Play Mode Script → Use Asset Database (FastMode)로 변경됨.");
        }
    }
}
#endif
