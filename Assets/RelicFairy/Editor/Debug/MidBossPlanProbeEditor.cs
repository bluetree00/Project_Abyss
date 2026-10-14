#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-12 정예 보스 방 런 계획 시뮬(배치 설계 §8-5-1) — 테스트 허브 Ch1 런의 실제 방 풀 · 런 구조로 시드 4,000개를 굴린다(문은 무작위로 고른다).
/// 풀에 MidBoss 행이 아직 없으면(CDN 미업로드) 시뮬 전용 행 하나를 끼운다. 보스 문턱 5(Ch1·2)와 6(Ch3·4)을 따로 센다.
/// 결과: Temp/midboss_plan_probe.txt · 로그 「[MidBossPlan] 끝」.
/// </summary>
public static class MidBossPlanProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int Runs = 4000;
    private static readonly RoomPlanKind[] Specials = { RoomPlanKind.Shop, RoomPlanKind.Event, RoomPlanKind.Crucible, RoomPlanKind.Refinery };

    [MenuItem("RelicFairy/Debug/10-12 정예 보스 런 계획 시뮬 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[MidBossPlan] 플레이 모드(테스트 허브)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder($"정예 보스 런 계획 시뮬 (시드 {Runs} · 문은 무작위)\n");
        string forced = EditorPrefs.GetString(RunSequencer.DebugForceDoorPrefsKey, "");
        EditorPrefs.SetString(RunSequencer.DebugForceDoorPrefsKey, "");   // 「다음 문 고정」이 켜져 있으면 시뮬이 그 방만 낸다
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

            pool = new List<ZonePoolEntry>(pool);
            if (!pool.Exists(p => p.category == "MidBoss"))
            {
                pool.Add(new ZonePoolEntry { pool_key = "sim_midboss", category = "MidBoss" });
                sb.AppendLine("풀에 MidBoss 행 없음 — 시뮬 전용 행을 끼움");
            }

            // ① 번갈이 — 런당 횟수(Ch1~4)
            var perRun = new int[5];
            var perCh  = new int[4];
            for (int seed = 1; seed <= Runs; seed++)
            {
                int n = 0;
                for (int i = 0; i < 4; i++)
                    if (RunSequencer.MidBossScheduled(seed, i)) { n++; perCh[i]++; }
                perRun[n]++;
            }
            sb.AppendLine($"① 번갈이 — 챕터별 {Pct(perCh[0])} / {Pct(perCh[1])} / {Pct(perCh[2])} / {Pct(perCh[3])} (설계 50 / 75 / 62.5 / 68.8)");
            sb.AppendLine($"   런당 0회 {Pct(perRun[0])} · 1회 {Pct(perRun[1])} · 2회 {Pct(perRun[2])} · 3회 {Pct(perRun[3])} · 4회 {Pct(perRun[4])} (설계 0 · 0 · 50 · 43.8 · 6.2)");
            bool okSchedule = perRun[0] == 0 && perRun[1] == 0;

            // ② 한 챕터 계획 — 문턱 5(Ch1·2) · 6(Ch3·4) × 재화 문턱(첫 챕터) × 예정 여부
            bool okAll = okSchedule;
            foreach (int threshold in new[] { 5, 6 })
            foreach (bool gate in new[] { true, false })
            foreach (bool planned in new[] { false, true })
            {
                var rng = new System.Random(threshold * 100 + (gate ? 10 : 0) + (planned ? 1 : 0));
                int midDoors = 0, midNotAt2 = 0, midNotSingle = 0, afterMidNotRest = 0, eliteOffered = 0, specialVisits = 0;
                int refineryNever = 0, sameSpecialRepeat = 0, restMissing = 0, combatRooms = 0, guardHit = 0;
                for (int seed = 1; seed <= Runs; seed++)
                {
                    var seq = new RunSequencer(pool, cfg, seed, threshold) { GateCurrencyRooms = gate, MidBossPlanned = planned };
                    bool sawRefinery = false, sawRest = false, lastMid = false;
                    var last = RoomPlanKind.Normal;
                    int guard = 0;
                    for (; guard < 64 && !seq.IsDone && !seq.InBossApproach; guard++)
                    {
                        int progress = seq.BossProgress;
                        var exits = seq.RollExits();
                        if (exits.Count == 0 || seq.InBossApproach) break;
                        if (lastMid && !(exits.Count == 1 && exits[0].kind == RoomPlanKind.Rest)) afterMidNotRest++;
                        foreach (var d in exits)
                        {
                            if (d.kind == RoomPlanKind.MidBoss)
                            {
                                midDoors++;
                                if (progress != 2) midNotAt2++;
                                if (exits.Count != 1) midNotSingle++;
                            }
                            if (d.kind == RoomPlanKind.Elite) eliteOffered++;
                            if (d.kind == RoomPlanKind.Refinery) sawRefinery = true;
                        }
                        var pick = exits[rng.Next(exits.Count)];
                        if (System.Array.IndexOf(Specials, pick.kind) >= 0)
                        {
                            specialVisits++;
                            if (pick.kind == last) sameSpecialRepeat++;
                        }
                        if (pick.kind == RoomPlanKind.Rest) sawRest = true;
                        if (pick.kind == RoomPlanKind.Normal || pick.kind == RoomPlanKind.Elite || pick.kind == RoomPlanKind.MidBoss) combatRooms++;
                        lastMid = pick.kind == RoomPlanKind.MidBoss;
                        last = pick.kind;
                        seq.CommitEntry(pick);
                    }
                    if (guard >= 64) guardHit++;
                    if (!sawRefinery) refineryNever++;
                    if (!sawRest) restMissing++;
                }
                sb.AppendLine($"■ 문턱 {threshold} · 재화 문턱 {(gate ? "켬(첫 챕터)" : "끔")} · 정예 보스 {(planned ? "예정" : "없음")}");
                sb.AppendLine($"   정예 보스 문 {midDoors}/{Runs} · 진행 2 밖 {midNotAt2} · 외길 아님 {midNotSingle} · 뒤가 쉼터 아님 {afterMidNotRest}");
                sb.AppendLine($"   정예방 제시 {eliteOffered / (float)Runs:0.00}/런 · 특수방 방문 {specialVisits / (float)Runs:0.00}/챕터 · 전투방(시작 방 빼고) {combatRooms / (float)Runs:0.00}");
                sb.AppendLine($"   정제소 한 번도 안 나옴 {Pct(refineryNever)} · 쉼터 없음 {restMissing} · 같은 특수방 연속 {sameSpecialRepeat} · 무한 루프 {guardHit}");
                bool ok = guardHit == 0 && sameSpecialRepeat == 0 && restMissing == 0
                          && (planned
                              ? midDoors == Runs && midNotAt2 == 0 && midNotSingle == 0 && afterMidNotRest == 0 && eliteOffered == 0
                              : midDoors == 0);
                sb.AppendLine($"   CHECK {(ok ? "통과" : "실패")}");
                okAll &= ok;
            }
            sb.AppendLine($"CHECK 전체: {(okAll ? "통과" : "실패")} (번갈이 0~1회 0%: {okSchedule})");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            EditorPrefs.SetString(RunSequencer.DebugForceDoorPrefsKey, forced);
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/midboss_plan_probe.txt", sb.ToString());
            Debug.Log("[MidBossPlan] 끝\n" + sb);
        }
    }

    private static string Pct(int n) => (n * 100f / Runs).ToString("0.0") + "%";
}
#endif
