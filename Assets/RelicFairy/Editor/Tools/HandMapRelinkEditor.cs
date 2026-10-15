#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 손맵 프리팹(Arena_Boss_Ch3 · Road_Boss_Ch3) 다시 연결 — 10-15 사용자 「깃 용량 한계」 원인 조사 결과.
/// 두 프리팹이 각 239 MB: Gothic_Interior 팩 장식을 프리팹 연결 없이 낱개로 풀어 둔 사본(게임 오브젝트 4만 6천 · 파티클 1103개가 개당 110 KB)이라
/// 한 번 고칠 때마다 LFS에 239 MB가 통째로 쌓였다. 구성이 팩 프리팹과 똑같은 사본을 팩 프리팹 인스턴스로 바꿔 「바뀐 값만」 저장되게 한다.
/// <para>
/// 지문 = 자식 이름 · 컴포넌트 종류 · 메시 · 재질 · 그림자 · LOD 설정 · 콜라이더/파티클 설정(JSON) · 자식 위치(루트 위치 · 이름은 뺀다).
/// 건너뜀 = 다른 자산(인트로 변형 · 씬)이 가리키는 오브젝트 · 게임 스크립트가 참조하는 오브젝트.
/// 저장 전 대조 = 모든 렌더러(메시 · 재질 · 월드 행렬) · 콜라이더(종류 · 월드 경계) · 파티클(월드 위치) 목록이 바꾸기 전과 같아야 저장 — 하나라도 다르면 저장하지 않는다.
/// </para>
/// 결과: Temp/handmap_relink.txt · 로그 「[HandMapRelink] 끝」.
/// </summary>
public static class HandMapRelinkEditor
{
    private const string Tag      = "[HandMapRelink]";
    private const string PackDir  = "Assets/RelicFairy/_Imported/Gothic_Interior/Environment/Asset/Prefabs";
    private static readonly string[] Targets =
    {
        "Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/Arena_Boss_Ch3.prefab",
        "Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/Road_Boss_Ch3.prefab",
    };
    // 이 프리팹들을 품고 오브젝트를 하나씩 가리키는(수정 · 제거) 자산 — 가리킨 오브젝트는 바꾸지 않는다
    private static readonly string[] Referrers =
    {
        "Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/Arena_Boss_Ch3_Intro.prefab",
        "Assets/RelicFairy/Scenes/Build/Game_Intro.unity",
    };

    [MenuItem("RelicFairy/Tools/10-15 손맵 프리팹 다시 연결 — 미리보기 (저장 안 함)")]
    private static void Preview() => Run(save: false);

    [MenuItem("RelicFairy/Tools/10-15 손맵 프리팹 다시 연결 — 적용")]
    private static void Apply() => Run(save: true);

    private static void Run(bool save)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning($"{Tag} 플레이 중엔 하지 않는다"); return; }
        var sb = new StringBuilder($"손맵 프리팹 다시 연결 — {(save ? "적용" : "미리보기")}\n");
        var pack = BuildPackIndex(sb);
        foreach (var path in Targets)
        {
            try { RelinkOne(path, pack, save, sb); }
            catch (System.Exception e) { sb.AppendLine($"  예외 {path}: {e}"); }
        }
        Directory.CreateDirectory("Temp");
        File.WriteAllText("Temp/handmap_relink.txt", sb.ToString());
        Debug.Log($"{Tag} 끝\n{sb}");
    }

    // ── 팩 지문 ─────────────────────────────────────────

    private static int s_maxPackNodes;   // 팩 프리팹 하나의 가장 큰 노드 수 — 이보다 큰 묶음은 지문을 만들지 않는다(「Meshes」 같은 4만 개 묶음)

    private static Dictionary<string, GameObject> BuildPackIndex(StringBuilder sb)
    {
        var map = new Dictionary<string, GameObject>();
        int n = 0;
        s_maxPackNodes = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PackDir }))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go == null) continue;
            n++;
            s_maxPackNodes = Mathf.Max(s_maxPackNodes, go.GetComponentsInChildren<Transform>(true).Length);
            string sig = Sig(go, true);
            if (!map.ContainsKey(sig)) map[sig] = go;
        }
        sb.AppendLine($"팩 프리팹 {n}개 · 지문 {map.Count} · 가장 큰 프리팹 노드 {s_maxPackNodes}");
        return map;
    }

    /// <summary>하위 트리 지문 — 루트는 이름 · 위치를 빼고, 자식은 이름 · 로컬 위치까지.</summary>
    private static string Sig(GameObject go, bool root)
    {
        var sb = new StringBuilder();
        if (!root)
        {
            var t = go.transform;
            sb.Append(go.name).Append('|').Append(V(t.localPosition)).Append(V(t.localEulerAngles)).Append(V(t.localScale)).Append('|');
        }
        foreach (var c in go.GetComponents<Component>())
        {
            if (c == null) { sb.Append("null;"); continue; }
            if (c is Transform) continue;
            sb.Append(c.GetType().Name).Append(':');
            switch (c)
            {
                case MeshFilter mf: sb.Append(Key(mf.sharedMesh)); break;
                case MeshRenderer mr:
                    foreach (var m in mr.sharedMaterials) sb.Append(Key(m)).Append(',');
                    sb.Append((int)mr.shadowCastingMode).Append(mr.receiveShadows ? 1 : 0).Append(mr.enabled ? 1 : 0);
                    break;
                case LODGroup lg:
                    var lods = lg.GetLODs();
                    sb.Append(lods.Length).Append((int)lg.fadeMode).Append(lg.enabled ? 1 : 0);
                    foreach (var l in lods)
                    {
                        sb.Append(l.screenRelativeTransitionHeight.ToString("F4")).Append('[');
                        foreach (var r in l.renderers) sb.Append(r != null ? r.name : "-").Append(',');
                        sb.Append(']');
                    }
                    break;
                case MeshCollider mc:
                    sb.Append(Key(mc.sharedMesh)).Append(mc.convex ? 1 : 0).Append(mc.isTrigger ? 1 : 0).Append(Key(mc.sharedMaterial)).Append(mc.enabled ? 1 : 0);
                    break;
                case ParticleSystemRenderer pr:
                    foreach (var m in pr.sharedMaterials) sb.Append(Key(m)).Append(',');
                    sb.Append(Json(c));
                    break;
                default: sb.Append(Json(c)); break;
            }
            sb.Append(';');
        }
        var kids = new List<string>();
        foreach (Transform ch in go.transform) kids.Add(Sig(ch.gameObject, false));
        kids.Sort(System.StringComparer.Ordinal);
        foreach (var k in kids) sb.Append('{').Append(k).Append('}');
        return sb.ToString();
    }

    private static string V(Vector3 v) => $"{v.x:F3},{v.y:F3},{v.z:F3};";

    private static string Key(Object o)
    {
        if (o == null) return "null";
        return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string g, out long id) ? $"{g}:{id}" : o.name;
    }

    private static readonly Regex InstanceIdRx = new(@"""instanceID"":(-?\d+)");
    private static readonly Regex SeedRx = new(@"""(randomSeed|m_RandomSeed)"":-?\d+");

    /// <summary>
    /// 컴포넌트 설정 JSON — 장면마다 다른 instanceID는 그 대상의 자산 키(GUID:fileID)로, 장면 오브젝트면 이름으로 바꾼다(재질 · 메시가 달라도 같다고 보지 않게).
    /// 무작위 씨앗은 지운다.
    /// </summary>
    private static string Json(Component c)
    {
        string j = EditorJsonUtility.ToJson(c);
        j = InstanceIdRx.Replace(j, m =>
        {
            var o = EditorUtility.InstanceIDToObject(int.Parse(m.Groups[1].Value));
            if (o == null) return "\"ref\":null";
            return AssetDatabase.Contains(o) ? $"\"ref\":\"{Key(o)}\"" : $"\"ref\":\"@{o.name}\"";
        });
        return SeedRx.Replace(j, "\"$1\":0");
    }

    // ── 한 프리팹 ───────────────────────────────────────

    private static void RelinkOne(string path, Dictionary<string, GameObject> pack, bool save, StringBuilder sb)
    {
        long before = new FileInfo(path).Length;
        sb.AppendLine($"\n[{Path.GetFileName(path)}] {before / 1048576f:0.0} MB");
        var protectedPaths = ProtectedPaths(path, sb);

        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var referenced = ReferencedByScripts(root);
            var snapBefore = Snapshot(root);

            // 바깥에서부터 — 바꾼 덩어리의 안쪽은 다시 보지 않는다
            var plan = new List<(GameObject old, GameObject prefab)>();
            int skippedRef = 0, skippedProt = 0;
            var stack = new Stack<Transform>();
            foreach (Transform ch in root.transform) stack.Push(ch);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                var go = t.gameObject;
                if (PrefabUtility.IsPartOfPrefabInstance(go)) continue;
                if (NodeCountAtMost(t, s_maxPackNodes) && pack.TryGetValue(Sig(go, true), out var prefab))
                {
                    if (SubtreeHas(go, x => protectedPaths.Contains(SiblingPath(x.transform, root.transform)))) { skippedProt++; continue; }
                    if (SubtreeHas(go, x => referenced.Contains(x))) { skippedRef++; continue; }
                    plan.Add((go, prefab));
                    continue;
                }
                foreach (Transform ch in t) stack.Push(ch);
            }

            var byPrefab = plan.GroupBy(p => p.prefab.name).OrderByDescending(g => g.Count()).Take(12);
            sb.AppendLine($"  바꿀 덩어리 {plan.Count}개 · 건너뜀(다른 자산이 가리킴 {skippedProt} · 스크립트 참조 {skippedRef})");
            foreach (var g in byPrefab) sb.AppendLine($"    {g.Count(),5} {g.Key}");
            if (!save || plan.Count == 0) return;

            foreach (var (old, prefab) in plan) Replace(old, prefab);

            var snapAfter = Snapshot(root);
            int diff = Diff(snapBefore, snapAfter, sb);
            if (diff > 0) { sb.AppendLine($"  ✗ 대조 불일치 {diff}건 — 저장하지 않는다"); return; }

            PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            long after = new FileInfo(path).Length;
            sb.AppendLine($"  저장 {(ok ? "성공" : "실패")} · {before / 1048576f:0.0} MB → {after / 1048576f:0.0} MB");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void Replace(GameObject old, GameObject prefab)
    {
        var t = old.transform;
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, t.parent);
        inst.name = old.name;
        inst.transform.SetLocalPositionAndRotation(t.localPosition, t.localRotation);
        inst.transform.localScale = t.localScale;
        inst.transform.SetSiblingIndex(t.GetSiblingIndex());
        CopyFlags(old, inst);
        if (inst.activeSelf != old.activeSelf) inst.SetActive(old.activeSelf);
        Object.DestroyImmediate(old);
    }

    /// <summary>레이어 · 태그 · static 플래그를 이름이 같은 자식끼리 옮긴다(NavMesh · 정적 배치가 그대로).</summary>
    private static void CopyFlags(GameObject from, GameObject to)
    {
        if (to.layer != from.layer) to.layer = from.layer;
        if (to.tag != from.tag) to.tag = from.tag;
        var f = GameObjectUtility.GetStaticEditorFlags(from);
        if (GameObjectUtility.GetStaticEditorFlags(to) != f) GameObjectUtility.SetStaticEditorFlags(to, f);
        foreach (Transform fc in from.transform)
        {
            var tc = to.transform.Find(fc.name);
            if (tc != null) CopyFlags(fc.gameObject, tc.gameObject);
            if (tc != null && tc.gameObject.activeSelf != fc.gameObject.activeSelf) tc.gameObject.SetActive(fc.gameObject.activeSelf);
        }
    }

    /// <summary>하위 노드 수가 limit 이하인가 — 넘으면 바로 멈춘다.</summary>
    private static bool NodeCountAtMost(Transform t, int limit)
    {
        int n = 0;
        var st = new Stack<Transform>(); st.Push(t);
        while (st.Count > 0)
        {
            var x = st.Pop();
            if (++n > limit) return false;
            foreach (Transform c in x) st.Push(c);
        }
        return true;
    }

    private static bool SubtreeHas(GameObject go, System.Func<GameObject, bool> pred)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (pred(t.gameObject)) return true;
        return false;
    }

    private static string SiblingPath(Transform t, Transform root)
    {
        var parts = new List<int>();
        for (var x = t; x != null && x != root; x = x.parent) parts.Add(x.GetSiblingIndex());
        parts.Reverse();
        return string.Join("/", parts);
    }

    /// <summary>다른 자산이 이 프리팹 안을 가리키는 오브젝트(수정 · 제거 대상)의 형제 순번 길 — 프리팹 자산에서 fileID로 찾는다.</summary>
    private static HashSet<string> ProtectedPaths(string path, StringBuilder sb)
    {
        var set = new HashSet<string>();
        string guid = AssetDatabase.AssetPathToGUID(path);
        var ids = new HashSet<long>();
        var rx = new Regex(@"fileID: (-?\d+), guid: " + guid);
        foreach (var r in Referrers)
        {
            if (!File.Exists(r)) continue;
            foreach (Match m in rx.Matches(File.ReadAllText(r))) ids.Add(long.Parse(m.Groups[1].Value));
        }
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        int found = 0;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (o == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string _, out long id) || !ids.Contains(id)) continue;
            var go = o as GameObject ?? (o as Component)?.gameObject;
            if (go == null) continue;
            set.Add(SiblingPath(go.transform, asset.transform));
            found++;
        }
        sb.AppendLine($"  다른 자산이 가리키는 fileID {ids.Count} · 이 프리팹에서 찾음 {found}(나머지는 이미 없는 옛 기록)");
        return set;
    }

    /// <summary>게임 스크립트(MonoBehaviour)가 인스펙터로 가리키는 오브젝트 — 바꾸면 참조가 끊긴다.</summary>
    private static HashSet<GameObject> ReferencedByScripts(GameObject root)
    {
        var set = new HashSet<GameObject>();
        foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            var so = new SerializedObject(mb);
            var it = so.GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                var v = it.objectReferenceValue;
                var g = v as GameObject ?? (v as Component)?.gameObject;
                if (g != null && g.scene == root.scene) set.Add(g);
            }
        }
        return set;
    }

    // ── 대조 ────────────────────────────────────────────

    private static Dictionary<string, int> Snapshot(GameObject root)
    {
        var d = new Dictionary<string, int>();
        void Add(string k) { d.TryGetValue(k, out int n); d[k] = n + 1; }
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var sb = new StringBuilder("R:").Append(r.GetType().Name).Append(r.enabled ? 1 : 0).Append(r.gameObject.activeInHierarchy ? 1 : 0).Append('|');
            if (r.TryGetComponent<MeshFilter>(out var mf)) sb.Append(Key(mf.sharedMesh)).Append('|');
            foreach (var m in r.sharedMaterials) sb.Append(Key(m)).Append(',');
            sb.Append('|').Append(M(r.transform.localToWorldMatrix)).Append((int)r.shadowCastingMode);
            Add(sb.ToString());
        }
        foreach (var c in root.GetComponentsInChildren<Collider>(true))
            Add($"C:{c.GetType().Name}{(c.enabled ? 1 : 0)}{(c.gameObject.activeInHierarchy ? 1 : 0)}|{c.isTrigger}|{M(c.transform.localToWorldMatrix)}|{c.gameObject.layer}|{Shape(c)}");
        foreach (var p in root.GetComponentsInChildren<ParticleSystem>(true))
            Add($"P:{M(p.transform.localToWorldMatrix)}|{p.gameObject.activeInHierarchy}");
        foreach (var l in root.GetComponentsInChildren<LODGroup>(true))
            Add($"L:{M(l.transform.localToWorldMatrix)}|{l.GetLODs().Length}|{l.enabled}");
        return d;
    }

    /// <summary>콜라이더 모양 — 자기 값(중심 · 크기 · 반지름 · 메시). 월드 경계(bounds)는 새로 만든 인스턴스에서 물리 동기화 전이라 비어 있어 쓰지 않는다.</summary>
    private static string Shape(Collider c) => c switch
    {
        BoxCollider b     => $"B{V(b.center)}{V(b.size)}",
        SphereCollider s  => $"S{V(s.center)}{s.radius:F3}",
        CapsuleCollider k => $"K{V(k.center)}{k.radius:F3},{k.height:F3},{k.direction}",
        MeshCollider mc   => $"M{Key(mc.sharedMesh)}{(mc.convex ? 1 : 0)}",
        _                 => c.GetType().Name,
    };

    private static string M(Matrix4x4 m)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 12; i++) sb.Append(Mathf.Round(m[i] * 1000f) / 1000f).Append(',');
        return sb.ToString();
    }

    private static int Diff(Dictionary<string, int> a, Dictionary<string, int> b, StringBuilder sb)
    {
        int diff = 0, shown = 0;
        foreach (var k in a.Keys.Union(b.Keys))
        {
            a.TryGetValue(k, out int x); b.TryGetValue(k, out int y);
            if (x == y) continue;
            diff += Mathf.Abs(x - y);
            if (shown++ < 12) sb.AppendLine($"    차이 {x}→{y} {k.Substring(0, Mathf.Min(160, k.Length))}");
        }
        sb.AppendLine($"  대조: 항목 {a.Values.Sum()} → {b.Values.Sum()} · 불일치 {diff}");
        return diff;
    }
}
#endif
