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
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// 10-26 5차 빌드 뒤 사용자 두 건 실측.
/// A. 「룬 회전 시 실제 보드에 보던 가이드라인도 반영」 — 보관함 룬을 고른 채 판 칸 위에 마우스를 둔 상태(클릭 배치 미리보기)에서 E로 돌리면
///    판 미리보기 칸이 새 모양(가로 · 세로 바뀜 · 지금 모양의 발자국과 같음)으로 바로 다시 그려지나.
/// B. 「포션이 가득 차 있는데도 포션을 구매할 수 있다」 — 행상 포션 x1 · x2를 3/3 · 2/3에서 사 본다: 가득이면 「포션 가득」 · 칸이 모자라면 「포션 칸 부족」으로
///    버튼이 꺼지고 눌러도 골드 · 포션이 그대로인가, 들어갈 때만 사지는가.
/// 결과 Temp/fix1026b.txt · Temp/ui_shots/FIX26B_*.png · 로그 「[Fix26b] 끝」.
/// </summary>
public static class Fix1026bProbeEditor
{
    private const string Tag = "[Fix26b]";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-26 룬 돌리기 판 미리보기 · 상점 포션 칸 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning($"{Tag} 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-26 룬 돌리기 판 미리보기 · 상점 포션 칸 실측\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok, string detail = "") { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}{(detail.Length > 0 ? "  — " + detail : "")}"); }
        var run = GameRunBootstrapper.Instance.Run;
        var inv = run.ItemInventory;
        RuntimeItemData item = null;
        UI_GridPanel panel = null;
        try
        {
            // ── A. 룬 돌리기 — 판 미리보기 ──
            foreach (var entry in Managers.ItemData.GetAllItems().Values)
            {
                var d = RuntimeItemData.FromServer(entry);
                if (d == null || d.shapeId <= 0 || d.rarity == ItemRarity.Legendary) continue;
                int cells = RuneCardKit.CellCount(d);
                if (cells < 3 || cells > 4) continue;
                var (w, h) = Size(RuneDataManager.ParseCellOffsets(Managers.RuneData.GetPiece(d.shapeId)));
                if (w == h) continue;
                d.EnsureElement();
                if (!inv.AddToStaging(d)) break;
                item = d;
                break;
            }
            if (item == null) sb.AppendLine("A 가로 · 세로가 다른 룬 없음 — 건너뜀");
            else
            {
                if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
                panel = UI_GridPanel.Instance;
                panel.ShowWithNewItem(null);
                await UniTask.Delay(1500, ignoreTimeScale: true);
                var staging = typeof(UI_GridPanel).GetField("_stagingArea", Inst)?.GetValue(panel) as StagingAreaView;
                var shape = staging?.GetShapeForItem(item);
                staging?.HighlightItem(item);
                var hover   = typeof(UI_GridPanel).GetMethod("HandleSquareHovered", Inst);
                var preview = typeof(GridManager).GetField("_previewSquares", Inst);
                var resolve = typeof(GridManager).GetMethod("ResolveFootprint", Inst);
                List<GridSquare> Preview() => (preview.GetValue(GridManager.Instance) as List<GridSquare>)?.Where(s => s != null).ToList() ?? new List<GridSquare>();

                // 앞 모양 · 돌린 모양 둘 다 판 안에 드는 빈 칸을 고른다(판 가장자리면 미리보기가 잘려 비교가 안 된다)
                GridSquare anchor = null;
                int n = shape != null ? shape.cellOffsets.Count : 0;
                foreach (var sq in GridManager.Instance.grid.GetGridSquares())
                {
                    if (sq == null || sq.isOccupied) continue;
                    hover.Invoke(panel, new object[] { sq });
                    if (Preview().Count != n) continue;
                    shape.SetRotation(shape.Rotation + 1, keepCenter: false);
                    var rot = resolve.Invoke(GridManager.Instance, new object[] { shape, sq, false }) as List<GridSquare>;
                    shape.SetRotation(shape.Rotation - 1, keepCenter: false);
                    if (rot != null && rot.Count(s => s != null) == n) { anchor = sq; break; }
                }
                hover.Invoke(panel, new object[] { anchor });
                var before = Preview();
                var (bw, bh) = Span(before);
                sb.AppendLine($"  룬 「{item.displayName}」 {n}칸 · 마우스 칸 ({anchor?.col},{anchor?.row}) · 미리보기 {before.Count}칸 가로 {bw} × 세로 {bh}");
                await Shot("FIX26B_1_hover");

                typeof(UI_GridPanel).GetMethod("RotateTarget", Inst)?.Invoke(panel, null);   // E와 같은 길(끄는 중 아님 · 고른 룬)
                await UniTask.Yield();
                var after = Preview();
                var (aw, ah) = Span(after);
                var expect = (resolve.Invoke(GridManager.Instance, new object[] { shape, anchor, false }) as List<GridSquare>)?.Where(s => s != null).ToList() ?? new List<GridSquare>();
                bool same = after.Count == expect.Count && after.All(expect.Contains);
                sb.AppendLine($"  돌린 뒤 미리보기 {after.Count}칸 가로 {aw} × 세로 {ah} · 룬 방향 {item.rotation}");
                Check("A 판 칸 위에서 E — 미리보기가 바로 새 모양(가로 · 세로 바뀜)", anchor != null && after.Count == n && aw == bh && ah == bw, $"전 {bw}×{bh} → 뒤 {aw}×{ah}");
                Check("A 미리보기 칸 = 돈 룬을 그 칸에 놓을 자리", anchor != null && same);
                await Shot("FIX26B_2_rotated");
                hover.Invoke(panel, new object[] { null });
                Check("A 칸에서 마우스를 떼면 미리보기 지움", Preview().Count == 0);
            }
            if (panel != null && panel.IsOpen) typeof(UI_GridPanel).GetMethod("ClosePanel", Inst)?.Invoke(panel, null);
            await UniTask.Delay(400, ignoreTimeScale: true);

            // ── B. 상점 포션 칸 ──
            var ps = run.PlayerState;
            int cnt0 = ps.PotionCount, cap0 = ps.PotionCapacity;
            var go = new GameObject("~Fix1026bShop");
            UI_ShopPanel shop = null;
            try
            {
                ps.AddTempGold(900);
                var stock = AbyssPeddlerCatalog.Build(new System.Random(7));
                stock.Special = null;
                var sc = go.AddComponent<ShopRoomController>();
                typeof(ShopRoomController).GetField("_run", Inst)?.SetValue(sc, run);
                typeof(ShopRoomController).GetField("_peddler", Inst)?.SetValue(sc, stock);
                shop = await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
                shop.Bind(sc);
                await UniTask.Delay(900, ignoreTimeScale: true);
                int potionAt = stock.Products.FindIndex(p => p.Category == ShopProductCategory.Potion);
                var btn = typeof(UI_ShopPanel).GetField("_buyBtn", Inst).GetValue(shop) as Button;
                var lbl = typeof(UI_ShopPanel).GetField("_buyLabel", Inst).GetValue(shop) as TMP_Text;

                async UniTask<(bool on, string label, int gold, int pots)> Try(int count, int amount, bool press)
                {
                    ps.RestorePotions(count, cap0);
                    stock.Products[potionAt] = Potion(amount);
                    Call(shop, "RefreshAll");
                    Call(shop, "Select", potionAt);
                    await UniTask.Delay(250, ignoreTimeScale: true);
                    bool on = btn.interactable; string label = lbl.text;
                    if (press) Call(shop, "OnBuyClicked");
                    await UniTask.Delay(300, ignoreTimeScale: true);
                    return (on, label, ps.TempGold, ps.PotionCount);
                }

                int g0 = ps.TempGold;
                var full = await Try(cap0, 1, true);
                sb.AppendLine($"  포션 {cap0}/{cap0} · x1 — 버튼 {full.on} 「{full.label}」 · 누른 뒤 골드 {g0} → {full.gold} · 포션 {full.pots}");
                Check("B 가득(3/3) · x1 — 버튼 꺼짐 「포션 가득」 · 눌러도 골드 · 포션 그대로", !full.on && full.label.Contains("포션 가득") && full.gold == g0 && full.pots == cap0);
                await Shot("FIX26B_3_full");

                var over = await Try(cap0 - 1, 2, true);
                sb.AppendLine($"  포션 {cap0 - 1}/{cap0} · x2 — 버튼 {over.on} 「{over.label}」 · 누른 뒤 골드 {g0} → {over.gold} · 포션 {over.pots}");
                Check("B 한 칸 남음 · x2 — 버튼 꺼짐 「포션 칸 부족」 · 눌러도 골드 · 포션 그대로", !over.on && over.label.Contains("칸 부족") && over.gold == g0 && over.pots == cap0 - 1);
                await Shot("FIX26B_4_room_short");

                var fit = await Try(cap0 - 1, 1, true);
                sb.AppendLine($"  포션 {cap0 - 1}/{cap0} · x1 — 버튼 {fit.on} 「{fit.label}」 · 누른 뒤 골드 {g0} → {fit.gold} · 포션 {fit.pots}");
                Check("B 한 칸 남음 · x1 — 산다(골드 60 빠지고 포션 가득)", fit.on && fit.gold == g0 - 60 && fit.pots == cap0);
            }
            finally
            {
                ps.RestorePotions(cnt0, cap0);
                if (shop != null) Managers.UI.ClosePopupUI(shop);
                Object.Destroy(go);
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (item != null)
            {
                if (inv.IsPlaced(item.instanceId) && panel != null) panel.DiscardPlacedRune(item);
                if (inv.IsStaging(item.instanceId)) inv.DiscardFromStaging(item);
            }
            if (panel != null && panel.IsOpen) typeof(UI_GridPanel).GetMethod("ClosePanel", Inst)?.Invoke(panel, null);
            sb.AppendLine($"CHECK 합계 {pass}/{total}");
            File.WriteAllText("Temp/fix1026b.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }

    private static ShopProduct Potion(int amount)
        => new ShopProduct(ShopProductCategory.Potion, $"체력 포션 x{amount}", "실측", "실측", 60 * amount, ItemRarity.Common, null,
                           s => { if (s?.PlayerState == null) return false; s.PlayerState.AddPotion(amount); return true; }, amount: amount);

    private static void Call(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, Inst)?.Invoke(target, args.Length == 0 ? null : args);

    private static (int w, int h) Size(IList<Vector2Int> cells)
    {
        if (cells == null || cells.Count == 0) return (0, 0);
        return (cells.Max(c => c.x) - cells.Min(c => c.x) + 1, cells.Max(c => c.y) - cells.Min(c => c.y) + 1);
    }

    private static (int w, int h) Span(List<GridSquare> sqs)
        => sqs.Count == 0 ? (0, 0) : (sqs.Max(s => s.col) - sqs.Min(s => s.col) + 1, sqs.Max(s => s.row) - sqs.Min(s => s.row) + 1);

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
