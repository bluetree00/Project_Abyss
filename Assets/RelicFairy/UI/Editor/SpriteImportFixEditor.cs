using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// UI 스킨 폴더의 PNG를 <b>스프라이트로</b> 임포트되게 맞춘다.
///
/// <para>새로 받은 아트를 폴더에 넣으면 Unity가 기본값(Texture)으로 임포트한다. 그러면
/// 스프라이트 서브에셋이 없어 <c>Image.sprite</c> 참조가 조용히 null이 되고, 화면에는
/// 아트가 아예 안 나온다 — 색 폴백으로 그려져 "레이아웃이 이상하다"로만 보인다.</para>
///
/// <para>실제로 재련소 신규 납품본 12장이 전부 이 상태였다. .meta를 직접 고치지 않고
/// <see cref="TextureImporter"/>에 값을 주어 Unity가 쓰게 한다.</para>
/// </summary>
public static class SpriteImportFixEditor
{
    private static readonly string[] Roots =
    {
        "Assets/RelicFairy/UI/Skin",
        "Assets/RelicFairy/UI/Popup/Sprites",
        "Assets/RelicFairy/UI/HUD/Sprites",
    };

    [MenuItem("RelicFairy/UI/UI 아트를 스프라이트로 임포트")]
    private static void Fix()
    {
        var fixedPaths = new List<string>();
        int already = 0;

        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", Roots))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".png") && !path.EndsWith(".PNG")) continue;
                if (AssetImporter.GetAtPath(path) is not TextureImporter imp) continue;

                if (imp.textureType == TextureImporterType.Sprite) { already++; continue; }

                imp.textureType         = TextureImporterType.Sprite;
                imp.spriteImportMode    = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled       = false;
                imp.SaveAndReimport();
                fixedPaths.Add(path);
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }

        AssetDatabase.Refresh();
        Debug.Log($"[SpriteImportFix] 스프라이트로 전환 {fixedPaths.Count}장 · 이미 스프라이트 {already}장");
        foreach (var p in fixedPaths) Debug.Log($"[SpriteImportFix]   {p}");
    }
}
