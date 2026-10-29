#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// [도구 · 편집 모드] 10-29 랭킹 비석 창 힉스필드 그림 반입 — 기록 석판 1장 · 금 · 은 · 동 메달 3장.
/// 그림은 `_Imported/Higgsfield/UIFrame/`에 먼저 복사해 둔다(스크래치 hf1029 · 바탕화면 4c 백업). 스프라이트로 들이고 Addressables 「UI/RankingBoard/…」로 등록한다.
/// </summary>
internal static class RankBoard1029ImportEditor
{
    private const string Dir = "Assets/RelicFairy/_Imported/Higgsfield/UIFrame/";

    [MenuItem("RelicFairy/Tools/10-29 랭킹 비석 힉스필드 그림 반입 (Edit)")]
    private static void Run()
    {
        Import(Dir + "Art_RankingTablet.png", "UI/RankingBoard/Tablet", alpha: false);
        for (int i = 1; i <= 3; i++) Import(Dir + $"Art_RankMedal_{i}.png", $"UI/RankingBoard/Medal{i}", alpha: true);
        AssetDatabase.SaveAssets();
    }

    private static void Import(string path, string address, bool alpha)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogWarning("[RankBoard1029] 그림 없음 " + path); return; }
        ti.textureType         = TextureImporterType.Sprite;
        ti.spriteImportMode    = SpriteImportMode.Single;
        ti.mipmapEnabled       = false;
        ti.alphaIsTransparency = alpha;
        ti.maxTextureSize      = 2048;
        ti.textureCompression  = TextureImporterCompression.CompressedHQ;
        ti.SaveAndReimport();

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return;
        var e = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), settings.DefaultGroup, false, false);
        e.address = address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, e, true);
        Debug.Log($"[RankBoard1029] {address} ← {path}");
    }
}
#endif
