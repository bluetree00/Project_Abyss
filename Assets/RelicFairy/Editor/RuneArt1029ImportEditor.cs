#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// [도구 · 편집 모드] 10-29 룬 획득 팝업 힉스필드 바탕 2장 반입 — 창 안쪽(흑요석 판) · 룬 실물 판(돌 제단 홈).
/// 그림은 `_Imported/Higgsfield/UIFrame/`에 먼저 복사해 둔다(스크래치 hf1029 · 바탕화면 4c 백업). 스프라이트로 들이고 RuneSelectSkin에 잇는다.
/// </summary>
internal static class RuneArt1029ImportEditor
{
    private const string Dir      = "Assets/RelicFairy/_Imported/Higgsfield/UIFrame/";
    private const string Window   = Dir + "Art_RuneSelectWindowFill.png";
    private const string Showcase = Dir + "Art_RuneShowcaseNiche.png";
    private const string SkinPath = "Assets/RelicFairy/UI/Skin/RuneSelect/RuneSelectSkin.asset";

    [MenuItem("RelicFairy/Tools/10-29 룬 획득 팝업 힉스필드 바탕 반입 (Edit)")]
    private static void Run()
    {
        var win  = ImportSprite(Window);
        var case_ = ImportSprite(Showcase);
        var skin = AssetDatabase.LoadAssetAtPath<RuneSelectSkinSO>(SkinPath);
        if (skin == null) { Debug.LogWarning("[RuneArt1029] 스킨 없음 " + SkinPath); return; }
        var so = new SerializedObject(skin);
        so.FindProperty("windowInterior").objectReferenceValue   = win;
        so.FindProperty("showcaseBackdrop").objectReferenceValue = case_;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(skin);
        AssetDatabase.SaveAssets();
        Debug.Log($"[RuneArt1029] 창 안쪽 {(win != null)} · 룬 판 {(case_ != null)} → {SkinPath}");
    }

    private static Sprite ImportSprite(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogWarning("[RuneArt1029] 그림 없음 " + path); return null; }
        ti.textureType         = TextureImporterType.Sprite;
        ti.spriteImportMode    = SpriteImportMode.Single;
        ti.mipmapEnabled       = false;
        ti.alphaIsTransparency = false;
        ti.maxTextureSize      = 2048;
        ti.textureCompression  = TextureImporterCompression.CompressedHQ;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
#endif
