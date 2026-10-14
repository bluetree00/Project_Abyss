#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 출시 설정을 한 번에 맞춘다(10-09 출시 점검). 설정은 에디터 메모리에 있어야 빌드에 들어가므로 YAML 대신 API로 바꾼다.
///  · PC 기본 품질 = Balanced — 07-25 결정(「저사양 첫 실행 부담 완화」)은 m_CurrentQuality만 바꿔 빌드에 안 먹었고,
///    PC 기본값이 비어 첫 실행이 최저(Performant)로 시작했다.
///  · 단일 실행 — 두 번 켜면 같은 세이브 파일을 동시에 썼다.
///  · 로그 스택트레이스 — Log · Warning 없음, Error · Assert · Exception = 스크립트만.
///  · Addressables = 플레이어 빌드와 함께 굽기(PC 설정에 따라 결과가 달라지지 않게).
///  · Unity 스플래시 끄기(게임 시작 영상이 있다).
/// 결과는 콘솔 「[ReleaseSettings]」.
/// </summary>
public static class ReleaseSettingsApplyEditor
{
    private const int StandaloneDefaultQuality = 1;   // 0 Performant · 1 Balanced · 2 High Fidelity

    [MenuItem("RelicFairy/Release/출시 설정 적용 (10-09)")]
    private static void Apply()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[ReleaseSettings] 플레이 중엔 하지 않는다."); return; }
        var log = new StringBuilder("[ReleaseSettings] ");

        // PC 기본 품질 — QualitySettings의 플랫폼별 기본값 맵(m_PerPlatformDefaultQuality)
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
        if (assets != null && assets.Length > 0)
        {
            var so  = new SerializedObject(assets[0]);
            var map = so.FindProperty("m_PerPlatformDefaultQuality");
            if (map != null && map.isArray)
            {
                SerializedProperty entry = null;
                for (int i = 0; i < map.arraySize; i++)
                {
                    var e = map.GetArrayElementAtIndex(i);
                    if (e.FindPropertyRelative("first")?.stringValue == "Standalone") { entry = e; break; }
                }
                if (entry == null)
                {
                    map.arraySize++;
                    entry = map.GetArrayElementAtIndex(map.arraySize - 1);
                    entry.FindPropertyRelative("first").stringValue = "Standalone";
                }
                entry.FindPropertyRelative("second").intValue = StandaloneDefaultQuality;
                so.ApplyModifiedPropertiesWithoutUndo();
                log.Append($"PC 기본 품질 {QualitySettings.names[StandaloneDefaultQuality]} · ");
            }
            else log.Append("⚠ 품질 맵 없음 · ");
        }

        PlayerSettings.forceSingleInstance = true;
        PlayerSettings.SetStackTraceLogType(LogType.Log,       StackTraceLogType.None);
        PlayerSettings.SetStackTraceLogType(LogType.Warning,   StackTraceLogType.None);
        PlayerSettings.SetStackTraceLogType(LogType.Error,     StackTraceLogType.ScriptOnly);
        PlayerSettings.SetStackTraceLogType(LogType.Assert,    StackTraceLogType.ScriptOnly);
        PlayerSettings.SetStackTraceLogType(LogType.Exception, StackTraceLogType.ScriptOnly);
        PlayerSettings.SplashScreen.show         = false;
        PlayerSettings.SplashScreen.showUnityLogo = false;
        log.Append("단일 실행 · 스택트레이스 · 스플래시 끔 · ");

        var aa = AddressableAssetSettingsDefaultObject.Settings;
        if (aa != null)
        {
            aa.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
            EditorUtility.SetDirty(aa);
            log.Append("Addressables 동반 빌드 · ");
        }

        AssetDatabase.SaveAssets();
        log.Append($"저장 끝 (스플래시 {PlayerSettings.SplashScreen.show} · 단일 {PlayerSettings.forceSingleInstance})");
        Debug.Log(log.ToString());
    }
}
#endif
