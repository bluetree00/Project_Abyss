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
/// 메뉴: RelicFairy/Setup/Apply Cinematic Look (Current Scene)
///
/// 하는 일
///   1) Assets/RelicFairy/Settings/GameVolumeProfile.asset 생성(없으면) + 오버라이드 일괄 세팅
///      Bloom / Vignette / ColorAdjustments / LiftGammaGain / Tonemapping(ACES)
///      ※ Azure Nature 의 AN_PostProcessing_Volume 구성을 기반으로 강도 톤다운
///      ※ MotionBlur 는 제외 (VolumePulseService 의 피격 펄스와 충돌)
///   2) 씬 내 모든 Camera의 UniversalAdditionalCameraData.renderPostProcessing = true
///
/// 하지 않는 일 (씬 디자인을 침해하므로 수동)
///   · @GlobalVolume 오브젝트 생성 → Volume은 씬마다 직접 배치하여 프로파일 연결
///   · Directional Light 강도/색/그림자 일괄 튜닝 → 라이트 디자인은 씬마다 수동
///   · SSAO Renderer Feature 추가 → 메뉴 'RelicFairy/Setup/Add SSAO to All URP Renderers' 사용
/// </summary>
public static class SetupCinematicLook
{
    private const string ProfileFolder = "Assets/RelicFairy/Settings";
    private const string ProfilePath   = ProfileFolder + "/GameVolumeProfile.asset";

    [MenuItem("RelicFairy/Setup/Apply Cinematic Look (Current Scene)")]
    public static void Apply()
    {
        var profile = LoadOrCreateProfile();
        ClearAndPopulate(profile);
        EditorUtility.SetDirty(profile);

        int camCount = EnablePostProcessingOnCameras();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log(
            $"[SetupCinematicLook] 적용 완료\n" +
            $"  · Profile: {ProfilePath} (오버라이드 5종, AN 기반 톤다운)\n" +
            $"  · Post-Processing 활성화된 Camera: {camCount}개\n" +
            $"  · Volume 배치는 씬에 직접 (Volume 컴포넌트 + sharedProfile = GameVolumeProfile)\n" +
            $"  · SSAO 추가는 메뉴 'RelicFairy/Setup/Add SSAO to All URP Renderers' 참고");
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
            if (comp == null) continue;
            profile.Remove(comp.GetType());
            Object.DestroyImmediate(comp, true);
        }
        profile.components.Clear();

        // Bloom — AN 기반 + 뿌연 느낌 줄임 (threshold ↑, intensity ↓)
        var bloom = AddOverride<Bloom>(profile);
        bloom.threshold.overrideState = true; bloom.threshold.value = 0.7f;
        bloom.intensity.overrideState = true; bloom.intensity.value = 0.25f;
        bloom.scatter.overrideState   = true; bloom.scatter.value   = 0.6f;
        bloom.tint.overrideState      = true; bloom.tint.value      = new Color(1f, 0.904989f, 0.8066038f, 1f);

        // Vignette — AN 원본 값 그대로
        var vign = AddOverride<Vignette>(profile);
        vign.intensity.overrideState  = true; vign.intensity.value  = 0.35f;
        vign.smoothness.overrideState = true; vign.smoothness.value = 0.35f;

        // Color Adjustments — AN 원본 + colorFilter 살짝 웜으로 따뜻함 보강
        var ca = AddOverride<ColorAdjustments>(profile);
        ca.postExposure.overrideState = true; ca.postExposure.value = 0.8f;
        ca.contrast.overrideState     = true; ca.contrast.value     = 10f;
        ca.saturation.overrideState   = true; ca.saturation.value   = 2f;
        ca.colorFilter.overrideState  = true; ca.colorFilter.value  = new Color(1f, 0.98f, 0.94f, 1f); // 살짝 웜 캐스트

        // Lift Gamma Gain — AN 원본 값 그대로 (그림자 따뜻한 시그니처)
        var lgg = AddOverride<LiftGammaGain>(profile);
        lgg.lift.overrideState = true; lgg.lift.value = new Vector4(1f, 0.9612974f, 0.9404001f, 0.01986097f);

        // Tonemapping — ACES (AN 그대로)
        var tm = AddOverride<Tonemapping>(profile);
        tm.mode.overrideState = true; tm.mode.value = TonemappingMode.ACES;

        // Motion Blur — AN 0.5 → 0.2 톤다운 (뿌연 느낌 줄임)
        // 주의: VolumePulseService 가 피격 시 별도 Volume(priority 10)으로 MotionBlur 펄스 적용.
        var mb = AddOverride<MotionBlur>(profile);
        mb.quality.overrideState   = true; mb.quality.value   = MotionBlurQuality.High;
        mb.intensity.overrideState = true; mb.intensity.value = 0.2f;
        mb.clamp.overrideState     = true; mb.clamp.value     = 0.05f;
    }

    // VolumeProfile.Add<T>() 만으로는 sub-asset 등록이 안 되어 직렬화 시 fileID:0 깨짐 발생.
    // 명시적으로 AddObjectToAsset 호출하여 프로파일 자산의 하위 자산으로 묶는다.
    private static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
    {
        var comp = profile.Add<T>(true);
        comp.hideFlags = HideFlags.HideInHierarchy;
        if (AssetDatabase.Contains(profile) && !AssetDatabase.Contains(comp))
        {
            AssetDatabase.AddObjectToAsset(comp, profile);
        }
        return comp;
    }

    // ─────────────────────────────────────────────
    // 2. Camera PP
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
    // 3. SSAO Renderer Feature
    // ─────────────────────────────────────────────

    /// <summary>프로젝트 내 모든 UniversalRendererData 에셋에 Screen Space Ambient Occlusion Feature를 추가.
    /// 이미 있으면 건너뜀. Feature는 sub-asset으로 renderer data에 바인딩됨.</summary>
    [MenuItem("RelicFairy/Setup/Add SSAO to All URP Renderers")]
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
