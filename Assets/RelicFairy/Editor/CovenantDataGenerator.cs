#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 서약 CovenantDataSO 에셋과 CovenantDataTableSO를 일괄 생성한다.
/// RelicFairy/Covenant/Generate Data Assets 메뉴에서 실행.
/// 이미 존재하는 에셋은 덮어쓰지 않는다.
/// </summary>
public static class CovenantDataGenerator
{
    private const string OutputDir = "Assets/RelicFairy/Systems/Covenant/Data";

    [MenuItem("RelicFairy/Gameplay/Covenant/Generate Data Assets")]
    public static void Generate()
    {
        if (!Directory.Exists(OutputDir))
            AssetDatabase.CreateFolder(
                Path.GetDirectoryName(OutputDir),
                Path.GetFileName(OutputDir));

        var entries = new CovenantDataSO[]
        {
            Create("nimue",      "니무에의 서약",
                basic:    new[] { 0.40f, 3f },
                enhanced: new[] { 0.70f, 4f },
                evolved:  new[] { 0.70f, 4f }),

            Create("arthur",     "아서의 서약",
                basic:    new[] { 35f,  0f,    0.30f, 5f },
                enhanced: new[] { 55f,  0.20f, 0.30f, 5f },
                evolved:  new[] { 55f,  0.20f, 0.30f, 5f }),

            Create("morgana",    "모르가나의 서약",
                basic:    new[] { 0.03f, 0f    },
                enhanced: new[] { 0.05f, 0.30f },
                evolved:  new[] { 0.05f, 0.30f }),

            Create("galahad",    "갤러해드의 서약",
                basic:    new[] { 10f, 3f },
                enhanced: new[] {  7f, 3f },
                evolved:  new[] {  7f, 3f }),
        };

        // ── CovenantDataTableSO ──────────────────────────
        var tablePath = $"{OutputDir}/CovenantDataTable.asset";
        var table = AssetDatabase.LoadAssetAtPath<CovenantDataTableSO>(tablePath);
        if (table == null)
        {
            table = ScriptableObject.CreateInstance<CovenantDataTableSO>();
            AssetDatabase.CreateAsset(table, tablePath);
        }

        var so = new SerializedObject(table);
        var prop = so.FindProperty("_entries");
        prop.arraySize = entries.Length;
        for (int i = 0; i < entries.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = entries[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[CovenantDataGenerator] {entries.Length}개 데이터 에셋 + 테이블 생성 완료 → {OutputDir}");
    }

    private static CovenantDataSO Create(
        string id, string displayName,
        float[] basic, float[] enhanced, float[] evolved)
    {
        var path = $"{OutputDir}/CovenantData_{id}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<CovenantDataSO>(path);
        if (existing != null) return existing;

        var data = ScriptableObject.CreateInstance<CovenantDataSO>();
        data.covenantId      = id;
        data.displayName     = displayName;
        data.basicValues     = basic;
        data.enhancedValues  = enhanced;
        data.evolvedValues   = evolved;

        AssetDatabase.CreateAsset(data, path);
        return data;
    }
}
#endif
