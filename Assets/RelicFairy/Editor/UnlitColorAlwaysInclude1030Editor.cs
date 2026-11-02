#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// [도구 · 편집 모드] 10-30 「Unlit/Color」를 빌드 포함 셰이더(Always Included)에 넣는다.
/// PatternGuideHelper 기본 도형(구 · 기둥 · 연결선 · 데칼 없는 원판)이 Shader.Find("Unlit/Color")로 재질을 만드는데, 빌드에선 이 셰이더가 빠져
/// new Material(null) 예외 → 탄막의 회랑 탄 0발 · 보물 창고 무한 재배치 · 일부 보스 예고가 안 보였다(빌드 Player.log 3,670번).
/// 코드에도 대체 사슬(Sprites/Default → URP Unlit)을 넣었지만, 원래 색 · 섞임 그대로 보이게 원래 셰이더를 빌드에 싣는다.
/// </summary>
internal static class UnlitColorAlwaysInclude1030Editor
{
    [MenuItem("RelicFairy/Tools/10-30 Unlit-Color 빌드 포함 (Edit)")]
    private static void Run()
    {
        var shader = Shader.Find("Unlit/Color");
        if (shader == null) { Debug.LogWarning("[UnlitInclude1030] Unlit/Color 셰이더를 찾지 못했다"); return; }
        var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (gs == null || gs.Length == 0) { Debug.LogWarning("[UnlitInclude1030] GraphicsSettings 없음"); return; }
        var so = new SerializedObject(gs[0]);
        var arr = so.FindProperty("m_AlwaysIncludedShaders");
        for (int i = 0; i < arr.arraySize; i++)
            if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) { Debug.Log("[UnlitInclude1030] 이미 있음"); return; }
        arr.InsertArrayElementAtIndex(arr.arraySize);
        arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = shader;
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        Debug.Log($"[UnlitInclude1030] Always Included에 넣음 — 이제 {arr.arraySize}개");
    }
}
#endif
