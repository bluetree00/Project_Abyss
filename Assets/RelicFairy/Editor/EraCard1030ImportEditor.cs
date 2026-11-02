#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// [도구 · 편집 모드] 10-30 해방기 · 악몽 두 장 창(NightmareGateChoice · RankingRoomChoice) 카드 그림 반입 — 힉스필드 2장(해방된 세계 · 악몽).
/// 그림은 `_Imported/Higgsfield/UIFrame/`에 먼저 복사해 둔다(스크래치 hf1030 · 원본은 바탕화면 구현설계). 스프라이트로 들이고 Addressables 「UI/EraChoice/…」로 등록한다.
/// </summary>
internal static class EraCard1030ImportEditor
{
    private const string Dir = "Assets/RelicFairy/_Imported/Higgsfield/UIFrame/";

    [MenuItem("RelicFairy/Tools/10-30 해방기 · 악몽 카드 그림 반입 (Edit)")]
    private static void Run()
    {
        Import(Dir + "Art_EraCard_Liberation.png", NightmareGateChoice.LiberationArtKey);
        Import(Dir + "Art_EraCard_Nightmare.png", NightmareGateChoice.NightmareArtKey);
        AssetDatabase.SaveAssets();
    }

    private static void Import(string path, string address)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogWarning("[EraCard1030] 그림 없음 " + path); return; }
        ti.textureType        = TextureImporterType.Sprite;
        ti.spriteImportMode   = SpriteImportMode.Single;
        ti.mipmapEnabled      = false;
        ti.alphaIsTransparency = false;
        ti.maxTextureSize     = 2048;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.SaveAndReimport();

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return;
        var e = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), settings.DefaultGroup, false, false);
        e.address = address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, e, true);
        Debug.Log($"[EraCard1030] {address} ← {path}");
    }
}
#endif
