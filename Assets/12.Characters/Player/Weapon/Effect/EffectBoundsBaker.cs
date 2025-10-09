#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public class EffectBoundsBaker : EditorWindow
{
    GameObject prefab;
    float simulateTime = 1f;  // 전체 재생 시간
    float step = 0.05f;       // 샘플링 간격

    [MenuItem("Tools/Effect/Bake Bounds")]
    static void ShowWindow() => GetWindow<EffectBoundsBaker>("Effect Bounds Baker");

    void OnGUI()
    {
        prefab = (GameObject)EditorGUILayout.ObjectField("Effect Prefab", prefab, typeof(GameObject), false);
        simulateTime = EditorGUILayout.FloatField("Simulate Time", simulateTime);
        step = EditorGUILayout.FloatField("Step (s)", step);

        if (GUILayout.Button("Bake"))
        {
            if (prefab != null)
                BakePrefabBounds();
        }
    }

    void BakePrefabBounds()
    {
        if (prefab == null) return;

        // Prefab 인스턴스 생성
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            // 루트 회전/스케일 초기화
            Vector3 originalRot = go.transform.eulerAngles;
            Vector3 originalScale = go.transform.localScale;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            Bounds totalBounds = new Bounds();
            bool haveBounds = false;

            float elapsed = 0f;
            while (elapsed < simulateTime)
            {
                // Particle 시뮬레이션
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
                    ps.Simulate(elapsed, true, true);

                // TrailRenderer 범위 반영
                foreach (var tr in go.GetComponentsInChildren<TrailRenderer>())
                {
                    Mesh mesh = new Mesh();
                    tr.BakeMesh(mesh, true);
                    Bounds b = mesh.bounds;
                    if (!haveBounds) { totalBounds = b; haveBounds = true; }
                    else totalBounds.Encapsulate(b);
                }

                // Renderer 범위 반영
                Renderer[] rends = go.GetComponentsInChildren<Renderer>(true);
                foreach (var r in rends)
                {
                    Bounds b = r.bounds;
                    if (!haveBounds) { totalBounds = b; haveBounds = true; }
                    else totalBounds.Encapsulate(b);
                }

                elapsed += step;
                EditorApplication.QueuePlayerLoopUpdate();
            }

            // ColliderDataSO 추가/저장
            var ecd = go.GetComponent<EffectColliderData>();
            if (ecd == null) ecd = go.AddComponent<EffectColliderData>();

            ecd.localCenter = go.transform.InverseTransformPoint(totalBounds.center);
            ecd.localSize = totalBounds.size;

            EditorUtility.SetDirty(ecd);

            // Prefab 저장
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.UserAction);

            Debug.Log($"Baked bounds: center={ecd.localCenter}, size={ecd.localSize}");
        }
        finally
        {
            DestroyImmediate(go);
        }
    }
}
#endif
