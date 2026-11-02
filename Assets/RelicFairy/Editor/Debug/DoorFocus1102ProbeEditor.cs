#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q10 「일반방만 둘이면 고를 의미가 없다」 → 문마다 속성 룬 실측(테스트 허브 런 중) — 결과 Temp/door_focus1102.txt · 로그 「[DF1102] 끝」.
///  · 이 런 계획에서 일반 | 일반 갈림길 수(속성이 걸리는 문 쌍).
///  · 일반 | 일반 문 쌍에 속성 굴림 → 두 문 속성이 다른가 · 문 이름표 둘째 줄.
///  · 방 세션에 속성(불)을 걸고 보상 후보를 5번 굴림 → 일반 · 희귀 룬이 모두 불인가 · 영웅 이상은 그대로인가. 끝나면 방 세션을 되돌린다.
/// </summary>
public static class DoorFocus1102ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags SAny = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/11-02 일반 | 일반 문 속성 룬 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[DF1102] 플레이 중에만"); return; }
        var sb = new StringBuilder("11-02 일반 | 일반 문 속성 룬\n");
        GameObject temp = null;
        var run = GameRunBootstrapper.Instance?.Run;
        RoomPlanKind kind0 = run != null ? run.CurrentRoomKind : RoomPlanKind.Normal;
        bool danger0 = run != null && run.CurrentRoomDanger;
        string focus0 = run?.CurrentRoomRuneFocus;
        int bad = 0;
        try
        {
            var flow = RunFlowController.Active;
            var seq = flow != null ? typeof(RunFlowController).GetField("_sequencer", Inst)?.GetValue(flow) as RunSequencer : null;
            if (run == null) { sb.AppendLine("런 없음"); return; }
            if (seq != null)
            {
                var plan = seq.BuildPlan();
                int pairs = 0, normalPairs = 0;
                foreach (var ch in plan.Chambers)
                {
                    if (ch.exitKinds == null || ch.exitKinds.Length < 2) continue;
                    pairs++;
                    if (ch.exitKinds[0] == RoomPlanKind.Normal && ch.exitKinds[1] == RoomPlanKind.Normal) normalPairs++;
                }
                sb.AppendLine($"이 챕터 계획 — 문 둘 갈림길 {pairs} · 그중 일반 | 일반 {normalPairs}(속성이 걸리는 갈림길)");
            }
            else
            {
                // 대기방엔 아직 순서기가 없다 — 굴림만 보려고 빈 순서기(시드 0 · 방문 0)를 만든다
                seq = (RunSequencer)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(RunSequencer));
                sb.AppendLine("대기방 — 런 계획 없음(빈 순서기로 굴림만)");
            }

            var roll = typeof(RunSequencer).GetMethod("RollRuneFocus", Inst);
            var sub  = typeof(RunFlowController).GetMethod("DoorSubtitle", SAny);
            var doors = new List<DoorPlan> { new DoorPlan { kind = RoomPlanKind.Normal }, new DoorPlan { kind = RoomPlanKind.Normal } };
            roll.Invoke(seq, new object[] { doors });
            bool diff = doors[0].runeFocus != null && doors[1].runeFocus != null && doors[0].runeFocus != doors[1].runeFocus;
            if (!diff) bad++;
            sb.AppendLine($"{(diff ? "PASS" : "FAIL")} 일반 | 일반 → 문 속성 {doors[0].runeFocus} | {doors[1].runeFocus}");
            for (int i = 0; i < 2; i++) sb.AppendLine($"  문 {i + 1} 둘째 줄 「{sub.Invoke(null, new object[] { doors[i] })}」");
            var mixed = new List<DoorPlan> { new DoorPlan { kind = RoomPlanKind.Normal }, new DoorPlan { kind = RoomPlanKind.Elite } };
            roll.Invoke(seq, new object[] { mixed });
            bool none = mixed[0].runeFocus == null && mixed[1].runeFocus == null;
            if (!none) bad++;
            sb.AppendLine($"{(none ? "PASS" : "FAIL")} 일반 | 정예 → 속성 안 걸림({mixed[0].runeFocus ?? "없음"} | {mixed[1].runeFocus ?? "없음"})");

            run.SetCurrentRoomKind(RoomPlanKind.Normal, countSpecialVisit: false, danger: false, runeFocus: "FIRE");
            temp = new GameObject("DF1102_Gate");
            var gate = temp.AddComponent<RoomClearGate>();
            typeof(RoomClearGate).GetField("_run", Inst).SetValue(gate, run);
            var rollChoices = typeof(RoomClearGate).GetMethod("RollRewardChoices", Inst);
            for (int t = 0; t < 5; t++)
            {
                var list = rollChoices.Invoke(gate, new object[] { 3, null, false }) as IList;
                var line = new StringBuilder();
                if (list == null || list.Count == 0) { sb.AppendLine($"SKIP 불 문 보상 {t + 1} — 드랍 없음(이 방 드랍 확률)"); continue; }
                bool ok = true;
                foreach (var o in list)
                {
                    var d = (RuntimeItemData)o.GetType().GetField("Item1").GetValue(o);
                    line.Append($" {d.displayName}({d.rarity} · {d.element})");
                    if (d.rarity <= ItemRarity.Rare && d.element != "FIRE") ok = false;
                }
                if (!ok) bad++;
                sb.AppendLine($"{(ok ? "PASS" : "FAIL")} 불 문 보상 {t + 1} —{line}");
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); bad++; }
        finally
        {
            run?.SetCurrentRoomKind(kind0, countSpecialVisit: false, danger: danger0, runeFocus: focus0);
            if (temp != null) Object.Destroy(temp);
            sb.AppendLine($"→ 실패 {bad}");
            File.WriteAllText("Temp/door_focus1102.txt", sb.ToString());
            Debug.Log("[DF1102] 끝 → Temp/door_focus1102.txt");
        }
    }
}
#endif
