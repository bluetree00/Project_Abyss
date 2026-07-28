#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// 스탠드얼론 앱(빌드 대표) 아이콘을 로비 타이틀 로고로 지정한다.
///
/// PlayerSettings의 아이콘은 인스펙터에서만 만질 수 있어 스크립트/자동화로 바꾸려면 이 경로가 필요하다.
/// 원본 <c>Title.png</c>는 715x459 가로형이라 그대로 넣으면 정사각으로 찌그러지므로,
/// 왜곡·잘림 없이 1024 정사각 캔버스 가운데에 맞춘 <c>AppIcon_Lobby.png</c>를 쓴다.
/// </summary>
public static class AppIconSetter
{
    private const string IconPath = "Assets/RelicFairy/UI/Lobby Intro/AppIcon_Lobby.png";

    [MenuItem("RelicFairy/Build/Set App Icon (Lobby Title)")]
    public static void Apply()
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        if (tex == null)
        {
            Debug.LogError($"[AppIconSetter] 아이콘 텍스처를 찾을 수 없다: {IconPath}");
            return;
        }

        // 크기별 슬롯을 전부 같은 원본으로 채운다 — 축소는 Unity가 처리한다.
        var target = NamedBuildTarget.Standalone;
        int slots  = PlayerSettings.GetIconSizes(target, IconKind.Any).Length;
        if (slots <= 0)
        {
            Debug.LogError("[AppIconSetter] 스탠드얼론 아이콘 슬롯이 없다 — 플랫폼 모듈 설치 상태를 확인할 것.");
            return;
        }

        var icons = new Texture2D[slots];
        for (int i = 0; i < slots; i++) icons[i] = tex;

        PlayerSettings.SetIcons(target, icons, IconKind.Any);
        AssetDatabase.SaveAssets();
        Debug.Log($"[AppIconSetter] 앱 아이콘 적용 완료: {IconPath} (슬롯 {slots}개)");
    }
}
#endif
