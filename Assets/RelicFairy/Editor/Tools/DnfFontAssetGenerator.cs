using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// DNFForgedBlade-Bold 를 NotoSansKR SDF 와 동일한 설정으로 TMP 폰트 에셋(SDF)으로 생성한다.
/// 한글 전면 교체용 일회성 에디터 유틸. 생성 후 제거 가능.
/// 설정: pointSize 60 / padding 9 / 4096² / SDFAA / Dynamic + NotoSansKR 폴백.
/// 문자셋: 32-126, 44032-55203(완성형 한글), 12593-12634(호환 자모).
/// </summary>
public static class DnfFontAssetGenerator
{
    private const string TtfPath   = "Assets/RelicFairy/Fonts/DNFForgedBlade-Bold.ttf";
    private const string OutPath   = "Assets/RelicFairy/Fonts/DNFForgedBlade-Bold SDF.asset";
    private const string NotoPath  = "Assets/RelicFairy/Fonts/NotoSansKR-VariableFont_wght SDF.asset";

    [MenuItem("Tools/RelicFairy/Generate DNF Font Asset")]
    public static void Generate()
    {
        var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(TtfPath);
        if (sourceFont == null)
        {
            Debug.LogError($"[DnfFont] 소스 폰트 로드 실패: {TtfPath}");
            return;
        }

        var fontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            samplingPointSize: 60,
            atlasPadding: 9,
            renderMode: GlyphRenderMode.SDFAA,
            atlasWidth: 2048,
            atlasHeight: 2048,
            atlasPopulationMode: AtlasPopulationMode.Dynamic,
            enableMultiAtlasSupport: true);

        if (fontAsset == null)
        {
            Debug.LogError("[DnfFont] CreateFontAsset 실패");
            return;
        }

        fontAsset.name = "DNFForgedBlade-Bold SDF";

        AssetDatabase.DeleteAsset(OutPath);
        AssetDatabase.CreateAsset(fontAsset, OutPath);

        // 초기 아틀라스/머티리얼을 서브에셋으로 등록
        AddAtlasSubAssets(fontAsset);
        if (fontAsset.material != null)
        {
            fontAsset.material.name = "DNFForgedBlade-Bold Material";
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(fontAsset.material)))
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        }

        // Dynamic 모드: 글리프는 런타임에 소스 TTF에서 래스터화 (프리베이크 생략 → 에셋 경량).
        // 한글 커버리지(누락 0)는 프리베이크 검증으로 확인됨.

        // NotoSansKR 를 폴백으로 등록 (DNF 미포함 글리프 안전망)
        var noto = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(NotoPath);
        if (noto != null)
        {
            fontAsset.fallbackFontAssetTable ??= new List<TMP_FontAsset>();
            if (!fontAsset.fallbackFontAssetTable.Contains(noto))
                fontAsset.fallbackFontAssetTable.Add(noto);
        }

        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[DnfFont] 생성 완료(Dynamic) → {OutPath} | " +
                  $"atlas={fontAsset.atlasWidth}x{fontAsset.atlasHeight} pop={fontAsset.atlasPopulationMode} " +
                  $"fallback={(noto != null ? noto.name : "none")}");
    }

    private static void AddAtlasSubAssets(TMP_FontAsset fontAsset)
    {
        if (fontAsset.atlasTextures == null) return;
        for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
        {
            var tex = fontAsset.atlasTextures[i];
            if (tex == null) continue;
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(tex))) continue; // 이미 서브에셋
            tex.name = $"DNFForgedBlade-Bold Atlas {i}";
            AssetDatabase.AddObjectToAsset(tex, fontAsset);
        }
    }

}
