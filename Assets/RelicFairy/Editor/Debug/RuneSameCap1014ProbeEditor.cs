#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-14 같은 룬 배치 상한 실측(런 중) — 사용자 「중복룬 배치의 한계를 두어야」. 같은 룬(itemId) 2개까지(<see cref="RuneBlockBudget.MaxSame"/>).
/// ① 같은 룬 두 개는 놓인다 ② 세 번째는 자리가 있어도 막히고 토스트 「같은 룬은 …」 ③ 놓인 것을 옮기는 건 자기를 세지 않는다
/// ④ 룬 선택 카드 배지 「같은 룬 2/2 · 판에 못 놓음」. 놓은 룬은 끝에 회수, 넣은 룬은 보관함에서 뺀다.
/// 결과: Temp/ui_shots/R14_*.png · Temp/rune_samecap_1014_probe.txt · 로그 「[RuneSameCap1014] 끝」.
/// </summary>
public static class RuneSameCap1014ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly string[] Elems = { "FIRE", "ICE", "GRASS", "DARK", "LIGHT", "ELECTRIC" };
    private static StringBuilder _sb;
    private static int _pass, _fail;

    [MenuItem("RelicFairy/Debug/10-14 같은 룬 배치 상한 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning("[RuneSameCap1014] 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        _sb = new StringBuilder("10-14 같은 룬 배치 상한\n");
        _pass = _fail = 0;
        var run    = GameRunBootstrapper.Instance.Run;
        var inv    = run.ItemInventory;
        var added  = new List<RuntimeItemData>();
        var placed = new List<RuntimeItemData>();
        UI_GridPanel panel = null;
        Directory.CreateDirectory("Temp/ui_shots");
        try
        {
            // 판에 아직 없는 작은 룬(1~2칸 · Common/Rare) 하나를 고른다
            var onBoard = new HashSet<string>(inv.PlacedItems.Where(i => i != null).Select(i => i.itemId));
            var src = Pool().FirstOrDefault(d => Cells(d) >= 1 && Cells(d) <= 2 && d.rarity <= ItemRarity.Rare && !onBoard.Contains(d.itemId));
            if (src == null) { _sb.AppendLine("고를 룬 없음"); _fail++; return; }
            _sb.AppendLine($"  룬 「{src.displayName}」({src.itemId}) · 판 룬 {RuneBlockBudget.Used} / {RuneBlockBudget.Max}");
            if (!RuneBlockBudget.Fits(RuneBlockBudget.Used, 3)) _sb.AppendLine("  주의: 판 룬 수 상한이 가까워 개수 상한이 먼저 막을 수 있다");

            var copies = new List<RuntimeItemData>();
            for (int i = 0; i < 3; i++)
            {
                var d = Fresh(src, Elems[i]);
                if (d == null || !inv.AddToStaging(d)) break;
                copies.Add(d); added.Add(d);
            }
            Check("같은 룬 세 개 보관함에", copies.Count == 3, $"{copies.Count}");

            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            panel = UI_GridPanel.Instance;
            panel.ShowWithNewItem(null);
            await UniTask.Delay(1300, ignoreTimeScale: true);

            // ① 두 개는 놓인다
            for (int i = 0; i < 2 && i < copies.Count; i++)
            {
                int used = RuneBlockBudget.Used;
                var sq = FindFit(panel, copies[i]);
                Call(panel, "OnStagingItemSelected", copies[i]);
                if (sq != null) Call(panel, "HandleSquareClicked", sq);
                await UniTask.Delay(120, ignoreTimeScale: true);
                bool ok = RuneBlockBudget.Used == used + 1;
                if (ok) placed.Add(copies[i]);
                Check($"{i + 1}번째 같은 룬 놓임({copies[i].element})", ok, $"자리 {(sq != null)} · {used} → {RuneBlockBudget.Used}");
            }

            // ② 세 번째 — 자리는 있지만 같은 룬 상한
            if (copies.Count == 3)
            {
                var third = copies[2];
                int before = RuneBlockBudget.Used;
                var sq = FindFit(panel, third);
                Call(panel, "OnStagingItemSelected", third);
                if (sq != null)
                {
                    // 칸 누르기 미리보기(빨강이어야)
                    GridManager.Instance.PreviewShapeAt(StagingShape(panel, third), sq);
                    await Shot("R14_preview_third", 120);
                    GridManager.Instance.ClearPreview();
                    Call(panel, "HandleSquareClicked", sq);
                }
                await UniTask.Delay(150, ignoreTimeScale: true);
                string toast = (typeof(UI_GridPanel).GetField("_synergyToastText", Inst)?.GetValue(panel) as TMP_Text)?.text;
                await Shot("R14_third_blocked", 0);
                Check("3번째 막힘 · 토스트 「같은 룬은 …」", sq != null && RuneBlockBudget.Used == before && toast != null && toast.StartsWith("같은 룬은"),
                      $"자리 {(sq != null)} · {before} → {RuneBlockBudget.Used} · 「{toast}」");
                Check("판 위 같은 룬 2", RuneBlockBudget.SameExcept(third) == 2, $"{RuneBlockBudget.SameExcept(third)}");
                Check("카드 문구 「같은 룬 2/2 · 판에 못 놓음」", RuneBlockBudget.SameLabel(third) == "같은 룬 2/2 · 판에 못 놓음", RuneBlockBudget.SameLabel(third));

                // ③ 이미 놓인 같은 룬을 옮기는 건 막지 않는다(자기 자신은 세지 않는다)
                if (placed.Count > 0)
                    Check("놓인 룬 옮기기는 통과(자기 제외 1)", RuneBlockBudget.FitsSame(placed[0]) && RuneBlockBudget.SameExcept(placed[0]) == 1,
                          $"{RuneBlockBudget.SameExcept(placed[0])}");
            }

            Close(panel);
            await UniTask.Delay(500, ignoreTimeScale: true);

            // ④ 룬 선택 카드 — 같은 룬이 상한이면 배지가 그 말을 먼저
            var card = Fresh(src, Elems[3]);
            var other = Pool().Where(d => d.itemId != src.itemId && d.rarity != ItemRarity.Legendary).Take(2).ToList();
            if (card != null)
            {
                var list = new List<RuntimeItemData> { card };
                list.AddRange(other);
                for (int i = 0; i < list.Count; i++) if (string.IsNullOrEmpty(list[i].element)) list[i].element = Elems[i];
                var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
                popup.Setup(list.Select(d => (d, ItemSORegistry.Find(d.itemId))).ToList(), inv);
                await UniTask.Delay(2600, ignoreTimeScale: true);
                await Shot("R14_popup", 0);
                var badges = popup.GetComponentsInChildren<TMP_Text>(true).Where(IsBadge).Select(t => t.text).ToList();
                Check("카드 배지 — 같은 룬 카드만 「같은 룬 2/2」", badges.Count(b => b.StartsWith("같은 룬 2/2")) == 1 && badges.Count(b => b.StartsWith("룬 ")) == badges.Count - 1,
                      string.Join(" | ", badges));
                Managers.UI.CloseAllPopupUI();
                await UniTask.Delay(400, ignoreTimeScale: true);
            }
        }
        catch (System.Exception e) { _sb.AppendLine("예외: " + e); _fail++; }
        finally
        {
            try
            {
                if (panel != null)
                {
                    panel.ShowWithNewItem(null);
                    await UniTask.Delay(600, ignoreTimeScale: true);
                    foreach (var it in placed) typeof(UI_GridPanel).GetMethod("RemovePlacedItem", Inst)?.Invoke(panel, new object[] { it });
                    await UniTask.Delay(300, ignoreTimeScale: true);
                    foreach (var d in added) if (inv.IsStaging(d.instanceId)) inv.DiscardFromStaging(d);
                    Close(panel);
                    await UniTask.Delay(600, ignoreTimeScale: true);
                }
            }
            catch (System.Exception e) { _sb.AppendLine("정리 예외: " + e.Message); }
            foreach (var d in added) if (inv.IsStaging(d.instanceId)) inv.DiscardFromStaging(d);
            _sb.AppendLine($"합계 통과 {_pass} · 실패 {_fail} · 정리 뒤 판 룬 {RuneBlockBudget.Used}");
            File.WriteAllText("Temp/rune_samecap_1014_probe.txt", _sb.ToString());
            Debug.Log("[RuneSameCap1014] 끝\n" + _sb);
        }
    }

    /// <summary>카드 아래 룬 수 배지 글 — 글 자신이나 위 두 단계 이름이 PlaceBadge.</summary>
    private static bool IsBadge(TMP_Text t)
    {
        var x = t.transform;
        for (int i = 0; i < 3 && x != null; i++, x = x.parent)
            if (x.name.StartsWith("PlaceBadge")) return true;
        return false;
    }

    private static void Check(string name, bool ok, string note)
    {
        if (ok) _pass++; else _fail++;
        _sb.AppendLine($"  {(ok ? "통과" : "실패")} — {name}{(string.IsNullOrEmpty(note) ? "" : " · " + note)}");
    }

    private static List<RuntimeItemData> Pool()
    {
        var list = new List<RuntimeItemData>();
        var seen = new HashSet<string>();
        foreach (var entry in Managers.ItemData.GetAllItems().Values)
        {
            var d = RuntimeItemData.FromServer(entry);
            if (d == null || d.shapeId <= 0 || !seen.Add(d.itemId)) continue;
            list.Add(d);
        }
        return list;
    }

    /// <summary>같은 룬을 새 인스턴스로(인스턴스 ID가 겹치면 판이 같은 룬 하나로 본다).</summary>
    private static RuntimeItemData Fresh(RuntimeItemData src, string element)
    {
        var entry = Managers.ItemData.GetItem(src.itemId);
        var d = entry != null ? RuntimeItemData.FromServer(entry) : null;
        if (d != null) d.element = element;
        return d;
    }

    private static int Cells(RuntimeItemData d)
    {
        var e = Managers.RuneData?.GetShape(d.shapeId);
        return e != null ? RuneDataManager.ParseCellOffsets(e).Length : 0;
    }

    private static Shape StagingShape(UI_GridPanel panel, RuntimeItemData item)
        => (typeof(UI_GridPanel).GetField("_stagingArea", Inst)?.GetValue(panel) as StagingAreaView)?.GetShapeForItem(item);

    /// <summary>미리보기 발자국(GridManager.ResolveFootprint — 상한은 안 본다)이 전부 맞는 첫 빈 칸.</summary>
    private static GridSquare FindFit(UI_GridPanel panel, RuntimeItemData item)
    {
        var shape = StagingShape(panel, item);
        var view  = Object.FindFirstObjectByType<MerlinRuneHexGridView>();
        var gm    = GridManager.Instance;
        if (shape == null || view?.HexGrid == null || gm == null) return null;
        var resolve = typeof(GridManager).GetMethod("ResolveFootprint", Inst);
        foreach (var sq in view.HexGrid.GetGridSquares())
        {
            if (sq == null || sq.isOccupied) continue;
            var args = new object[] { shape, sq, false };
            resolve.Invoke(gm, args);
            if ((bool)args[2]) return sq;
        }
        return null;
    }

    private static void Close(UI_GridPanel panel) => typeof(UI_GridPanel).GetMethod("ClosePanel", Inst)?.Invoke(panel, null);

    private static void Call(UI_GridPanel panel, string method, object arg)
        => typeof(UI_GridPanel).GetMethod(method, Inst)?.Invoke(panel, new[] { arg });

    private static async UniTask Shot(string name, int waitMs)
    {
        if (waitMs > 0) await UniTask.Delay(waitMs, ignoreTimeScale: true);
        ScreenCapture.CaptureScreenshot($"Temp/ui_shots/{name}.png");
        await UniTask.Yield();
    }
}
#endif
