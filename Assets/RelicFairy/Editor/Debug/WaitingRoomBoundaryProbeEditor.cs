#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-11 대기방 경계 띠 실측 — 사용자 「대부분의 대기방들이 해당 테두리 이펙트때문에 보스 입구가 입구처럼 보이지 않아」.
/// 테스트 허브에서 보스 대기방(PreBoss = 손맵 아레나 Road_Boss_ChN)으로 바로 들어가,
/// ① 출구 마커(Exit*)마다 경계 띠(BoundaryEdge_*)가 입구 폭 안을 지나가는지 ② 마커 안쪽 3 m ~ 바깥 4 m를 0.25 m 간격으로
/// 경계 추적기(RoomBoundaryTracer)와 같은 판정(바닥 광선 · 몸 캡슐)으로 재서 처음 막히는 곳과 그 원인(콜라이더 이름 · 층)을 적는다.
/// 인수 Temp/boundary_probe_args.txt 1줄 = 챕터(기본 1). 결과: Temp/boundary_probe.txt · Temp/ui_shots/Boundary_chN.png · 로그 「[BoundaryProbe] 끝」.
/// </summary>
public static class WaitingRoomBoundaryProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string ArgsPath = "Temp/boundary_probe_args.txt";
    // RoomBoundaryTracer와 같은 값
    private const float BodyRadius = 0.27f, BodyLow = 0.4f, BodyHigh = 1.5f, StepMax = 0.4f, RayUp = 1.2f, RayDown = 1.2f;

    [MenuItem("RelicFairy/Debug/10-11 대기방 경계 띠 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BoundaryProbe] 플레이 모드(테스트 허브)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        int ch = 1;
        if (File.Exists(ArgsPath) && int.TryParse(File.ReadAllText(ArgsPath).Trim(), out var c)) ch = Mathf.Clamp(c, 1, 4);
        var sb = new StringBuilder($"대기방 경계 띠 실측 · Ch{ch}\n");
        try
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher != null && GameRunBootstrapper.Instance?.Run?.Player == null)
            {
                launcher.SelectChapter(ch);
                typeof(TestHubLauncher).GetField("_bossApproach", Inst)?.SetValue(launcher, true);
                launcher.TryLaunch();
            }
            float t0 = Time.realtimeSinceStartup;
            ArcaneEdge[] edges = null;
            while (Time.realtimeSinceStartup - t0 < 60f)
            {
                edges = Object.FindObjectsByType<ArcaneEdge>(FindObjectsSortMode.None).Where(e => e.name.StartsWith("BoundaryEdge_")).ToArray();
                if (edges.Length > 0) break;
                await UniTask.Delay(500, DelayType.Realtime);
            }
            await UniTask.Delay(2500, DelayType.Realtime);   // 방 등장 연출

            var arena = typeof(GameRunBootstrapper).GetField("_currentArena", Inst)?.GetValue(GameRunBootstrapper.Instance) as Transform;
            sb.AppendLine($"아레나 {(arena != null ? arena.name : "없음(격자 방)")} · 경계 띠 {edges?.Length ?? 0}개");
            if (arena == null || edges == null || edges.Length == 0) return;

            int mask = ~LayerMask.GetMask("Player", "Monster", "MonsterHit", "Ignore Raycast", "UI", "TransparentFX", "Water");
            var pathField = typeof(ArcaneEdge).GetField("path", Inst);
            var lines = edges.Select(e => ((Vector3[])pathField.GetValue(e) ?? new Vector3[0]).Select(p => e.transform.TransformPoint(p)).ToArray()).ToList();

            var exits = arena.GetComponentsInChildren<Transform>(true).Where(t => t != arena && t.name.StartsWith("Exit", System.StringComparison.OrdinalIgnoreCase)).ToList();
            sb.AppendLine($"출구 마커 {exits.Count}개");
            foreach (var ex in exits)
            {
                Vector3 fwd = Vector3.ProjectOnPlane(ex.forward, Vector3.up).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, fwd);
                float half = (ex.localScale.x > 0.01f ? ex.localScale.x : 3f) * 0.5f;
                sb.AppendLine($"■ {ex.name} @ {ex.position:F1} · 밖 방향 {fwd:F2} · 입구 반폭 {half:0.0} m");

                // ① 띠가 입구 폭 안을 지나가나 — 마커 기준 좌표(앞 · 옆)로 바꿔 |옆| ≤ 반폭 · |앞| ≤ 1.5 m 안의 띠 점
                int inGap = 0; float nearest = float.MaxValue;
                foreach (var line in lines)
                    foreach (var p in line)
                    {
                        Vector3 d = p - ex.position; d.y = 0f;
                        float f = Vector3.Dot(d, fwd), s = Vector3.Dot(d, side);
                        if (Mathf.Abs(s) <= half && Mathf.Abs(f) <= 1.5f) { inGap++; nearest = Mathf.Min(nearest, Mathf.Abs(f)); }
                    }
                sb.AppendLine($"  입구 폭 안 띠 점 {inGap}개{(inGap > 0 ? $" · 마커에서 앞뒤 {nearest:0.00} m" : "")}{(inGap > 0 ? "  ★입구를 가로지름" : "")}");

                // ② 마커 중심선을 따라 걸어 보며 처음 막히는 곳
                float refY = ex.position.y;
                bool reported = false;
                for (float t = -3f; t <= 4.01f; t += 0.25f)
                {
                    Vector3 q = ex.position + fwd * t;
                    string why = Probe(q, ref refY, mask);
                    if (why == null) continue;
                    sb.AppendLine($"  처음 막힘: 마커 기준 {t:+0.00;-0.00} m · {why}");
                    reported = true;
                    break;
                }
                if (!reported) sb.AppendLine("  안쪽 3 m ~ 바깥 4 m 전부 설 수 있음(막힌 곳 없음)");
            }

            Directory.CreateDirectory("Temp/ui_shots");
            ScreenCapture.CaptureScreenshot($"Temp/ui_shots/Boundary_ch{ch}.png");
            await UniTask.Delay(500, DelayType.Realtime);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/boundary_probe.txt", sb.ToString());
            Debug.Log("[BoundaryProbe] 끝\n" + sb);
        }
    }

    /// <summary>추적기의 Standable과 같은 판정 — 설 수 있으면 null, 아니면 막힌 이유.</summary>
    private static string Probe(Vector3 q, ref float refY, int mask)
    {
        if (!Physics.Raycast(new Vector3(q.x, refY + RayUp, q.z), Vector3.down, out var hit, RayUp + RayDown, mask, QueryTriggerInteraction.Ignore))
            return "바닥 없음(광선 빗나감)";
        if (hit.normal.y < 0.5f) return $"가파른 면 {Name(hit.collider)}";
        if (Mathf.Abs(hit.point.y - refY) > StepMax) return $"높이차 {hit.point.y - refY:+0.00;-0.00} m · {Name(hit.collider)}";
        float y = hit.point.y;
        var cols = Physics.OverlapCapsule(new Vector3(q.x, y + BodyLow + BodyRadius, q.z), new Vector3(q.x, y + BodyHigh - BodyRadius, q.z),
                                          BodyRadius, mask, QueryTriggerInteraction.Ignore);
        if (cols.Length > 0) return "몸이 막힘 · " + string.Join(", ", cols.Take(4).Select(Name));
        refY = y;
        return null;
    }

    private static string Name(Collider c)
    {
        if (c == null) return "?";
        var t = c.transform;
        string path = t.name;
        if (t.parent != null) path = t.parent.name + "/" + path;
        return $"{path}[{LayerMask.LayerToName(c.gameObject.layer)}·{c.GetType().Name}]";
    }
}
#endif
