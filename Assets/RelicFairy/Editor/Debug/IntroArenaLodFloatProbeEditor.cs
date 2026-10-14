#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-11 인트로 아레나 「멀리서 뜨고 가까이 가면 붙는」 소품 실측 — 사용자 「의자들도 떠있어 인트로씬 · 근데 가까이가면 붙어」.
/// LOD 단계마다 그리는 렌더러의 바닥 높이가 다르면(LOD 자식 로컬 TRS 어긋남 — 메모 lodgroup_child_offset_corruption)
/// 가까울 땐(LOD0) 붙고 멀어지면(LOD1+) 뜬다. 프리팹을 <b>읽기만</b> 한다(LoadPrefabContents → 저장 없이 Unload).
/// 인수 Temp/lod_probe_args.txt 1줄 = 프리팹 경로(기본 Arena_Boss_Ch3_Intro). 결과: Temp/lod_float_probe.txt · 로그 「[LodFloat] 끝」.
/// </summary>
public static class IntroArenaLodFloatProbeEditor
{
    private const string DefaultPath = "Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/Arena_Boss_Ch3_Intro.prefab";
    private const string ArgsPath    = "Temp/lod_probe_args.txt";
    private const float  Tolerance   = 0.03f;   // 이보다 작은 차이는 무시
    private const int    MaxRows     = 80;

    [MenuItem("RelicFairy/Debug/10-11 인트로 아레나 LOD 뜸 실측 (편집 모드)")]
    private static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[LodFloat] 편집 모드에서"); return; }
        string path = File.Exists(ArgsPath) ? File.ReadAllText(ArgsPath).Trim() : DefaultPath;
        var sb = new StringBuilder($"LOD 뜸 실측 · {path}\n");
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(path);
            var phys = root.scene.GetPhysicsScene();
            Physics.SyncTransforms();

            // 이 변형이 위치를 덮어쓴 Transform(원본 쪽 객체) — 어긋남이 변형에서 왔는지 가른다
            var overridden = new HashSet<Object>();
            foreach (var m in PrefabUtility.GetPropertyModifications(root) ?? new PropertyModification[0])
                if (m.target != null && m.propertyPath.StartsWith("m_Local")) overridden.Add(m.target);

            var rows = new List<(float score, string line)>();
            var seatRows = new List<string>();
            int groups = 0, bad = 0;
            foreach (var g in root.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = g.GetLODs();
                if (lods.Length < 2) continue;
                groups++;
                var r0 = lods[0].renderers.FirstOrDefault(r => r != null);
                if (r0 == null) continue;
                Vector3 p0 = g.transform.InverseTransformPoint(r0.transform.position);
                float worstD = 0f; int worst = 0; Vector3 worstP = p0;
                for (int i = 1; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers)
                    {
                        if (r == null) continue;
                        Vector3 p = g.transform.InverseTransformPoint(r.transform.position);
                        float d = Vector3.Distance(p, p0) * g.transform.lossyScale.y;
                        if (d > worstD) { worstD = d; worst = i; worstP = p; }
                    }
                string name = PathOf(g.transform, root.transform);
                bool seat = System.Text.RegularExpressions.Regex.IsMatch(g.name, "Pew|Bench|Chair|Seat|Stall|Choir|Stool|Throne", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (worstD < Tolerance) { if (seat) seatRows.Add($"{name} · 단계 {lods.Length} · 어긋남 없음"); continue; }
                bad++;
                Vector3 wd = g.transform.TransformPoint(worstP) - g.transform.TransformPoint(p0);
                string ov = "";
                var wr = lods[worst].renderers.FirstOrDefault(r => r != null);
                if (wr != null)
                {
                    var src = PrefabUtility.GetCorrespondingObjectFromSource(wr.transform);
                    if (src != null && overridden.Contains(src)) ov = " · ★변형이 위치를 덮어씀";
                }
                string line = $"{name} · 단계 {lods.Length} · LOD{worst} 자식이 LOD0 자식에서 월드 Δ({wd.x:+0.00;-0.00}, {wd.y:+0.00;-0.00}, {wd.z:+0.00;-0.00}) m{ov}";
                rows.Add((worstD, line));
                if (seat) seatRows.Add(line);
            }
            sb.AppendLine($"LODGroup {groups}개 중 LOD 단계 자식 위치가 LOD0과 어긋난 것 {bad}개(허용 {Tolerance} m)");
            sb.AppendLine($"■ 의자류(Pew · Bench · Chair · Seat · Stall · Choir · Stool · Throne) {seatRows.Count}개");
            foreach (var s in seatRows.Take(MaxRows)) sb.AppendLine("  " + s);
            sb.AppendLine("■ 어긋난 것 전체(큰 순)");
            foreach (var r in rows.OrderByDescending(r => r.score)) sb.AppendLine("  " + r.line);
            // 의자류 단계별 바닥 — 렌더러 bounds 바닥(LOD0 대비)과 전환 높이. 메시 단순화로 다리가 사라지면 먼 단계만 뜬다.
            sb.AppendLine("■ 의자류 단계별 바닥(LOD0 바닥 대비 m) · 전환 화면 높이");
            foreach (var g in root.GetComponentsInChildren<LODGroup>(true))
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(g.name, "Pew|Bench|Chair|Seat|Stall|Choir|Stool|Throne", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                var lods = g.GetLODs();
                float y0 = float.NaN;
                var parts = new List<string>();
                for (int i = 0; i < lods.Length; i++)
                {
                    var rs = lods[i].renderers.Where(r => r != null).ToArray();
                    if (rs.Length == 0) { parts.Add($"L{i} -"); continue; }
                    var b = rs[0].bounds; for (int k = 1; k < rs.Length; k++) b.Encapsulate(rs[k].bounds);
                    if (float.IsNaN(y0)) y0 = b.min.y;
                    var mf = rs[0].GetComponent<MeshFilter>();
                    string mesh = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "?";
                    parts.Add($"L{i} {b.min.y - y0:+0.00;-0.00}({mesh}·{lods[i].screenRelativeTransitionHeight:0.000})");
                }
                // 실제 틈 — LOD0 바닥 네 귀퉁이 · 가운데에서 아래로 쏴서 자기 것이 아닌 첫 면까지
                string gap = "";
                var l0 = lods[0].renderers.Where(r => r != null).ToArray();
                if (l0.Length > 0)
                {
                    var bb = l0[0].bounds; foreach (var r in l0) bb.Encapsulate(r.bounds);
                    var gaps = new List<string>();
                    foreach (var off in new[] { Vector2.zero, new Vector2(-0.4f, -0.4f), new Vector2(0.4f, -0.4f), new Vector2(-0.4f, 0.4f), new Vector2(0.4f, 0.4f) })
                    {
                        var from = new Vector3(bb.center.x + bb.extents.x * off.x, bb.min.y + 0.3f, bb.center.z + bb.extents.z * off.y);
                        var hs = new RaycastHit[8];
                        int hn = phys.Raycast(from, Vector3.down, hs, 3f, ~0, QueryTriggerInteraction.Ignore);
                        float best = float.NaN; string what = ""; Collider bestCol = null;
                        for (int h = 0; h < hn; h++)
                        {
                            if (hs[h].collider.transform.IsChildOf(g.transform)) continue;
                            if (float.IsNaN(best) || hs[h].distance < best) { best = hs[h].distance; what = hs[h].collider.name; bestCol = hs[h].collider; }
                        }
                        gaps.Add(float.IsNaN(best) ? "없음" : $"{(bb.min.y - (from.y - best)):+0.00;-0.00}");
                        // 가운데 아래 바닥의 LOD 단계별 윗면(LOD0 대비) — 먼 단계에서 바닥이 내려가면 의자만 떠 보인다
                        if (off == Vector2.zero && bestCol != null)
                        {
                            var fg = bestCol.GetComponentInParent<LODGroup>();
                            if (fg == null) gaps.Add($"[바닥 {what} · LOD 없음]");
                            else
                            {
                                var fl = fg.GetLODs(); float top0 = float.NaN; var fp = new List<string>();
                                for (int i = 0; i < fl.Length; i++)
                                {
                                    var frs = fl[i].renderers.Where(r => r != null).ToArray();
                                    if (frs.Length == 0) { fp.Add("-"); continue; }
                                    var fb = frs[0].bounds; foreach (var r in frs) fb.Encapsulate(r.bounds);
                                    if (float.IsNaN(top0)) top0 = fb.max.y;
                                    fp.Add($"{fb.max.y - top0:+0.00;-0.00}");
                                }
                                gaps.Add($"[바닥 {fg.name} 윗면 " + string.Join(" ", fp) + $" · 콜라이더 윗면 {bestCol.bounds.max.y - top0:+0.00;-0.00}]");
                            }
                        }
                    }
                    gap = " · 틈(가운데 · 네 귀) " + string.Join(" ", gaps);
                }
                sb.AppendLine($"  {PathOf(g.transform, root.transform)} · 크기 {g.size:0.0}{gap} · " + string.Join(" ", parts.Take(2)));
            }
            sb.AppendLine("■ 이 변형의 위치 덮어쓰기(m_LocalPosition)");
            foreach (var m in PrefabUtility.GetPropertyModifications(root) ?? new PropertyModification[0])
                if (m.propertyPath.StartsWith("m_LocalPosition") && m.target is Transform tt)
                    sb.AppendLine($"  {tt.name} (부모 {(tt.parent != null ? tt.parent.name : "-")}) · {m.propertyPath} = {m.value}");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (root != null) PrefabUtility.UnloadPrefabContents(root);   // 저장하지 않는다
            File.WriteAllText("Temp/lod_float_probe.txt", sb.ToString());
            Debug.Log("[LodFloat] 끝\n" + sb);
        }
    }

    private static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (var c = t; c != null && c != root; c = c.parent) parts.Add(c.name);
        parts.Reverse();
        return string.Join("/", parts.Skip(System.Math.Max(0, parts.Count - 3)));
    }
}
#endif
