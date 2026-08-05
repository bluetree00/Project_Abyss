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

    // ── 데칼 ───────────────────────────────────────────────────────
    // 바닥 타일이 근거리에서 반복되는 것을 깨는 수단. 텍스처를 바꾸는 것보다 싸고,
    // 스크린/버퍼 기반이라 런타임에 생성되는 챕터 방 지오메트리에도 그대로 얹힌다.
    //
    // 기법은 DBuffer 를 쓴다. 근거:
    //   · DBuffer 는 조명 평가 전에 데칼을 버퍼에 그려서, 밑면 메시와 똑같이 그림자와
    //     스페큘러를 받는다. Screen Space 는 노멀 블렌딩만 지원한다.
    //   · Surface Data 를 Albedo Normal MAOS 로 두면 데칼이 알베도뿐 아니라 노멀·메탈릭·
    //     스무스니스·AO 까지 덮어쓴다 — 젖은 자국이나 마모가 반사까지 바뀌어야 설득력이 생긴다.
    //   · 제약은 OpenGL/GLES 비호환과 DepthNormal 프리패스 요구. 이 프로젝트는 d3d11 이라 무관.
    private const string DecalMaterialDir = "Assets/RelicFairy/Prefabs/Stage/Decals";

    // 프로젝트에 데칼용 마스크 텍스처가 없다. 후보를 전부 실측한 결과:
    //   T_StoneDebris_01a/02a_B : 알파 255 고정 → 사각형이 통째로 찍힌다. 사용 불가.
    //   T_Grunge_01b            : 알파 1~127
    //   T_Grunge_02a            : 알파 8~212
    // 그런지 둘은 알파에 변화가 있지만 타일링용이라 가장자리에서 0이 되지 않는다.
    // 그대로 쓰면 경계가 직선으로 보인다.
    //
    // 그래서 원본 알파에 가장자리 감쇠를 곱한 데칼 전용 텍스처를 생성해서 쓴다.
    // (원본은 건드리지 않는다 — 다른 곳에서 타일링 텍스처로 쓰고 있다)
    private const string DecalTextureDir = "Assets/RelicFairy/Prefabs/Stage/Decals";

    // 원본 그런지 RGB 에 곱할 값. 1이면 원본(밝은 회색)이라 어두운 바닥에서 튄다.
    private const float DecalAlbedoScale = 0.4f;

    private static readonly (string outName, string source)[] DecalMaskSources =
    {
        ("T_DecalGrunge_01", "Assets/RelicFairy/_Imported/Gothic_Interior/Environment/Asset/Texture/T_Grunge/T_Grunge_01b.png"),
        ("T_DecalGrunge_02", "Assets/RelicFairy/_Imported/Gothic_Interior/Environment/Asset/Texture/T_Grunge/T_Grunge_02a.png"),
    };

    /// <summary>원본 그런지에 가장자리 감쇠를 입혀 데칼용 텍스처를 만든다.</summary>
    private static void GenerateDecalMasks()
    {
        foreach (var (outName, source) in DecalMaskSources)
        {
            string outPath = $"{DecalTextureDir}/{outName}.png";

            // 에셋 임포터의 isReadable 을 건드리지 않도록 원본 파일을 직접 디코드한다.
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!src.LoadImage(File.ReadAllBytes(source)))
            {
                Debug.LogError($"[Rendering] 텍스처 디코드 실패: {source}");
                continue;
            }

            int w = src.width, h = src.height;
            var px = src.GetPixels32();
            float maxA = 0f;

            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h * 2f - 1f;      // -1 ~ 1
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f;

                    // 원형 감쇠. 반지름 0.55 까지는 그대로 두고 1.0 에서 0 이 되게 부드럽게 떨군다.
                    float r = Mathf.Sqrt(u * u + v * v);
                    float fall = 1f - Mathf.SmoothStep(0.55f, 1f, r);

                    int i = y * w + x;

                    // 원본 그런지의 RGB 는 밝은 회색이다. 그대로 쓰면 밝은 석재 바닥에서는 때로 읽히지만
                    // 어두운 숲 바닥에서는 눈 자국처럼 튄다. 때는 어둡게 입혀야 어느 바닥에서든 성립한다.
                    px[i].r = (byte)(px[i].r * DecalAlbedoScale);
                    px[i].g = (byte)(px[i].g * DecalAlbedoScale);
                    px[i].b = (byte)(px[i].b * DecalAlbedoScale);

                    float a = px[i].a / 255f * fall;
                    if (a > maxA) maxA = a;
                    px[i].a = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
                }
            }

            // 원본 알파 상한이 127/255 수준이라 그대로 두면 너무 옅다. 중심이 1에 닿도록 정규화한다.
            if (maxA > 0.001f && maxA < 0.99f)
            {
                float gain = 1f / maxA;
                for (int i = 0; i < px.Length; i++)
                    px[i].a = (byte)Mathf.RoundToInt(Mathf.Clamp01(px[i].a / 255f * gain) * 255f);
            }

            src.SetPixels32(px);
            src.Apply(false, false);
            File.WriteAllBytes(outPath, src.EncodeToPNG());
            Object.DestroyImmediate(src);

            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(outPath) is TextureImporter ti)
            {
                ti.textureType          = TextureImporterType.Default;
                ti.alphaSource          = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency  = true;     // 밉맵 생성 시 가장자리 색 번짐 방지
                ti.sRGBTexture          = true;
                ti.maxTextureSize       = 1024;     // 데칼에 2048 은 과하다
                ti.SaveAndReimport();
            }
            Debug.Log($"[Rendering] 데칼 마스크 생성 → {outName}.png ({w}x{h}, 알파 상한 {maxA:0.00} → 정규화)");
        }
    }

    // (머티리얼명, 베이스맵(알파=불투명도), 노멀맵)
    private static readonly (string name, string baseMap, string normal, string maos)[] DecalSources =
    {
        ("Mat_Decal_Grunge_01", $"{DecalTextureDir}/T_DecalGrunge_01.png", null, null),
        ("Mat_Decal_Grunge_02", $"{DecalTextureDir}/T_DecalGrunge_02.png", null, null),
    };

    [MenuItem("RelicFairy/Rendering/8. 데칼 배선 (DBuffer) + 데칼 머티리얼 생성")]
    public static void SetupDecals()
    {
        // 1) 렌더러 페이처 등록
        foreach (string path in RendererPaths)
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data == null) { Debug.LogError($"[Rendering] 렌더러 없음: {path}"); continue; }

            var feature = data.rendererFeatures.OfType<DecalRendererFeature>().FirstOrDefault();
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<DecalRendererFeature>();
                feature.name = "DecalRendererFeature";
                data.rendererFeatures.Add(feature);
                AssetDatabase.AddObjectToAsset(feature, data);
                Debug.Log($"[Rendering] 데칼 페이처 추가 → {Path.GetFileName(path)}");
            }

            // surfaceData 는 m_Settings 직속이 아니라 dBufferSettings 안에 있다.
            // 경로를 틀리면 FindProperty 가 null 을 돌려주고 그대로 NRE 가 난다 — 반드시 확인하고 쓴다.
            var fso = new SerializedObject(feature);
            void SetEnum(string propPath, int value, string label)
            {
                var p = fso.FindProperty(propPath);
                if (p == null) { Debug.LogWarning($"[Rendering] {label} 경로 없음: {propPath}"); return; }
                p.enumValueIndex = value;
            }
            void SetFloat(string propPath, float value, string label)
            {
                var p = fso.FindProperty(propPath);
                if (p == null) { Debug.LogWarning($"[Rendering] {label} 경로 없음: {propPath}"); return; }
                p.floatValue = value;
            }

            // SurfaceData 는 Albedo Normal(1). 스톡 'Shader Graphs/Decal' 이 노출하는 출력이
            // Base_Map / Normal_Map 뿐이라, AlbedoNormalMAOS(2)로 두면 쓰지도 않는 MAOS
            // 렌더타깃을 하나 더 잡아 대역폭만 버린다. MAOS 가 필요해지면 데칼 셰이더그래프를
            // 따로 만든 뒤 여기도 2로 올린다.
            SetEnum ("m_Settings.technique",                    1,    "기법(DBuffer)");
            SetEnum ("m_Settings.dBufferSettings.surfaceData",  1,    "SurfaceData(AlbedoNormal)");
            SetFloat("m_Settings.maxDrawDistance",              120f, "최대 그리기 거리");
            fso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
        }

        // 2) 데칼 마스크 텍스처 생성 후 머티리얼 생성
        if (!AssetDatabase.IsValidFolder(DecalMaterialDir))
            AssetDatabase.CreateFolder("Assets/RelicFairy/Prefabs/Stage", "Decals");

        GenerateDecalMasks();

        var shader = Shader.Find("Shader Graphs/Decal");
        if (shader == null) { Debug.LogError("[Rendering] 'Shader Graphs/Decal' 셰이더를 못 찾았다."); return; }

        int made = 0;
        foreach (var (name, baseMap, normal, maos) in DecalSources)
        {
            string path = $"{DecalMaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
                made++;
            }

            // 스톡 데칼 셰이더의 프로퍼티명은 Base_Map / Normal_Map / Normal_Blend 다.
            // URP Lit 관례인 _BaseMap 이 아니다 — 이름이 틀리면 조용히 아무 일도 안 일어난다.
            void Bind(string prop, string texPath)
            {
                if (texPath == null) return;
                if (!mat.HasProperty(prop)) { Debug.LogWarning($"[Rendering] 프로퍼티 없음: {prop}"); return; }
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if (tex == null) { Debug.LogWarning($"[Rendering] 텍스처 없음: {texPath}"); return; }
                mat.SetTexture(prop, tex);
            }
            Bind("Base_Map",   baseMap);
            Bind("Normal_Map", normal);

            if (mat.HasProperty("Normal_Blend")) mat.SetFloat("Normal_Blend", normal != null ? 1f : 0f);
            EditorUtility.SetDirty(mat);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Rendering] 데칼 — DBuffer · Albedo Normal MAOS · 최대거리 120 · 머티리얼 신규 {made}개 " +
                  $"(총 {DecalSources.Length}개)");
    }

    // 프로젝트에 룩 프로파일이 두 개 병존한다.
    //   GameVolumeProfile : BaseCamp · Ch1~4 · Tutorial
    //   PP_Global_Dark    : Game_Intro · LichTest · DragonTest · Test
    // PP_Global_Dark 는 이미 ACES 에 Bloom 1.1/0.4, FilmGrain 0.12, WhiteBalance -8,
    // saturation -5 로 잘 잡혀 있다 — 의도적으로 더 어둡고 차갑고 탈채도된 룩이다.
    // 그 성격은 유지하고, 빠져 있는 명암 색분리만 같은 값으로 맞춰 톤 언어를 통일한다.
    private const string DarkProfilePath = "Assets/Settings/PostProcessing/PP_Global_Dark.asset";

    [MenuItem("RelicFairy/Rendering/9. 인트로·보스테스트 프로파일에 명암 색분리 적용")]
    public static void ApplyShadowSplitToDarkProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(DarkProfilePath);
        if (profile == null) { Debug.LogError($"[Rendering] 프로파일 없음: {DarkProfilePath}"); return; }

        profile.components.RemoveAll(c => c == null);

        ShadowsMidtonesHighlights smh;
        if (profile.Has<ShadowsMidtonesHighlights>())
        {
            smh = profile.components.OfType<ShadowsMidtonesHighlights>().First();
        }
        else
        {
            smh = profile.Add<ShadowsMidtonesHighlights>(overrides: false);
            smh.name = nameof(ShadowsMidtonesHighlights);
            AssetDatabase.AddObjectToAsset(smh, profile);
        }

        void Set<TV>(VolumeParameter<TV> p, TV v) { p.overrideState = true; p.value = v; }
        Set(smh.shadows,         new Vector4(0.88f, 0.93f, 1.12f, 0f));
        Set(smh.midtones,        new Vector4(1.00f, 1.00f, 1.00f, 0f));
        Set(smh.highlights,      new Vector4(1.10f, 1.03f, 0.90f, 0f));
        Set(smh.shadowsEnd,      0.35f);
        Set(smh.highlightsStart, 0.55f);

        // 볼류메트릭도 이 계열에는 아예 없었다. 스폰 포인트 실측 결과 이 프로파일을 쓰는 씬은
        // 전부 y ≈ 0 대다 — Game_Intro 1.0 / LichTest 0.0 / DragonTest 0.0.
        // 챕터 방과 같은 높이대이므로 같은 기준(-2 부터 15m)을 쓴다.
        // 밀도는 BaseCamp(개방 광장, 0.35)보다 높이되 챕터 기본값(1.2)보다는 낮게 잡는다 —
        // 인트로 아레나는 반쯤 닫힌 공간이라 그 중간이다.
        ButoVolumetricFog fog;
        if (profile.Has<ButoVolumetricFog>())
        {
            fog = profile.components.OfType<ButoVolumetricFog>().First();
        }
        else
        {
            fog = profile.Add<ButoVolumetricFog>(overrides: false);
            fog.name = nameof(ButoVolumetricFog);
            AssetDatabase.AddObjectToAsset(fog, profile);
        }
        Set(fog.mode,                    VolumetricFogMode.On);
        Set(fog.baseHeight,              -2f);
        Set(fog.attenuationBoundarySize, 15f);
        Set(fog.fogDensity,              0.6f);
        Set(fog.lightIntensity,          0.8f);
        Set(fog.maxDistanceVolumetric,   70f);
        Set(fog.anisotropy,              0.3f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        Debug.Log("[Rendering] PP_Global_Dark — 명암 색분리 + 볼류메트릭(baseHeight -2 / boundary 15 / " +
                  "density 0.6) 적용. 노출·채도·화이트밸런스는 기존 유지");
    }

    // 챕터 방은 런타임 생성이라 라이트맵·리플렉션 프로브를 못 쓴다. 표면 디테일을 넣을 수단이
    // 데칼밖에 없으므로, 6개 팔레트 전부에 같은 그런지 데칼을 물려 최소 기준선을 만든다.
    // 챕터별로 다른 데칼을 쓰고 싶으면 팔레트에서 개별 교체하면 된다.
    [MenuItem("RelicFairy/Rendering/10. 팔레트에 바닥 데칼 배선")]
    public static void WireDecalsToPalettes()
    {
        var mats = new[]
        {
            AssetDatabase.LoadAssetAtPath<Material>($"{DecalMaterialDir}/Mat_Decal_Grunge_01.mat"),
            AssetDatabase.LoadAssetAtPath<Material>($"{DecalMaterialDir}/Mat_Decal_Grunge_02.mat"),
        }.Where(m => m != null).ToArray();

        if (mats.Length == 0) { Debug.LogError("[Rendering] 데칼 머티리얼이 없다. 메뉴 8을 먼저 실행."); return; }

        var guids = AssetDatabase.FindAssets("t:BlockPalette");
        int wired = 0;

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var palette = AssetDatabase.LoadAssetAtPath<BlockPalette>(path);
            if (palette == null) continue;

            var so   = new SerializedObject(palette);
            var list = so.FindProperty("floorDecalMaterials");
            if (list == null) { Debug.LogWarning($"[Rendering] floorDecalMaterials 없음: {path}"); continue; }

            list.ClearArray();
            for (int i = 0; i < mats.Length; i++)
            {
                list.InsertArrayElementAtIndex(i);
                list.GetArrayElementAtIndex(i).objectReferenceValue = mats[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(palette);
            wired++;
            Debug.Log($"[Rendering] 데칼 배선 → {Path.GetFileNameWithoutExtension(path)} ({mats.Length}종)");
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Rendering] 팔레트 {wired}개에 바닥 데칼 배선 완료");
    }

    // Unity 6 의 GPU Resident Drawer.
    // 렌더링할 오브젝트 데이터를 GPU 에 상주시켜 CPU 측 드로우 제출과 컬링 비용을 크게 줄인다.
    // BaseCamp 만 해도 Static 오브젝트가 17,336개라 이 기능이 겨냥하는 지점에 정확히 해당한다.
    // 성능 여유가 생기면 광원·데칼·볼류메트릭의 상한도 함께 올라간다.
    //
    // 전제: 대상 오브젝트가 Static(배칭 가능)이어야 한다 — 1번 메뉴로 이미 지정돼 있다.
    [MenuItem("RelicFairy/Rendering/11. GPU Resident Drawer 활성화")]
    public static void EnableGpuResidentDrawer()
    {
        var urpAssets = new[]
        {
            "Assets/Settings/URP-Performant.asset",
            "Assets/Settings/URP-Balanced.asset",
            "Assets/Settings/URP-HighFidelity.asset",
        };

        foreach (string path in urpAssets)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null) { Debug.LogError($"[Rendering] URP 에셋 없음: {path}"); continue; }

            var so = new SerializedObject(asset);
            void Set(string prop, int value, string label)
            {
                var p = so.FindProperty(prop);
                if (p == null) { Debug.LogWarning($"[Rendering] {label} 경로 없음: {prop}"); return; }
                if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = value != 0;
                else p.intValue = value;
            }

            Set("m_GPUResidentDrawerMode", 1, "GPU Resident Drawer(InstancedDrawing)");
            Set("m_GPUResidentDrawerEnableOcclusionCullingInCameras", 1, "GPU 오클루전 컬링");
            // 화면에서 이 비율보다 작아지는 메시는 그리지 않는다. 0이면 컷오프 없음.
            // 소품이 만 단위라 아주 작은 것부터 걷어내면 효과가 크다.
            var smp = so.FindProperty("m_SmallMeshScreenPercentage");
            if (smp != null) smp.floatValue = 1.5f;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            Debug.Log($"[Rendering] {Path.GetFileName(path)} — GPU Resident Drawer ON · 오클루전 컬링 ON · " +
                      $"소형 메시 컷오프 1.5%");
        }

        // GRD 는 DOTS 인스턴싱 셰이더 변형을 쓴다. 기본 스트리핑 설정이면 빌드 때 그 변형이
        // 통째로 제거돼 플레이어에서만 깨진다 — 에디터에서는 멀쩡해 보이므로 놓치기 쉽다.
        // 프로퍼티는 읽기 전용이라 GraphicsSettings 에셋을 직접 쓴다.
        // 열거형 값은 이름으로 얻어 하드코딩을 피한다(버전에 따라 정수값이 달라질 수 있다).
        var gsAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
        if (gsAsset == null) Debug.LogWarning("[Rendering] GraphicsSettings 에셋을 못 읽었다.");
        else
        {
            var gso  = new SerializedObject(gsAsset);
            var prop = gso.FindProperty("m_BrgStripping");
            if (prop == null) Debug.LogWarning("[Rendering] m_BrgStripping 프로퍼티 없음");
            else
            {
                int keepAll = (int)UnityEditor.Rendering.BatchRendererGroupStrippingMode.KeepAll;
                prop.intValue = keepAll;
                gso.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[Rendering] BatchRendererGroup Variants → Keep All (값 {keepAll}) " +
                          "— 빌드 시 DOTS 인스턴싱 셰이더 보존");
            }
        }

        AssetDatabase.SaveAssets();
    }

    // 화면공간 렌즈 플레어. 블룸 밉을 재사용하므로 추가 비용이 작고, 광원이 화면에 들어올 때
    // 렌즈가 반응하는 느낌을 준다 — 화톳불·포털처럼 밝은 점광원이 많은 이 게임에 잘 맞는다.
    // 값을 세게 주면 싸구려 렌즈 효과가 되므로 "있는 줄 모르게" 정도로만 잡는다.
    [MenuItem("RelicFairy/Rendering/12. 화면공간 렌즈 플레어")]
    public static void ApplyScreenSpaceLensFlare()
    {
        foreach (string profilePath in new[] { GameProfilePath, DarkProfilePath })
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null) { Debug.LogError($"[Rendering] 프로파일 없음: {profilePath}"); continue; }

            profile.components.RemoveAll(c => c == null);

            ScreenSpaceLensFlare flare;
            if (profile.Has<ScreenSpaceLensFlare>())
            {
                flare = profile.components.OfType<ScreenSpaceLensFlare>().First();
            }
            else
            {
                flare = profile.Add<ScreenSpaceLensFlare>(overrides: false);
                flare.name = nameof(ScreenSpaceLensFlare);
                AssetDatabase.AddObjectToAsset(flare, profile);
            }

            void Set<TV>(VolumeParameter<TV> p, TV v) { p.overrideState = true; p.value = v; }
            Set(flare.intensity,               0.35f);
            Set(flare.tintColor,               new Color(1f, 0.95f, 0.85f, 1f));  // 화톳불 금색 쪽
            Set(flare.firstFlareIntensity,     0.6f);
            Set(flare.secondaryFlareIntensity, 0.3f);
            Set(flare.warpedFlareIntensity,    0.25f);
            Set(flare.streaksIntensity,        0.2f);   // 수평 줄기는 약하게 — 강하면 SF 느낌이 된다
            Set(flare.vignetteEffect,          0.7f);   // 화면 중앙보다 가장자리에서 강하게

            EditorUtility.SetDirty(profile);
            Debug.Log($"[Rendering] 렌즈 플레어 → {Path.GetFileNameWithoutExtension(profilePath)} (세기 0.35)");
        }
        AssetDatabase.SaveAssets();
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
