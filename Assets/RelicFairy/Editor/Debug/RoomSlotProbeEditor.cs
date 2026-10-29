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
/// 10-24 B2 권장안 A 검사 — 재화방(상점 · 재련소 · 정제소)이 정해진 세 칸(앞 · 쉼터 뒤 · 보스 전 마지막)의 [재화 | 전투] 갈림길로만 나오는가.
/// 테스트 허브 Ch1 런의 실제 방 풀 · 런 구조로 시드 300 × 표 문턱(5 · 6) × 정예 보스 유무 × 첫 챕터 여부 × 고르는 버릇(찾아감 · 반반 · 피함)을 굴린다.
/// 결과: Temp/room_slot_probe.txt · 로그 「[RoomSlot] 끝」(CHECK 줄).
/// </summary>
public static class RoomSlotProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int Runs = 300;
    private static readonly RoomPlanKind[] Currency = { RoomPlanKind.Shop, RoomPlanKind.Crucible, RoomPlanKind.Refinery };
    private static readonly string[] Habits = { "찾아감", "반반", "피함" };

    [MenuItem("RelicFairy/Debug/10-24 B2 재화방 칸 검사 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[RoomSlot] 플레이 모드(테스트 허브)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("재화방 칸 검사(10-24 B2) — 시드 300 · 문 고르기 버릇 3종\n");
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
            var ensure = typeof(ServiceRoomTourProbeEditor).GetMethod("EnsureProcRunAsync", BindingFlags.Static | BindingFlags.NonPublic);
            if (ensure != null) await (UniTask<bool>)ensure.Invoke(null, null);
            var flow = RunFlowController.Active;
            if (flow == null) { sb.AppendLine("절차 방 진행이 시작되지 않음"); return; }
            var pool = typeof(RunFlowController).GetField("_pool", Inst)?.GetValue(flow) as List<ZonePoolEntry>;
            var cfg  = typeof(RunFlowController).GetField("_resolvedStructure", Inst)?.GetValue(flow) as IRunStructure;
            if (pool == null || cfg == null) { sb.AppendLine("방 풀 · 런 구조를 못 읽었다"); return; }

            bool hasRest = pool.Any(p => string.Equals(p.category, "Rest", System.StringComparison.OrdinalIgnoreCase));
            sb.AppendLine($"풀 쉼터 방 {(hasRest ? "있음" : "없음 — 가운데 칸 = 쉼터 자리")}");
            bool allPairOk = true, allSlotOk = true, allProgOk = true, allOnceOk = true, allShopOk = true;
            foreach (int baseThr in new[] { 5, 6 })
            foreach (bool midBoss in new[] { false, true })
            foreach (bool first in new[] { true, false })
            {
                var structure = new ThresholdOverride(cfg, baseThr);
                sb.AppendLine($"■ 표 문턱 {baseThr}(실효 {baseThr + 2}) · 정예 보스 {(midBoss ? "있음" : "없음")} · {(first ? "첫 챕터" : "다음 챕터")}");
                for (int h = 0; h < Habits.Length; h++)
                {
                    int pairBad = 0, slotBad = 0, progBad = 0, onceBad = 0, shopBad = 0, offers = 0;
                    var combats = new List<int>(); var visited = new List<int>(); var rooms = new List<int>();
                    var rng = new System.Random(11 + h);
                    for (int seed = 1; seed <= Runs; seed++)
                    {
                        var seq = new RunSequencer(pool, structure, seed) { GateCurrencyRooms = first, MidBossPlanned = midBoss };
                        var seen = new HashSet<RoomPlanKind>();
                        RoomPlanKind last = RoomPlanKind.Normal;
                        int combat = 0, currency = 0, count = 0, thr = baseThr + 2;
                        for (int guard = 0; guard < 64 && !seq.IsDone && !seq.InBossApproach; guard++)
                        {
                            int progress = seq.BossProgress;
                            var exits = seq.RollExits();
                            if (exits.Count == 0 || seq.InBossApproach) break;
                            int cur = exits.Count(d => Currency.Contains(d.kind));
                            if (cur > 1) pairBad++;
                            if (cur > 0)
                            {
                                offers++;
                                bool atSlot = progress == 1 || progress == thr - 1 || (hasRest ? last == RoomPlanKind.Rest : progress == (thr + 1) / 2);
                                if (!atSlot) slotBad++;
                                foreach (var d in exits.Where(d => Currency.Contains(d.kind)))
                                {
                                    if (!seen.Add(d.kind)) onceBad++;
                                    if (first && progress == 1 && d.kind != RoomPlanKind.Shop) shopBad++;
                                }
                            }
                            DoorPlan pick = Choose(exits, h, rng);
                            seq.CommitEntry(pick);
                            count++;
                            if (Currency.Contains(pick.kind))
                            {
                                currency++;
                                if (seq.BossProgress != progress + 1) progBad++;
                            }
                            if (pick.kind == RoomPlanKind.Normal || pick.kind == RoomPlanKind.Elite) combat++;
                            last = pick.kind;
                        }
                        combats.Add(combat); visited.Add(currency); rooms.Add(count);
                    }
                    allPairOk &= pairBad == 0; allSlotOk &= slotBad == 0; allProgOk &= progBad == 0; allOnceOk &= onceBad == 0; allShopOk &= shopBad == 0;
                    sb.AppendLine($"  {Habits[h]}: 전투방 평균 {combats.Average():0.0}(최소 {combats.Min()} · 최대 {combats.Max()}) · 재화방 {visited.Average():0.0} · 방(시작 · 보스 전 제외) {rooms.Average():0.0}"
                                  + $" · 재화 제시 {offers} · 재화|재화 {pairBad} · 칸 밖 {slotBad} · 진행+1 어김 {progBad} · 같은 재화 두 번 {onceBad} · 첫 챕터 앞 칸 상점 아님 {shopBad}");
                }
            }
            sb.AppendLine($"CHECK 재화|재화 0: {allPairOk} · 칸 밖 0: {allSlotOk} · 재화 진행 +1: {allProgOk} · 같은 재화 한 번: {allOnceOk} · 첫 챕터 앞 칸 상점: {allShopOk}");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/room_slot_probe.txt", sb.ToString());
            Debug.Log("[RoomSlot] 끝\n" + sb);
        }
    }

    /// <summary>찾아감 = 재화방 있으면 그것 · 반반 = 무작위 · 피함 = 전투(일반 · 정예) 있으면 그것.</summary>
    private static DoorPlan Choose(List<DoorPlan> exits, int habit, System.Random rng)
    {
        if (habit == 0) foreach (var d in exits) if (Currency.Contains(d.kind)) return d;
        if (habit == 2) foreach (var d in exits) if (d.kind == RoomPlanKind.Normal || d.kind == RoomPlanKind.Elite) return d;
        return exits[rng.Next(exits.Count)];
    }

    /// <summary>표 문턱만 바꿔 끼운 런 구조(나머지는 실제 값) — Ch1 풀로 Ch3 · 4 문턱(6)도 본다.</summary>
    private sealed class ThresholdOverride : IRunStructure
    {
        private readonly IRunStructure _inner;
        private readonly int _threshold;
        public ThresholdOverride(IRunStructure inner, int threshold) { _inner = inner; _threshold = threshold; }
        public int    BossThreshold      => _threshold;
        public float  ShopChance         => _inner.ShopChance;
        public float  EventChance        => _inner.EventChance;
        public float  EliteChance        => _inner.EliteChance;
        public int    ShopMaxPerChapter  => _inner.ShopMaxPerChapter;
        public int    EventMaxPerChapter => _inner.EventMaxPerChapter;
        public string PreBossRoomKey     => _inner.PreBossRoomKey;
        public string BossRoomKey        => _inner.BossRoomKey;
        public float  DifficultyAt(int visitCount) => _inner.DifficultyAt(visitCount);
        public RoomPlanKind? GetMilestoneKind(int visitIndex) => _inner.GetMilestoneKind(visitIndex);
    }
}
#endif
