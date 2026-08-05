#if UNITY_EDITOR
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
///      Bloom / Vignette / ColorAdjustments / LiftGammaGain / Tonemapping(Neutral) / MotionBlur(비활성)
///      ※ 이 프로파일은 전 챕터 공통 톤이다. 챕터별 분위기는 씬 라이팅(Directional/Ambient/Fog)과
///        BlockPalette 의 방 조명 무드가 담당하며, 여기서 챕터를 나누지 않는다.
///      ※ 서드파티 오버라이드(ButoVolumetricFog 등)는 재실행해도 보존된다.
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

    [MenuItem("RelicFairy/Rendering/Apply Cinematic Look")]
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
            $"  · Profile: {ProfilePath} (툴 소유 오버라이드 6종 재작성, 그 외는 보존)\n" +
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
        // 이 툴이 소유하는 오버라이드만 제거하고 다시 만든다.
        // 전체 Clear 를 하지 않는 이유: 프로파일에는 툴이 모르는 서드파티 오버라이드
        // (ButoVolumetricFog 등)가 함께 들어 있고, 예전 구현은 재실행 시 그것까지 지웠다.
        RemoveIfPresent<Bloom>(profile);
        RemoveIfPresent<Vignette>(profile);
        RemoveIfPresent<ColorAdjustments>(profile);
        RemoveIfPresent<LiftGammaGain>(profile);
        RemoveIfPresent<Tonemapping>(profile);
        RemoveIfPresent<MotionBlur>(profile);

        // ── 아래 값은 GameVolumeProfile.asset 의 현재 저작값과 일치해야 한다 ──
        // 이 메뉴는 "초기 생성용"이지 "재적용용"이 아니다. 값이 어긋나 있으면 한 번 눌렀을 때
        // 색보정 리서치 결론(ACES→Neutral 전환, 노출/채도 재조정)이 통째로 되돌아간다.

        // Bloom
        var bloom = AddOverride<Bloom>(profile);
        bloom.threshold.overrideState = true; bloom.threshold.value = 0.9f;
        bloom.intensity.overrideState = true; bloom.intensity.value = 0.62f;
        bloom.scatter.overrideState   = true; bloom.scatter.value   = 0.75f;
        bloom.tint.overrideState      = true; bloom.tint.value      = new Color(1f, 0.96f, 0.9f, 1f);
        bloom.highQualityFiltering.overrideState = true; bloom.highQualityFiltering.value = true;
        bloom.maxIterations.overrideState = true; bloom.maxIterations.value = 6;

        // Vignette
        var vign = AddOverride<Vignette>(profile);
        vign.intensity.overrideState  = true; vign.intensity.value  = 0.33f;
        vign.smoothness.overrideState = true; vign.smoothness.value = 0.4f;

        // Color Adjustments — 웜 캐스트 유지, 노출/채도는 툰 기준으로 낮게
        var ca = AddOverride<ColorAdjustments>(profile);
        ca.postExposure.overrideState = true; ca.postExposure.value = 0.4f;
        ca.contrast.overrideState     = true; ca.contrast.value     = 10f;
        ca.saturation.overrideState   = true; ca.saturation.value   = 5f;
        ca.colorFilter.overrideState  = true; ca.colorFilter.value  = new Color(1f, 0.97f, 0.93f, 1f);

        // Lift Gamma Gain — 쿨 그림자 + 웜 하이라이트 (split toning)
        var lgg = AddOverride<LiftGammaGain>(profile);
        lgg.lift.overrideState  = true; lgg.lift.value  = new Vector4(0.96039605f, 0.96039605f, 1f, 0.02f);
        lgg.gamma.overrideState = true; lgg.gamma.value = new Vector4(1f, 1f, 1f, 0.05f);
        lgg.gain.overrideState  = true; lgg.gain.value  = new Vector4(1f, 0.9809524f, 0.9238096f, 0.05f);

        // Tonemapping — Neutral.
        // ACES 는 채널별 시그모이드로 밝은 색을 흰색화하고 채도를 파괴해 툰 플랫컬러와 충돌한다.
        // 스타일라이즈드/툰에서 ACES 회피는 업계 합의이며, 이 프로젝트도 그 결론으로 전환했다.
        var tm = AddOverride<Tonemapping>(profile);
        tm.mode.overrideState = true; tm.mode.value = TonemappingMode.Neutral;

        // Motion Blur — 비활성 상태로 보유.
        // VolumePulseService 가 피격 시 별도 Volume(priority 10)으로 펄스를 주므로 기본은 꺼둔다.
        var mb = AddOverride<MotionBlur>(profile);
        mb.active = false;
        mb.quality.overrideState   = true; mb.quality.value   = MotionBlurQuality.High;
        mb.intensity.overrideState = true; mb.intensity.value = 0.2f;
        mb.clamp.overrideState     = true; mb.clamp.value     = 0.05f;
    }

    private static void RemoveIfPresent<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet<T>(out var comp) || comp == null) return;
        profile.Remove<T>();
        Object.DestroyImmediate(comp, true);
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
    [MenuItem("RelicFairy/Rendering/Add SSAO to All URP Renderers")]
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
