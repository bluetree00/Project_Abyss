#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

/// <summary>
/// 에디터 <b>재생 모드</b>를 FastMode(Use Asset Database)로 강제한다.
/// Addressables 그룹을 바꿔도 번들 재빌드 없이 바로 반영되게 하기 위한 개발 편의 설정이다.
///
/// ⚠ 과거엔 <c>settings.ActivePlayerDataBuilderIndex</c>(= <b>플레이어 빌드</b>용 빌더)를 0으로 덮었다.
/// 이름이 비슷하지만 완전히 다른 설정이라, 다음 두 문제를 일으켰다.
///   1) [InitializeOnLoad]라 도메인 리로드마다 실행 → 인덱스가 늘 2(PackedMode)에서 0으로 회귀.
///      작업트리가 항상 더러워지고, 0이 커밋되면 팀 전체 플레이어 빌드가 깨진다.
///   2) 패키지의 AddressablesPlayerBuildProcessor가 플레이어 빌드 전 콘텐츠를 구울 때
///      FastMode라 0초 만에 실패 → 옛 번들이 그대로 포장된다.
/// 재생 모드 빌더는 ProjectConfigData.ActivePlayModeIndex로 완전히 분리돼 있으므로 그쪽만 건드린다.
/// </summary>
[InitializeOnLoad]
public static class AddressablesPlayModeFixEditor
{
    // 재생 모드 빌더 배열: 0 = Use Asset Database(FastMode), 1 = Simulate Groups, 2 = Use Existing Build
    private const int FastModeIndex = 0;

    static AddressablesPlayModeFixEditor()
    {
        // 설정 에셋이 아직 없으면(최초 임포트 등) 조용히 넘어간다.
        if (AddressableAssetSettingsDefaultObject.Settings == null) return;

        if (ProjectConfigData.ActivePlayModeIndex != FastModeIndex)
        {
            ProjectConfigData.ActivePlayModeIndex = FastModeIndex;
            UnityEngine.Debug.Log("[AddressablesFix] 재생 모드 스크립트 → Use Asset Database(FastMode)로 설정.");
        }
    }
}
#endif
