#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 플레이어 의상 설정(10-08 · 기획 플레이어의상_유물별덧입기) — 다시 돌려도 같은 결과.
///  ① 재질 사본: 견습 기사 옷을 유물 색으로(원본 팩 재질은 그대로).
///  ② 유물 의상 에셋 Costume_Gawain · Costume_Lancelot → RelicClass_Gawain · _Lancelot의 costume 칸.
///  ③ PlayerCharacter 프리팹: 0단계(견습 기사 옷 · 갑옷 끔) + PlayerCostume.
/// 망토는 폐기했다(10-10 사용자 「망토는 페기하자」 — 등 뼈 사슬 + 생성 메시로 만든 망토가 판자처럼 보였다). 다시 돌리면 프리팹에 남은 망토를 지운다.
/// </summary>
public static class PlayerCostumeSetup
{
    private const string Dir          = "Assets/RelicFairy/Characters/Player/Costume/";
    private const string PlayerPrefab = "Assets/RelicFairy/Characters/Player/Gawain/Prefabs/PlayerCharacter.prefab";
    private const string SquireSrc    = "Assets/CombatGirlsCharacterPack/CombatGirl_Shield/Materials/Squire_Cloth/Squire_Cloth.mat";
    private const string ArmorMat     = "Assets/CombatGirlsCharacterPack/CombatGirl_Shield/Materials/Cloth/Shield_Sword_Cloth_0{0}.mat";
    private const string RelicClass   = "Assets/RelicFairy/Systems/Relic/Data/RelicClass_{0}.asset";

    private struct RelicLook
    {
        public string id;
        public Color squire;      // 견습 옷 곱하기 색
        public int armor;         // 팩 갑옷 색 번호 — 갑옷이 이 색을 따른다
    }

    private static readonly RelicLook[] Looks =
    {
        // 가웨인(태양 · 정오) — 흰 · 금(갑옷 05)
        new RelicLook { id = "Gawain",   squire = new Color(1f, 0.96f, 0.86f), armor = 5 },
        // 랜슬롯(광기 · 피) — 검 · 숯 · 붉은(갑옷 04)
        new RelicLook { id = "Lancelot", squire = new Color(0.42f, 0.40f, 0.46f), armor = 4 },
    };

    [MenuItem("RelicFairy/Player/Costume Setup (견습 기사 → 기사)")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[PlayerCostume] 편집 모드에서만"); return; }
        if (!AssetDatabase.IsValidFolder(Dir.TrimEnd('/')))
            AssetDatabase.CreateFolder("Assets/RelicFairy/Characters/Player", "Costume");

        foreach (var look in Looks)
        {
            var squire = CopyMaterial(SquireSrc, Dir + $"Squire_{look.id}.mat");
            Tint(squire, look.squire);

            var armor = AssetDatabase.LoadAssetAtPath<Material>(string.Format(ArmorMat, look.armor));

            var costume = LoadOrCreate<RelicCostumeSO>(Dir + $"Costume_{look.id}.asset");
            var so = new SerializedObject(costume);
            so.FindProperty("squireMaterial").objectReferenceValue      = squire;
            so.FindProperty("armorMaterial").objectReferenceValue       = armor;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(costume);

            var relic = AssetDatabase.LoadAssetAtPath<RelicClassSO>(string.Format(RelicClass, look.id));
            if (relic == null) { Debug.LogWarning("[PlayerCostume] 유물 없음: " + look.id); continue; }
            var rso = new SerializedObject(relic);
            rso.FindProperty("costume").objectReferenceValue = costume;
            rso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(relic);
        }
        AssetDatabase.SaveAssets();

        SetupPrefab();
        AssetDatabase.SaveAssets();
        Debug.Log("[PlayerCostume] 설정 끝 — 재질 · 유물 의상 · 프리팹(0단계 + PlayerCostume · 망토 없음)");
    }

    // ── 재질 ──────────────────────────────────────────────────────
    private static Material CopyMaterial(string src, string dst)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(dst);
        if (m != null) return m;
        if (!AssetDatabase.CopyAsset(src, dst)) { Debug.LogWarning("[PlayerCostume] 재질 복사 실패: " + dst); return null; }
        return AssetDatabase.LoadAssetAtPath<Material>(dst);
    }

    /// <summary>견습 옷 — 원본 무늬 위에 유물 색을 곱한다(바탕 · 그늘 둘 다).</summary>
    private static void Tint(Material m, Color c)
    {
        if (m == null) return;
        var src = AssetDatabase.LoadAssetAtPath<Material>(SquireSrc);
        m.SetColor("_BaseColor", c);
        if (src != null && src.HasProperty("_1st_ShadeColor")) m.SetColor("_1st_ShadeColor", src.GetColor("_1st_ShadeColor") * c);
        if (src != null && src.HasProperty("_2nd_ShadeColor")) m.SetColor("_2nd_ShadeColor", src.GetColor("_2nd_ShadeColor") * c);
        EditorUtility.SetDirty(m);
    }

    private static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a != null) return a;
        a = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(a, path);
        return a;
    }

    // ── 프리팹 ────────────────────────────────────────────────────
    private static void SetupPrefab()
    {
        var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
        try
        {
            var byName = new Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name)) byName[t.name] = t;

            // 0단계 = 견습 기사 옷(갑옷 · 갑옷 아래 몸 끔)
            void Set(string n, bool on) { if (byName.TryGetValue(n, out var t)) t.gameObject.SetActive(on); }
            Set("Body_Squire", true);
            Set("Squire_Cloth", true);
            Set("Body_Cloth", false);
            Set("Body_Full", false);
            Set("SwordAndShiled_Cloth_Top", false);
            Set("SwordAndShiled_Cloth_Skirt", false);
            Set("SwordAndShiled_Cloth_Boots", false);
            Set("SwordAndShiled_Clotth_Pauldrons", false);

            // 망토 폐기(10-10) — 예전 설정이 만든 망토 메시 · 뼈 사슬(Cape · Cape_Root · 쇄골에 붙인 양끝 사슬 Cape_C*)을 지운다
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && (t.name == "Cape" || t.name == "Cape_Root" || t.name.StartsWith("Cape_C"))) Object.DestroyImmediate(t.gameObject);

            if (!root.TryGetComponent<PlayerCostume>(out _)) root.AddComponent<PlayerCostume>();
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

}
#endif
