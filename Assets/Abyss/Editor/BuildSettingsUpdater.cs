#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class BuildSettingsUpdater
{
    [MenuItem("Abyss/Update Build Settings")]
    public static void UpdateBuildSettings()
    {
        var keep = new[]
        {
            "Assets/Abyss/Scenes/Logo.unity",
            "Assets/Abyss/Scenes/Lobby.unity",
            "Assets/Abyss/Scenes/Tutorial.unity",
            "Assets/Abyss/Scenes/BaseCamp.unity",
            "Assets/Abyss/Scenes/GameScene.unity",
            "Assets/Abyss/Scenes/StartScene.unity",
            "Assets/Abyss/Scenes/GameScene_Ch1.unity",
            "Assets/Abyss/Scenes/GameScene_Ch2.unity",
            "Assets/Abyss/Scenes/GameScene_Ch3.unity",
            "Assets/Abyss/Scenes/GameScene_Ch4.unity",
        };

        var scenes = new List<EditorBuildSettingsScene>();
        foreach (var path in keep)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogWarning($"[BuildSettingsUpdater] Scene not found: {path}");
                continue;
            }
            scenes.Add(new EditorBuildSettingsScene(path, true));
        }

        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log($"[BuildSettingsUpdater] Build Settings updated: {scenes.Count} scenes.");
        foreach (var s in scenes)
            Debug.Log($"  [{System.Array.IndexOf(scenes.ToArray(), s)}] {s.path}");
    }
}
#endif
