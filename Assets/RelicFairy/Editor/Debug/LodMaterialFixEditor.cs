#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q1 사용자 「인트로 입구 바닥이 플레이어 거리에 따라 바뀐다 · 챕터 3 보스(방)도 같이」.
/// 원인(11-02 실측): 바닥 조각 SM_ArchFloor_01a는 원본이 모든 단계 M_Tiles_01a(잔 체크무늬)인데, 아레나에 놓인 사본이 LOD0 · LOD1만
/// M_Cathedral_Floor_01a(큰 마름모 대리석)로 덮어쓰고 LOD2는 그대로 둬 — 가까이는 대리석, 멀리선 잔 체크무늬로 바뀌었다.
/// 보스 아레나 · 길 · 광장 프리팹의 LODGroup을 훑어 단계마다 재질이 LOD0과 다른 것을 적는다(점검).
/// 고치기는 바닥 조각(이름에 Floor · Tile)만 — 먼 단계 렌더러 재질을 LOD0과 같게(서브메시 수가 같을 때만) 맞추고 프리팹을 저장한다.
/// 결과: Temp/lod_material_{점검|고침}.txt · 로그 「[LodMaterial] 끝」.
/// </summary>
public static class LodMaterialFixEditor
{
    private static readonly string[] NameKeys = { "Arena_", "Road_", "Plaza_" };

    [MenuItem("RelicFairy/Debug/11-02 LOD 재질 어긋남 — 보스 아레나 프리팹 점검 (Edit)")]
    private static void Audit() => Run(false);

    [MenuItem("RelicFairy/Debug/11-02 LOD 재질 어긋남 — 바닥 조각 고치기 (Edit)")]
    private static void Fix() => Run(true);

    private static void Run(bool fix)
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[LodMaterial] 편집 모드에서"); return; }
        var sb = new StringBuilder($"LOD 재질 어긋남 — {(fix ? "고침(바닥 조각만)" : "점검")}\n");
        int total = 0, fixedN = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/RelicFairy" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/_Imported/")) continue;
            string file = Path.GetFileNameWithoutExtension(path);
            if (!NameKeys.Any(k => file.StartsWith(k))) continue;

            var root = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;
            int found = 0;
            try
            {
                foreach (var g in root.GetComponentsInChildren<LODGroup>(true))
                {
                    var lods = g.GetLODs();
                    if (lods.Length < 2 || lods[0].renderers == null || lods[0].renderers.Length == 0) continue;
                    var r0 = lods[0].renderers.FirstOrDefault(r => r != null);
                    if (r0 == null) continue;
                    string sig0 = Sig(r0);
                    for (int i = 1; i < lods.Length; i++)
                        foreach (var r in lods[i].renderers)
                        {
                            if (r == null || Sig(r) == sig0) continue;
                            found++; total++;
                            bool floor = g.name.Contains("Floor") || g.name.Contains("Tile");
                            bool canFix = floor && r.sharedMaterials.Length == r0.sharedMaterials.Length;
                            sb.AppendLine($"  {file} · {PathOf(g.transform, root.transform)} · L{i} {r.name}: {Sig(r)} ≠ L0 {sig0} · 월드 {g.transform.position:F1}{(floor ? " · 바닥" : "")}{(fix && canFix ? " → 고침" : "")}");
                            if (fix && canFix) { r.sharedMaterials = r0.sharedMaterials; dirty = true; fixedN++; }
                        }
                }
                if (dirty) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            if (found > 0) sb.AppendLine($"── {path}: {found}{(dirty ? " · 저장" : "")}");
        }
        sb.Insert(0, $"요약 — 어긋난 렌더러 {total}{(fix ? $" · 고침 {fixedN}" : "")}\n");
        string outPath = Path.Combine(Directory.GetCurrentDirectory(), "Temp", $"lod_material_{(fix ? "고침" : "점검")}.txt");
        File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
        if (fix) AssetDatabase.SaveAssets();
        Debug.Log($"[LodMaterial] 끝 — {outPath} · 어긋남 {total}{(fix ? $" · 고침 {fixedN}" : "")}");
    }

    // 11-02 Q1 2차 — 재질은 같은데 무늬 크기가 거리마다 바뀌는 것(사진 02: 같은 잔 타일이 가까이선 4배쯤 크게).
    // 단계마다 메시 UV 밀도(UV 범위 넓이 ÷ 메시 수평 넓이)를 LOD0과 견준다 — 20% 넘게 다르면 적는다. 바닥 같은 것(높이 1.2 m 미만)만.
    [MenuItem("RelicFairy/Debug/11-02 LOD UV 밀도 어긋남 — 보스 아레나 프리팹 점검 (Edit)")]
    private static void AuditUv()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[LodMaterial] 편집 모드에서"); return; }
        var sb = new StringBuilder("LOD UV 밀도 어긋남(바닥) — 단계 UV 밀도 ÷ LOD0" + System.Environment.NewLine);
        var seenMesh = new Dictionary<string, string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/RelicFairy" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/_Imported/")) continue;
            string file = Path.GetFileNameWithoutExtension(path);
            if (!NameKeys.Any(k => file.StartsWith(k))) continue;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var g in root.GetComponentsInChildren<LODGroup>(true))
                {
                    var lods = g.GetLODs();
                    if (lods.Length < 2) continue;
                    var m0 = MeshOf(lods[0]);
                    if (m0 == null || m0.bounds.size.y > 1.2f) continue;
                    float d0 = UvDensity(m0);
                    if (d0 <= 0f) continue;
                    for (int i = 1; i < lods.Length; i++)
                    {
                        var mi = MeshOf(lods[i]);
                        if (mi == null) continue;
                        float di = UvDensity(mi);
                        if (di <= 0f || Mathf.Abs(di / d0 - 1f) < 0.2f) continue;
                        string key = m0.name + "|" + mi.name;
                        string line = $"{m0.name} L{i} {mi.name}: UV 밀도 ×{di / d0:0.00} (무늬 크기 ×{Mathf.Sqrt(d0 / di):0.00})";
                        if (!seenMesh.ContainsKey(key)) seenMesh[key] = line;
                        sb.AppendLine($"  {file} · {PathOf(g.transform, root.transform)} · 월드 {g.transform.position:F1} · {line}");
                    }
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        sb.Insert(0, $"요약 — 어긋난 메시 쌍 {seenMesh.Count}: {string.Join(" / ", seenMesh.Values)}" + System.Environment.NewLine);
        string outPath = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "lod_uv_점검.txt");
        File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
        Debug.Log($"[LodMaterial] UV 끝 — {outPath} · 어긋난 메시 쌍 {seenMesh.Count}");
    }

    private static Mesh MeshOf(LOD lod)
    {
        var r = lod.renderers?.FirstOrDefault(x => x != null);
        return r != null && r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null;
    }

    /// <summary>UV 범위 넓이 ÷ 메시 수평(XZ) 넓이 — 읽을 수 없는 메시는 0.</summary>
    private static float UvDensity(Mesh m)
    {
        Vector2[] uv;
        try { uv = m.uv; } catch { return 0f; }   // 편집 모드에선 Read/Write 꺼진 메시도 읽힌다(플레이 중엔 못 읽음)
        if (uv == null || uv.Length == 0) return 0f;
        Vector2 lo = uv[0], hi = uv[0];
        foreach (var u in uv) { lo = Vector2.Min(lo, u); hi = Vector2.Max(hi, u); }
        float area = m.bounds.size.x * m.bounds.size.z;
        return area > 0.01f ? (hi.x - lo.x) * (hi.y - lo.y) / area : 0f;
    }

    private static string Sig(Renderer r) => string.Join("+", r.sharedMaterials.Select(m => m != null ? m.name : "-"));

    private static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (; t != null && t != root; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
}
#endif
