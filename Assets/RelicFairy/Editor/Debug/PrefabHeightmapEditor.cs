using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// [실측 도구 · 편집 모드 · 읽기 전용] 프리팹의 <b>걸을 수 있는 바닥 높이</b>를 격자로 잰다(09-27 베이스캠프 재설계).
/// 프리팹을 미리보기 씬에 원점으로 띄우고, 격자 칸마다 위에서 아래로 충돌체에 광선을 쏴 맨 위 표면 높이를 적는다.
/// 계단 조각의 단 높이·오르는 방향 확인, 지은 지형의 구멍·턱 찾기에 쓴다. 프로젝트 씬·에셋은 건드리지 않는다.
///
/// 작업 파일 Temp/heightmap_jobs.json:
///   { "jobs": [ { "name": "stair", "prefab": "Assets/...prefab", "min": [x, z], "max": [x, z], "step": 0.25, "top": 60 } ] }
/// 결과 Temp/heightmap_{name}.tsv — 첫 줄 x 좌표, 각 줄 첫 칸 z 좌표, 값 = 높이(없으면 빈칸). 트리거 충돌체는 뺀다.
/// </summary>
public static class PrefabHeightmapEditor
{
    private const string JobPath = "Temp/heightmap_jobs.json";

    [Serializable] private class JobFile { public Job[] jobs; }

    [Serializable]
    private class Job
    {
        public string  name;
        public string  prefab;
        public float[] min;          // x, z
        public float[] max;          // x, z
        public float   step = 0.25f;
        public float   top  = 60f;   // 광선 시작 높이
        // > 0이면 위→아래 대신 이 높이에서 +X로 쏜다(문 구멍 · 통로가 몸 높이에서 막혔는지). 값 = 처음 맞은 x.
        public float   hy;
    }

    [MenuItem("RelicFairy/Debug/프리팹 바닥 높이맵 (편집 모드, 읽기 전용)")]
    private static void Run()
    {
        if (!File.Exists(JobPath)) { Debug.LogWarning("[높이맵] 작업 파일 없음: " + JobPath); return; }
        var file = JsonUtility.FromJson<JobFile>(File.ReadAllText(JobPath));
        int done = 0;
        foreach (var job in file.jobs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(job.prefab);
            if (prefab == null) { Debug.LogWarning("[높이맵] 프리팹 없음: " + job.prefab); continue; }
            Measure(job, prefab);
            done++;
        }
        Debug.Log($"[높이맵] 완료 — {done}건 → {Path.GetFullPath("Temp")}");
    }

    private static void Measure(Job job, GameObject prefab)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Physics.SyncTransforms();

            var cols = go.GetComponentsInChildren<Collider>(true);
            float step = Mathf.Max(0.05f, job.step);
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("z\\x");
            for (float x = job.min[0]; x <= job.max[0] + 1e-4f; x += step) sb.Append('\t').Append(x.ToString("0.##", ci));
            sb.Append('\n');

            int hits = 0, cells = 0;
            if (job.hy > 0f)
            {
                // 가로 모드: 줄마다(z) min x에서 +X로 한 발 — 첫 칸 헤더는 무시한다.
                sb.Clear().Append("z\tfirst_hit_x\n");
                float len = job.max[0] - job.min[0];
                for (float z = job.min[1]; z <= job.max[1] + 1e-4f; z += step)
                {
                    cells++;
                    var ray = new Ray(new Vector3(job.min[0], job.hy, z), Vector3.right);
                    float best = float.PositiveInfinity;
                    foreach (var c in cols)
                    {
                        if (c == null || !c.enabled || c.isTrigger || !c.gameObject.activeInHierarchy) continue;
                        if (c.Raycast(ray, out var hit, len) && hit.point.x < best) best = hit.point.x;
                    }
                    sb.Append(z.ToString("0.##", ci)).Append('\t');
                    if (!float.IsPositiveInfinity(best)) { sb.Append(best.ToString("0.00", ci)); hits++; }
                    sb.Append('\n');
                }
            }
            else
            for (float z = job.min[1]; z <= job.max[1] + 1e-4f; z += step)
            {
                sb.Append(z.ToString("0.##", ci));
                for (float x = job.min[0]; x <= job.max[0] + 1e-4f; x += step)
                {
                    cells++;
                    var ray = new Ray(new Vector3(x, job.top, z), Vector3.down);
                    float best = float.NegativeInfinity;
                    foreach (var c in cols)
                    {
                        if (c == null || !c.enabled || c.isTrigger || !c.gameObject.activeInHierarchy) continue;
                        if (c.Raycast(ray, out var hit, job.top + 200f) && hit.point.y > best) best = hit.point.y;
                    }
                    sb.Append('\t');
                    if (!float.IsNegativeInfinity(best)) { sb.Append(best.ToString("0.00", ci)); hits++; }
                }
                sb.Append('\n');
            }

            string outPath = $"Temp/heightmap_{job.name}.tsv";
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log($"[높이맵] {job.name}: 충돌체 {cols.Length} · 칸 {cells} · 맞음 {hits} → {outPath}");
            Object.DestroyImmediate(go);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
