#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-11 인트로 의자 바닥 맞춤(1회) — 사용자 「의자들도 떠있어 인트로씬」. 실측(10-11 인트로 아레나 LOD 뜸 실측):
/// 제단 옆 장의자 SM_Bench_01a ×4가 바닥 콜라이더 위 +0.06 m 떠 있고(인트로 변형이 받침 바닥을 끄며 −0.5로 내렸지만 6 cm가 남음),
/// 식탁 의자 2개는 −0.15 m 묻혀 있다. LOD 단계 · 바닥 LOD 높이는 모두 같다(거리 탓이 아님 — 먼 곳에선 SSAO 절반 해상도로 틈이 더 드러난다).
/// 변형 프리팹 Arena_Boss_Ch3_Intro를 열어 의자류마다 LOD0 바닥 다섯 점 아래 바닥 면을 재고, 어긋남이 3 cm 이상이면 그만큼 루트를 옮겨 저장한다.
/// 원본 Arena_Boss_Ch3는 건드리지 않는다(변형에 위치 덮어쓰기로만 남는다). 결과: Temp/seat_ground_fix.txt · 로그 「[SeatFix] 끝」.
/// </summary>
public static class IntroSeatGroundFixEditor
{
    private const string VariantPath = "Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/Arena_Boss_Ch3_Intro.prefab";
    private const float  Threshold   = 0.03f;

    [MenuItem("RelicFairy/Debug/10-11 인트로 의자 바닥 맞춤 (편집 모드, 1회)")]
    private static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[SeatFix] 편집 모드에서"); return; }
        var sb = new StringBuilder("인트로 의자 바닥 맞춤\n");
        GameObject root = null;
        int moved = 0;
        try
        {
            root = PrefabUtility.LoadPrefabContents(VariantPath);
            var phys = root.scene.GetPhysicsScene();
            Physics.SyncTransforms();
            foreach (var g in root.GetComponentsInChildren<LODGroup>(true))
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(g.name, "Pew|Bench|Chair|Seat|Stall|Choir|Stool|Throne", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                var l0 = g.GetLODs()[0].renderers.Where(r => r != null).ToArray();
                if (l0.Length == 0) continue;
                var bb = l0[0].bounds; foreach (var r in l0) bb.Encapsulate(r.bounds);
                var gaps = new List<float>();
                foreach (var off in new[] { Vector2.zero, new Vector2(-0.4f, -0.4f), new Vector2(0.4f, -0.4f), new Vector2(-0.4f, 0.4f), new Vector2(0.4f, 0.4f) })
                {
                    var from = new Vector3(bb.center.x + bb.extents.x * off.x, bb.min.y + 0.3f, bb.center.z + bb.extents.z * off.y);
                    var hs = new RaycastHit[8];
                    int hn = phys.Raycast(from, Vector3.down, hs, 3f, ~0, QueryTriggerInteraction.Ignore);
                    float best = float.NaN;
                    for (int h = 0; h < hn; h++)
                    {
                        if (hs[h].collider.transform.IsChildOf(g.transform)) continue;
                        if (float.IsNaN(best) || hs[h].distance < best) best = hs[h].distance;
                    }
                    if (!float.IsNaN(best)) gaps.Add(bb.min.y - (from.y - best));
                }
                if (gaps.Count < 3) { sb.AppendLine($"  {g.name} · 바닥을 못 찾음 — 그대로"); continue; }
                gaps.Sort();
                float gap = gaps[gaps.Count / 2];   // 가운데값 — 한 귀가 턱에 걸려도 흔들리지 않게
                if (Mathf.Abs(gap) < Threshold) continue;
                var t = g.transform;
                float before = t.position.y;
                t.position -= new Vector3(0f, gap, 0f);
                moved++;
                sb.AppendLine($"  {g.name} · 틈 {gap:+0.00;-0.00} m → 루트 y {before:0.000} → {t.position.y:0.000}");
            }
            if (moved > 0) PrefabUtility.SaveAsPrefabAsset(root, VariantPath);
            sb.AppendLine($"옮김 {moved}개{(moved > 0 ? " · 변형 저장" : " · 저장 안 함")}");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (root != null) PrefabUtility.UnloadPrefabContents(root);
            File.WriteAllText("Temp/seat_ground_fix.txt", sb.ToString());
            Debug.Log("[SeatFix] 끝\n" + sb);
        }
    }
}
#endif
