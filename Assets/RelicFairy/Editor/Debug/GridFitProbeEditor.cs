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
/// 10-08 룬판 「놓을 자리 있음」 판정 대조(사용자 보고 「제대로 작동하지 못한다」) — 보관함에 모양이 다른 룬 여럿을 넣고
/// 룬마다 ① 배지 판정(RuneCardKit.CanPlace) ② 발자국 판정(GridManager.ResolveFootprint — 미리보기) ③ 실제 배치 판정
/// (칸 누르기와 같은 길: 첫 블록을 칸에 옮기고 블록마다 가장 가까운 칸 · 놓을 수 있음 · 비어 있음 · 속성 존)을 판의 모든 빈 칸에서 센다.
/// 빈 판 한 번, 룬 하나를 실제로 놓은 뒤 한 번. 부작용: 놓은 룬은 끝에 회수, 넣은 룬은 보관함에서 뺀다.
/// 결과: Temp/grid_fit_probe.txt · 로그 「[GridFit] 끝」.
/// </summary>
public static class GridFitProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly string[] Elems = { "FIRE", "ICE", "ELECTRIC", "GRASS", "LIGHT", "DARK" };

    [MenuItem("RelicFairy/Debug/10-08 룬판 놓을 자리 판정 대조 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning("[GridFit] 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb  = new StringBuilder("놓을 자리 판정 대조\n");
        var inv = GameRunBootstrapper.Instance.Run.ItemInventory;
        var added = new List<RuntimeItemData>();
        RuntimeItemData placed = null;
        UI_GridPanel panel = null;
        try
        {
            // 모양이 서로 다른 룬을 고른다(칸 수가 다양하게)
            var seen = new HashSet<int>();
            foreach (var entry in Managers.ItemData.GetAllItems().Values)
            {
                var d = RuntimeItemData.FromServer(entry);
                if (d == null || d.shapeId <= 0 || !seen.Add(d.shapeId)) continue;
                d.element = Elems[added.Count % Elems.Length];
                if (!inv.AddToStaging(d)) break;
                added.Add(d);
            }
            sb.AppendLine($"  넣은 룬 {added.Count} (보관함 상한 {RunItemInventory.StagingCapacity})");

            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            panel = UI_GridPanel.Instance;
            panel.ShowWithNewItem(null);
            await UniTask.Delay(1500, ignoreTimeScale: true);

            sb.AppendLine("■ 빈 판");
            var fits = new Dictionary<RuntimeItemData, GridSquare>();
            foreach (var it in added) Compare(it, sb, fits);

            // 룬 하나를 실제로 놓는다(칸 누르기와 같은 길)
            var gm = GridManager.Instance;
            foreach (var it in added)
            {
                if (!fits.TryGetValue(it, out var sq) || sq == null) continue;
                var shape = Staging(panel)?.GetShapeForItem(it);
                if (shape == null) continue;
                ToGridHost(shape);
                if (gm.TryPlaceShapeAt(shape, sq)) { BoardManager.Instance?.OnShapePlaced(shape); gm.ClearPreview(); placed = it; break; }
                BoardManager.Instance?.ReSlotAndReturn(shape);
            }
            await UniTask.Delay(600, ignoreTimeScale: true);
            sb.AppendLine($"■ 하나 놓은 뒤 — 놓은 룬 「{placed?.displayName}」 {placed?.element}");
            foreach (var it in added) if (it != placed) Compare(it, sb, null);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            try
            {
                if (placed != null && panel != null) typeof(UI_GridPanel).GetMethod("RemovePlacedItem", Inst)?.Invoke(panel, new object[] { placed });
                await UniTask.Delay(300, ignoreTimeScale: true);
                if (panel != null) typeof(UI_GridPanel).GetMethod("ClosePanel", Inst)?.Invoke(panel, null);
                await UniTask.Delay(600, ignoreTimeScale: true);
            }
            catch (System.Exception e) { sb.AppendLine("정리 예외: " + e.Message); }
            foreach (var d in added) if (inv.IsStaging(d.instanceId)) inv.DiscardFromStaging(d);
            File.WriteAllText("Temp/grid_fit_probe.txt", sb.ToString());
            Debug.Log("[GridFit] 끝\n" + sb);
        }
    }

    private static StagingAreaView Staging(UI_GridPanel panel)
        => typeof(UI_GridPanel).GetField("_stagingArea", Inst)?.GetValue(panel) as StagingAreaView;

    private static void ToGridHost(Shape shape)
    {
        var host = BoardManager.Instance?.gridHost;
        if (host != null && shape.transform.parent != host) { shape.transform.SetParent(host, false); shape.transform.localScale = Vector3.one; }
    }

    /// <summary>배지 · 발자국 · 실제 배치 판정을 판의 모든 빈 칸에서 센다. 실제로 놓이는 첫 칸을 fits에 남긴다.</summary>
    private static void Compare(RuntimeItemData item, StringBuilder sb, Dictionary<RuntimeItemData, GridSquare> fits)
    {
        bool badge = RuneBlockBudget.Fits(RuneBlockBudget.Used, item != null ? RuneCardKit.CellCount(item) : 0);   // 10-08: 배지 → 블록 예산
        var panel  = UI_GridPanel.Instance;
        var shape  = Staging(panel)?.GetShapeForItem(item);
        var view   = Object.FindFirstObjectByType<MerlinRuneHexGridView>();
        var gm     = GridManager.Instance;
        if (shape == null || view?.HexGrid == null || gm == null) { sb.AppendLine($"  {item.displayName}: 모양 · 판 없음"); return; }

        var tr = (RectTransform)shape.transform;
        var oldParent = tr.parent; int oldIndex = tr.GetSiblingIndex(); var oldScale = tr.localScale; var oldPos = tr.anchoredPosition;
        bool oldActive = shape.gameObject.activeSelf;
        ToGridHost(shape);
        shape.gameObject.SetActive(true);

        var resolve = typeof(GridManager).GetMethod("ResolveFootprint", Inst);
        var move    = typeof(GridManager).GetMethod("MoveShapeAnchorTo", Stat);
        var closest = typeof(GridManager).GetMethod("FindClosestSquare", Inst);
        string element = RuneZoneRule.ElementOf(item);
        bool noCenter  = RuneZoneRule.NoCenter(item);

        int fp = 0, geo = 0, empty = 0;
        string mismatch = null;
        GridSquare firstGeo = null;
        foreach (var sq in view.HexGrid.GetGridSquares())
        {
            if (sq == null || sq.isOccupied) continue;
            empty++;
            var args = new object[] { shape, sq, false };
            resolve.Invoke(gm, args);
            bool fpOk = (bool)args[2];

            move.Invoke(null, new object[] { shape, sq });
            bool geoOk = true;
            var used = new HashSet<GridSquare>();
            for (int i = 0; i < tr.childCount; i++)
            {
                if (tr.GetChild(i) is not RectTransform block) continue;
                var s = closest.Invoke(gm, new object[] { block, -1f }) as GridSquare;
                if (s == null || !s.isPlaceable || s.isOccupied || !RuneZoneRule.Accepts(s, element, noCenter) || !used.Add(s)) { geoOk = false; break; }
            }
            if (fpOk) fp++;
            if (geoOk) { geo++; if (firstGeo == null) firstGeo = sq; }
            if (fpOk != geoOk && mismatch == null) mismatch = $"({sq.col},{sq.row}) 발자국 {fpOk} · 실제 {geoOk}";
        }

        tr.SetParent(oldParent, false); tr.SetSiblingIndex(oldIndex); tr.localScale = oldScale; tr.anchoredPosition = oldPos;
        shape.gameObject.SetActive(oldActive);
        if (fits != null) fits[item] = firstGeo;

        int cells = shape.cellOffsets?.Count ?? 0;
        bool agree = badge == (geo > 0);
        sb.AppendLine($"  {(agree ? "  " : "✗ ")}{item.displayName} · 모양 {item.shapeId}({cells}칸) · {item.element} · {item.rarity} · 배지 {badge} · 발자국 {fp} · 실제 {geo} / 빈칸 {empty}" +
                      (mismatch != null ? $" · 첫 어긋남 {mismatch}" : ""));
        sb.AppendLine($"    CHECK 배지 = 실제로 놓이는 자리 있음: {agree}");
    }
}
#endif
