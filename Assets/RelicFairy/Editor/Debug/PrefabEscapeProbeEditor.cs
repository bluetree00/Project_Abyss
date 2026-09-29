using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// [실측 도구 · 편집 모드 · 읽기 전용] 프리팹 지형의 <b>떨어지는 틈</b>을 사방 광선으로 찾는다(09-27 베이스캠프 재설계).
/// 기준점마다 몸 높이(바닥 +1 m)에서 1° 간격 360발을 가로로 쏜다. 광선마다
///   · 벽(아무 충돌체)에 닿는 거리까지 0.25 m 간격으로 발밑 바닥을 내려 확인 — 바닥이 끊기면 <b>벽 앞 구멍</b>(떨어짐)
///   · 끝까지 벽에 안 닿으면 <b>열림</b>(경계 없음)
/// 으로 판정해 각도 구간으로 묶어 적는다. 층을 내려가는 입구 쪽은 정상적으로 열림/구멍이 나오니 기준점 위치와 함께 읽는다.
/// 프로젝트 씬·에셋은 건드리지 않는다. 결과: Temp/edgeray_{name}.txt
///
/// 작업 파일 Temp/edgeray_jobs.json:
///   { "jobs": [ { "name": "sanctum", "prefab": "Assets/...prefab",
///                 "labels": ["광장 가운데", ...], "points": [x, y, z,  x, y, z, ...] } ] }   (y는 그 층 바닥 근처)
/// </summary>
public static class PrefabEscapeProbeEditor
{
    private const string JobPath = "Temp/edgeray_jobs.json";

    [Serializable] private class JobFile { public Job[] jobs; }

    [Serializable]
    private class Job
    {
        public string   name;
        public string   prefab;
        public string[] labels;
        public float[]  points;               // x, y, z 반복
        public int      rays      = 360;
        public float    maxDist   = 80f;
        public float    sample    = 0.25f;    // 발밑 확인 간격
        public float    dropLimit = 7f;       // 기준 바닥보다 이만큼 아래까지 바닥이 없으면 구멍
    }

    [MenuItem("RelicFairy/Debug/프리팹 사방 광선 이탈 검사 (편집 모드, 읽기 전용)")]
    private static void Run()
    {
        if (!File.Exists(JobPath)) { Debug.LogWarning("[사방광선] 작업 파일 없음: " + JobPath); return; }
        var file = JsonUtility.FromJson<JobFile>(File.ReadAllText(JobPath));
        int done = 0;
        foreach (var job in file.jobs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(job.prefab);
            if (prefab == null) { Debug.LogWarning("[사방광선] 프리팹 없음: " + job.prefab); continue; }
            Probe(job, prefab);
            done++;
        }
        Debug.Log($"[사방광선] 완료 — {done}건 → {Path.GetFullPath("Temp")}");
    }

    private static void Probe(Job job, GameObject prefab)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Physics.SyncTransforms();

            var cols = go.GetComponentsInChildren<Collider>(true)
                         .Where(c => c != null && c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy)
                         .ToArray();
            var bounds = cols.Select(c => c.bounds).ToArray();

            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder($"[사방광선] {job.name} · 기준점 {job.points.Length / 3} · {job.rays}발 · 최대 {job.maxDist} m\n\n");
            int totalOpen = 0, totalHole = 0;

            for (int p = 0; p + 2 < job.points.Length; p += 3)
            {
                string label = job.labels != null && p / 3 < job.labels.Length ? job.labels[p / 3] : $"#{p / 3}";
                var guess = new Vector3(job.points[p], job.points[p + 1], job.points[p + 2]);
                if (!Cast(cols, bounds, new Ray(guess + Vector3.up * 1.5f, Vector3.down), 4f, out var floor))
                {
                    sb.AppendLine($"■ {label} {guess} — 바닥 없음(기준점 확인)");
                    continue;
                }
                var eye = floor.point + Vector3.up * 1.0f;

                // 광선마다 0 = 막힘(정상) · 1 = 열림 · 2 = 벽 앞 구멍
                var kind = new int[job.rays];
                var dist = new float[job.rays];
                for (int r = 0; r < job.rays; r++)
                {
                    float ang = 360f * r / job.rays;
                    var dir = new Vector3(Mathf.Cos(ang * Mathf.Deg2Rad), 0f, Mathf.Sin(ang * Mathf.Deg2Rad));
                    bool wall = Cast(cols, bounds, new Ray(eye, dir), job.maxDist, out var wh);
                    float reach = wall ? wh.distance : job.maxDist;

                    // 벽까지 발밑 바닥이 이어지는가(벽 바로 앞 반 칸은 벽 밑동이라 뺀다)
                    float hole = -1f;
                    for (float s = job.sample; s < reach - job.sample; s += job.sample)
                    {
                        var at = eye + dir * s;
                        if (!Cast(cols, bounds, new Ray(at, Vector3.down), 1f + job.dropLimit, out _)) { hole = s; break; }
                    }
                    kind[r] = hole >= 0f ? 2 : (wall ? 0 : 1);
                    dist[r] = hole >= 0f ? hole : reach;
                }

                int open = kind.Count(k => k == 1), holes = kind.Count(k => k == 2);
                totalOpen += open; totalHole += holes;
                sb.AppendLine($"■ {label} 바닥 {floor.point.y.ToString("0.00", ci)} ({floor.point.x.ToString("0.0", ci)}, {floor.point.z.ToString("0.0", ci)}) — 열림 {open} · 구멍 {holes}");
                foreach (var (k, a0, a1, dmin) in Runs(kind, dist, job.rays))
                    sb.AppendLine($"    {(k == 1 ? "열림" : "구멍")} {a0}°~{a1}° · 가장 가까운 {dmin.ToString("0.0", ci)} m");
            }

            sb.Insert(0, $"합계 — 열림 {totalOpen} · 구멍 {totalHole}\n");
            File.WriteAllText($"Temp/edgeray_{job.name}.txt", sb.ToString());
            Debug.Log($"[사방광선] {job.name}: 열림 {totalOpen} · 구멍 {totalHole} → Temp/edgeray_{job.name}.txt");
            Object.DestroyImmediate(go);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    /// <summary>같은 판정이 이어지는 각도 구간(막힘 제외)을 묶는다. 0°를 넘어 이어지는 구간도 하나로.</summary>
    private static IEnumerable<(int kind, int a0, int a1, float dmin)> Runs(int[] kind, float[] dist, int n)
    {
        int start = 0;
        while (start < n && kind[start] != 0 && kind[(start - 1 + n) % n] == kind[start]) start++;   // 구간 경계에서 시작
        if (start == n) start = 0;
        for (int i = 0; i < n;)
        {
            int idx = (start + i) % n;
            int k = kind[idx];
            int j = i;
            float dmin = float.MaxValue;
            while (j < n && kind[(start + j) % n] == k) { dmin = Mathf.Min(dmin, dist[(start + j) % n]); j++; }
            if (k != 0) yield return (k, 360 * idx / n, 360 * ((start + j - 1) % n) / n, dmin);
            i = j;
        }
    }

    /// <summary>광선이 지나는 충돌체만 골라(경계 상자 선별) 가장 가까운 맞음을 돌려준다. 광선이 안에서 시작한 충돌체는 맞지 않는다.</summary>
    private static bool Cast(Collider[] cols, Bounds[] bounds, Ray ray, float maxDist, out RaycastHit best)
    {
        best = default;
        float bestDist = maxDist;
        bool any = false;
        for (int i = 0; i < cols.Length; i++)
        {
            if (!bounds[i].IntersectRay(ray, out float enter) || enter > bestDist) continue;
            if (cols[i].Raycast(ray, out var hit, bestDist) && hit.distance < bestDist)
            {
                bestDist = hit.distance;
                best = hit;
                any = true;
            }
        }
        return any;
    }
}
