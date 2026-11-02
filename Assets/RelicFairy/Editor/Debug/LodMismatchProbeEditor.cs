#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 11-02 Q1 사용자 「인트로 입구 바닥이 거리에 따라 바뀐다 · 챕터 3 보스(방)도」 · Q24 「모드레드 자리 바닥 타일이 가까이 보면 깨진다」.
/// 플레이 중 지금 씬들의 LODGroup을 훑어 단계(LOD0 · 1 · 2 …)끼리 어긋난 것을 적는다 — 거리가 바뀌면 다른 그림이 되는 원인:
///  · 재질 · 텍스처 · 타일링(_BaseMap_ST · mainTextureScale)이 단계마다 다름
///  · 단계 렌더러의 위치 · 크기(그룹 기준)가 다름(10-14 Gothic_Interior 자식 TRS 깨짐과 같은 부류)
///  · 메시 크기(범위)가 크게 다름
/// 또 플레이어 둘레의 같은 높이에 겹친 바닥 면(z-파이팅 — 「가까이 보면 깨진다」) 후보도 적는다.
/// 결과: Temp/lod_mismatch_{씬}.txt · 로그 「[LodMismatch] 끝」.
/// </summary>
public static class LodMismatchProbeEditor
{
    private const float Radius = 70f;

    [MenuItem("RelicFairy/Debug/11-02 LOD 단계 어긋남 · 겹친 바닥 점검 (지금 씬, Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[LodMismatch] 플레이 중에"); return; }
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        Vector3 at = player != null ? player.transform.position : Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        var sb = new StringBuilder($"LOD 단계 어긋남 — 기준 {at} · 반경 {Radius} m · 씬 {SceneManager.GetActiveScene().name}\n");
        int groups = 0, bad = 0;
        foreach (var g in UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None)
                     .Where(g => g.enabled && g.gameObject.activeInHierarchy)
                     .OrderBy(g => Vector3.Distance(g.transform.position, at)))
        {
            float dist = Vector3.Distance(g.transform.position, at);
            if (dist > Radius) continue;
            groups++;
            var lods = g.GetLODs();
            var rows = new List<string>();
            string baseSig = null; Vector3 baseScale = Vector3.zero, basePos = Vector3.zero, baseSize = Vector3.zero;
            var issues = new List<string>();
            for (int i = 0; i < lods.Length; i++)
            {
                var rs = lods[i].renderers.Where(r => r != null).ToArray();
                if (rs.Length == 0) { rows.Add($"L{i}: 빈 단계(높이 {lods[i].screenRelativeTransitionHeight:0.000})"); continue; }
                var r0 = rs[0];
                string sig = string.Join(" + ", rs.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(MatSig).Distinct());
                Vector3 lp = g.transform.InverseTransformPoint(r0.transform.position);
                Vector3 ls = Div(r0.transform.lossyScale, g.transform.lossyScale);
                Vector3 size = r0.bounds.size;
                string mesh = r0 is MeshRenderer && r0.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null ? mf.sharedMesh.name : r0.GetType().Name;
                rows.Add($"L{i}: {r0.name} · 메시 {mesh} · 위치 {lp:F2} · 크기 {ls:F2} · 범위 {size:F1} · 재질 {sig} · 전환 {lods[i].screenRelativeTransitionHeight:0.000}");
                if (baseSig == null) { baseSig = sig; baseScale = ls; basePos = lp; baseSize = size; continue; }
                if (sig != baseSig) issues.Add($"L{i} 재질/텍스처/타일링 다름");
                if ((lp - basePos).magnitude > 0.02f) issues.Add($"L{i} 위치 {(lp - basePos).magnitude:0.00}");
                if ((ls - baseScale).magnitude > 0.02f) issues.Add($"L{i} 크기 {ls:F2} ≠ {baseScale:F2}");
                if (Mathf.Abs(size.x * size.z - baseSize.x * baseSize.z) > 0.15f * Mathf.Max(0.01f, baseSize.x * baseSize.z)) issues.Add($"L{i} 범위 {size:F1} ≠ {baseSize:F1}");
            }
            if (issues.Count == 0) continue;
            bad++;
            sb.AppendLine($"★ {Path(g.transform)} · 거리 {dist:0.0} m · 월드 {g.transform.position:F1} · {string.Join(" · ", issues)}");
            foreach (var row in rows) sb.AppendLine("     " + row);
        }
        sb.Insert(0, $"요약 — 반경 안 LODGroup {groups} · 어긋남 {bad}\n");

        // 겹친 바닥 면 — 플레이어 둘레 격자로 아래를 쏴 같은 높이(±2 cm) 다른 렌더러 면이 둘 이상인 곳
        sb.AppendLine("\n겹친 바닥 면(같은 높이 ±2 cm에 다른 콜라이더 둘 이상 — z-파이팅 후보, 콜라이더 기준):");
        var seen = new HashSet<string>();
        for (int x = -12; x <= 12; x++)
        for (int z = -12; z <= 12; z++)
        {
            Vector3 o = at + new Vector3(x * 1.5f, 6f, z * 1.5f);
            var hits = Physics.RaycastAll(o, Vector3.down, 14f, ~0, QueryTriggerInteraction.Ignore)
                              .Where(h => h.collider.GetComponentInParent<PlayerController>() == null && h.collider.GetComponentInParent<MonsterBase>() == null)
                              .OrderBy(h => h.distance).ToArray();
            if (hits.Length < 2) continue;
            if (Mathf.Abs(hits[0].point.y - hits[1].point.y) > 0.02f || hits[0].collider == hits[1].collider) continue;
            string key = hits[0].collider.name + "|" + hits[1].collider.name;
            if (!seen.Add(key)) continue;
            sb.AppendLine($"  {o.x:0.0},{o.z:0.0} 높이 {hits[0].point.y:0.000} — {Path(hits[0].collider.transform)}  ↔  {Path(hits[1].collider.transform)} (차 {hits[1].point.y - hits[0].point.y:0.000})");
        }

        string file = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "Temp", $"lod_mismatch_{SceneManager.GetActiveScene().name}.txt");
        File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
        Debug.Log($"[LodMismatch] 끝 — {file} · 어긋남 {bad}/{groups}");
    }

    private static string MatSig(Material m)
    {
        var t = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.HasProperty("_MainTex") ? m.mainTexture : null;
        Vector4 st = m.HasProperty("_BaseMap_ST") ? m.GetVector("_BaseMap_ST") : m.HasProperty("_MainTex_ST") ? m.GetVector("_MainTex_ST") : Vector4.zero;
        return $"{m.name}[{(t != null ? t.name : "-")} {st.x:0.##}x{st.y:0.##}]";
    }

    private static Vector3 Div(Vector3 a, Vector3 b) => new(a.x / Nz(b.x), a.y / Nz(b.y), a.z / Nz(b.z));
    private static float Nz(float v) => Mathf.Abs(v) < 1e-5f ? 1e-5f : v;

    private static string Path(Transform t)
    {
        var parts = new List<string>();
        for (int i = 0; t != null && i < 4; i++, t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
}
#endif
