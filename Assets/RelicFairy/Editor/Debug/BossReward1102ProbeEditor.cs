#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q25 보스 처치 구슬 실측(테스트 허브 런 중) — 결과 Temp/boss_reward1102.txt · 사진 Temp/ui_shots/BR1102.png · 로그 「[BR1102] 끝」.
/// 보스방 게이트를 임시로 세우고(방 종류 = 보스) 플레이어 앞 5 m에 SpawnBossRewardAsync → 구슬 · 후보 등급(Epic 이상) · 원석 +20 · 강화재료 +10.
/// 끝나면 구슬을 치우고 연료 · 방 종류를 되돌린다.
/// </summary>
public static class BossReward1102ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/11-02 보스 처치 구슬 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BR1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-02 보스 처치 구슬\n");
        Directory.CreateDirectory("Temp/ui_shots");
        var run = GameRunBootstrapper.Instance?.Run;
        GameObject temp = null;
        ClearRewardTrigger orb = null;
        RoomPlanKind kind0 = run != null ? run.CurrentRoomKind : RoomPlanKind.Normal;
        bool danger0 = run != null && run.CurrentRoomDanger;
        string focus0 = run?.CurrentRoomRuneFocus;
        int ore0 = 0, mat0 = 0;
        try
        {
            var p = run?.Player;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RelicFairy/Systems/Stage/UseEffect/EndEffect2.prefab");
            if (run == null || p == null || prefab == null) { sb.AppendLine($"런 {run != null} · 플레이어 {p != null} · 프리팹 {prefab != null}"); return; }
            ore0 = run.FuelBank.Get(FuelKind.RuneOre);
            mat0 = run.FuelBank.EnhanceMaterial;
            run.SetCurrentRoomKind(RoomPlanKind.Boss, countSpecialVisit: false);
            temp = new GameObject("BR1102_BossGate");
            var gate = temp.AddComponent<RoomClearGate>();
            gate.Initialize(run, null, null, prefab, isBossRoom: true);
            sb.AppendLine($"보스방 게이트 {gate.IsBossRoom} · 제단 Epic 열림 {MemoryAltarService.ClampRarity(ItemRarity.Epic) == ItemRarity.Epic}");

            Vector3 fw = p.transform.forward; fw.y = 0f; fw.Normalize();
            await gate.SpawnBossRewardAsync(p.transform.position + fw * 5f, default);
            orb = Object.FindFirstObjectByType<ClearRewardTrigger>();
            if (orb == null) { sb.AppendLine("FAIL 구슬 안 섬"); return; }
            var list = typeof(ClearRewardTrigger).GetField("_rewards", Inst).GetValue(orb) as System.Collections.IList;
            bool allEpic = list != null && list.Count == 3;
            var line = new StringBuilder();
            if (list != null)
                foreach (var o in list)
                {
                    var d = (RuntimeItemData)o.GetType().GetField("Item1").GetValue(o);
                    line.Append($" {d.displayName}({d.rarity})");
                    if (d.rarity < ItemRarity.Epic) allEpic = false;
                }
            sb.AppendLine($"{(allEpic ? "PASS" : "FAIL")} 후보 {list?.Count ?? 0}장 Epic 이상 —{line}");
            int dOre = run.FuelBank.Get(FuelKind.RuneOre) - ore0, dMat = run.FuelBank.EnhanceMaterial - mat0;
            bool fuel = dOre > 0 && dMat > 0;
            sb.AppendLine($"{(fuel ? "PASS" : "FAIL")} 연료 — 원석 +{dOre} · 강화재료 +{dMat}(표 20 · 10 × 시기 · 챕터 배율)");
            sb.AppendLine($"구슬 자리 — 플레이어에서 {Vector3.Distance(orb.transform.position, p.transform.position):0.0} m");
            await UniTask.Delay(600, ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/BR1102.png");
            await UniTask.Delay(300, ignoreTimeScale: true);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (orb != null) Object.Destroy(orb.gameObject);
            if (temp != null) Object.Destroy(temp);
            if (run != null)
            {
                int extraOre = run.FuelBank.Get(FuelKind.RuneOre) - ore0, extraMat = run.FuelBank.EnhanceMaterial - mat0;
                if (extraOre > 0) run.FuelBank.TrySpend(FuelKind.RuneOre, extraOre);
                if (extraMat > 0) run.FuelBank.TrySpend(FuelKind.EnhanceMaterial, extraMat);
                run.SetCurrentRoomKind(kind0, countSpecialVisit: false, danger: danger0, runeFocus: focus0);
            }
            File.WriteAllText("Temp/boss_reward1102.txt", sb.ToString());
            Debug.Log("[BR1102] 끝 → Temp/boss_reward1102.txt");
        }
    }
}
#endif
