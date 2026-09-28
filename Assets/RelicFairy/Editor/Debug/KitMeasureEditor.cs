using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// [실측 도구 · 편집 모드 · 읽기 전용] 환경 조각 치수를 잰다(09-26 베이스캠프 재설계 0단계).
/// 고딕 실내 · 판타지 성 팩의 프리팹을 <b>미리보기 씬</b>에 하나씩 띄워 렌더러 경계(크기 · 기준점 대비 위치)와
/// 충돌체 종류, LOD 여부를 적는다. 프로젝트 씬·에셋은 건드리지 않는다. 결과: Temp/kit_bounds.tsv
/// </summary>
public static class KitMeasureEditor
{
    private const string OutPath = "Temp/kit_bounds.tsv";

    private static readonly (string pack, string folder)[] Folders =
    {
        ("gothic", "Assets/RelicFairy/_Imported/Gothic_Interior/Environment/Asset/Prefabs"),
        ("castle", "Assets/RelicFairy/_Imported/LeartesStudios/FantasyCastle/~HDRP/Art/Prefabs"),
    };

    [MenuItem("RelicFairy/Debug/환경 조각 치수 실측 (편집 모드, 읽기 전용)")]
    private static void Run()
    {
        var sb = new StringBuilder("pack\tname\tsizeX\tsizeY\tsizeZ\tminX\tminY\tminZ\tmaxX\tmaxY\tmaxZ\tcolliders\tlod\tpath\n");
        var scene = EditorSceneManager.NewPreviewScene();
        int n = 0;
        try
        {
            foreach (var (pack, folder) in Folders)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    if (go == null) continue;
                    go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    go.transform.localScale = Vector3.one;

                    var rs = go.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
                    Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(Vector3.zero, Vector3.zero);
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    var cols = go.GetComponentsInChildren<Collider>(true)
                                 .GroupBy(c => c.GetType().Name).Select(g => $"{g.Key}×{g.Count()}");
                    bool lod = go.GetComponentInChildren<LODGroup>(true) != null;

                    sb.Append(pack).Append('\t').Append(prefab.name).Append('\t')
                      .Append(F(b.size.x)).Append('\t').Append(F(b.size.y)).Append('\t').Append(F(b.size.z)).Append('\t')
                      .Append(F(b.min.x)).Append('\t').Append(F(b.min.y)).Append('\t').Append(F(b.min.z)).Append('\t')
                      .Append(F(b.max.x)).Append('\t').Append(F(b.max.y)).Append('\t').Append(F(b.max.z)).Append('\t')
                      .Append(string.Join(" ", cols)).Append('\t').Append(lod ? "LOD" : "").Append('\t').Append(path).Append('\n');
                    Object.DestroyImmediate(go);
                    n++;
                }
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        File.WriteAllText(OutPath, sb.ToString());
        Debug.Log($"[조각치수] 완료 — {n}개 → {Path.GetFullPath(OutPath)}");
    }

    private static string F(float v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
