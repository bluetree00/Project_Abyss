#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class AbyssSceneSetup
{
    private static readonly string[] ScenePaths =
    {
        "Assets/Abyss/Scenes/Tutorial.unity",
        "Assets/Abyss/Scenes/BaseCamp.unity",
        "Assets/Abyss/Scenes/GameScene_Ch1.unity",
        "Assets/Abyss/Scenes/GameScene_Ch2.unity",
        "Assets/Abyss/Scenes/GameScene_Ch3.unity",
        "Assets/Abyss/Scenes/GameScene_Ch4.unity",
    };

    [MenuItem("Abyss/Setup New Scenes (Camera + Light)")]
    public static void SetupAllNewScenes()
    {
        foreach (var path in ScenePaths)
        {
            // 새 빈 씬 생성 — 기존 dirty 씬의 저장 다이얼로그 없이 진행
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags  = CameraClearFlags.Skybox;
            cam.fieldOfView = 60f;
            camGo.transform.SetPositionAndRotation(new Vector3(0, 1, -10), Quaternion.identity);
            camGo.AddComponent<UniversalAdditionalCameraData>();

            var lightGo = new GameObject("Directional Light");
            var light   = lightGo.AddComponent<Light>();
            light.type      = LightType.Directional;
            light.intensity = 1f;
            light.color     = new Color(1f, 0.956f, 0.839f);
            lightGo.transform.SetPositionAndRotation(
                new Vector3(0, 3, 0),
                Quaternion.Euler(50f, -30f, 0f));

            bool saved = EditorSceneManager.SaveScene(scene, path);
            if (saved)
                Debug.Log($"[AbyssSceneSetup] Done: {path}");
            else
                Debug.LogWarning($"[AbyssSceneSetup] Save failed: {path}");
        }

        AssetDatabase.Refresh();
        Debug.Log("[AbyssSceneSetup] All scenes setup complete.");
    }
}
#endif
