using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;

namespace Abyss.EditorTools
{
    /// <summary>
    /// 씬의 모든 NavMeshSurface 컴포넌트를 일괄 베이크하는 임시 에디터 툴.
    /// MCP 에서 execute_menu_item 으로 트리거 후 파일 삭제 예정.
    /// </summary>
    internal static class NavMeshBakeTool
    {
        [MenuItem("Abyss/Bake All NavMesh Surfaces In Scene")]
        public static void BakeAll()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning("[NavMeshBakeTool] 활성 씬이 없거나 로드되지 않음.");
                return;
            }

            var surfaces = Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None);
            if (surfaces.Length == 0)
            {
                Debug.LogWarning("[NavMeshBakeTool] 씬에 NavMeshSurface 컴포넌트가 없음.");
                return;
            }

            int baked = 0;
            foreach (var surface in surfaces)
            {
                if (surface == null) continue;
                surface.BuildNavMesh();
                Debug.Log($"[NavMeshBakeTool] Baked: {surface.gameObject.name}");
                baked++;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[NavMeshBakeTool] 베이크 완료: {baked}개 surface, 씬 저장됨.");
        }
    }
}
