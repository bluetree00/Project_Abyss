
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TMPro;

/// <summary>
/// NotoSansKR 폰트에 한글 글리프를 강제로 미리 생성하는 에디터 유틸리티.
/// Menu: Tools > Fix Korean Font Atlas
/// </summary>
public class FontFixHelper
{
    [MenuItem("RelicFairy/Rendering/Fix Korean Font Atlas")]
    public static void FixKoreanFontAtlas()
    {
        // NotoSansKR 폰트 에셋 찾기
        string[] guids = AssetDatabase.FindAssets("NotoSansKR t:TMP_FontAsset");
        if (guids.Length == 0)
        {
            Debug.LogError("[FontFixHelper] NotoSansKR TMP_FontAsset을 찾지 못했습니다.");
            return;
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font == null)
        {
            Debug.LogError("[FontFixHelper] 폰트 에셋 로드 실패: " + path);
            return;
        }

        Debug.Log($"[FontFixHelper] 폰트 로드 성공: {path}");
        Debug.Log($"[FontFixHelper] AtlasPopulationMode: {font.atlasPopulationMode}");

        // Dynamic OS 모드로 설정 (OS 폰트 렌더링 사용, 아틀라스 불필요)
        font.atlasPopulationMode = AtlasPopulationMode.DynamicOS;
        Debug.Log("[FontFixHelper] DynamicOS 모드로 변경");

        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssets();
        Debug.Log("[FontFixHelper] 완료! DynamicOS 모드로 저장됨.");
    }

    [MenuItem("RelicFairy/Rendering/Regenerate Korean Font")]
    public static void RegenerateKoreanFontHighQuality()
    {
        // NotoSansKR 폰트 에셋 찾기
        string[] guids = AssetDatabase.FindAssets("NotoSansKR t:TMP_FontAsset");
        if (guids.Length == 0)
        {
            Debug.LogError("[FontFixHelper] NotoSansKR TMP_FontAsset을 찾지 못했습니다.");
            return;
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font == null)
        {
            Debug.LogError("[FontFixHelper] 폰트 에셋 로드 실패: " + path);
            return;
        }

        // Dynamic 모드로 리셋 후 고해상도 샘플링을 위해 글리프 테이블 초기화
        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        font.ClearFontAssetData(true);
        Debug.Log("[FontFixHelper] 폰트 데이터 초기화 완료");

        // 아틀라스 텍스처를 readable로 생성 (4096x4096 Alpha8)
        Texture2D newAtlas = new Texture2D(4096, 4096, TextureFormat.Alpha8, false);
        newAtlas.name = font.name + " Atlas";
        var pixels = new Color32[4096 * 4096];
        System.Array.Clear(pixels, 0, pixels.Length);
        newAtlas.SetPixels32(pixels);
        newAtlas.Apply();

        // 서브에셋으로 추가
        AssetDatabase.AddObjectToAsset(newAtlas, path);

        // 자주 쓰는 한글 문자 추가 (Dynamic 모드에서 미리 생성)
        string koreanChars = "가나다라마바사아자차카타파하확인취소캐릭터이름선택기사전사마법사궁수닫기열기";
        var unicodes = new List<uint>();
        foreach (char c in koreanChars)
            unicodes.Add((uint)c);

        uint[] missingUnicodes;
        bool success = font.TryAddCharacters(unicodes.ToArray(), out missingUnicodes);
        Debug.Log($"[FontFixHelper] TryAddCharacters 결과: success={success}, missing={missingUnicodes?.Length ?? 0}개");

        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssets();
        Debug.Log("[FontFixHelper] 고품질 재생성 완료!");
    }

    [MenuItem("RelicFairy/Dev/Screenshot Game View")]
    public static void TakeScreenshot()
    {
        string path = "Assets/Screenshots/gameview_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
        ScreenCapture.CaptureScreenshot(path);
        Debug.Log("[FontFixHelper] 스크린샷 저장: " + path);
    }
}
#endif
