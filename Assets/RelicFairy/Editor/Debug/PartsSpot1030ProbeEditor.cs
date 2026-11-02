#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-30 사용자 「예전 파츠 공방 자리가 제대로 정리되지 않았다」 — 열린 거점 씬에서 옛 파츠 공방 자리(월드 97.2, 11.4, -187.3) 둘레 6 m 안의
/// 렌더러 · 빛 · 입자 · 콜라이더 · 이름표를 경로와 함께 적는다(편집 모드). 결과 Temp/parts_spot_1030.txt · 로그 「[PS30] 끝」.
/// </summary>
public static class PartsSpot1030ProbeEditor
{
    private static readonly Vector3 Spot = new(97.2f, 11.4f, -187.3f);
    private const float Radius = 6f;

    [MenuItem("RelicFairy/Debug/10-30 옛 파츠 공방 자리 둘레 목록 (Edit)")]
    private static void Run()
    {
        var sb = new StringBuilder($"옛 파츠 공방 자리 {Spot} · 반지름 {Radius} m\n");
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Vector3 p = t.position;
            var r = t.GetComponent<Renderer>();
            if (r != null) p = r.bounds.center;
            Vector3 d = p - Spot; d.y = 0f;
            if (d.magnitude > Radius) continue;
            var comps = new StringBuilder();
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) { comps.Append("Missing "); continue; }
                if (c is Transform) continue;
                comps.Append(c.GetType().Name).Append(' ');
            }
            if (comps.Length == 0) continue;
            string src = PrefabUtility.IsPartOfPrefabInstance(t) ? $" ← {Path.GetFileName(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t))}" : "";
            sb.AppendLine($"{d.magnitude:0.0} m · {(t.gameObject.activeInHierarchy ? "켜짐" : "꺼짐")} · {PathOf(t)} · {comps}{src}");
        }
        File.WriteAllText("Temp/parts_spot_1030.txt", sb.ToString(), new UTF8Encoding(false));
        Debug.Log("[PS30] 끝\n" + sb);
    }

    private static string PathOf(Transform t)
    {
        var s = t.name;
        for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }
}
#endif
