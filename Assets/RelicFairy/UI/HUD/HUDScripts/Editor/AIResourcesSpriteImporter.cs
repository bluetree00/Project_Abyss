// AIResourcesSpriteImporter.cs
// AI Resources 폴더의 PNG 파일을 Sprite 타입으로 자동 설정하는 에디터 유틸리티
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class AIResourcesSpriteImporter
{
    private const string AIResourcesFolder = "Assets/RelicFairy/UI/AI Resources";

    [MenuItem("Tools/RelicFairy/AI Resources 스프라이트 임포트 설정")]
    public static void SetAIResourcesAsSprites()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { AIResourcesFolder });

        int changed = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)) continue;

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType      = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled    = false;
                importer.filterMode       = FilterMode.Bilinear;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                changed++;
                Debug.Log($"[AIResourcesSpriteImporter] Sprite 설정 완료: {path}");
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"[AIResourcesSpriteImporter] 총 {changed}개 텍스처를 Sprite로 변환했습니다.");
        EditorUtility.DisplayDialog("완료", $"AI Resources: {changed}개 텍스처를 Sprite로 변환했습니다.", "확인");
    }
}
#endif
