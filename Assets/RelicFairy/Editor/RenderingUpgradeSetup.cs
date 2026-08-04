#if UNITY_EDITOR
using System.IO;
using System.Linq;
using OccaSoftware.Buto.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 렌더링 품질 배선 도구. BaseCamp 전용인 <see cref="BaseCampQualitySetup"/>와 달리
/// 프로젝트 전역(렌더러 에셋·공용 프리팹)에 적용되는 것만 여기 둔다.
///
/// 배경: GameVolumeProfile에 <c>ButoVolumetricFog</c> 오버라이드가 값까지 잡혀 있는데
/// 정작 <c>ButoRenderFeature</c>가 세 렌더러 어디에도 등록돼 있지 않았다. 페이처가 없으면
/// 볼륨 오버라이드는 읽히지도 않는다 — 지금껏 볼류메트릭이 한 번도 그려진 적이 없다.
/// </summary>
public static class RenderingUpgradeSetup
{
    private static readonly string[] RendererPaths =
    {
        "Assets/Settings/URP-Performant-Renderer.asset",
        "Assets/Settings/URP-Balanced-Renderer.asset",
        "Assets/Settings/URP-HighFidelity-Renderer.asset",
    };

    [MenuItem("RelicFairy/Rendering/1. Buto 렌더러 페이처 등록")]
    public static void RegisterButoFeature()
    {
        int added = 0;
        foreach (string path in RendererPaths)
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data == null) { Debug.LogError($"[Rendering] 렌더러를 못 읽었다: {path}"); continue; }

            if (data.rendererFeatures.Any(f => f is ButoRenderFeature))
            {
                Debug.Log($"[Rendering] 이미 등록됨 — {Path.GetFileName(path)}");
                continue;
            }

            var feature = ScriptableObject.CreateInstance<ButoRenderFeature>();
            feature.name = "ButoRenderFeature";
            data.rendererFeatures.Add(feature);
            AssetDatabase.AddObjectToAsset(feature, data);
            EditorUtility.SetDirty(data);
            added++;
            Debug.Log($"[Rendering] Buto 페이처 추가 → {Path.GetFileName(path)}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Rendering] Buto 등록 완료 — {added}개 렌더러 갱신");
    }

    // 볼륨 세기 배율. 1이면 Light 세기를 그대로 쓴다. 절제된 공기감이 목표라 낮춘다.
    private const float VolumetricIntensityMul = 0.6f;

    // 챕터 방은 MapBuilder가 런타임에 세우므로 라이트맵도 APV도 못 쓴다.
    // 그런데 6개 팔레트가 전부 같은 조명 프리팹 두 개(WallTorch·CenterLight)를 참조한다 —
    // 여기에 ButoLight를 달면 절차 생성되는 모든 방이 볼륨 조명을 갖는다.
    // inheritDataFromLightComponent를 켜두는 것이 핵심이다. 팔레트의 LightTint가 Light의
    // color/intensity/range를 덮어쓰므로, 볼륨 색도 챕터 지배색을 자동으로 따라간다.
    private static readonly string[] RoomLightPrefabs =
    {
        "Assets/RelicFairy/Prefabs/Stage/WallTorch.prefab",
        "Assets/RelicFairy/Prefabs/Stage/CenterLight.prefab",
    };

    [MenuItem("RelicFairy/Rendering/3. 방 조명 프리팹에 ButoLight 부착")]
    public static void AttachButoLightToRoomPrefabs()
    {
        foreach (string path in RoomLightPrefabs)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { Debug.LogError($"[Rendering] 프리팹을 못 열었다: {path}"); continue; }

            try
            {
                var light = root.GetComponentInChildren<Light>(true);   // WallTorch는 Light가 자식에 있다
                if (light == null) { Debug.LogError($"[Rendering] Light 없음: {path}"); continue; }

                if (!light.TryGetComponent<ButoLight>(out var buto))
                    buto = light.gameObject.AddComponent<ButoLight>();

                var so = new SerializedObject(buto);
                so.FindProperty("inheritDataFromLightComponent").boolValue = true;
                so.FindProperty("lightComponent").objectReferenceValue = light;
                so.FindProperty("IntensityMultiplier").floatValue = VolumetricIntensityMul;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[Rendering] ButoLight 부착 → {Path.GetFileName(path)} " +
                          $"('{light.gameObject.name}', 세기×{VolumetricIntensityMul})");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
    }

    // 열려 있는 씬(BaseCamp 등 저작 씬)의 주요 광원에만 ButoLight를 단다.
    //
    // 처음에 1.5로 잡았는데 이 씬 광원의 실제 세기가 15~157이라 63개 중 55개가 통과했다.
    // 그 결과 안개가 55개 광원에 동시에 밝혀져 우유빛이 되고 우주 배경까지 덮였다.
    // 볼류메트릭은 '주인공 광원' 몇 개만 받아야 빛줄기가 읽힌다 — 전부 켜면 그냥 안개다.
    private const float SceneButoLightMinIntensity = 15f;

    [MenuItem("RelicFairy/Rendering/4. 현재 씬 주요 광원에 ButoLight 부착")]
    public static void AttachButoLightToSceneLights()
    {
        int added = 0, removed = 0, unchanged = 0;
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            bool want = light.intensity >= SceneButoLightMinIntensity;
            bool has  = light.TryGetComponent<ButoLight>(out var existing);

            if (want == has) { unchanged++; continue; }

            if (!want) { Object.DestroyImmediate(existing); removed++; continue; }

            var buto = light.gameObject.AddComponent<ButoLight>();
            var so = new SerializedObject(buto);
            so.FindProperty("inheritDataFromLightComponent").boolValue = true;
            so.FindProperty("lightComponent").objectReferenceValue = light;
            so.FindProperty("IntensityMultiplier").floatValue = VolumetricIntensityMul;
            so.ApplyModifiedPropertiesWithoutUndo();
            added++;
        }
        Debug.Log($"[Rendering] 씬 광원 ButoLight — 부착 {added} / 제거 {removed} / 유지 {unchanged} " +
                  $"(세기 {SceneButoLightMinIntensity} 이상만)");
    }

    // ── 렌더링 경로 ────────────────────────────────────────────────
    // Forward 는 오브젝트당 추가 광원이 8개(Performant 4개)로 제한된다. 초과분은 거리순으로
    // 잘려나가므로, 카메라나 오브젝트가 움직이면 어떤 광원이 잘리는지가 바뀌어 조명이 팝핑한다.
    //
    // 챕터 방의 벽 조명 상한은 팔레트 기준 Castle 24 · Cave 18 · Throne 18 개다. 여기에 중앙 조명과
    // 문 앞 부스트가 더해진다 — Forward 의 8개 제한을 크게 초과한다.
    //
    // Forward+ 는 화면을 타일로 나눠 광원을 클러스터링하므로 오브젝트당 제한이 없다.
    // Unity 6 URP 는 모든 렌더링 경로에서 리플렉션 프로브 블렌딩을 지원하므로 반사 손실도 없다.
    private const int RenderingModeForwardPlus = 2;

    [MenuItem("RelicFairy/Rendering/5. 렌더링 경로 Forward+ 전환")]
    public static void SwitchToForwardPlus()
    {
        foreach (string path in RendererPaths)
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data == null) { Debug.LogError($"[Rendering] 렌더러 없음: {path}"); continue; }

            var so = new SerializedObject(data);
            var prop = so.FindProperty("m_RenderingMode");
            if (prop == null) { Debug.LogError($"[Rendering] m_RenderingMode 없음: {path}"); continue; }

            int before = prop.intValue;
            prop.intValue = RenderingModeForwardPlus;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            Debug.Log($"[Rendering] {Path.GetFileName(path)} 렌더링 경로 {before} → {RenderingModeForwardPlus} (Forward+)");
        }
        AssetDatabase.SaveAssets();
    }

    // ── 그림자 캐스케이드 ──────────────────────────────────────────
    // 3인칭 카메라의 타깃까지 거리를 CinemachineFreeLook 궤도에서 실측했다.
    //   Top    height 8.8 · radius 3.4 → 9.43m
    //   Middle height 7.3 · radius 4.0 → 8.32m
    //   Bottom height 4.8 · radius 3.8 → 6.12m
    //
    // 기존 첫 캐스케이드 경계는 120 × 0.045 = 5.4m 였다. 카메라 거리(6.1~9.4m)보다 가까워서
    // 캐릭터가 최고해상도 캐스케이드에 한 번도 들어가지 못했고, 경계선이 캐릭터 바로 주변을
    // 쓸고 다녔다 — 이동 중 그림자 선명도가 변하는 원인이다.
    //
    // 첫 경계를 15m 로 잡는다. 카메라 최대 거리 9.43m 보다 충분히 멀어 캐릭터와 그 주변 지면이
    // 통째로 캐스케이드 0 에 들어가고, 경계는 중경으로 밀려나 눈에 덜 띈다.
    // 4096 아틀라스를 4분할하면 캐스케이드당 2048 이므로 15m 구간은 136 texel/m — 충분하다.
    private const float FirstCascadeMeters = 15f;

    [MenuItem("RelicFairy/Rendering/6. 캐스케이드 재설계 (카메라 거리 기준)")]
    public static void RetuneShadowCascades()
    {
        // (에셋 경로, 그림자 거리, 캐스케이드 수)
        var targets = new (string path, float distance, int count)[]
        {
            ("Assets/Settings/URP-Performant.asset",   50f, 2),
            ("Assets/Settings/URP-Balanced.asset",    100f, 4),
            ("Assets/Settings/URP-HighFidelity.asset", 120f, 4),
        };

        foreach (var (path, distance, count) in targets)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null) { Debug.LogError($"[Rendering] URP 에셋 없음: {path}"); continue; }

            var so = new SerializedObject(asset);
            float x = FirstCascadeMeters / distance;

            if (count == 2)
            {
                so.FindProperty("m_Cascade2Split").floatValue = x;
                Debug.Log($"[Rendering] {Path.GetFileName(path)} 2캐스케이드 경계 " +
                          $"{FirstCascadeMeters}m (split {x:0.###})");
            }
            else
            {
                // 첫 경계 이후는 실용 분할(로그·균등 절충)에 가깝게 벌린다.
                float y = Mathf.Min(0.95f, x * 2.15f);
                float z = Mathf.Min(0.98f, x * 4.4f);
                so.FindProperty("m_Cascade4Split").vector3Value = new Vector3(x, y, z);
                Debug.Log($"[Rendering] {Path.GetFileName(path)} 4캐스케이드 경계 " +
                          $"{distance * x:0.#} / {distance * y:0.#} / {distance * z:0.#} / {distance}m");
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }
        AssetDatabase.SaveAssets();
    }

    // ── 룩 그레이딩 ────────────────────────────────────────────────
    // 지금 조합이 톤이 평평한 직접적 원인이다.
    //   Tonemapping = Neutral : 범위 리매핑만 하고 색조·채도를 거의 건드리지 않는다.
    //                           "광범위한 그레이딩의 출발점"이지 완성된 룩이 아니다.
    //   ColorAdjustments      : contrast 10 / saturation 5 (±100 스케일) — 사실상 없는 값.
    // 톤매퍼도 색을 만들지 않고 그레이딩도 만들지 않으니 평평하게 나온다.
    //
    // ACES 는 영화용 참조 색공간을 써서 시네마틱한 대비를 만들고, 하이라이트를 부드럽게
    // 롤오프한다. 대비를 올리면서 동시에 "쨍하지 않게" 가 성립하는 이유가 이 롤오프다.
    //
    // 여기에 Shadows/Midtones/Highlights 로 명부와 암부의 색을 갈라놓는다.
    // 암부는 우주 배경의 청보라, 명부는 화톳불의 금색 — 보색 대비가 화면에 깊이를 만든다.
    // 밝기를 올려서 좋아 보이게 하는 것과는 다른 접근이다.
    private const string GameProfilePath = "Assets/RelicFairy/Settings/GameVolumeProfile.asset";

    [MenuItem("RelicFairy/Rendering/7. 룩 그레이딩 적용 (ACES + 명암 색분리)")]
    public static void ApplyLookGrading()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(GameProfilePath);
        if (profile == null) { Debug.LogError($"[Rendering] 프로파일 없음: {GameProfilePath}"); return; }

        // 서브에셋 등록 없이 Add 했던 컴포넌트는 리로드 후 fileID 0 의 깨진 참조로 남는다.
        // 그대로 두면 Has<T>() 가 오판하거나 볼륨 스택이 null 을 만나므로 먼저 걷어낸다.
        int broken = profile.components.RemoveAll(c => c == null);
        if (broken > 0) Debug.Log($"[Rendering] 깨진 볼륨 컴포넌트 참조 {broken}개 제거");

        // VolumeProfile.Add<T>() 는 컴포넌트를 components 리스트에만 넣는다.
        // 서브에셋으로 등록하지 않으면 도메인 리로드 때 사라지므로 AddObjectToAsset 이 필수다.
        T Get<T>() where T : VolumeComponent
        {
            if (profile.Has<T>()) return profile.components.OfType<T>().First();

            var added = profile.Add<T>(overrides: false);
            added.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(added, profile);
            return added;
        }

        void Set<TV>(VolumeParameter<TV> p, TV v) { p.overrideState = true; p.value = v; }

        var tone = Get<Tonemapping>();
        Set(tone.mode, TonemappingMode.ACES);

        // ACES 는 중간톤을 눌러 어둡게 만든다. 노출을 조금 올려 되받는다.
        var color = Get<ColorAdjustments>();
        Set(color.postExposure, 0.35f);
        Set(color.contrast,     15f);
        Set(color.saturation,   8f);

        var smh = Get<ShadowsMidtonesHighlights>();
        // Vector4 는 (r, g, b, 강도). 1이 중립이다.
        Set(smh.shadows,    new Vector4(0.88f, 0.93f, 1.12f, 0f));   // 암부 → 청보라
        Set(smh.midtones,   new Vector4(1.00f, 1.00f, 1.00f, 0f));   // 중간톤 중립 유지
        Set(smh.highlights, new Vector4(1.10f, 1.03f, 0.90f, 0f));   // 명부 → 금색
        Set(smh.shadowsEnd,     0.35f);
        Set(smh.highlightsStart, 0.55f);

        // 완전히 매끈한 디지털 화면은 싸구려로 읽힌다. 아주 옅은 그레인이 질감을 만든다.
        var grain = Get<FilmGrain>();
        Set(grain.type,      FilmGrainLookup.Thin1);
        Set(grain.intensity, 0.15f);
        Set(grain.response,  0.8f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        Debug.Log("[Rendering] 룩 그레이딩 — ACES · 노출 0.35 · 대비 15 · 채도 8 · " +
                  "암부 청보라 / 명부 금색 · 그레인 0.15");
    }

    [MenuItem("RelicFairy/Rendering/2. Buto 등록 상태 확인")]
    public static void VerifyButoFeature()
    {
        foreach (string path in RendererPaths)
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data == null) { Debug.LogError($"[Rendering] 렌더러 없음: {path}"); continue; }

            string names = string.Join(", ", data.rendererFeatures.Select(f =>
                f == null ? "(null)" : $"{f.GetType().Name}{(f.isActive ? "" : ":off")}"));
            Debug.Log($"[Rendering] {Path.GetFileName(path)} → {names}");
        }
    }
}
#endif
