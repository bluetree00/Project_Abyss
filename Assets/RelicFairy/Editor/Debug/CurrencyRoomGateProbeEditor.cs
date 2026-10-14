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
/// 10-10 재화 방(상점 · 재련소 · 정제소) 문턱 · 묶음 실측 — 테스트 허브 Ch1 런의 실제 방 풀 · 런 구조로
/// 시드 300개를 굴려(문은 무작위로 고른다) 재화 방이 몇 번째 전투방부터 나오는지, 재화 방이 연달아 나오는지 센다.
/// 문턱을 끈 판(옛 동작)과 나란히 적는다. 결과: Temp/currency_gate_probe.txt · 로그 「[CurrencyGate] 끝」.
/// </summary>
public static class CurrencyRoomGateProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly RoomPlanKind[] Currency = { RoomPlanKind.Shop, RoomPlanKind.Crucible, RoomPlanKind.Refinery };

    [MenuItem("RelicFairy/Debug/10-10 재화 방 문턱 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[CurrencyGate] 플레이 모드(테스트 허브)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("재화 방 문턱 · 묶음 (Ch1, 시드 300 · 문은 무작위)\n");
        try
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher != null && GameRunBootstrapper.Instance?.Run?.Player == null)
            {
                typeof(TestHubLauncher).GetField("_chapter", Inst)?.SetValue(launcher, 1);
                typeof(TestHubLauncher).GetField("_bossApproach", Inst)?.SetValue(launcher, false);
                launcher.TryLaunch();
            }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 60f && GameRunBootstrapper.Instance?.Run?.Player == null)
                await UniTask.Delay(500, DelayType.Realtime);
            // 절차 방 진행은 시작방 게이트를 지나야 생긴다 — 서비스 방 둘러보기 도구의 진입 절차를 그대로 쓴다.
            var ensure = typeof(ServiceRoomTourProbeEditor).GetMethod("EnsureProcRunAsync", BindingFlags.Static | BindingFlags.NonPublic);
            if (ensure != null) await (UniTask<bool>)ensure.Invoke(null, null);
            var flow = RunFlowController.Active;
            if (flow == null) { sb.AppendLine("절차 방 진행이 시작되지 않음"); return; }
            var pool = typeof(RunFlowController).GetField("_pool", Inst)?.GetValue(flow) as List<ZonePoolEntry>;
            var cfg  = typeof(RunFlowController).GetField("_resolvedStructure", Inst)?.GetValue(flow) as IRunStructure;
            if (pool == null || cfg == null) { sb.AppendLine("방 풀 · 런 구조를 못 읽었다"); return; }
            sb.AppendLine($"보스까지 전투방 {cfg.BossThreshold}");

            foreach (bool gate in new[] { false, true })
            {
                var first = Currency.ToDictionary(k => k, _ => new List<int>());
                int early = 0, back2back = 0, offered = 0, runs = 300;
                var rng = new System.Random(7);
                for (int seed = 1; seed <= runs; seed++)
                {
                    var seq = new RunSequencer(pool, cfg, seed) { GateCurrencyRooms = gate };
                    var seen = new HashSet<RoomPlanKind>();
                    bool lastCurrency = false;
                    for (int guard = 0; guard < 64 && !seq.IsDone && !seq.InBossApproach; guard++)
                    {
                        int progress = seq.BossProgress;
                        var exits = seq.RollExits();
                        if (exits.Count == 0) break;
                        foreach (var d in exits)
                        {
                            if (!Currency.Contains(d.kind)) continue;
                            offered++;
                            if (seen.Add(d.kind)) first[d.kind].Add(progress);
                            int need = d.kind == RoomPlanKind.Refinery ? 3 : 2;
                            if (progress < need) early++;
                            if (lastCurrency) back2back++;
                        }
                        var pick = exits[rng.Next(exits.Count)];
                        lastCurrency = Currency.Contains(pick.kind);
                        seq.CommitEntry(pick);
                    }
                }
                sb.AppendLine(gate ? "■ 문턱 켬(새 동작)" : "■ 문턱 끔(옛 동작)");
                foreach (var k in Currency)
                {
                    var l = first[k];
                    string dist = string.Join(" ", Enumerable.Range(0, 7).Select(i => $"{i}:{l.Count(x => x == i)}"));
                    sb.AppendLine($"  {k,-9} 처음 나온 전투방 수 분포 {dist} · 나온 런 {l.Count}/{runs}");
                }
                sb.AppendLine($"  문턱 전에 나온 재화 방 {early}/{offered} · 재화 방 바로 뒤에 또 재화 방 {back2back}");
                if (gate) sb.AppendLine($"CHECK 문턱 전 재화 방 0: {early == 0} · 연달아 0: {back2back == 0}");
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/currency_gate_probe.txt", sb.ToString());
            Debug.Log("[CurrencyGate] 끝\n" + sb);
        }
    }
}
#endif
