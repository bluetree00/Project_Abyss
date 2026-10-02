#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 10-01 짓기 누적 실측 — 전주기 시뮬 2회차에서 Ch2 · Ch3 안에서 방을 지을 때마다 「짓기」가 1초가량씩 늘었다(Ch3 0.9 → 9.4초,
/// 챕터가 바뀌면 원래대로). 방 NavMesh 굽기(BuildMapNavMeshAsync)는 CollectObjects.All이라 장면 전체를 굽는다 —
/// 그래서 방마다 남는 것이 있는지 본다: 같은 챕터 일반 방을 N번 연달아 지어 들어가며, 들어가기 직전마다
/// 굽기 재료(켜진 콜라이더 · Player/Monster 층 제외)를 최상위 부모별로 세고, 입장에 걸린 시간을 잰다.
/// 인자: Temp/build_growth_args.txt — 1줄 방 풀 챕터(기본 2) · 2줄 방 수(기본 8).
/// 결과: Temp/build_growth/&lt;시각&gt;/report.txt. ⚠️ 테스트 허브 런에서만 — 지금 런이 실제로 방을 옮긴다.
/// </summary>
public static class BuildGrowthProbeEditor
{
    private const BindingFlags NonPub = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-01 짓기 누적 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            Debug.LogWarning("[BuildGrowth] 런에 들어간 뒤 실행해야 한다.");
            return;
        }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        int chapter = 2, count = 8;
        string argPath = Path.Combine("Temp", "build_growth_args.txt");
        if (File.Exists(argPath))
        {
            var lines = File.ReadAllLines(argPath);
            if (lines.Length > 0 && int.TryParse(lines[0].Trim(), out int c)) chapter = c;
            if (lines.Length > 1 && int.TryParse(lines[1].Trim(), out int n)) count = n;
        }

        string dir = Path.Combine("Temp", "build_growth", DateTime.Now.ToString("HHmmss"));
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder($"짓기 누적 실측 — 방 풀 Ch{chapter} · 일반 방 {count}개 · 장면 {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}\n");
        bool prevLocal = EditorPrefs.GetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, false);
        EditorPrefs.SetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, true);
        try
        {
            var run = GameRunBootstrapper.Instance.Run;
            var ensure = typeof(ServiceRoomTourProbeEditor).GetMethod("EnsureProcRunAsync", BindingFlags.Static | BindingFlags.NonPublic);
            if (ensure != null) await (UniTask<bool>)ensure.Invoke(null, null);
            var flow  = RunFlowController.Active;
            var enter = typeof(RunFlowController).GetMethod("EnterRoomAsync", NonPub);
            if (flow == null || enter == null) { sb.AppendLine("절차 진행 없음"); return; }

            var pool = await Managers.ZoneLayout.LoadPoolAsync($"CHAPTER_{chapter}_ROOM_POOL");
            var normals = pool?.Where(p => string.Equals(p.category, "Normal", StringComparison.OrdinalIgnoreCase)).ToList();
            if (normals == null || normals.Count == 0) { sb.AppendLine("일반 방이 풀에 없음"); return; }

            for (int i = 0; i < count; i++)
            {
                Snapshot(sb, $"#{i} 들어가기 전");
                var entry = normals[i % normals.Count];
                var stats = run.Player.RuntimeStats;
                stats.SetHp(stats.MaxHp);

                float t0 = Time.realtimeSinceStartup;
                var plan = new DoorPlan { kind = RoomPlanKind.Normal, entry = entry };
                var task = (UniTask)enter.Invoke(flow, new object[] { plan, DoorEdge.North, CancellationToken.None, 0 });
                await UniTask.WhenAny(task, UniTask.Delay(40000, ignoreTimeScale: true));
                sb.AppendLine($"  → {entry.pool_key} 입장 {Time.realtimeSinceStartup - t0:0.00}초");
                await UniTask.Delay(3000, ignoreTimeScale: true);   // 이전 방 나눠 부수기가 끝날 때까지
            }
            Snapshot(sb, "끝");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            EditorPrefs.SetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, prevLocal);
            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString());
            Debug.Log($"[BuildGrowth] 끝 → {dir}/report.txt");
        }
    }

    /// <summary>NavMesh 굽기 재료가 될 켜진 콜라이더를 최상위 부모별로 센다(굽기와 같은 층 제외).</summary>
    private static void Snapshot(StringBuilder sb, string tag)
    {
        int excluded = LayerMask.GetMask("Player", "Monster");
        var byRoot = new Dictionary<string, int>();
        int total = 0;
        foreach (var col in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!col.enabled || ((1 << col.gameObject.layer) & excluded) != 0) continue;
            total++;
            string root = col.transform.root.name;
            byRoot[root] = byRoot.TryGetValue(root, out int n) ? n + 1 : 1;
        }
        int surfaces = UnityEngine.Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None).Length;
        int verts    = NavMesh.CalculateTriangulation().vertices.Length;
        int roots    = UnityEngine.SceneManagement.SceneManager.GetActiveScene().rootCount;
        sb.AppendLine($"{tag}: 콜라이더 {total} · 최상위 {roots} · NavMeshSurface {surfaces} · NavMesh 정점 {verts} · 프레임 {1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime):0}fps");
        sb.AppendLine("    " + string.Join(" · ", byRoot.OrderByDescending(kv => kv.Value).Take(10).Select(kv => $"{kv.Key} {kv.Value}")));
        // 남아 있는 방 — 이름 · 직속 자식 수 · 켜짐 · 그 아래 콜라이더 수(이전 방이 부서지는 중인지 · 안 부서지는지)
        var rooms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                                      .Where(t => t.name.StartsWith("ProcRoom_") || t.name.StartsWith("BlockMap_"));
        sb.AppendLine("    방: " + string.Join(" · ", rooms.Select(t =>
            $"{t.name}(자식 {t.childCount}{(t.gameObject.activeInHierarchy ? "" : " 꺼짐")} · 콜라이더 {t.GetComponentsInChildren<Collider>(true).Length})")));
    }
}
#endif
