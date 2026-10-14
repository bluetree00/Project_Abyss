#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-11 런 결과 창 Light(300) 글자가 에디터에서 안 보이던 것(10-08부터) — Noto 동적 글꼴의 글리프 표와 아틀라스 그림이 어긋났다.
/// 실측(10-11 상점·모루·결과·메뉴 촬영): Light 글자 「+128」 「214」 「38」 밝은 픽셀 0 · 같은 화면 Regular는 정상.
/// 동적 글꼴(원본 TTF 연결 · ClearDynamicDataOnBuild 켜짐)이라 동적 데이터를 비우면 다음 사용 때 원본에서 다시 굽는다(빌드는 원래 빌드 때 비운다).
/// 글리프마다 가리키는 아틀라스 번호가 실제 아틀라스 수를 넘거나, 글리프 사각이 비어 있으면 「어긋남」으로 센다.
/// 결과: Temp/noto_font_reset.txt · 로그 「[NotoReset] 끝」.
/// </summary>
public static class NotoDynamicFontResetEditor
{
    private static readonly string[] Paths =
    {
        "Assets/RelicFairy/Fonts/NotoSansKR-Light SDF.asset",
        "Assets/RelicFairy/Fonts/NotoSansKR-Regular SDF.asset",
        "Assets/RelicFairy/Fonts/NotoSansKR-Medium SDF.asset",
    };

    [MenuItem("RelicFairy/Debug/10-11 Noto 동적 글꼴 점검 · Light 비우기 (편집 모드)")]
    private static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[NotoReset] 편집 모드에서"); return; }
        var sb = new StringBuilder("Noto 동적 글꼴 점검\n");
        foreach (var path in Paths)
        {
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (fa == null) { sb.AppendLine($"  {path} — 없음"); continue; }
            int atlases = fa.atlasTextures != null ? fa.atlasTextures.Count(t => t != null) : 0;
            int badIndex = fa.glyphTable.Count(g => g.atlasIndex >= atlases);
            int emptyRect = fa.glyphTable.Count(g => g.glyphRect.width == 0);
            sb.AppendLine($"  {Path.GetFileNameWithoutExtension(path)} · 방식 {fa.atlasPopulationMode} · 원본 {(fa.sourceFontFile != null ? "있음" : "없음")} · 아틀라스 {atlases} · 글리프 {fa.glyphTable.Count} · 문자 {fa.characterTable.Count} · 번호 어긋남 {badIndex} · 빈 사각(공백 포함) {emptyRect}");

            // Light만 비운다 — 같은 화면의 Regular · Medium은 정상으로 그려졌다(필요하면 목록을 늘린다)
            bool target = path.Contains("Light");
            if (!target) continue;
            if (fa.atlasPopulationMode != AtlasPopulationMode.Dynamic || fa.sourceFontFile == null)
            {
                sb.AppendLine("    동적 · 원본 연결이 아니라 비우지 않는다");
                continue;
            }
            fa.ClearFontAssetData(true);
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            int after = fa.atlasTextures != null ? fa.atlasTextures.Count(t => t != null) : 0;
            sb.AppendLine($"    → 동적 데이터 비움 · 글리프 {fa.glyphTable.Count} · 아틀라스 {after}(다음 사용 때 다시 굽는다)");
        }
        File.WriteAllText("Temp/noto_font_reset.txt", sb.ToString());
        Debug.Log("[NotoReset] 끝\n" + sb);
    }
}
#endif
