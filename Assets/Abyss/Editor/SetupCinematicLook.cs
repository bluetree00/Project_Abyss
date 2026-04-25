#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// "게임다운" 룩을 한 번에 적용하는 에디터 유틸.
///
/// 메뉴: Abyss/Setup/Apply Cinematic Look (Current Scene)
///
/// 하는 일
///   1) Assets/Abyss/Settings/GameVolumeProfile.asset 생성(없으면) + 오버라이드 일괄 세팅
///      Bloom / Vignette / ColorAdjustments / ChromaticAberration / Tonemapping(ACES) / FilmGrain
///   2) 현재 씬에 @GlobalVolume 오브젝트 없으면 생성하여 Global Volume 으로 프로파일 연결
///   3) 씬 내 모든 Camera의 UniversalAdditionalCameraData.renderPostProcessing = true
///
/// 하지 않는 일 (Renderer Data 조작은 위험하므로 수동)
///   · SSAO Renderer Feature 추가 → URP Renderer Data 선택 → Add Renderer Feature → Screen Space Ambient Occlusion
/// </summary>
public static class SetupCinematicLook
{
    private const string ProfileFolder = "Assets/Abyss/Settings";
    private const string ProfilePath   = ProfileFolder + "/GameVolumeProfile.asset";
    private const string VolumeName    = "@GlobalVolume";

    [MenuItem("Abyss/Setup/Apply Cinematic Look (Current Scene)")]
    public static void Apply()
    {
        var profile = LoadOrCreateProfile();
        ClearAndPopulate(profile);
        EditorUtility.SetDirty(profile);

        var volumeGO = EnsureGlobalVolumeInScene(profile);
        int camCount = EnablePostProcessingOnCameras();
        int lightCount = TuneDirectionalLights();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log(
            $"[SetupCinematicLook] 적용 완료\n" +
            $"  · Profile: {ProfilePath} (오버라이드 6종)\n" +
            $"  · Volume Object: {volumeGO.name} (씬={volumeGO.scene.name})\n" +
            $"  · Post-Processing 활성화된 Camera: {camCount}개\n" +
            $"  · Directional Light 튜닝: {lightCount}개\n" +
            $"  · SSAO 추가는 메뉴 'Abyss/Setup/Add SSAO to All URP Renderers' 참고");
    }

    // ─────────────────────────────────────────────
    // 4. Directional Light tuning
    // ─────────────────────────────────────────────

    private static int TuneDirectionalLights()
    {
        int count = 0;
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (light.type != LightType.Directional) continue;

            light.intensity     = 0.9f;
            light.color         = new Color(1f, 0.96f, 0.88f, 1f); // 살짝 웜
            light.shadows       = LightShadows.Soft;
            light.shadowStrength= 0.75f;
            light.shadowBias    = 0.05f;
            light.shadowNormalBias = 0.4f;
            EditorUtility.SetDirty(light);
            count++;
        }
        return count;
    }

    // ─────────────────────────────────────────────
    // 1. Profile
    // ─────────────────────────────────────────────

    private static VolumeProfile LoadOrCreateProfile()
    {
        var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (existing != null) return existing;

        EnsureFolder(ProfileFolder);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, ProfilePath);
        return profile;
    }

    private static void ClearAndPopulate(VolumeProfile profile)
    {
        // 기존 오버라이드 제거 (재실행 안전성)
        var existing = new List<VolumeComponent>(profile.components);
        foreach (var comp in existing)
        {
            profile.Remove(comp.GetType());
            Object.DestroyImmediate(comp, true);
        }

        // Bloom — 이펙트·아이템·Emission 빛남
        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.overrideState = true; bloom.threshold.value = 1.05f;
        bloom.intensity.overrideState = true; bloom.intensity.value = 0.55f;
        bloom.scatter.overrideState   = true; bloom.scatter.value   = 0.72f;
        bloom.tint.overrideState      = true; bloom.tint.value      = Color.white;

        // Vignette — 화면 모서리 어둡게 (시선 집중)
        var vign = profile.Add<Vignette>(true);
        vign.intensity.overrideState  = true; vign.intensity.value  = 0.32f;
        vign.smoothness.overrideState = true; vign.smoothness.value = 0.45f;
        vign.color.overrideState      = true; vign.color.value      = new Color(0.03f, 0.02f, 0.05f, 1f);

        // Color Adjustments — 톤 통일 (대비·채도 살짝 강화)
        var ca = profile.Add<ColorAdjustments>(true);
        ca.postExposure.overrideState = true; ca.postExposure.value = 0.15f;
        ca.contrast.overrideState     = true; ca.contrast.value     = 10f;
        ca.saturation.overrideState   = true; ca.saturation.value   = 10f;
        ca.colorFilter.overrideState  = true; ca.colorFilter.value  = new Color(1f, 0.98f, 0.94f, 1f); // 살짝 웜

        // Chromatic Aberration — 아주 약하게 (스타일)
        var chrom = profile.Add<ChromaticAberration>(true);
        chrom.intensity.overrideState = true; chrom.intensity.value = 0.12f;

        // Tonemapping — ACES로 시네마틱 필름룩
        var tm = profile.Add<Tonemapping>(true);
        tm.mode.overrideState = true; tm.mode.value = TonemappingMode.ACES;

        // Film Grain — 아주 약하게 (질감)
        var fg = profile.Add<FilmGrain>(true);
        fg.type.overrideState      = true; fg.type.value      = FilmGrainLookup.Thin1;
        fg.intensity.overrideState = true; fg.intensity.value = 0.18f;
        fg.response.overrideState  = true; fg.response.value  = 0.85f;
    }

    // ─────────────────────────────────────────────
    // 2. Scene Volume
    // ─────────────────────────────────────────────

    private static GameObject EnsureGlobalVolumeInScene(VolumeProfile profile)
    {
        GameObject go = GameObject.Find(VolumeName);
        if (go == null)
        {
            go = new GameObject(VolumeName);
            EditorSceneManager.MoveGameObjectToScene(go, EditorSceneManager.GetActiveScene());
        }

        var volume = go.GetComponent<Volume>();
        if (volume == null) volume = go.AddComponent<Volume>();
        volume.isGlobal  = true;
        volume.priority  = 0f;
        volume.weight    = 1f;
        volume.profile   = profile;
        EditorUtility.SetDirty(go);

        return go;
    }

    // ─────────────────────────────────────────────
    // 3. Camera PP
    // ─────────────────────────────────────────────

    private static int EnablePostProcessingOnCameras()
    {
        int count = 0;
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null) continue;
            if (!data.renderPostProcessing)
            {
                data.renderPostProcessing = true;
                EditorUtility.SetDirty(cam);
            }
            count++;
        }
        return count;
    }

    // ─────────────────────────────────────────────
    // 5. SSAO Renderer Feature
    // ─────────────────────────────────────────────

    /// <summary>프로젝트 내 모든 UniversalRendererData 에셋에 Screen Space Ambient Occlusion Feature를 추가.
    /// 이미 있으면 건너뜀. Feature는 sub-asset으로 renderer data에 바인딩됨.</summary>
    [MenuItem("Abyss/Setup/Add SSAO to All URP Renderers")]
    public static void AddSSAOToAllRenderers()
    {
        var ssaoType = System.Type.GetType(
            "UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion, Unity.RenderPipelines.Universal.Runtime");
        if (ssaoType == null)
        {
            Debug.LogError("[SetupCinematicLook] ScreenSpaceAmbientOcclusion 타입을 찾지 못했습니다. URP 패키지 버전 확인 필요.");
            return;
        }

        var guids = AssetDatabase.FindAssets("t:UniversalRendererData");
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning("[SetupCinematicLook] UniversalRendererData 에셋이 없습니다.");
            return;
        }

        int added = 0, skipped = 0;
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data == null) continue;

            bool hasSsao = false;
            foreach (var f in data.rendererFeatures)
            {
                if (f != null && f.GetType() == ssaoType) { hasSsao = true; break; }
            }
            if (hasSsao) { skipped++; continue; }

            var feature = ScriptableObject.CreateInstance(ssaoType) as ScriptableRendererFeature;
            if (feature == null) continue;

            feature.name = "ScreenSpaceAmbientOcclusion";
            data.rendererFeatures.Add(feature);
            AssetDatabase.AddObjectToAsset(feature, data);

            EditorUtility.SetDirty(data);
            added++;
            Debug.Log($"[SetupCinematicLook] SSAO 추가: {path}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guids[0])); // 메타 리임포트 트리거
        AssetDatabase.Refresh();

        Debug.Log($"[SetupCinematicLook] SSAO 추가 완료 — 신규 {added}개 / 스킵 {skipped}개. " +
                  "각 Renderer Data Inspector에서 Intensity/Radius/DirectLightingStrength 등 세부값을 튜닝하세요.");
    }

    // ─────────────────────────────────────────────
    // Utility
    // ─────────────────────────────────────────────

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        var leaf   = System.IO.Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
#endif
