#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 각인 고르기 카드 그림의 X자 베기 한 획 반입(11-02 사용자 「무기 스킬 각인 — 있는 리소스로 표현」).
/// 그림 = 환영베기 X자 이펙트(KatanaDelayedCut = SpecialSkillsEffectsPack Effect_06_Slash)가 쓰는 베기 묶음 Slash_6의 한 칸(10×10 중 0부터 3행 6열)을
/// 밝기 → 투명도로 바꾸고(검은 테 없이) 세로로 눌러 호의 가운데를 그림 가운데에 맞춘 것 — ±45°로 엇갈리면 X자가 된다(스크래치 engrave1102).
/// 스프라이트로 들이고 CrucibleSkin.asset의 각인 그림 칸에 넣는다(스킨이 Addressable이라 따로 등록하지 않는다 — 대장장이 초상과 같은 길).
/// </summary>
internal static class EngraveSlashArt1102Editor
{
    private const string ArtPath  = "Assets/RelicFairy/UI/Skin/Crucible/Art/EngraveSlashArc.png";
    private const string SkinPath = "Assets/RelicFairy/UI/Skin/Crucible/CrucibleSkin.asset";

    [MenuItem("RelicFairy/UI/11-02 각인 X자 베기 그림 반입 (편집 모드)")]
    private static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[각인 그림] 편집 모드에서만"); return; }
        AssetDatabase.ImportAsset(ArtPath, ImportAssetOptions.ForceUpdate);
        if (AssetImporter.GetAtPath(ArtPath) is TextureImporter imp)
        {
            imp.textureType         = TextureImporterType.Sprite;
            imp.spriteImportMode    = SpriteImportMode.Single;
            imp.mipmapEnabled       = false;
            imp.alphaIsTransparency = true;
            imp.filterMode          = FilterMode.Bilinear;
            imp.wrapMode            = TextureWrapMode.Clamp;
            var platform = imp.GetDefaultPlatformTextureSettings();
            platform.maxTextureSize     = 256;   // 카드 안 큰 X자 58px(4K 116) — 256이면 넉넉하다
            platform.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SetPlatformTextureSettings(platform);
            imp.SaveAndReimport();
        }
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath);
        var skin   = AssetDatabase.LoadAssetAtPath<CrucibleSkinSO>(SkinPath);
        if (sprite == null || skin == null) { Debug.LogWarning($"[각인 그림] 스프라이트 {sprite != null} · 스킨 {skin != null}"); return; }
        var so   = new SerializedObject(skin);
        var prop = so.FindProperty("engraveSlash");
        if (prop == null) { Debug.LogWarning("[각인 그림] 스킨에 engraveSlash 칸 없음"); return; }
        prop.objectReferenceValue = sprite;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(skin);
        AssetDatabase.SaveAssets();
        Debug.Log($"[각인 그림] 끝 — {ArtPath} → CrucibleSkin.engraveSlash");
    }
}
#endif
