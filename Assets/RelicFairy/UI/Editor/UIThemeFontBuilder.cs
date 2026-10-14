#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// 톤 v2 글꼴 만들기 · 등록(10-07) — 정적 굵기 TTF 3개로 TMP 동적 글꼴을 만들고(이미 있으면 그대로),
/// <see cref="UIThemeFontsSO"/>에 묶어 Addressables 「UI/ThemeFonts」로 등록한다(UISkin 패턴).
/// 동적 글꼴이라 원본 TTF가 함께 빌드에 들어가고 글리프는 실행 중 구워진다.
/// </summary>
public static class UIThemeFontBuilder
{
    private const string FontDir   = "Assets/RelicFairy/Fonts/";
    private const string AssetPath = "Assets/RelicFairy/UI/Base/UIThemeFonts.asset";

    [MenuItem("RelicFairy/UI/10-07 톤 v2 글꼴 만들기·등록")]
    private static void Build()
    {
        var l = MakeFont("NotoSansKR-Light");
        var r = MakeFont("NotoSansKR-Regular");
        var m = MakeFont("NotoSansKR-Medium");
        if (l == null || r == null || m == null) { Debug.LogError("[ThemeFonts] 글꼴 만들기 실패 — TTF 확인"); return; }

        var so = AssetDatabase.LoadAssetAtPath<UIThemeFontsSO>(AssetPath);
        if (so == null)
        {
            so = ScriptableObject.CreateInstance<UIThemeFontsSO>();
            AssetDatabase.CreateAsset(so, AssetPath);
        }
        so.EditorAssign(l, r, m);
        EditorUtility.SetDirty(so);
        AssetDatabase.SaveAssets();

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("[ThemeFonts] AddressableAssetSettings 없음"); return; }
        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(AssetPath), settings.DefaultGroup, false, false);
        entry.address = UISkin.ThemeFontsAddress;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ThemeFonts] 끝 — {AssetPath} → '{entry.address}' ({settings.DefaultGroup.Name})");
    }

    private static TMP_FontAsset MakeFont(string name)
    {
        string path = FontDir + name + " SDF.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (existing != null) return existing;

        var font = AssetDatabase.LoadAssetAtPath<Font>(FontDir + name + ".ttf");
        if (font == null) { Debug.LogError($"[ThemeFonts] TTF 없음: {FontDir}{name}.ttf"); return null; }

        var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
        fa.name = name + " SDF";
        AssetDatabase.CreateAsset(fa, path);
        fa.atlasTextures[0].name = name + " SDF Atlas";
        AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
        fa.material.name = name + " SDF Material";
        AssetDatabase.AddObjectToAsset(fa.material, fa);
        EditorUtility.SetDirty(fa);
        AssetDatabase.SaveAssets();
        return fa;
    }
}
#endif
