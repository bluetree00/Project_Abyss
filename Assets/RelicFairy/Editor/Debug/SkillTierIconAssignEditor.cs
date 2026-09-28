using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 스킬 단계별 아이콘(2·3단계) 임포트 + 연결. 한 번만 쓰는 정리 도구다.
///
/// 강화·진화로 스킬 <b>기능</b>이 바뀌는데 아이콘은 1장뿐이라 단계가 화면에 안 보였다(09-21 사용자 지적).
/// 각 SkillSO가 이미 물고 있는 1단계 그림 옆의 <c>_t2 / _t3</c> 파일을 찾아 붙인다 — 이름 규칙만 맞으면 된다.
/// </summary>
public static class SkillTierIconAssignEditor
{
    [MenuItem("RelicFairy/Debug/스킬 단계 아이콘 연결")]
    private static void Assign()
    {
        AssetDatabase.Refresh();

        int linked = 0, skipped = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:SkillSO"))
        {
            string soPath = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<SkillSO>(soPath);
            if (so == null || so.icon == null) { skipped++; continue; }

            string iconPath = AssetDatabase.GetAssetPath(so.icon);
            if (string.IsNullOrEmpty(iconPath)) { skipped++; continue; }

            string dir  = Path.GetDirectoryName(iconPath)?.Replace('\\', '/');
            string stem = Path.GetFileNameWithoutExtension(iconPath);

            var sp = new SerializedObject(so);
            bool any = false;
            any |= TryLink(sp, "iconTier2", $"{dir}/{stem}_t2.png");
            any |= TryLink(sp, "iconTier3", $"{dir}/{stem}_t3.png");
            if (any)
            {
                sp.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(so);
                linked++;
                Debug.Log($"[스킬단계아이콘] {so.name} ← {stem}_t2/_t3");
            }
            else skipped++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[스킬단계아이콘] 연결 {linked}개 · 건너뜀 {skipped}개");
    }

    private static bool TryLink(SerializedObject sp, string field, string png)
    {
        if (!File.Exists(png)) return false;

        // UI에 쓰려면 Sprite여야 한다 — 새로 넣은 PNG는 기본 임포트가 Texture일 수 있다.
        if (AssetImporter.GetAtPath(png) is TextureImporter imp &&
            (imp.textureType != TextureImporterType.Sprite || imp.mipmapEnabled))
        {
            imp.textureType         = TextureImporterType.Sprite;
            imp.spriteImportMode    = SpriteImportMode.Single;
            imp.mipmapEnabled       = false;
            imp.wrapMode            = TextureWrapMode.Clamp;
            imp.alphaIsTransparency = true;
            imp.SaveAndReimport();
        }

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(png);
        if (sprite == null) return false;
        sp.FindProperty(field).objectReferenceValue = sprite;
        return true;
    }
}
