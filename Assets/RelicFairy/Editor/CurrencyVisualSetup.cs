using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 10-12 재화 월드 오브젝트(사용자 「재화의 오브젝트가 너무 안 좋아 · 제대로 재화를 표현할 수 있는 오브젝트」) — 후보 표 · 만들기.
///
/// 고른 기준: HUD 재화 아이콘(바탕화면 「재화 UI」 납품 = 정본)과 <b>같은 물건</b>으로 보일 것 — 주운 것이 그 칸으로 날아간다.
/// 네 재화는 실루엣과 빛깔이 모두 달라야 한다(원판 금 · 길쭉한 결정 파랑 · 결정 무더기 초록(10-14 — 예전 병 초록) · 캡슐 보라).
/// 정본 문서: 바탕화면 RelicFairy_문서/구현설계/재화_오브젝트_점검·교체_20261012.md.
///
/// 메뉴 RelicFairy/Setup/10-12 재화 오브젝트 만들기 (편집 모드 · 1회) — 다시 눌러도 같은 결과(재질 · 프리팹 GUID 유지).
///   ① _ThirdParty → _Imported 이동(AssetDatabase.MoveAsset, GUID 보존) — 왕관 금화 메시 하나
///   ② 재질 4(URP/Lit 불투명) · 프리팹 4(콜라이더 · 스크립트 없음 · 피벗 가운데 · 크기 맞춤)
///   ③ 목록 SO + Addressables(Effects / Run/Drops/CurrencyVisualSet)
/// 결과 로그: Temp/currency_setup.txt · 콘솔 「[재화 오브젝트] 끝」.
/// 비교 촬영은 RelicFairy/Debug/10-12 재화 오브젝트 비교 촬영 (테스트 허브, 플레이 중) — 같은 후보 표를 메모리에서만 만든다.
/// </summary>
public static class CurrencyVisualSetup
{
    public  const string OutDir    = "Assets/RelicFairy/Characters/Monster/Monster/Drops/Visuals";
    public  const string SetPath   = OutDir + "/CurrencyVisualSet.asset";
    private const string GroupName = "Effects";
    private const string LogPath   = "Temp/currency_setup.txt";

    private const string Fx      = "Assets/RelicFairy/_Imported/EffectSource/";
    private const string Hovl    = Fx + "Hovl Studio/HSFiles/Models/";
    private const string Msp     = Fx + "MasterStylizedProjectiles/Projectiles/";
    private const string Spells  = "Assets/RelicFairy/_Imported/Spells Pack/Particles/Models/";
    private const string CoinTo  = "Assets/RelicFairy/_Imported/Piloto Studio/Models/Loot/SM_coin_001.fbx";
    private const string CoinFrom = "Assets/_ThirdParty/Piloto Studio/Models/Loot/SM_coin_001.fbx";
    private const string CoinGuid = "65299d968e2524844a66ca3fc2d8337c";
    private const string Rhinox  = "Assets/_ThirdParty/Rhinox Studio/RPG - Stylized Fantasy Environment URP/";

    internal struct Spec
    {
        public string id;              // 비교 이름표 · 파일 이름(영문)
        public CurrencyVisualKind kind;
        public bool   pick;            // 권장안(목록 SO에 들어가는 것)
        public string mesh;            // 모델(FBX) — 지금 자리
        public string meshAlt;         // 이동 전 원래 자리(_ThirdParty) — 없으면 null
        public Vector3 euler;          // 모델을 세우거나 눕히는 회전(모델 자체 축 보정 위에 곱한다)
        public float  size;            // 가장 긴 변(m)
        public string existingMat;     // 팩 재질을 그대로 쓸 때(비교 전용)
        public Color  baseColor;
        public float  metallic, smoothness;
        public Color  emission;        // 검정이면 끔
        public string baseMap, emissionMap, metallicMap;
        public string note;
    }

    private static readonly Color Gold   = new(1.00f, 0.74f, 0.26f);
    private static readonly Color Violet = new(0.42f, 0.26f, 0.95f);
    private static readonly Color Blue   = new(0.30f, 0.55f, 1.00f);
    private static readonly Color OreGreen = new(0.40f, 1.00f, 0.45f);    // HUD 아이콘_원석(초록 결정 무더기)
    private static readonly Color OreGlow  = new(0.06f, 0.55f, 0.14f);
    private static readonly Color OreGlowPick = new(0.10f, 1.00f, 0.22f);   // 권장안 — 옛 초록 병(0.12, 1.3, 0.2)과 비슷한 세기

    /// <summary>후보 표 — 재화마다 권장(pick) 하나 + 비교용. 크기 · 빛은 톤 절제 기준(점광원 없음 · 발광은 바닥에서 읽힐 만큼만).</summary>
    internal static readonly Spec[] Specs =
    {
        // ── 골드 — HUD 「아이콘_골드」 = 왕관 문양 금화 ─────────────────────────
        new Spec { id = "G1_PilotoCrownCoin", kind = CurrencyVisualKind.Gold, pick = true,
                   mesh = CoinTo, meshAlt = CoinFrom, euler = Vector3.zero, size = 0.32f,
                   baseColor = Gold, metallic = 0.8f, smoothness = 0.6f, emission = new Color(0.18f, 0.12f, 0.03f),
                   note = "Piloto Loot SM_coin_001 · 964 tris · 왕관 양각 · 원래 재질은 툰 그라디언트(이동 안 함) → URP/Lit 금" },
        new Spec { id = "G2_RhinoxCoinSheet", kind = CurrencyVisualKind.Gold,
                   mesh = Rhinox + "Models/Chest/Coin.fbx", euler = Vector3.zero, size = 0.5f,
                   existingMat = Rhinox + "Materials/Chest/coin.mat",
                   note = "Rhinox 상자 속 금화 판 · 224 tris · 두께 5 cm 판에 금화 무늬를 구운 것(탈락 근거용)" },

        // ── 심연의 정수 — HUD 「아이콘_정수」 = 황동 마개 유리 캡슐 속 보랏빛 ─────────
        new Spec { id = "E1_MemoryCapsule", kind = CurrencyVisualKind.Essence, pick = true,
                   mesh = Fx + "PixPlays/Components/Meshes/capsule_mesh.fbx", euler = Vector3.zero, size = 0.36f,
                   baseColor = Violet, metallic = 0f, smoothness = 0.88f, emission = new Color(0.5f, 0.28f, 1.2f),
                   note = "PixPlays capsule_mesh · 1160 tris · 아이콘 실루엣(캡슐)만 맞춘 임시 — 황동 마개 · 유리는 요청 에셋(H6)" },
        new Spec { id = "E2_Star", kind = CurrencyVisualKind.Essence,
                   mesh = Msp + "Star/Models/Star5Pointed.fbx", euler = new Vector3(-60f, 0f, 0f), size = 0.34f,
                   baseColor = Violet, metallic = 0f, smoothness = 0.8f, emission = new Color(0.5f, 0.28f, 1.2f),
                   note = "MSP Star5Pointed · 100 tris · 둥근 별(「작은 빛」) — 아이콘과 다른 물건" },
        new Spec { id = "E3_SpikyOrb", kind = CurrencyVisualKind.Essence,
                   mesh = "Assets/_ThirdParty/Piloto Studio/Models/Orbs/Models/SM_SpecialOrb_I_Green.fbx", euler = Vector3.zero, size = 0.32f,
                   baseColor = Violet, metallic = 0f, smoothness = 0.8f, emission = new Color(0.5f, 0.28f, 1.2f),
                   note = "Piloto SpecialOrb · 1080 tris · 가시 구체 — 룬 보상 구체(흰 빛 공)와 실루엣이 겹친다" },

        // ── 강화재료 — HUD 「아이콘_강화재료」 = 길쭉한 푸른 결정 조각 ─────────────
        new Spec { id = "M1_DiamondShard", kind = CurrencyVisualKind.EnhanceMaterial, pick = true,
                   mesh = Msp + "Common/Models/BulletHeadDiamond.fbx", euler = Vector3.zero, size = 0.42f,   // 긴 축(Z)을 바닥에 눕힌다 — 위에서 보는 카메라에 길이가 읽힌다
                   baseColor = Blue, metallic = 0.1f, smoothness = 0.92f, emission = new Color(0.10f, 0.26f, 0.8f),
                   note = "MSP BulletHeadDiamond · 48 tris · 육각 쌍뿔 결정 — 아이콘의 길쭉한 다면 결정에 가장 가깝다" },
        new Spec { id = "M2_HovlShard", kind = CurrencyVisualKind.EnhanceMaterial,
                   mesh = Hovl + "HOVLCrystalPiese.fbx", euler = new Vector3(90f, 0f, 0f), size = 0.42f,
                   baseColor = Blue, metallic = 0.1f, smoothness = 0.92f, emission = new Color(0.10f, 0.26f, 0.8f),
                   note = "Hovl HOVLCrystalPiese · 14 tris · 정제소 수정 정령의 조각과 같은 메시(정제소 = 원석과 헷갈림)" },
        new Spec { id = "M3_HovlCrystal", kind = CurrencyVisualKind.EnhanceMaterial,
                   mesh = Hovl + "Crystal.fbx", euler = Vector3.zero, size = 0.42f,
                   baseColor = Blue, metallic = 0.1f, smoothness = 0.92f, emission = new Color(0.10f, 0.26f, 0.8f),
                   note = "Hovl Crystal · 8 tris · 사각 쌍뿔 — 면이 너무 적다" },

        // ── 원석(정제소 재료) — HUD 「아이콘_원석」 = 코르크 마개 초록 시험관(10-14부터 초록 결정 무더기 — 아래 O2~O5) ───────────
        new Spec { id = "O1_GreenFlask", kind = CurrencyVisualKind.RuneOre,
                   mesh = Spells + "Potion.FBX", euler = Vector3.zero, size = 0.34f,
                   baseColor = new Color(0.85f, 1f, 0.85f), metallic = 0f, smoothness = 0.6f, emission = new Color(0.12f, 1.3f, 0.2f),
                   baseMap = Spells + "Potion.png", emissionMap = Spells + "Potion_Emission.png", metallicMap = Spells + "Potion_Metalic.png",
                   note = "Spells Pack Potion · 760 tris · 둥근 플라스크(금 넝쿨 무늬) — 시험관이 아니고 체력 물약처럼 읽힐 수 있는 임시 · 정식은 요청 에셋(H7)" },
        // 10-14 사용자 「원석 리소스를 룬 원석처럼 보이는 것으로」 — HUD 아이콘_원석을 초록 결정 무더기(Artsystack Crystal_3)로 바꿨다.
        // 주운 것이 그 칸으로 날아가므로 월드 물체도 결정으로 — 강화재료(길쭉한 푸른 결정 하나)와 실루엣 · 색이 다르게 「무더기 · 초록」.
        new Spec { id = "O2_HovlCrystal", kind = CurrencyVisualKind.RuneOre, pick = true,
                   mesh = Hovl + "HOVLCrystal.fbx", euler = Vector3.zero, size = 0.40f,
                   baseColor = OreGreen, metallic = 0.1f, smoothness = 0.92f, emission = OreGlowPick,
                   note = "Hovl HOVLCrystal · 168 tris · 뾰족한 결정 무더기 — 아이콘(Artsystack Crystal_3 무더기)에 가장 가깝다(10-14 비교 촬영 5번). " +
                          "풀 바닥에서 새싹처럼 읽히지 않게 조금 크게 · 발광은 옛 병과 같은 세기" },
        new Spec { id = "O3_SpellsCrystal", kind = CurrencyVisualKind.RuneOre,
                   mesh = Spells + "Crystal.FBX", euler = Vector3.zero, size = 0.36f,
                   baseColor = OreGreen, metallic = 0.1f, smoothness = 0.9f, emission = OreGlow,
                   baseMap = Spells + "Crystal 2.png", emissionMap = Spells + "Crystal 2_Emission.png",
                   note = "Spells Pack Crystal — 결정 무더기 후보(팩 텍스처)" },
        new Spec { id = "O4_SsepCrystal", kind = CurrencyVisualKind.RuneOre,
                   mesh = Fx + "SpecialSkillsEffectsPack/Models/Crystal.fbx", euler = Vector3.zero, size = 0.36f,
                   baseColor = OreGreen, metallic = 0.1f, smoothness = 0.9f, emission = OreGlow,
                   note = "SpecialSkillsEffectsPack Crystal — 결정 후보" },
        new Spec { id = "O5_PilotoCrystal", kind = CurrencyVisualKind.RuneOre,
                   mesh = "Assets/_ThirdParty/Piloto Studio/Models/fbx_crystal_003.fbx", euler = Vector3.zero, size = 0.36f,
                   baseColor = OreGreen, metallic = 0.1f, smoothness = 0.9f, emission = OreGlow,
                   note = "Piloto fbx_crystal_003 — 결정 무더기 후보(_ThirdParty — 고르면 _Imported로 옮긴다)" },
    };

    // ── 메뉴 ───────────────────────────────────────────────

    [MenuItem("RelicFairy/Setup/10-12 재화 오브젝트 만들기 (편집 모드 · 1회)")]
    private static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[재화 오브젝트] 편집 모드에서 — 플레이 중 Assets 쓰기는 도메인 리로드로 런을 깬다");
            return;
        }

        var sb = new StringBuilder("10-12 재화 오브젝트 만들기\n");
        try
        {
            MoveCoin(sb);
            EnsureFolder(OutDir);

            var set = AssetDatabase.LoadAssetAtPath<CurrencyVisualSetSO>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<CurrencyVisualSetSO>();
                AssetDatabase.CreateAsset(set, SetPath);
            }

            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (var s in Specs)
                {
                    if (!s.pick) continue;
                    var mat = SaveMaterial(s, $"{OutDir}/M_Currency_{s.kind}.mat");
                    if (mat == null) { sb.AppendLine($"{s.kind}: 재질 실패"); continue; }

                    var root = BuildVisual(s, mat, scene, out string info);
                    if (root == null) { sb.AppendLine($"{s.kind}: 모델 없음 — {s.mesh}"); continue; }
                    root.name = $"CurrencyVisual_{s.kind}";
                    string prefabPath = $"{OutDir}/CurrencyVisual_{s.kind}.prefab";
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    Object.DestroyImmediate(root);
                    set.EditorSet(s.kind, prefab);
                    sb.AppendLine($"{s.kind} ← {s.id} · {info} · {prefabPath}");
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }

            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            RegisterAddressable(sb);
        }
        catch (System.Exception e)
        {
            sb.AppendLine("예외: " + e);
        }
        finally
        {
            File.WriteAllText(LogPath, sb.ToString());
            Debug.Log("[재화 오브젝트] 끝\n" + sb);
        }
    }

    // ── 공용(비교 촬영도 쓴다) ──────────────────────────────

    /// <summary>후보의 모델 경로 — 이동 뒤 자리를 먼저, 없으면 원래 자리.</summary>
    internal static string ResolveMesh(in Spec s)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(s.mesh) != null) return s.mesh;
        if (!string.IsNullOrEmpty(s.meshAlt) && AssetDatabase.LoadAssetAtPath<GameObject>(s.meshAlt) != null) return s.meshAlt;
        return null;
    }

    /// <summary>후보 재질 — 메모리에서만 만든다(저장은 <see cref="SaveMaterial"/>). 팩 재질을 쓰는 후보는 그 재질.</summary>
    internal static Material MakeMaterial(in Spec s)
    {
        if (!string.IsNullOrEmpty(s.existingMat)) return AssetDatabase.LoadAssetAtPath<Material>(s.existingMat);

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) return null;
        var m = new Material(shader) { name = "M_Currency_" + s.id };
        m.SetColor("_BaseColor", s.baseColor);
        m.SetFloat("_Metallic", s.metallic);
        m.SetFloat("_Smoothness", s.smoothness);

        var baseMap = Load<Texture2D>(s.baseMap);
        if (baseMap != null) m.SetTexture("_BaseMap", baseMap);

        var metallicMap = Load<Texture2D>(s.metallicMap);
        if (metallicMap != null)
        {
            m.SetTexture("_MetallicGlossMap", metallicMap);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
        }

        if (s.emission.maxColorComponent > 0.001f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", s.emission);
            var emissionMap = Load<Texture2D>(s.emissionMap);
            if (emissionMap != null) m.SetTexture("_EmissionMap", emissionMap);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;   // 굽지 않는다 — 발광은 표면에서만
        }
        return m;
    }

    /// <summary>
    /// 후보 하나를 세운다 — 뿌리(가운데 피벗) 아래 「Body」에 모델 · 재질 · 크기 맞춤. 콜라이더 · 애니메이터는 지운다.
    /// <paramref name="scene"/>을 주면 그 장면에(편집 모드 · 미리보기 장면), 안 주면 지금 장면에(플레이 중 비교 촬영).
    /// </summary>
    internal static GameObject BuildVisual(in Spec s, Material mat, Scene? scene, out string info)
    {
        info = null;
        string path = ResolveMesh(s);
        var model = path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
        if (model == null || mat == null) return null;

        GameObject body;
        if (scene.HasValue)
        {
            body = (GameObject)PrefabUtility.InstantiatePrefab(model, scene.Value);
            PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }
        else
        {
            body = Object.Instantiate(model);
        }
        body.name = "Body";

        var root = new GameObject("CurrencyVisual_" + s.id);
        if (scene.HasValue) SceneManager.MoveGameObjectToScene(root, scene.Value);

        foreach (var c in body.GetComponentsInChildren<Collider>(true))  Object.DestroyImmediate(c);
        foreach (var a in body.GetComponentsInChildren<Animator>(true))  Object.DestroyImmediate(a);
        foreach (var a in body.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(a);

        int tris = 0;
        foreach (var r in body.GetComponentsInChildren<Renderer>(true))
        {
            var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials     = mats;
            r.shadowCastingMode   = ShadowCastingMode.On;   // 바닥에 붙어 보이게(작은 그림자)
            r.receiveShadows      = true;
            if (r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
                for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    tris += (int)(mf.sharedMesh.GetIndexCount(sub) / 3);
        }

        // 축 보정(3ds Max 모델의 뿌리 회전 등)은 그대로 두고 그 위에 세우기 회전을 곱한다
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.Euler(s.euler) * body.transform.localRotation;

        if (!TryBounds(root, out var b) || b.size.sqrMagnitude < 1e-8f)
        {
            Object.DestroyImmediate(root);
            return null;
        }
        float k = s.size / Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        body.transform.localScale *= k;
        TryBounds(root, out b);
        body.transform.localPosition -= b.center - root.transform.position;   // 가운데 피벗

        TryBounds(root, out b);
        info = $"{tris} tris · 크기 {b.size.x:0.00}×{b.size.y:0.00}×{b.size.z:0.00} m · 모델 {Path.GetFileName(path)}";
        return root;
    }

    internal static bool TryBounds(GameObject go, out Bounds b)
    {
        b = default;
        bool has = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }
        return has;
    }

    // ── 내부 ───────────────────────────────────────────────

    /// <summary>왕관 금화 메시를 _ThirdParty → _Imported로 옮긴다(GUID 보존) · 원래 툰 재질 연결을 끊는다(그 재질 사슬은 안 옮긴다).</summary>
    private static void MoveCoin(StringBuilder sb)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(CoinTo) == null)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CoinFrom) == null)
            {
                sb.AppendLine($"금화 메시 없음 — {CoinFrom} · {CoinTo} 둘 다 없다");
                return;
            }
            EnsureFolder(Path.GetDirectoryName(CoinTo).Replace('\\', '/'));
            string err = AssetDatabase.MoveAsset(CoinFrom, CoinTo);
            // MoveAsset이 실패를 말해도 실제로는 옮겨졌을 수 있다(09-18 MCP 함정) — 자리 · GUID로 확인한다
            sb.AppendLine(string.IsNullOrEmpty(err) ? $"이동 {CoinFrom} → {CoinTo}" : $"이동 응답: {err}");
        }
        string guid = AssetDatabase.AssetPathToGUID(CoinTo);
        sb.AppendLine($"금화 메시 GUID {guid} {(guid == CoinGuid ? "= 원래와 같음" : $"≠ 원래 {CoinGuid} ★확인")}");

        if (AssetImporter.GetAtPath(CoinTo) is ModelImporter imp)
        {
            // _ThirdParty 툰 재질(GradModels_Toony) 연결 — 남기면 _ThirdParty가 없는 PC에서 끊긴 참조. 프리팹은 자기 재질을 쓴다
            if (imp.RemoveRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "m_gradient")))
            {
                imp.SaveAndReimport();
                sb.AppendLine("금화 메시의 _ThirdParty 재질 연결(m_gradient → GradModels_Toony) 끊음");
            }
        }
    }

    /// <summary>재질을 저장한다. 이미 있으면 속성만 덮어써 GUID를 지킨다.</summary>
    private static Material SaveMaterial(in Spec s, string path)
    {
        var fresh = MakeMaterial(s);
        if (fresh == null) return null;
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(fresh, path);
            return fresh;
        }
        existing.shader = fresh.shader;
        existing.CopyPropertiesFromMaterial(fresh);
        existing.shaderKeywords = fresh.shaderKeywords;
        existing.globalIlluminationFlags = fresh.globalIlluminationFlags;
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(fresh);
        return existing;
    }

    private static void RegisterAddressable(StringBuilder sb)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { sb.AppendLine("AddressableAssetSettings 없음 — 목록 등록 못 함"); return; }
        var group = settings.FindGroup(GroupName) ?? settings.DefaultGroup;
        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(SetPath), group, false, false);
        entry.address = CurrencyVisuals.SetAddress;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();
        sb.AppendLine($"Addressables {group.Name} / {CurrencyVisuals.SetAddress}");
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static T Load<T>(string path) where T : Object
        => string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
}
