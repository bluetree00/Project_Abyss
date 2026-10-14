#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-13 Ch3 바닥 · 벽 재질 개선(Ch3 점검 문서 §8-3 D6 ①안 · 사용자 「재질도 개선해줘」).
/// <list type="bullet">
/// <item>바닥: 1 m 큐브마다 성당 바닥 한 장(메시 UV)이 붙어 별 무늬가 0.5 m로 잘게 반복됐다 →
/// 같은 키트의 월드 정렬 셰이더(<c>M_Tiles_01a</c>)를 복제해 성당 바닥 01a 텍스처를 4 m 크기로 편다(키트 바닥 메시와 같은 척도 · 큐브 이음매 없음).</item>
/// <item>벽: 벽 재질이 바닥 돌 텍스처(<c>T_StoneFloor_01a</c>)라 마름모 줄무늬로 보였다 → 같은 월드 정렬 돌 벽돌(<c>M_StoneBrick_01a_ScaleBased</c>) 사본.</item>
/// </list>
/// 키트 원본 재질은 보스 대기방 · 아레나도 쓰므로 고치지 않는다 — 새 재질 2개를 만들고 Ch3 블록 프리팹 2개(<c>Block_Floor_Castle</c> · <c>Block_Wall_Castle</c> — Ch3 팔레트만 쓴다)에만 끼운다.
/// 여러 번 눌러도 같다. 로그 「[Ch3Material] 끝」.
/// <para>10-13 Ch4(Fortress · <c>Block_Floor_Throne</c> · <c>Block_Wall_Throne</c>)도 같은 방식 — 벽 <c>M_PillarMiddle_01a</c>는 기둥용 UV 아틀라스라
/// 1 m 큐브를 8 m로 늘린 벽에 세로 줄이 났다 → 월드 정렬 성채 벽돌(<c>M_Brick_01a_ScaleBased</c>) 사본 · 바닥 격자(<c>M_Tiles_01a</c>)는 사본을 가라앉힌다. 로그 「[Ch4Material] 끝」.</para>
/// </summary>
public static class Ch3MaterialUpgradeEditor
{
    private const string GothicMat = "Assets/RelicFairy/_Imported/Gothic_Interior/Environment/Asset/Materials/";
    private const string MatDir    = "Assets/RelicFairy/Systems/Stage/MapGen/Materials/";
    private const string PrefabDir = "Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/";
    private const string WorldTile = "Vector1_1";   // S_OpaqueORMWorldAlign — 월드 정렬 무늬 한 장의 크기(m)
    private const float  FloorTile = 4f;
    // 같은 셰이더의 색 · 매끈함 · 대비 칸(그래프 속성 이름 Color · Smoothness Intensity · ContrastValue)
    private const string BaseColor  = "Color_AF14EE56";
    private const string Smoothness = "Vector1_ED619312";
    private const string Contrast   = "Vector1_eb681cc57c3e46829a9e2c40614de15c";
    private const float  ColorScale = 0.8f, SmoothScale = 0.7f, FloorContrast = 0.8f;
    private const float  Ch4WallTile = 3f;                              // 성채 벽돌 한 장 3 m(벽돌 한 줄 약 0.5 m) — 6 m는 카메라 앞 벽에 벽돌 한두 장만 걸려 민돌처럼 보였다(②d 사진)
    private const float  Ch4WallColorScale = 0.75f, Ch4WallSmoothScale = 0.6f;   // 키트 값(매끈함 1.12)이면 푸른 환경광을 되비쳐 벽이 바닥보다 밝았다

    [MenuItem("RelicFairy/Tools/10-13 Ch3 바닥 · 벽 재질 — 월드 정렬(성당 바닥 4 m · 돌 벽돌)")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[Ch3Material] 플레이 중엔 하지 않는다"); return; }

        var cath  = AssetDatabase.LoadAssetAtPath<Material>(GothicMat + "M_Cathedral_Floor_01a.mat");
        var tiles = AssetDatabase.LoadAssetAtPath<Material>(GothicMat + "M_Tiles_01a.mat");
        var brick = AssetDatabase.LoadAssetAtPath<Material>(GothicMat + "M_StoneBrick_01a_ScaleBased.mat");
        if (cath == null || tiles == null || brick == null) { Debug.LogError("[Ch3Material] 키트 재질을 못 찾았다"); return; }

        var floor = CreateOrUpdate(MatDir + "M_RF_Ch3_Floor_WorldAlign.mat", tiles, m =>
        {
            foreach (var key in cath.GetTexturePropertyNames())
                if (m.HasProperty(key) && cath.GetTexture(key) != null) m.SetTexture(key, cath.GetTexture(key));
            // 색 · 밝기 · 대비 · 2층 섞임도 성당 바닥 값으로(10-13 ②c 사진 — 텍스처만 옮기면 타일 값(색 배율 1 · 대비 1 · 섞임 0.99)이 남아 밝고 대비가 셌다).
            // 같은 키트라 두 셰이더의 속성 이름이 같다. 무늬 크기(WorldTile)만 따로 둔다.
            CopyScalarsAndColors(cath, m);
            if (m.HasProperty(WorldTile)) m.SetFloat(WorldTile, FloorTile);
            // 10-13 ②c2 사진 — 성당 값 그대로면 흰 칸이 푸른 환경광을 되비쳐 화면에서 가장 밝았다(몬스터 · 바닥 예고가 묻힘)
            // → 색 0.8배 · 매끈함 0.7배 · 대비 0.8로 한 단계 가라앉힌다. 무늬 · 텍스처는 그대로.
            SettleFloor(m, cath);
        });
        var wall = CreateOrUpdate(MatDir + "M_RF_Ch3_Wall_StoneBrick.mat", brick, null);

        int a = SetPrefabMaterial(PrefabDir + "Block_Floor_Castle.prefab", floor);
        int b = SetPrefabMaterial(PrefabDir + "Block_Wall_Castle.prefab", wall);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Ch3Material] 끝 — 바닥 {floor.name}(무늬 {FloorTile} m · 색 ×{ColorScale} · 매끈함 ×{SmoothScale} · 대비 {FloorContrast}) 렌더러 {a} · 벽 {wall.name} 렌더러 {b}");
    }

    [MenuItem("RelicFairy/Tools/10-13 Ch4 바닥 · 벽 재질 — 월드 정렬(격자 바닥 가라앉힘 · 성채 벽돌)")]
    private static void RunCh4()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[Ch4Material] 플레이 중엔 하지 않는다"); return; }

        var tiles = AssetDatabase.LoadAssetAtPath<Material>(GothicMat + "M_Tiles_01a.mat");
        var brick = AssetDatabase.LoadAssetAtPath<Material>(GothicMat + "M_Brick_01a_ScaleBased.mat");
        if (tiles == null || brick == null) { Debug.LogError("[Ch4Material] 키트 재질을 못 찾았다"); return; }

        // 바닥 밝기는 재질 손잡이로 안 움직였다(10-13 ②f 견본 5장 — 색 배수 · 금속 · 매끈함 · 색 알파 모두 화면 밝기 차 없음, 조명이 정한다).
        // Ch3와 같은 가라앉힘(대비 · 반사)만 둔다 — 더 어둡게는 챕터 톤(빛 세기 · 노출)에서.
        var floor = CreateOrUpdate(MatDir + "M_RF_Ch4_Floor_Tiles.mat", tiles, m => SettleFloor(m, tiles));
        var wall  = CreateOrUpdate(MatDir + "M_RF_Ch4_Wall_Brick.mat", brick, m =>
        {
            if (m.HasProperty(WorldTile))  m.SetFloat(WorldTile, Ch4WallTile);
            if (m.HasProperty(BaseColor))  m.SetColor(BaseColor, brick.GetColor(BaseColor) * Ch4WallColorScale);
            if (m.HasProperty(Smoothness)) m.SetFloat(Smoothness, brick.GetFloat(Smoothness) * Ch4WallSmoothScale);
        });

        int a = SetPrefabMaterial(PrefabDir + "Block_Floor_Throne.prefab", floor);
        int b = SetPrefabMaterial(PrefabDir + "Block_Wall_Throne.prefab", wall);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Ch4Material] 끝 — 바닥 {floor.name}(색 ×{ColorScale} · 매끈함 ×{SmoothScale} · 대비 {FloorContrast}) 렌더러 {a} · 벽 {wall.name}(무늬 {Ch4WallTile} m · 색 ×{Ch4WallColorScale} · 매끈함 ×{Ch4WallSmoothScale}) 렌더러 {b}");
    }

    /// <summary>바닥 사본을 한 단계 가라앉힌다 — 색 · 매끈함은 <paramref name="src"/> 값의 배수, 대비는 고정값.</summary>
    private static void SettleFloor(Material m, Material src)
    {
        if (m.HasProperty(BaseColor))  m.SetColor(BaseColor, src.GetColor(BaseColor) * ColorScale);
        if (m.HasProperty(Smoothness)) m.SetFloat(Smoothness, src.GetFloat(Smoothness) * SmoothScale);
        if (m.HasProperty(Contrast))   m.SetFloat(Contrast, FloorContrast);
    }

    /// <summary><paramref name="from"/>의 숫자 · 색 속성 중 <paramref name="to"/>에도 있는 것을 옮긴다(무늬 크기 칸은 빼고).</summary>
    private static void CopyScalarsAndColors(Material from, Material to)
    {
        var sh = from.shader;
        for (int i = 0; i < sh.GetPropertyCount(); i++)
        {
            string name = sh.GetPropertyName(i);
            if (name == WorldTile || !to.HasProperty(name)) continue;
            switch (sh.GetPropertyType(i))
            {
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    to.SetFloat(name, from.GetFloat(name));
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    to.SetColor(name, from.GetColor(name));
                    break;
            }
        }
    }

    private static Material CreateOrUpdate(string path, Material src, System.Action<Material> configure)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(src);
            configure?.Invoke(mat);
            AssetDatabase.CreateAsset(mat, path);
        }
        else
        {
            mat.shader = src.shader;
            mat.CopyPropertiesFromMaterial(src);
            configure?.Invoke(mat);
            EditorUtility.SetDirty(mat);
        }
        return mat;
    }

    private static int SetPrefabMaterial(string path, Material mat)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        int n = 0;
        try
        {
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
                n++;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return n;
    }
}
#endif
