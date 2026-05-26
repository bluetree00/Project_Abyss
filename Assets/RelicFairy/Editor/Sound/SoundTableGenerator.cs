using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class SoundTableGenerator
{
    private const string DefaultCsvPath   = "Assets/RelicFairy/Systems/Sound/Data/SoundEventTable.csv";
    private const string DefaultAssetPath = "Assets/RelicFairy/Systems/Sound/Generated/SoundEventTable.asset";

    [MenuItem("Tools/Sound/Generate Event Table From CSV")]
    private static void Generate()
    {
        var csvPath = EditorUtility.OpenFilePanel("Select SoundEventTable CSV", "Assets", "csv");
        if (string.IsNullOrEmpty(csvPath)) return;

        var relativePath = "Assets" + csvPath.Substring(Application.dataPath.Length);
        GenerateFromCsv(relativePath, DefaultAssetPath);
    }

    [MenuItem("Tools/Sound/Generate Event Table (Default Path)")]
    private static void GenerateDefault()
    {
        GenerateFromCsv(DefaultCsvPath, DefaultAssetPath);
    }

    private static void GenerateFromCsv(string csvPath, string assetPath)
    {
        if (!File.Exists(csvPath))
        {
            Debug.LogError($"[SoundTableGenerator] CSV not found: {csvPath}");
            return;
        }

        var lines = File.ReadAllLines(csvPath);
        if (lines.Length < 2)
        {
            Debug.LogError("[SoundTableGenerator] CSV is empty or header-only.");
            return;
        }

        var entries = new System.Collections.Generic.List<SoundEventTableSO.Entry>();

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var cols = line.Split(',');
            if (cols.Length < 3) continue;

            var eventId = cols[0].Trim();
            var sfxKey  = cols[1].Trim();
            float volume = float.TryParse(cols[2].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 1f;

            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(sfxKey)) continue;

            entries.Add(new SoundEventTableSO.Entry
            {
                eventId = eventId,
                sfxKey  = sfxKey,
                volume  = Mathf.Clamp01(volume),
            });
        }

        var dir = Path.GetDirectoryName(assetPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

        var so = AssetDatabase.LoadAssetAtPath<SoundEventTableSO>(assetPath)
                 ?? ScriptableObject.CreateInstance<SoundEventTableSO>();

        SerializedObject serialized = new SerializedObject(so);
        var prop = serialized.FindProperty("_entries");
        prop.arraySize = entries.Count;
        for (int i = 0; i < entries.Count; i++)
        {
            var elem = prop.GetArrayElementAtIndex(i);
            elem.FindPropertyRelative("eventId").stringValue = entries[i].eventId;
            elem.FindPropertyRelative("sfxKey").stringValue  = entries[i].sfxKey;
            elem.FindPropertyRelative("volume").floatValue   = entries[i].volume;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();

        if (!AssetDatabase.Contains(so))
            AssetDatabase.CreateAsset(so, assetPath);

        EditorUtility.SetDirty(so);
        AssetDatabase.SaveAssets();

        Debug.Log($"[SoundTableGenerator] {entries.Count}개 항목 생성 완료 → {assetPath}");
        EditorGUIUtility.PingObject(so);
    }
}
