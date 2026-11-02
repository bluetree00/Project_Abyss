#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// [도구 · 편집 모드] 10-30 재련소 모루 위 검 그림 반입(사용자 「재련되는 검 리소스도 변경 — 무형검부터 · 실제 재련하는 리소스도 UI에」) — 힉스필드 3장.
/// 그림은 `_Imported/Higgsfield/UIFrame/Art_CrucibleWeapon_*.png`에 먼저 복사해 둔다(가장자리 투명 1024 · 스크래치 sword1030).
/// 스프라이트로 들이고 Addressables 「UI/CrucibleWeapon/…」로 등록한다(UI_CruciblePanel이 키로 불러 모루 위 무기 칸에 쓴다).
/// </summary>
internal static class CrucibleWeaponArt1030ImportEditor
{
    private const string Dir = "Assets/RelicFairy/_Imported/Higgsfield/UIFrame/";

    [MenuItem("RelicFairy/Tools/10-30 재련소 검 그림 반입 (Edit)")]
    private static void Run()
    {
        Import(Dir + "Art_CrucibleWeapon_Nameless.png",   UI_CruciblePanel.NamelessArtKey);
        Import(Dir + "Art_CrucibleWeapon_Katana.png",     UI_CruciblePanel.KatanaArtKey);
        Import(Dir + "Art_CrucibleWeapon_Greatsword.png", UI_CruciblePanel.GreatswordArtKey);
        AssetDatabase.SaveAssets();
    }

    private static void Import(string path, string address)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogWarning("[CrucibleArt1030] 그림 없음 " + path); return; }
        ti.textureType         = TextureImporterType.Sprite;
        ti.spriteImportMode    = SpriteImportMode.Single;
        ti.mipmapEnabled       = false;
        ti.alphaIsTransparency = true;   // 가장자리를 투명으로 풀어 둔 그림
        ti.maxTextureSize      = 1024;
        ti.textureCompression  = TextureImporterCompression.CompressedHQ;
        ti.SaveAndReimport();

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return;
        var e = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), settings.DefaultGroup, false, false);
        e.address = address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, e, true);
        Debug.Log($"[CrucibleArt1030] {address} ← {path}");
    }
}
#endif
