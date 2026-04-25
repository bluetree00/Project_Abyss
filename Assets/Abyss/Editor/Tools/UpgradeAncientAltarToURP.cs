#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// _AncientAltar 서드파티 팩은 Built-In RP의 Standard 셰이더를 쓴다.
/// 현재 프로젝트는 URP이므로 그대로 사용하면 분홍색(magenta)으로 렌더되는 문제.
/// 이 유틸은 _AncientAltar/Materials/ 폴더의 Standard 머티리얼을 URP/Lit로 일괄 업그레이드하고
/// 주요 프로퍼티(MainTex → BaseMap, Color → BaseColor, BumpMap, MetallicGlossMap, Occlusion)를 매핑한다.
///
/// 메뉴: Abyss/Setup/Upgrade Ancient Altar Materials to URP
/// </summary>
public static class UpgradeAncientAltarToURP
{
    private const string TargetFolder = "Assets/_ThirdParty/_AncientAltar/Materials";

    [MenuItem("Abyss/Setup/Upgrade Ancient Altar Materials to URP")]
    public static void Execute()
    {
        var urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            Debug.LogError("[UpgradeAncientAltar] URP/Lit 셰이더를 찾지 못했습니다. URP 패키지가 설치되어 있어야 합니다.");
            return;
        }

        var guids = AssetDatabase.FindAssets("t:Material", new[] { TargetFolder });
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning($"[UpgradeAncientAltar] 대상 폴더에 머티리얼 없음: {TargetFolder}");
            return;
        }

        int converted = 0, skipped = 0, failed = 0;

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) { failed++; continue; }
            if (!IsUpgradeableShader(mat.shader.name))
            {
                Debug.Log($"[UpgradeAncientAltar] skip: {mat.name} shader='{mat.shader.name}'");
                skipped++;
                continue;
            }

            TryUpgradeStandardToUrp(mat, urpLit);
            EditorUtility.SetDirty(mat);
            converted++;
            Debug.Log($"[UpgradeAncientAltar] converted: {mat.name}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[UpgradeAncientAltar] 완료 — 변환 {converted}개 / 스킵 {skipped}개 (Standard 아님) / 실패 {failed}개");
    }

    /// <summary>Standard + ASE/ASE_Standart* 계열은 MainTex/Color/BumpMap 프로퍼티가 거의 동일하므로 일괄 처리.</summary>
    private static bool IsUpgradeableShader(string shaderName)
    {
        if (string.IsNullOrEmpty(shaderName)) return false;
        if (shaderName == "Standard") return true;
        if (shaderName.StartsWith("ASE/ASE_Standart")) return true; // Standart(불완전 철자), StandartCutout, StandartVertex 등
        return false;
    }

    private static void TryUpgradeStandardToUrp(Material mat, Shader urpLit)
    {
        // Standard → URP Lit 프로퍼티 매핑
        var baseColor  = mat.HasProperty("_Color")             ? mat.GetColor("_Color")             : Color.white;
        var mainTex    = mat.HasProperty("_MainTex")           ? mat.GetTexture("_MainTex")         : null;
        var texScale   = mat.HasProperty("_MainTex")           ? mat.GetTextureScale("_MainTex")    : Vector2.one;
        var texOffset  = mat.HasProperty("_MainTex")           ? mat.GetTextureOffset("_MainTex")   : Vector2.zero;
        var bumpMap    = mat.HasProperty("_BumpMap")           ? mat.GetTexture("_BumpMap")         : null;
        var bumpScale  = mat.HasProperty("_BumpScale")         ? mat.GetFloat("_BumpScale")         : 1f;
        var metalMap   = mat.HasProperty("_MetallicGlossMap")  ? mat.GetTexture("_MetallicGlossMap"): null;
        var metallic   = mat.HasProperty("_Metallic")          ? mat.GetFloat("_Metallic")          : 0f;
        var glossiness = mat.HasProperty("_Glossiness")        ? mat.GetFloat("_Glossiness")        : 0.3f;
        var occMap     = mat.HasProperty("_OcclusionMap")      ? mat.GetTexture("_OcclusionMap")    : null;
        var occStr     = mat.HasProperty("_OcclusionStrength") ? mat.GetFloat("_OcclusionStrength") : 1f;
        var emitMap    = mat.HasProperty("_EmissionMap")       ? mat.GetTexture("_EmissionMap")     : null;
        var emitColor  = mat.HasProperty("_EmissionColor")     ? mat.GetColor("_EmissionColor")     : Color.black;

        mat.shader = urpLit;

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
        if (mat.HasProperty("_BaseMap") && mainTex != null)
        {
            mat.SetTexture("_BaseMap", mainTex);
            mat.SetTextureScale("_BaseMap", texScale);
            mat.SetTextureOffset("_BaseMap", texOffset);
        }
        if (mat.HasProperty("_BumpMap") && bumpMap != null) mat.SetTexture("_BumpMap", bumpMap);
        if (mat.HasProperty("_BumpScale"))                   mat.SetFloat("_BumpScale", bumpScale);
        if (mat.HasProperty("_MetallicGlossMap") && metalMap != null) mat.SetTexture("_MetallicGlossMap", metalMap);
        if (mat.HasProperty("_Metallic"))                    mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Smoothness"))                  mat.SetFloat("_Smoothness", glossiness);
        if (mat.HasProperty("_OcclusionMap") && occMap != null) mat.SetTexture("_OcclusionMap", occMap);
        if (mat.HasProperty("_OcclusionStrength"))           mat.SetFloat("_OcclusionStrength", occStr);
        if (mat.HasProperty("_EmissionMap") && emitMap != null) mat.SetTexture("_EmissionMap", emitMap);
        if (mat.HasProperty("_EmissionColor"))               mat.SetColor("_EmissionColor", emitColor);
    }
}
#endif
