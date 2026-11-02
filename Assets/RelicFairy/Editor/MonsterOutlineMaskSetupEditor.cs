#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 적 외곽선 「바깥 테두리만」 구성(10-30 사용자 「보스 테두리가 아직 두껍다」 — 원인은 인버티드 헐이 겹친 부위 · 조각마다 긋는 안쪽 선).
/// ① 재질 Mat_MonsterOutlineMask(셰이더 RelicFairy/MonsterOutlineMask — 몸 픽셀에 스텐실 비트 128) 만들기
/// ② URP 렌더러 셋(Performant · Balanced · HighFidelity)에 Render Objects 피처 「MonsterOutlineMask」를 MonsterRim 바로 앞에 넣기
///    (같은 이벤트 AfterRenderingOpaques — 목록 순서대로 돈다 · LayerMask=Monster · Opaque 큐) — 이미 있으면 건너뛴다
/// ③ 셰이더를 Always Included에 넣기(빌드에서 빠지지 않게 — 외곽선 두 셰이더와 같이)
/// 메뉴: RelicFairy/Graphics/10-30 몬스터 외곽선 바깥 테두리만 구성 (Edit)
/// </summary>
public static class MonsterOutlineMaskSetupEditor
{
    private const string ShaderPath   = "Assets/RelicFairy/Art/Shaders/MonsterOutlineMask.shader";
    private const string MaterialPath = "Assets/RelicFairy/Art/Materials/Mat_MonsterOutlineMask.mat";
    private const string FeatureName  = "MonsterOutlineMask";
    private const string RimName      = "MonsterRim";
    private static readonly string[] Renderers =
    {
        "Assets/Settings/URP-Performant-Renderer.asset",
        "Assets/Settings/URP-Balanced-Renderer.asset",
        "Assets/Settings/URP-HighFidelity-Renderer.asset",
    };

    [MenuItem("RelicFairy/Graphics/10-30 몬스터 외곽선 바깥 테두리만 구성 (Edit)")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[OutlineMask] 편집 모드에서"); return; }
        var log = new System.Text.StringBuilder("[OutlineMask] 구성\n");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null) { Debug.LogError("[OutlineMask] 셰이더 없음 " + ShaderPath); return; }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            mat = new Material(shader) { name = "Mat_MonsterOutlineMask" };
            AssetDatabase.CreateAsset(mat, MaterialPath);
            log.AppendLine("  재질 만듦 " + MaterialPath);
        }

        int layer = LayerMask.NameToLayer("Monster");
        if (layer < 0) { Debug.LogError("[OutlineMask] Monster 레이어 없음"); return; }
        var mapField = typeof(ScriptableRendererData).GetField("m_RendererFeatureMap", BindingFlags.Instance | BindingFlags.NonPublic);

        foreach (var path in Renderers)
        {
            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            if (data == null) { log.AppendLine("  ✗ 렌더러 없음 " + path); continue; }
            var feats = data.rendererFeatures;
            if (feats.Exists(f => f != null && f.name == FeatureName)) { log.AppendLine($"  이미 있음 {System.IO.Path.GetFileName(path)}"); continue; }
            int rim = feats.FindIndex(f => f != null && f.name == RimName);
            if (rim < 0) { log.AppendLine($"  ✗ {RimName} 없음 {System.IO.Path.GetFileName(path)}"); continue; }

            var ro = ScriptableObject.CreateInstance<RenderObjects>();
            ro.name = FeatureName;
            ro.settings.passTag = "RenderObjectsFeature";
            ro.settings.Event = RenderPassEvent.AfterRenderingOpaques;
            ro.settings.filterSettings.RenderQueueType = RenderQueueType.Opaque;
            ro.settings.filterSettings.LayerMask = 1 << layer;
            ro.settings.overrideMode = RenderObjects.RenderObjectsSettings.OverrideMaterialMode.Material;
            ro.settings.overrideMaterial = mat;
            ro.settings.overrideMaterialPassIndex = 0;
            AssetDatabase.AddObjectToAsset(ro, data);

            feats.Insert(rim, ro);
            var map = mapField?.GetValue(data) as List<long>;
            if (map != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ro, out string _, out long id)) map.Insert(rim, id);
            ro.Create();
            data.SetDirty();
            EditorUtility.SetDirty(data);
            log.AppendLine($"  넣음 {System.IO.Path.GetFileName(path)} — {RimName} 앞(칸 {rim}) · 피처 {feats.Count}개 · 지도 {(map != null ? map.Count : -1)}");
        }

        // Always Included
        var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (gs != null && gs.Length > 0)
        {
            var so = new SerializedObject(gs[0]);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            bool has = false;
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) has = true;
            if (!has)
            {
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = shader;
                so.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine("  Always Included에 넣음");
            }
            else log.AppendLine("  Always Included 이미 있음");
        }

        AssetDatabase.SaveAssets();
        Debug.Log(log.ToString());
    }
}
#endif
