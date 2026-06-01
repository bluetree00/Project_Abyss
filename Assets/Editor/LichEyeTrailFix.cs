#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class LichEyeTrailFix
{
    [MenuItem("Tools/Lich/Fix Eye Trail Shader (URP)")]
    public static void FixShader()
    {
        const string lichPath = "Assets/RelicFairy/Characters/Monster/Boss_Monster/Lich/Lich.prefab";
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) { Debug.LogError("[LichEyeTrailFix] URP Particles/Unlit 셰이더 없음"); return; }

        var contents = PrefabUtility.LoadPrefabContents(lichPath);
        int fixed_ = 0;
        foreach (var trail in contents.GetComponentsInChildren<TrailRenderer>(true))
        {
            if (trail.name != "EyeTrail_L" && trail.name != "EyeTrail_R") continue;
            var mat = trail.sharedMaterial;
            if (mat == null || mat.shader == shader) continue;
            mat.shader = shader;
            mat.SetColor("_BaseColor", new Color(1f, 0.05f, 0.05f, 0.9f));
            EditorUtility.SetDirty(mat);
            fixed_++;
        }
        PrefabUtility.SaveAsPrefabAsset(contents, lichPath);
        PrefabUtility.UnloadPrefabContents(contents);
        AssetDatabase.Refresh();
        Debug.Log($"[LichEyeTrailFix] Eye Trail 셰이더 {fixed_}개 URP로 수정 완료");
    }
}
#endif
