using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-01 룬 보유 상한 · 분해 실측 — 보관함을 상한까지 채우고 하나 더 받아(넘침) 룬판을 연다 →
/// [완료]가 정리 확인창을 띄우는지 · 넘친 룬 분해(원석) · 상한 안이면 그냥 닫히는지 · 판에 놓은 룬 분해(효과 해제 · 원석)까지.
/// ⚠️ 이 테스트 런의 보관함 · 판 · 원석이 실제로 바뀐다 — 테스트 허브 런에서만. 결과: Temp/rune_hold_probe.txt · Temp/ui_shots/S1001_Rune_*.png.
/// </summary>
public static class RuneHoldProbeEditor
{
    private const BindingFlags NonPub = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Out = "Temp/rune_hold_probe.txt";

    [MenuItem("RelicFairy/UI/10-01 룬 보유 상한 · 분해 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.ItemInventory == null)
        {
            Debug.LogWarning("[RuneHoldProbe] 런에 들어간 뒤 실행해야 한다.");
            return;
        }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb  = new StringBuilder();
        var run = GameRunBootstrapper.Instance.Run;
        var inv = run.ItemInventory;
        Invoke("HideTestHubGui");
        try
        {
            sb.AppendLine($"분해 값(표): 일반 {Ore(ItemRarity.Common)} · 희귀 {Ore(ItemRarity.Rare)} · 영웅 {Ore(ItemRarity.Epic)} · 전설 {Ore(ItemRarity.Legendary)}");

            // ① 상한까지 채우고 하나 더 받는다
            int cap = RunItemInventory.StagingCapacity;
            var samples = Samples(cap + 2);
            int si = 0;
            while (inv.StagingCount < cap && si < samples.Count) inv.AddToStaging(samples[si++]);
            var extra = si < samples.Count ? samples[si++] : null;
            inv.AddToStagingOverflow(extra);
            sb.AppendLine($"보관함 {inv.StagingCount}/{cap} · 넘침 {inv.OverflowCount} (받은 룬 「{extra?.displayName}」)");

            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            var panel = UI_GridPanel.Instance;
            panel.ShowWithNewItem(extra);
            await UniTask.Delay(900, ignoreTimeScale: true);
            await Shot("S1001_Rune_Overflow", 0);
            sb.AppendLine($"머리줄: 「{Txt(panel, "_headerStatusText")}」 · 보관함 줄: 「{Txt(panel, "_stagingCountText")}」");

            // ② 넘친 채 [완료] → 정리 확인창
            Call(panel, "OnConfirmClicked");
            await Shot("S1001_Rune_OverflowDialog", 400);
            sb.AppendLine($"[완료] 뒤 열림 {panel.IsOpen} · 확인창 {DialogOn(panel)} · 「{Txt(panel, "_confirmDialogText")}」");
            Call(panel, "OnDialogKeep");

            // ③ 넘친 룬을 골라 [분해]
            var info = Field<ItemInfoPanel>(panel, "_itemInfoPanel");
            info?.ShowItem(extra, isNew: false);
            await Shot("S1001_Rune_SalvageBtn", 300);
            var btn = Field<UnityEngine.UI.Button>(panel, "_salvageButton");
            sb.AppendLine($"[분해] 보임 {btn != null && btn.gameObject.activeSelf} · 「{Txt(panel, "_salvageBtnLabel")}」");
            int ore0 = run.FuelBank?.RuneOre ?? 0;
            Call(panel, "OnSalvageClicked");
            await Shot("S1001_Rune_SalvageDialog", 300);
            sb.AppendLine($"분해 확인창: 「{Txt(panel, "_confirmDialogText")}」");
            Call(panel, "OnSalvageConfirm");
            await UniTask.Delay(300, ignoreTimeScale: true);
            sb.AppendLine($"분해 뒤 보관함 {inv.StagingCount}/{cap} · 넘침 {inv.OverflowCount} · 원석 {ore0} → {run.FuelBank?.RuneOre}");

            // ④ 상한 안이면 [완료]가 그냥 닫는다(보관함 룬은 남는다)
            int keep = inv.StagingCount;
            Call(panel, "OnConfirmClicked");
            await UniTask.Delay(500, ignoreTimeScale: true);
            sb.AppendLine($"[완료] 뒤 열림 {panel.IsOpen} · 보관함 {keep} → {inv.StagingCount}(그대로여야 한다)");

            // ⑤ 판에 하나 놓고 그 룬을 분해
            panel.ShowWithNewItem(null);
            await UniTask.Delay(700, ignoreTimeScale: true);
            int placed0 = inv.PlacedItems.Count;
            var grid = Field<MerlinRuneHexGridView>(panel, "_hexGridView")?.HexGrid;
            if (grid != null)
                foreach (var sq in grid.GetComponentsInChildren<GridSquare>(true))
                {
                    if (sq == null || sq.isOccupied) continue;
                    GridManager.Instance?.NotifySquareClicked(sq);
                    if (inv.PlacedItems.Count > placed0) break;
                }
            RuntimeItemData placedRune = inv.PlacedItems.Count > placed0 ? inv.PlacedItems[inv.PlacedItems.Count - 1] : null;
            sb.AppendLine($"판에 놓기: {(placedRune != null ? $"「{placedRune.displayName}」 놓음" : "못 놓음")} · 배치 {placed0} → {inv.PlacedItems.Count}");
            if (placedRune != null)
            {
                await Shot("S1001_Rune_Placed", 300);
                info?.ShowItem(placedRune, isNew: false);
                sb.AppendLine($"판 룬 [분해] 보임 {btn != null && btn.gameObject.activeSelf} · 「{Txt(panel, "_salvageBtnLabel")}」");
                int ore1 = run.FuelBank?.RuneOre ?? 0, staged1 = inv.StagingCount;
                Call(panel, "OnSalvageClicked");
                await Shot("S1001_Rune_PlacedSalvageDialog", 300);
                Call(panel, "OnSalvageConfirm");
                await UniTask.Delay(400, ignoreTimeScale: true);
                await Shot("S1001_Rune_PlacedSalvaged", 200);
                sb.AppendLine($"판 룬 분해 뒤 배치 {inv.PlacedItems.Count} · 보관함 {staged1} → {inv.StagingCount} · 원석 {ore1} → {run.FuelBank?.RuneOre}");
            }
            Call(panel, "OnConfirmClicked");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Invoke("RestoreTestHubGui");
            File.WriteAllText(Out, sb.ToString());
            Debug.Log($"[RuneHoldProbe] 끝 → {Out}");
        }
    }

    private static int Ore(ItemRarity r) => RuneSalvage.OreValueOf(new RuntimeItemData { rarity = r });

    private static List<RuntimeItemData> Samples(int n)
    {
        var list = new List<RuntimeItemData>();
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("SampleRunes", BindingFlags.Static | BindingFlags.NonPublic);
        if (m?.Invoke(null, new object[] { n }) is List<(RuntimeItemData data, ItemSO so)> res)
            foreach (var (data, _) in res) list.Add(data);
        return list;
    }

    private static string DialogOn(UI_GridPanel p)
    {
        var d = Field<GameObject>(p, "_confirmDialog");
        return d == null ? "없음" : d.activeSelf ? "켜짐" : "꺼짐";
    }

    private static string Txt(object o, string field)
    {
        var t = Field<TMP_Text>(o, field);
        return t == null ? "(없음)" : t.GetParsedText().Replace("\n", "/");
    }

    private static T Field<T>(object o, string name) where T : class
        => o?.GetType().GetField(name, NonPub)?.GetValue(o) as T;

    private static void Call(object o, string method) => o?.GetType().GetMethod(method, NonPub)?.Invoke(o, null);

    private static UniTask Shot(string name, int settleMs)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, settleMs }) : UniTask.CompletedTask;
    }

    private static void Invoke(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
}
