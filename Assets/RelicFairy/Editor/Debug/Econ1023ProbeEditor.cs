#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-23 경제 수치 확인(런 중) — 사용자 U3 · U4 · U9 반입 뒤 지금 런의 비용 · 수입을 한 장에 적는다.
/// ① 정제소: 비용 사다리(누를 때마다 +10 · 방마다 처음) · 지금 값 · 다음 값 · 이벤트 · 천장 · 원석
/// ② 재련소: 방문 n번째 기본 비용(10 · 20 · 40) · 지금 띠(근접 · 원거리) · 인장 · 재련소 방이면 실제 화면 값 · 재료
/// ③ 방 보상: 일반 · 정예 · 정예 보스 · 이벤트 원석 · 강화재료 · 하한 · 확률 막대 값 · 도전 등급별 강화재료(지금 챕터)
/// ④ 정예 보스 굴림: 실제 RoomClearGate 굴림(방 종류만 잠깐 정예 보스로)을 200번 — 하한 Epic이 제단 잠금보다 앞서는지(Rare 0장)
/// ⑤ 정수: 처치 · 방 · 정예 보스 · 보스 값(×1.75) · 제단 「정수 획득」 배율 · 계약 · 초행 · 이번 런 번 정수 · 제단 값 합 · 남은 값 · 보유
/// 결과 Temp/fb1023_econ.txt · 로그 「[Econ23] 끝」. CHECK 줄이 전부 True면 합격.
/// </summary>
public static class Econ1023ProbeEditor
{
    private const string Tag    = "[Econ23]";
    private const string OutTxt = "Temp/fb1023_econ.txt";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-23 경제 수치 확인 (런 중)")]
    private static void Run()
    {
        var sb = new StringBuilder("10-23 경제 수치 확인\n");
        try
        {
            var run = EditorApplication.isPlaying ? GameRunBootstrapper.Instance?.Run : null;
            if (run == null || !run.IsRunning) sb.AppendLine("※ 런 중이 아님 — 정적 표만 적는다(런 값은 비움)");
            Refinery(run, sb);
            Crucible(run, sb);
            RoomRewards(run, sb);
            MidBossRoll(run, sb);
            Essence(run, sb);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    // ── ① 정제소 ──
    private static void Refinery(GameRunSession run, StringBuilder sb)
    {
        sb.AppendLine("\n[1 정제소]");
        var ladder = new List<int>();
        for (int i = 0; i < 5; i++) ladder.Add(RefineryService.CostAt(i));
        sb.AppendLine($"  비용 사다리(이번 방문 1 ~ 5번째) {string.Join(" · ", ladder)} · 천장 {RefineryService.PityLimit}회");
        bool rising = true;
        for (int i = 1; i < ladder.Count; i++) rising &= ladder[i] > ladder[i - 1];
        sb.AppendLine($"CHECK 누를 때마다 오른다(같은 값 두 번 없음) · 첫 값 {RefineryService.BaseCost}: {rising && ladder[0] == RefineryService.BaseCost}");
        var svc = run?.Refinery;
        if (svc == null) return;
        sb.AppendLine($"  지금 값 {svc.CurrentCost} · 다음 {svc.NextCost} · 이번 방문 {svc.VisitCraftCount}번 · 런 {svc.CraftCount}번 · 이벤트 {svc.RoomEvent} · 천장 {svc.PityCount}/{RefineryService.PityLimit} · 원석 {svc.OreOwned}");
        sb.AppendLine($"CHECK 지금 값 = 사다리(이벤트 반값 감안): {svc.CurrentCost == (svc.RoomEvent == RefineryEvent.HalfPrice ? Mathf.Max(1, Mathf.RoundToInt(RefineryService.CostAt(svc.VisitCraftCount) * 0.5f)) : RefineryService.CostAt(svc.VisitCraftCount))}");
    }

    // ── ② 재련소 ──
    private static void Crucible(GameRunSession run, StringBuilder sb)
    {
        sb.AppendLine("\n[2 재련소]");
        var basic = new List<int>();
        for (int n = 1; n <= 4; n++) basic.Add(WeaponReforgeService.BaseCost(n));
        sb.AppendLine($"  방문 n번째 기본 비용(1 ~ 4번째) {string.Join(" · ", basic)} · 띠 근접 +{WeaponReforgeService.MeleeBandLevel1} ×{WeaponReforgeService.BandMult1} · +{WeaponReforgeService.MeleeBandLevel2} ×{WeaponReforgeService.BandMult2} · 원거리 {WeaponReforgeService.RangedBandCount1}번째 ×{WeaponReforgeService.BandMult1} · {WeaponReforgeService.RangedBandCount2}번째 ×{WeaponReforgeService.BandMult2} · 인장 ×{WeaponReforgeService.SigilMult:0.##}");
        sb.AppendLine($"  두 번 합 {basic[0] + basic[1]} · 세 번 합 {basic[0] + basic[1] + basic[2]}(띠 ×1 · 인장 없음)");
        sb.AppendLine($"CHECK 10 · 20 · 40: {basic[0] == 10 && basic[1] == 20 && basic[2] == 40 && basic[3] == 40}");
        if (run == null) return;
        var w0 = run.Player?.WeaponManager?.Weapon0Data;
        float mb = WeaponReforgeService.MeleeBand(w0);
        float rb = WeaponReforgeService.RangedBand(RangedPartsState.Current.ReforgeCount + 1);
        var m = new List<int>(); var r = new List<int>();
        for (int n = 1; n <= 3; n++) { m.Add(WeaponReforgeService.Cost(n, mb, 1f)); r.Add(WeaponReforgeService.Cost(n, rb, 1f)); }
        sb.AppendLine($"  근접 {w0?.displayName ?? "-"} +{w0?.enhanceLevel ?? 0}(띠 ×{mb}) 1 ~ 3번째 {string.Join(" · ", m)} · 원거리 재련 {RangedPartsState.Current.ReforgeCount}번(띠 ×{rb}) {string.Join(" · ", r)} · 재료 {run.FuelBank?.EnhanceMaterial ?? 0}");
        var cr = Object.FindFirstObjectByType<CrucibleRoomController>();
        if (cr != null)
            sb.AppendLine($"  재련소 방 — 이번 방문 {cr.ReforgeCountThisVisit}번 · 다음 근접 {cr.ReforgeCost(false)} · 원거리 {cr.ReforgeCost(true)} · 화로 {cr.ActiveEvent}");
    }

    // ── ③ 방 보상 ──
    private static void RoomRewards(GameRunSession run, StringBuilder sb)
    {
        sb.AppendLine("\n[3 방 보상 — 지금 챕터 · 시기 · 제단]");
        foreach (var k in new[] { RoomPlanKind.Normal, RoomPlanKind.Elite, RoomPlanKind.MidBoss, RoomPlanKind.Event })
        {
            var rule = RoomRewardTable.For(k);
            var (ra, ep, lg) = RoomRewardTable.DisplayOdds(k);
            sb.AppendLine($"  {k,-8} 원석 {rule.Ore} · 강화재료 {rule.EnhanceMaterial} · 하한 {(rule.RarityFloor.HasValue ? rule.RarityFloor.Value.ToString() : "-")} · 후보 {rule.ChoiceCount} · 막대 Rare {ra:P0} Epic {ep:P0} Legend {lg:P0}{(RoomRewardTable.FloorIgnoresAltarLock(k) ? " · 하한이 제단 잠금보다 앞섬" : "")}");
        }
        var (mr, me, ml) = RoomRewardTable.DisplayOdds(RoomPlanKind.MidBoss);
        sb.AppendLine($"CHECK 정예 보스 막대에 Rare 0 · Epic 이상 100%: {mr < 0.001f && Mathf.Abs(me + ml - 1f) < 0.01f} (제단 Epic {(MemoryAltarService.IsEpicUnlocked ? "열림" : "잠김")} · Legendary {(MemoryAltarService.IsLegendaryUnlocked ? "열림" : "잠김")})");
        int ch = run != null ? (int)run.CurrentChapter : 1;
        var g = new List<string>();
        foreach (var gr in new[] { ChallengeGrade.Platinum, ChallengeGrade.Gold, ChallengeGrade.Silver, ChallengeGrade.Bronze, ChallengeGrade.Fail })
            g.Add($"{gr} {ChallengeRewardTable.DefaultReward(gr, ch, false).fuelAmount}");
        sb.AppendLine($"  도전(이벤트방) 강화재료 Ch{ch}: {string.Join(" · ", g)}");
    }

    // ── ④ 정예 보스 굴림(실제 코드 경로) ──
    private static void MidBossRoll(GameRunSession run, StringBuilder sb)
    {
        sb.AppendLine("\n[4 정예 보스 룬 굴림 — RoomClearGate.RollRewardChoices × 200]");
        if (run == null) { sb.AppendLine("  런 중이 아님 — 건너뜀"); return; }
        var prop = typeof(GameRunSession).GetProperty("CurrentRoomKind", Any);
        var roll = typeof(RoomClearGate).GetMethod("RollRewardChoices", Any);
        if (prop == null || roll == null) { sb.AppendLine("  리플렉션 실패(CurrentRoomKind · RollRewardChoices)"); return; }
        var prevKind = run.CurrentRoomKind;
        var go = new GameObject("Econ23_RollProbe");
        var prevRandom = Random.state;
        var count = new Dictionary<ItemRarity, int>();
        int cards = 0;
        try
        {
            var gate = go.AddComponent<RoomClearGate>();
            gate.Initialize(run, null);
            prop.SetValue(run, RoomPlanKind.MidBoss);
            for (int i = 0; i < 200; i++)
            {
                if (roll.Invoke(gate, new object[] { 3, (object)ItemRarity.Epic, false }) is not List<(RuntimeItemData, ItemSO)> list) continue;
                foreach (var (d, _) in list)
                {
                    if (d == null) continue;
                    count.TryGetValue(d.rarity, out int c); count[d.rarity] = c + 1; cards++;
                }
            }
        }
        finally
        {
            prop.SetValue(run, prevKind);
            Random.state = prevRandom;
            Object.DestroyImmediate(go);
        }
        count.TryGetValue(ItemRarity.Common, out int cc); count.TryGetValue(ItemRarity.Rare, out int rc);
        count.TryGetValue(ItemRarity.Epic, out int ec); count.TryGetValue(ItemRarity.Legendary, out int lc);
        sb.AppendLine($"  카드 {cards}장 — Common {cc} · Rare {rc} · Epic {ec} · Legendary {lc}(제단 Epic {(MemoryAltarService.IsEpicUnlocked ? "열림" : "잠김")})");
        sb.AppendLine($"CHECK 정예 보스 카드가 모두 Epic 이상: {cards > 0 && cc == 0 && rc == 0}");
        sb.AppendLine($"CHECK 방 종류 되돌림: {run.CurrentRoomKind == prevKind}");
    }

    // ── ⑤ 정수 ──
    private static void Essence(GameRunSession run, StringBuilder sb)
    {
        sb.AppendLine("\n[5 정수 · 기억의 제단]");
        var tracker = GameRunBootstrapper.Instance != null ? GameRunBootstrapper.Instance.GetComponentInChildren<EssenceTracker>(true) : null;
        int F(string name) => tracker != null && typeof(EssenceTracker).GetField(name, Any)?.GetValue(tracker) is int v ? v : -1;
        int batch = F("essencePerBatch"), per = F("killsPerBatch"), room = F("essencePerRoom"), mid = F("essencePerMidBoss"), boss = F("essencePerBoss");
        sb.AppendLine(tracker == null ? "  EssenceTracker 없음(런 중에 다시)" : $"  처치 {per}마리마다 {batch} · 방 {room} · 정예 보스 {mid} · 보스 {boss} · 제단 정수 획득 ×{MemoryAltarService.EssenceGainMult:0.##}");
        if (tracker != null)
            sb.AppendLine($"CHECK ×1.75(16 · 28 · 130 · 230): {batch == 16 && room == 28 && mid == 130 && boss == 230}");
        var con = new List<string> { $"{ContractCatalog.Reach.Title} {ContractCatalog.Reach.Essence}" };
        foreach (var d in ContractCatalog.Pool) con.Add($"{d.Title} {d.Essence}");
        sb.AppendLine($"  계약 {string.Join(" · ", con)} · 초행 ×{FirstRunService.Multiplier}");
        if (run != null) sb.AppendLine($"  이번 런 번 정수 {run.RunDelta?.GainedEssence ?? 0}");
        int total = 0, left = 0;
        foreach (var n in MemoryAltarCatalog.All)
        {
            int lv = MemoryAltarService.Level(n.Id);
            for (int l = 1; l <= n.MaxLevel; l++)
            {
                int c = n.CostForLevel(l, false);
                total += c;
                if (l > lv) left += c;
            }
        }
        sb.AppendLine($"  제단 값 합 {total:N0} · 남은 값 {left:N0} · 보유 정수 {MemoryAltarService.Essence:N0}");
    }
}
#endif
