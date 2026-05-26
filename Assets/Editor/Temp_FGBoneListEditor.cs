using UnityEditor;
using UnityEngine;

public static class Temp_FGBoneListEditor
{
    [MenuItem("RelicFairy/Dev/List FG Bone Names")]
    public static void ListBones()
    {
        string prefabPath = "Assets/RelicFairy/Characters/Monster/Monster/ForestGuardian/Art/Treant_Body.fbx";
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (go == null)
        {
            Debug.LogError("[FGBoneList] Treant_Body.fbx를 찾지 못했습니다.");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[FGBoneList] {prefabPath} 뼈 목록:");
        PrintHierarchy(go.transform, 0, sb);
        Debug.Log(sb.ToString());
    }

    private static void PrintHierarchy(Transform t, int depth, System.Text.StringBuilder sb)
    {
        sb.AppendLine(new string(' ', depth * 2) + t.name);
        foreach (Transform child in t)
            PrintHierarchy(child, depth + 1, sb);
    }
}
