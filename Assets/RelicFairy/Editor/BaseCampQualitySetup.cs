#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using OccaSoftware.Buto.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// BaseCamp 품질 셋업 도구.
///
/// BaseCamp는 챕터와 달리 <b>고정 씬</b>이라 라이트맵 베이크가 가능하다.
/// 그런데 실측해보니 프로브 0개 · 라이트맵 0장 · 조명 63개가 전부 Realtime ForcePixel이었다.
/// 이 도구는 그 셋업을 단계별로 수행한다 — 각 메뉴가 독립이라 하나씩 확인하며 진행할 수 있다.
///
/// 순서: ① Static 지정 → ② 조명 베이크 타입 전환 → ③ Light Probe → ④ Reflection Probe → ⑤ 부유 먼지
/// 그 뒤 Lighting 창(또는 MCP bake_start)에서 베이크한다.
/// </summary>
public static class BaseCampQualitySetup
{
    // 정적으로 지정할 씬 루트. 건축·지형처럼 절대 움직이지 않는 것만 넣는다.
    private static readonly string[] StaticRoots =
    {
        "map", "VoidScape", "DisplayPlaza", "IntroProcessional",
        "Pedestal_Sword", "DisplayArch_Entrance", "DungeonPortalVisual",
        "ForgeStation", "GawainStation", "LancelotStation", "AwakeningStation",
    };

    // 플레이 영역 대략 범위 — 스테이션 좌표(X 11~118, Y 7.4~14, Z 0~25)에서 잡았다.
    private static readonly Bounds PlayArea = new(new Vector3(64f, 11f, 14f), new Vector3(120f, 12f, 34f));

    // ─────────────────────────────────────────────────────────────
    // ① Static
    // ─────────────────────────────────────────────────────────────

    [MenuItem("RelicFairy/BaseCamp/1. Set Static (map + 건축물)")]
    public static void SetStatic()
    {
        const StaticEditorFlags Flags =
            StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
            StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic |
            StaticEditorFlags.ReflectionProbeStatic;

        int done = 0, skipped = 0;
        foreach (var rootName in StaticRoots)
        {
            var root = GameObject.Find(rootName);
            if (root == null) { Debug.LogWarning($"[BaseCampQuality] '{rootName}' 없음 — 건너뜀"); continue; }

            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                // 움직이는 것은 정적으로 만들면 안 된다 — VoidDrifter(부유), Rigidbody(물리).
                // 자기 자신 또는 조상에 하나라도 있으면 그 가지 전체를 건너뛴다.
                if (HasMoverInAncestry(tr)) { skipped++; continue; }

                Undo.RecordObject(tr.gameObject, "Set Static");
                GameObjectUtility.SetStaticEditorFlags(tr.gameObject, Flags);
                done++;
            }
        }
        Debug.Log($"[BaseCampQuality] Static 지정 {done}개 · 움직이는 가지 제외 {skipped}개");
    }

    private static bool HasMoverInAncestry(Transform tr)
    {
        for (var t = tr; t != null; t = t.parent)
            if (t.GetComponent<VoidDrifter>() != null || t.GetComponent<Rigidbody>() != null)
                return true;
        return false;
    }

    // ─────────────────────────────────────────────────────────────
    // ② 조명 베이크 타입
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 장식 조명(촛불·횃불 등 작고 약한 Point)은 Baked로, 주광원은 Mixed로 돌린다.
    /// Baked는 런타임 비용이 0이고 간접광까지 굽는다. 동적 오브젝트(플레이어)는
    /// Light Probe로 그 결과를 받으므로 ③을 반드시 함께 수행해야 한다.
    /// </summary>
    [MenuItem("RelicFairy/BaseCamp/2. 조명 베이크 타입 전환 (장식=Baked, 주광=Mixed)")]
    public static void ConvertLights()
    {
        int baked = 0, mixed = 0;
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Undo.RecordObject(light, "Convert Light Bake Type");

            bool isKeyLight = light.type == LightType.Directional
                              || light.shadows != LightShadows.None
                              || light.intensity >= 50f;   // 아레나 스포트(156) 같은 연출 광원

            if (isKeyLight)
            {
                light.lightmapBakeType = LightmapBakeType.Mixed;
                mixed++;
            }
            else
            {
                light.lightmapBakeType = LightmapBakeType.Baked;
                // Baked 광원은 픽셀 라이트 예산을 쓸 이유가 없다(런타임에 존재하지 않는다).
                light.renderMode = LightRenderMode.Auto;
                baked++;
            }
            EditorUtility.SetDirty(light);
        }
        Debug.Log($"[BaseCampQuality] 조명 전환 — Baked {baked} / Mixed {mixed}");
    }

    // ─────────────────────────────────────────────────────────────
    // ③ Light Probe
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 플레이 영역에 격자 프로브를 깐다. 동적 오브젝트(플레이어·위습·부유 폐허)가
    /// 베이크된 간접광을 받는 유일한 경로다. 없으면 ②의 Baked 전환이 캐릭터를 어둡게 만든다.
    /// </summary>
    [MenuItem("RelicFairy/BaseCamp/3. Light Probe 격자 배치")]
    public static void PlaceLightProbes()
    {
        const float Spacing = 7f;
        float[] heights = { 1.0f, 4.5f, 9.0f };   // 발밑 / 가슴 / 머리 위 — 3층이면 세로 그라데이션이 잡힌다

        var go = EnsureObject("@LightProbes");
        var grp = Ensure<LightProbeGroup>(go);
        go.transform.position = Vector3.zero;

        var pts = new List<Vector3>();
        var min = PlayArea.min; var max = PlayArea.max;
        for (float x = min.x; x <= max.x; x += Spacing)
            for (float z = min.z; z <= max.z; z += Spacing)
                foreach (var h in heights)
                    pts.Add(new Vector3(x, PlayArea.min.y + h, z));

        Undo.RecordObject(grp, "Place Light Probes");
        grp.probePositions = pts.ToArray();
        EditorUtility.SetDirty(grp);
        Debug.Log($"[BaseCampQuality] Light Probe {pts.Count}개 배치 (간격 {Spacing}m · 높이 3층)");
    }

    // ─────────────────────────────────────────────────────────────
    // ④ Reflection Probe
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 프로브가 하나도 없어 모든 표면이 스카이박스를 반사한다 — 실내 회랑이 우주를 비추고 있다.
    /// 구역별로 베이크 프로브를 둔다. URP의 프로브 블렌딩은 파이프라인에서 이미 켜 뒀다.
    /// </summary>
    [MenuItem("RelicFairy/BaseCamp/4. Reflection Probe 배치")]
    public static void PlaceReflectionProbes()
    {
        (string name, Vector3 pos, Vector3 size)[] spec =
        {
            ("@ReflProbe_Entrance",  new Vector3(34f,  11f, 17f), new Vector3(28f, 14f, 26f)),
            ("@ReflProbe_Colonnade", new Vector3(60f,  11f, 17f), new Vector3(32f, 14f, 26f)),
            ("@ReflProbe_Arena",     new Vector3(105f, 11f, 16f), new Vector3(34f, 14f, 30f)),
            ("@ReflProbe_Plaza",     new Vector3(80f,  11f, 16f), new Vector3(26f, 14f, 26f)),
        };

        foreach (var (name, pos, size) in spec)
        {
            var go = EnsureObject(name);
            go.transform.position = pos;
            var p = Ensure<ReflectionProbe>(go);
            Undo.RecordObject(p, "Setup Reflection Probe");
            p.mode           = ReflectionProbeMode.Baked;
            p.size           = size;
            p.resolution     = 256;
            p.hdr            = true;
            p.shadowDistance = 60f;
            p.clearFlags     = ReflectionProbeClearFlags.Skybox;
            p.boxProjection  = true;   // 실내에서 반사가 무한 원거리로 보이는 것을 막는다
            EditorUtility.SetDirty(p);
        }
        Debug.Log($"[BaseCampQuality] Reflection Probe {spec.Length}개 (Baked · 256 · Box Projection)");
    }

    // ─────────────────────────────────────────────────────────────
    // ⑤ 부유 먼지
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 우주에 떠 있는 폐허인데 씬에 파티클이 0개다. 아주 느린 부유 입자를 깔아
    /// 공간에 부피감(공기)을 준다. 카메라를 따라다니게 두지 않고 플레이 영역을 덮는다.
    /// </summary>
    [MenuItem("RelicFairy/BaseCamp/5. 부유 먼지 파티클")]
    public static void CreateDust()
    {
        var go = EnsureObject("@VoidDust");
        go.transform.position = PlayArea.center;

        var ps = Ensure<ParticleSystem>(go);
        var main = ps.main;
        main.duration            = 12f;
        main.loop                = true;
        main.startLifetime       = new ParticleSystem.MinMaxCurve(14f, 26f);
        main.startSpeed          = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
        main.startSize           = new ParticleSystem.MinMaxCurve(0.03f, 0.11f);
        main.startColor          = new ParticleSystem.MinMaxGradient(
                                       new Color(0.78f, 0.74f, 1f, 0.5f), new Color(1f, 0.93f, 0.82f, 0.35f));
        main.maxParticles        = 900;
        main.simulationSpace     = ParticleSystemSimulationSpace.World;
        main.gravityModifier     = -0.004f;   // 아주 약하게 떠오른다

        var em = ps.emission; em.rateOverTime = 45f;

        var sh = ps.shape;
        sh.enabled    = true;
        sh.shapeType  = ParticleSystemShapeType.Box;
        sh.scale      = PlayArea.size;

        var noise = ps.noise;
        noise.enabled   = true;
        noise.strength  = 0.35f;
        noise.frequency = 0.12f;
        noise.scrollSpeed = 0.05f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f),
                          new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode    = ParticleSystemRenderMode.Billboard;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;
        // 머티리얼은 URP 기본 파티클 셰이더로 두고, 아트가 필요하면 인스펙터에서 교체한다.

        EditorUtility.SetDirty(go);
        Debug.Log("[BaseCampQuality] @VoidDust 생성 — 부유 먼지 (World space · 최대 900)");
    }

    // ─────────────────────────────────────────────────────────────
    // 베이크 설정
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 베이크 해상도가 40 texels/unit이라 200m 규모 씬에는 과하다(라이트맵 수십 장·수 시간).
    /// 실사용 가능한 값으로 낮추고 AO를 켠다.
    /// </summary>
    [MenuItem("RelicFairy/BaseCamp/6. 라이트맵 베이크 설정 조정")]
    public static void TuneBakeSettings()
    {
        // lightingSettings 게터는 미할당 상태에서 예외를 던진다(??= 로는 못 잡는다).
        // 씬에 라이팅 설정이 한 번도 지정된 적이 없으면 새로 만들어 붙인다.
        if (!Lightmapping.TryGetLightingSettings(out var s) || s == null)
        {
            s = new LightingSettings { name = "BaseCamp Lighting" };
            Lightmapping.lightingSettings = s;
        }
        s.lightmapper          = LightingSettings.Lightmapper.ProgressiveGPU;
        s.lightmapResolution   = 8f;      // 40 → 8 texels/unit
        s.lightmapPadding      = 2;
        // 1024로는 이 씬이 안 들어간다. Static 오브젝트가 만 단위라 아틀라스가 병목이 되어
        // 해상도를 올려도 Unity가 도로 축소해 넣는다(바닥에 사각 블록 자국). 2048은 용량 4배.
        s.lightmapMaxSize      = 2048;
        s.ao                   = true;    // 베이크 AO — 지금 꺼져 있다
        s.aoMaxDistance        = 1.5f;
        s.aoExponentIndirect   = 1f;
        s.aoExponentDirect     = 0.4f;
        s.directSampleCount    = 32;
        s.indirectSampleCount  = 256;
        s.maxBounces              = 2;
        s.lightmapCompression  = LightmapCompression.NormalQuality;

        // 전체 톤은 앰비언트(단색 다크블루 0.13)가 지배해서, 빛 웅덩이 바깥이 올라오지 않는다.
        // 앰비언트를 올리면 명암 대비가 통째로 죽으므로, 대신 간접광(바운스)을 키워 빛이
        // 번지게 한다. 광원 주변이 넓어질 뿐 어두운 곳은 어둡게 남아 무드가 유지된다.
        // 1.6 / 1.4 로 올렸더니 톤은 올라왔지만 화면이 통째로 평평해졌다. 밝기를 간접광으로 벌면
        // 명암 대비가 먼저 죽는다. 밝기는 주인공광 Mixed 의 스페큘러로 벌고 여기서는 대비를 되찾는다.
        s.albedoBoost  = 1.25f;  // 표면이 되튕기는 양
        s.indirectScale = 1.15f; // 간접광 출력 배율

        // 방향성·섀도마스크는 못 켠다. S_OpaqueORMWorldAlign(서드파티)이 텍스처 프로퍼티 10개를
        // 선언하는데, 여기에 URP가 unity_Lightmap / unity_LightmapInd / unity_ShadowMask 3개를
        // 더 얹으면 d3d11 ps_4_0 샘플러 한도 16을 넘겨 셰이더가 컴파일에 실패한다(마젠타).
        // 둘을 반납하면 한도 안으로 들어온다. 대가는
        //   NonDirectional → 법선맵이 베이크광에 반응 못 함
        //   IndirectOnly   → 혼합광 그림자가 전부 실시간 (그림자 거리 120이라 BaseCamp는 전 구역 커버)
        // 근본 해결은 셰이더에서 샘플러를 공유시키는 것이고, 그건 ShaderGraph UI 작업이다.
        s.directionalityMode   = LightmapsMode.NonDirectional;
        s.mixedBakeMode        = MixedLightingMode.IndirectOnly;

        Debug.Log("[BaseCampQuality] 베이크 설정 — 8 texel/u · 아틀라스 2048 · AO ON · 바운스 2 · " +
                  "NonDirectional · IndirectOnly");
    }

    // 라이트맵 아틀라스는 유한한데 Static 오브젝트가 17,000개가 넘는다. 전부 GI에 기여시키면
    // 큰 벽·바닥이 받는 텍셀이 개당 17×17 수준으로 쪼그라들어 블록 자국이 생긴다.
    // 큰 건축물만 라이트맵을 받게 하고 소품은 Light Probe로 넘긴다 — 소품은 어차피 자기 그림자가
    // 라이트맵에 기록될 만큼 크지 않아 손해가 거의 없다.
    // 3m로 잡았더니 화톳불·좌대·진열대·상자(대개 1~2m)가 통째로 빠져 프로브 조명만 받아 납작해졌다.
    // 아틀라스를 2048로 올려 여유가 생겼으니 1.2m로 낮춰 소품을 라이트맵에 복귀시킨다.
    // 이보다 작은 것(부스러기·잔해 조각)은 자기 그림자가 라이트맵에 기록될 만큼 크지 않다.
    private const float ContributeGiMinSize = 1.2f;   // 렌더러 월드 바운드 최대 변(m)

    // 기준을 낮췄을 때 이전에 뺀 것이 돌아와야 하므로 양방향으로 맞춘다.
    // 대상은 Static으로 지정된 오브젝트뿐이다(1번 메뉴가 지정한 것) — 이동체는 건드리지 않는다.
    [MenuItem("RelicFairy/BaseCamp/13. Contribute GI 정리 (크기 기준 재적용)")]
    public static void TrimContributeGI()
    {
        int added = 0, removed = 0, unchanged = 0;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            var go = r.gameObject;
            var flags = GameObjectUtility.GetStaticEditorFlags(go);
            if (flags == 0) continue;                       // Static이 아닌 이동체는 제외

            var size = r.bounds.size;
            bool want = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) >= ContributeGiMinSize;
            bool has  = (flags & StaticEditorFlags.ContributeGI) != 0;
            if (want == has) { unchanged++; continue; }

            GameObjectUtility.SetStaticEditorFlags(go, want
                ? flags |  StaticEditorFlags.ContributeGI
                : flags & ~StaticEditorFlags.ContributeGI);
            if (want) added++; else removed++;
        }
        Debug.Log($"[BaseCampQuality] Contribute GI — 추가 {added} / 해제 {removed} / 유지 {unchanged} " +
                  $"(기준 최대변 {ContributeGiMinSize}m)");
    }

    // 테스트는 4 texel/u로 먼저 굽는다. 해상도는 면적당 텍셀 수라 8→4는 용량·시간이 대략 1/4이 된다.
    // 톤·AO·바운스가 의도대로 나오는지는 이 해상도에서도 판별되고, 아니면 본 베이크가 통째로 낭비다.
    [MenuItem("RelicFairy/BaseCamp/9. 라이트맵 베이크 실행 (테스트 4 texel/u)")]
    public static void BakeTest() => Bake(4f);

    [MenuItem("RelicFairy/BaseCamp/10. 라이트맵 베이크 실행 (본 8 texel/u)")]
    public static void BakeFull() => Bake(8f);

    [MenuItem("RelicFairy/BaseCamp/11. 베이크 진행 상태")]
    public static void BakeStatus()
    {
        Debug.Log($"[BaseCampQuality] 베이크 실행중={Lightmapping.isRunning} · 진행률={Lightmapping.buildProgress:P1}");
    }

    // 바닥에 주황색 얼룩이 반복해서 찍히는 원인.
    // Gothic_Interior의 바닥 메시(ArchFloor 6종·FloorDecal 5종)와 유리창 6종은 임포터에서
    // Generate Lightmap UVs가 꺼져 있어 UV2가 없다. UV2가 없으면 Unity는 라이트맵을 UV0로
    // 샘플링하는데, 이 메시들의 UV0는 다이아몬드 무늬를 타일링하려고 0~1을 여러 번 반복한다.
    // 결과적으로 라이트맵의 한 조각이 표면 전체에 반복 투영되어 얼룩이 된다.
    // 해상도를 올려도 절대 사라지지 않는다 — 언랩 문제이지 텍셀 문제가 아니다.
    [MenuItem("RelicFairy/BaseCamp/14. 라이트맵 UV 생성 (Gothic_Interior 메시)")]
    public static void GenerateLightmapUVs()
    {
        const string Root = "Assets/RelicFairy/_Imported/Gothic_Interior";
        var guids = AssetDatabase.FindAssets("t:Model", new[] { Root });
        int done = 0;

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is not ModelImporter mi) continue;
            if (mi.generateSecondaryUV) continue;

            mi.generateSecondaryUV = true;
            mi.SaveAndReimport();
            done++;
            Debug.Log($"[BaseCampQuality]   UV2 생성 → {System.IO.Path.GetFileName(path)}");
        }
        Debug.Log($"[BaseCampQuality] 라이트맵 UV 생성 — {done}개 메시 재임포트 (전체 {guids.Length}개 검사)");
    }

    // 라이트맵은 확산광만 담는다. Baked 광원은 런타임 실시간 성분이 0이라
    // 스페큘러도, 법선맵 반응도, 실시간 그림자도 만들지 못한다.
    // 바닥은 T_Cathedral_Floor_02a/b 의 D·N·ORM 을 전부 물고 있는데도 무광 종이처럼 보였던 이유가
    // 이것이다 — 텍스처가 아니라 그걸 자극할 광원이 없었다.
    //
    // 주인공 광원만 Mixed 로 올려 직접광을 실시간으로 되살린다. 간접광은 그대로 라이트맵에 남으므로
    // 비용은 실시간 광원 몇 개분이다. 약한 장식광은 Baked 로 둔다 — 스페큘러가 필요 없고,
    // 전부 실시간으로 돌리면 additionalLightsPerObjectLimit(8) 경쟁만 심해진다.
    //
    // 기준을 ButoLight 와 같은 15로 맞춘다. 볼륨 빛줄기가 나오는 광원과 스페큘러를 만드는 광원이
    // 어긋나면 빛줄기는 있는데 바닥은 반응하지 않는 이상한 그림이 된다.
    private const float HeroLightMinIntensity = 15f;

    [MenuItem("RelicFairy/BaseCamp/16. 재질 반응 복구 (주인공광 Mixed + 프로브 512)")]
    public static void RestoreMaterialResponse()
    {
        int promoted = 0, alreadyMixed = 0, leftBaked = 0;
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.intensity < HeroLightMinIntensity) { leftBaked++; continue; }
            if (light.lightmapBakeType == LightmapBakeType.Mixed) { alreadyMixed++; continue; }

            // 강등은 하지 않는다. 세기가 낮아도 의도적으로 Mixed 로 둔 광원이 있을 수 있다.
            light.lightmapBakeType = LightmapBakeType.Mixed;
            promoted++;
        }
        Debug.Log($"[BaseCampQuality] 주인공광 Mixed — 승격 {promoted} / 이미 Mixed {alreadyMixed} / " +
                  $"Baked 유지 {leftBaked} (기준 세기 {HeroLightMinIntensity})");

        int probes = 0;
        foreach (var probe in Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))
        {
            probe.resolution = 512;   // 256은 바닥 반사에 부족하다
            probes++;
        }
        Debug.Log($"[BaseCampQuality] 리플렉션 프로브 {probes}개 → 해상도 512");
    }

    // ── 포그를 씬 소유로 분리 ──────────────────────────────────────
    // Buto 의 baseHeight / attenuationBoundarySize 는 월드 Y 절대값이다.
    // 그런데 공유 프로파일(GameVolumeProfile)이 BaseCamp 와 챕터 씬에 동시에 걸려 있고,
    // 두 씬의 바닥 Y가 다르다.
    //   BaseCamp 광장 바닥 : y 7.42
    //   챕터 방 바닥       : y 0     (GameRunBootstrapper: StartRunAsync(new Vector3(0,0,2000)))
    // 공유 프로파일의 원래 값 (baseHeight -2 / boundary 15) 은 챕터 방(y 0~12) 기준으로
    // 저작된 것이었다. 그걸 BaseCamp 에 맞춰 덮어쓰면 챕터에서는 포그층이 통째로 천장에 얹힌다.
    //
    // 그래서 공유 프로파일은 챕터 기준을 유지하고, BaseCamp 는 우선순위가 높은 로컬 볼륨으로
    // 자기 높이대만 덮어쓴다. 톤매핑·그레이딩·블룸은 게임 전체 룩이므로 공유 프로파일에 남긴다.
    private const string BaseCampFogProfilePath = "Assets/RelicFairy/Settings/BaseCampFogProfile.asset";
    private const string BaseCampFogVolumeName  = "@FogVolume_BaseCamp";

    [MenuItem("RelicFairy/BaseCamp/17. BaseCamp 전용 포그 볼륨 생성")]
    public static void CreateBaseCampFogVolume()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(BaseCampFogProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, BaseCampFogProfilePath);
        }

        // Add<T>() 는 components 리스트에만 넣는다. 서브에셋 등록을 안 하면 리로드 때 사라진다.
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

        // 광장 바닥이 7.42 다. 그 살짝 아래에서 시작해 6m 위(y 12)에서 걷히게 하면
        // 안개가 광장 높이에만 깔리고 위를 향한 시선은 빠져나간다 — 우주 배경이 살아난다.
        void Set<T>(VolumeParameter<T> p, T v) { p.overrideState = true; p.value = v; }
        Set(fog.mode,                    VolumetricFogMode.On);
        Set(fog.baseHeight,              6f);
        Set(fog.attenuationBoundarySize, 6f);
        Set(fog.fogDensity,              0.35f);
        Set(fog.lightIntensity,          0.8f);
        Set(fog.maxDistanceVolumetric,   60f);
        Set(fog.anisotropy,              0.3f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        var go = EnsureObject(BaseCampFogVolumeName);
        var volume = Ensure<Volume>(go);
        volume.isGlobal = true;
        volume.priority = 10f;          // 공유 프로파일(기본 0)보다 위
        volume.sharedProfile = profile;

        Debug.Log($"[BaseCampQuality] BaseCamp 전용 포그 볼륨 — 우선순위 {volume.priority}, " +
                  $"baseHeight 6 / boundary 6 / density 0.35 (공유 프로파일은 챕터 기준 유지)");
    }

    [MenuItem("RelicFairy/BaseCamp/15. 베이크 취소")]
    public static void CancelBake()
    {
        if (!Lightmapping.isRunning) { Debug.Log("[BaseCampQuality] 실행 중인 베이크 없음."); return; }
        Lightmapping.Cancel();
        Debug.Log("[BaseCampQuality] 베이크 취소함.");
    }

    private static void Bake(float resolution)
    {
        if (Lightmapping.isRunning)
        {
            Debug.LogWarning("[BaseCampQuality] 이미 베이크가 돌고 있다. 중복 실행 무시.");
            return;
        }

        TuneBakeSettings();
        Lightmapping.TryGetLightingSettings(out var s);
        s.lightmapResolution = resolution;

        Lightmapping.BakeAsync();
        Debug.Log($"[BaseCampQuality] 베이크 시작 — 해상도 {resolution} texel/u · " +
                  $"방향성 {s.directionalityMode} · 혼합광 {s.mixedBakeMode}");
    }

    // ─────────────────────────────────────────────────────────────
    // 광장 바닥 격자 재구축
    // ─────────────────────────────────────────────────────────────
    //
    // 현재 바닥은 타일(7.00×7.00m)을 6.06~7.00m 간격으로 겹쳐 깔고, 남는 틈은
    // 스케일(0.83~1.66)로 늘려 메운 상태다. 그래서 다이아몬드 무늬 밀도가 구역마다
    // 달라지고 이음매가 어긋난다. 완전 중복(같은 좌표 2장)도 있어 Z-fighting이 난다.
    //
    // 규칙 — "셀 중심이 기존 바닥 안에 들어오면 채운다"
    //   · 격자가 평면을 빈틈없이 분할하므로 채운 셀끼리는 틈도 겹침도 없다(내부 완전 충전).
    //   · 가장자리는 격자에 맞춰 잘린다. 기존 footprint 밖으로 튀어나가지 않는 쪽을 택했다.
    //   · 앵커는 사용자가 지목한 기준 타일 (60.78, 21.76). 그 타일은 제자리에 남는다.

    private const float TileSize   = 7.0f;      // SM_ArchFloor_01a 실측 7.0024 × 7.0020
    private const float FloorY     = 7.42f;     // 기준층. 피벗이 타일 윗면이다

    // 격자 앵커는 NaveFloor 열에서 가져온다.
    // NaveFloor 25장은 이미 X 68.48+7n / Z 2.53+7m 의 완전한 7m 정격자다(스케일도 1.0).
    // 처음엔 사용자가 지목한 SM_ArchFloor_01a11(3)을 앵커로 썼는데, 그건 "같은 Y축"을 짚어준 것이라
    // 높이 기준이었고 XZ 위상은 별개였다. 그 결과 두 격자가 X 0.70m / Z 1.77m 어긋나 63쌍이 겹쳤다.
    // 이미 정확한 쪽에 맞추는 것이 이동량도 최소다.
    private static readonly Vector2 GridAnchor = new(68.48f, 2.53f);
    private const string FloorPrefab = "Assets/RelicFairy/_Imported/Gothic_Interior/Environment/Asset/Prefabs/SM_ArchFloor_01a.prefab";
    private const string GridRootName = "PlazaFloor_Grid";

    [MenuItem("RelicFairy/BaseCamp/7. 광장 바닥 격자 재구축 (미리보기)")]
    public static void RegridPreview() => Regrid(false);

    [MenuItem("RelicFairy/BaseCamp/8. 광장 바닥 격자 재구축 (실행)")]
    public static void RegridApply() => Regrid(true);

    private static void Regrid(bool apply)
    {
        CollectBaseFloorTiles(out var movable, out var keep);
        if (movable.Count + keep.Count == 0) { Debug.LogError("[Regrid] 기준층 바닥 타일을 찾지 못했다."); return; }

        // footprint는 '움직일 것 + 남길 것' 전체가 덮는 영역이다.
        var boxes = movable.Concat(keep).Select(t =>
        {
            var s = t.lossyScale;
            var p = t.position;
            return new Rect(p.x - TileSize * 0.5f * s.x, p.z - TileSize * 0.5f * s.z,
                            TileSize * s.x, TileSize * s.z);
        }).ToList();

        // 남길 타일(NaveFloor)이 이미 점유한 격자 셀 — 여기엔 새로 놓지 않는다.
        var occupied = new HashSet<(int, int)>(keep.Select(t => (
            Mathf.RoundToInt((t.position.x - GridAnchor.x) / TileSize),
            Mathf.RoundToInt((t.position.z - GridAnchor.y) / TileSize))));

        float minX = boxes.Min(b => b.xMin), maxX = boxes.Max(b => b.xMax);
        float minZ = boxes.Min(b => b.yMin), maxZ = boxes.Max(b => b.yMax);

        int n0 = Mathf.FloorToInt((minX - GridAnchor.x) / TileSize);
        int n1 = Mathf.CeilToInt((maxX - GridAnchor.x) / TileSize);
        int m0 = Mathf.FloorToInt((minZ - GridAnchor.y) / TileSize);
        int m1 = Mathf.CeilToInt((maxZ - GridAnchor.y) / TileSize);

        // 셀별 피복률 — 기존 바닥이 그 셀을 얼마나 덮고 있었나
        var coverage = new Dictionary<(int, int), float>();
        for (int n = n0; n <= n1; n++)
            for (int m = m0; m <= m1; m++)
                coverage[(n, m)] = CellCoverage(GridAnchor.x + n * TileSize, GridAnchor.y + m * TileSize, boxes);

        // 1차 — 절반 이상 덮였으면 채운다. (중심 포함' 규칙은 가장자리에서 너무 빡빡했다.
        //        예: 원본이 x 34.00~43.36을 덮었는데 셀 중심 33.48이 0.52m 차로 탈락 → 모서리에 노치)
        var filled = new HashSet<(int, int)>(occupied);
        foreach (var kv in coverage)
            if (kv.Value >= 0.5f) filled.Add(kv.Key);

        // 2차 — 오목한 노치·구멍만 메운다. 이웃 3칸 이상 = 세 면이 둘러싸인 자리다.
        //        이웃 2칸으로 하면 볼록 모서리(ㄱ자 바깥쪽)까지 끌려와 허공으로 타일이 튀어나온다.
        //        (입구 쪽 33.48/2.53·9.53·30.53, 26.48/23.53 네 장이 실제로 그렇게 튀어나왔다)
        //        더 채울 게 없을 때까지 반복해야 계단형 노치가 한 번에 사라진다.
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var kv in coverage)
            {
                if (filled.Contains(kv.Key) || kv.Value <= 0.05f) continue;
                var (n, m) = kv.Key;
                int nb = 0;
                if (filled.Contains((n - 1, m))) nb++;
                if (filled.Contains((n + 1, m))) nb++;
                if (filled.Contains((n, m - 1))) nb++;
                if (filled.Contains((n, m + 1))) nb++;
                if (nb >= 3) { filled.Add(kv.Key); grew = true; }
            }
        }

        var cells = filled.Where(k => !occupied.Contains(k))
                          .Select(k => new Vector2(GridAnchor.x + k.Item1 * TileSize,
                                                   GridAnchor.y + k.Item2 * TileSize))
                          .ToList();

        Debug.Log($"[Regrid] 교체 {movable.Count}장 · 보존 {keep.Count}장(NaveFloor) → 신규 {cells.Count}장 " +
                  $"= 총 {keep.Count + cells.Count}장 " +
                  $"(앵커 {GridAnchor.x:0.##}/{GridAnchor.y:0.##} · footprint X {minX:0.##}~{maxX:0.##} / Z {minZ:0.##}~{maxZ:0.##}) " +
                  $"{(apply ? "— 실행" : "— 미리보기(변경 없음)")}");
        if (!apply) return;
        var old = movable;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FloorPrefab);
        if (prefab == null) { Debug.LogError($"[Regrid] 타일 프리팹 없음: {FloorPrefab}"); return; }

        var root = GameObject.Find(GridRootName);
        if (root == null) root = new GameObject(GridRootName);
        Undo.RegisterCreatedObjectUndo(root, "Regrid Plaza Floor");
        root.transform.position = Vector3.zero;

        foreach (var t in old) Undo.DestroyObjectImmediate(t.gameObject);

        foreach (var c in cells)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            go.transform.SetPositionAndRotation(new Vector3(c.x, FloorY, c.y), Quaternion.identity);
            go.transform.localScale = Vector3.one;
            go.layer = LayerMask.NameToLayer("Ground");
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ReflectionProbeStatic);
            Undo.RegisterCreatedObjectUndo(go, "Regrid Plaza Floor");
        }
        Debug.Log($"[Regrid] 완료 — '{GridRootName}' 아래 {cells.Count}장 (간격 {TileSize}m · 스케일 1.0 · 겹침 0)");
    }

    // Unity의 Object는 == 을 오버로드해 '파괴됨/없음'을 null처럼 보이게 하지만, C#의 ?? 는
    // 그 오버로드를 타지 않는다. 그래서 `GetComponent<T>() ?? AddComponent<T>()` 는
    // 실제로는 fake-null 인스턴스를 반환해 MissingComponentException 으로 터진다. 명시 비교로 감싼다.
    private static GameObject EnsureObject(string name)
    {
        var go = GameObject.Find(name);
        if (go == null) go = new GameObject(name);
        return go;
    }

    private static T Ensure<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null) c = go.AddComponent<T>();
        return c;
    }

    /// <summary>셀을 0.5m 간격으로 샘플링해 기존 바닥이 덮은 비율을 구한다.</summary>
    private static float CellCoverage(float cx, float cz, List<Rect> boxes)
    {
        int hit = 0, tot = 0;
        const float step = 0.5f;
        for (float z = cz - TileSize * 0.5f + step * 0.5f; z < cz + TileSize * 0.5f; z += step)
            for (float x = cx - TileSize * 0.5f + step * 0.5f; x < cx + TileSize * 0.5f; x += step)
            {
                tot++;
                var p = new Vector2(x, z);
                if (boxes.Any(b => b.Contains(p))) hit++;
            }
        return tot == 0 ? 0f : (float)hit / tot;
    }

    /// <summary>
    /// 기준층(Y≈7.42)의 사각 바닥 타일을 모아 둘로 나눈다.
    ///   keep    — 이미 격자에 정확히 올라가 있고 스케일 1.0인 NaveFloor. 건드리지 않는다.
    ///   movable — 그 외 전부(겹쳐 깔린 것, 스케일로 늘린 것, 격자를 벗어난 NaveFloor 포함).
    /// 곡선 타일(Curv)은 별개 메시라 어느 쪽에도 넣지 않는다.
    /// </summary>
    private static void CollectBaseFloorTiles(out List<Transform> movable, out List<Transform> keep)
    {
        movable = new List<Transform>();
        keep    = new List<Transform>();

        foreach (var tr in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string n = tr.name;
            if (n.Contains("ArchFloorCurv")) continue;
            bool isFloor = n.Contains("SM_ArchFloor") || n.StartsWith("Floor_N_")
                           || n.StartsWith("Floor_S_") || n.StartsWith("NaveFloor");
            if (!isFloor) continue;
            if (Mathf.Abs(tr.position.y - FloorY) > 0.1f) continue;
            if (tr.GetComponent<MeshCollider>() == null) continue;   // LOD 자식이 아니라 타일 루트만

            var s = tr.lossyScale;
            bool unitScale = Mathf.Abs(s.x - 1f) < 0.01f && Mathf.Abs(s.z - 1f) < 0.01f;
            float dx = Mathf.Abs(Mathf.Repeat(tr.position.x - GridAnchor.x + TileSize * 0.5f, TileSize) - TileSize * 0.5f);
            float dz = Mathf.Abs(Mathf.Repeat(tr.position.z - GridAnchor.y + TileSize * 0.5f, TileSize) - TileSize * 0.5f);
            bool onGrid = dx < 0.05f && dz < 0.05f;

            if (n.StartsWith("NaveFloor") && unitScale && onGrid) keep.Add(tr);
            else                                                 movable.Add(tr);
        }
    }
}
#endif
