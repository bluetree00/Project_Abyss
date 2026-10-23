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
/// 10-22 ⑨ 룬 스탯 실시간 반영 실측(사용자 「룬의 스탯이 그리드에 올리는 대로 실시간으로 잘 반영되는지 연결만 체크」).
/// 보관함에 효과 있는 룬 둘을 넣고 칸 누르기와 같은 길(TryPlaceShapeAt)로 놓을 때마다 <b>같은 프레임</b>에 스탯 · 영역 칸 수가 바뀌는지,
/// 룬판 「초기화」(DoReset) 뒤 스탯 · 칸 점유 · 영역 칸 수가 놓기 전으로 돌아오는지, 룬판을 닫은 채 ESC 책 「버리기」 길(DiscardPlacedRune)로
/// 놓인 룬을 없앨 때 칸 · 시너지까지 걷히는지 잰다. 결과: Temp/fb1022_rune.txt · 로그 「[Fb1022Rune] 끝」.
/// </summary>
public static class Fb1022RuneBoardProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly string[] Elems = { "FIRE", "ICE", "ELECTRIC", "GRASS", "LIGHT", "DARK" };

    [MenuItem("RelicFairy/Debug/10-22 룬판 스탯 실시간 · 초기화 · 버리기 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning("[Fb1022Rune] 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb  = new StringBuilder("룬판 스탯 실시간 · 초기화 · 버리기 실측\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}"); }
        var run = GameRunBootstrapper.Instance.Run;
        var inv = run.ItemInventory;
        var added = new List<RuntimeItemData>();
        UI_GridPanel panel = null;
        try
        {
            // 효과 있는 작은 룬(1~3칸) 둘 — 모양이 다르게
            var seen = new HashSet<int>();
            foreach (var entry in Managers.ItemData.GetAllItems().Values)
            {
                var d = RuntimeItemData.FromServer(entry);
                if (d == null || d.shapeId <= 0 || d.effects == null || d.effects.Count == 0) continue;
                // 늘 켜진 스탯 효과(체력 · 공격 · 방어 · 공속 · 이동)만 — 발동형(OnHit 등)은 놓아도 스탯이 안 바뀐다
                if (!d.effects.Any(e => e != null && e.trigger == "Always" && e.effectType is "MaxHp" or "MeleeAttack" or "Defense" or "AttackSpeed" or "MoveSpeed")) continue;
                int cells = RuneCardKit.CellCount(d);
                if (cells < 1 || cells > 3 || !seen.Add(d.shapeId)) continue;
                d.element = Elems[added.Count % Elems.Length];
                if (!inv.AddToStaging(d)) break;
                added.Add(d);
                if (added.Count == 2) break;
            }
            sb.AppendLine($"  넣은 룬 {added.Count}: {string.Join(" · ", added.Select(a => $"{a.displayName}({a.element} · {RuneCardKit.CellCount(a)}칸)"))}");

            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            panel = UI_GridPanel.Instance;
            panel.ShowWithNewItem(null);
            await UniTask.Delay(1500, ignoreTimeScale: true);

            int baseOcc = Occupied(), baseZone = ZoneSum(), basePlaced = inv.PlacedItems.Count;
            string s0 = Stats(run);
            sb.AppendLine($"  놓기 전 — 놓인 룬 {basePlaced} · 칸 점유 {baseOcc} · 영역 칸 {baseZone} · 스탯 {s0}");

            // ① 놓을 때마다 같은 프레임에 반영
            string prev = s0;
            foreach (var it in added)
            {
                int zoneBefore = ZoneSum();
                bool ok = Place(panel, it);
                string now = Stats(run);   // 같은 프레임(await 없음)
                sb.AppendLine($"  놓음 「{it.displayName}」 {ok} · 스탯 {now} · 영역 칸 {zoneBefore} → {ZoneSum()}");
                Check($"놓자마자(같은 프레임) 스탯 바뀜 — {it.displayName}", ok && now != prev);
                Check($"놓자마자 영역 칸 늘어남 — {it.displayName}", ok && ZoneSum() > zoneBefore);
                prev = now;
                await UniTask.Delay(300, ignoreTimeScale: true);
            }

            // ② 룬판 「초기화」
            typeof(UI_GridPanel).GetMethod("DoReset", Inst)?.Invoke(panel, null);
            await UniTask.Delay(400, ignoreTimeScale: true);
            sb.AppendLine($"  초기화 뒤 — 놓인 룬 {inv.PlacedItems.Count} · 칸 점유 {Occupied()} · 영역 칸 {ZoneSum()} · 스탯 {Stats(run)}");
            Check("초기화 뒤 놓인 룬 = 놓기 전", inv.PlacedItems.Count == basePlaced);
            Check("초기화 뒤 칸 점유 = 놓기 전(칸이 「차 있음」으로 안 남음)", Occupied() == baseOcc);
            Check("초기화 뒤 영역 칸 = 놓기 전(시너지 되살아나지 않음)", ZoneSum() == baseZone);
            Check("초기화 뒤 스탯 = 놓기 전", Stats(run) == s0);
            Check("초기화 뒤 룬은 보관함에", added.All(a => inv.IsStaging(a.instanceId)));

            // ③ 룬판을 닫은 채 ESC 책 「버리기」 길
            var victim = added.FirstOrDefault(a => Place(panel, a));
            await UniTask.Delay(300, ignoreTimeScale: true);
            string withOne = Stats(run);
            sb.AppendLine($"  다시 놓음 「{victim?.displayName}」 · 칸 점유 {Occupied()} · 영역 칸 {ZoneSum()}");
            typeof(UI_GridPanel).GetMethod("ClosePanel", Inst)?.Invoke(panel, null);
            await UniTask.Delay(600, ignoreTimeScale: true);
            Check("버리기 전 — 룬판 닫힘", !panel.gameObject.activeInHierarchy || !panel.isActiveAndEnabled);
            if (victim != null) panel.DiscardPlacedRune(victim);
            await UniTask.Delay(300, ignoreTimeScale: true);
            sb.AppendLine($"  버린 뒤 — 놓인 룬 {inv.PlacedItems.Count} · 칸 점유 {Occupied()} · 영역 칸 {ZoneSum()} · 스탯 {Stats(run)} (놓았을 때 {withOne})");
            Check("버린 뒤 인벤토리에 없음", victim != null && !inv.IsPlaced(victim.instanceId) && !inv.IsStaging(victim.instanceId));
            Check("버린 뒤 칸 점유 = 놓기 전", Occupied() == baseOcc);
            Check("버린 뒤 영역 칸 = 놓기 전", ZoneSum() == baseZone);
            Check("버린 뒤 스탯 = 놓기 전", Stats(run) == s0);

            // ④ 다시 열어도 판이 맞나(닫힌 채 버린 뒤)
            panel.ShowWithNewItem(null);
            await UniTask.Delay(800, ignoreTimeScale: true);
            Check("다시 연 판 — 칸 점유 · 영역 칸 그대로", Occupied() == baseOcc && ZoneSum() == baseZone);
            typeof(UI_GridPanel).GetMethod("ClosePanel", Inst)?.Invoke(panel, null);
            await UniTask.Delay(600, ignoreTimeScale: true);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var d in added)
            {
                if (inv.IsPlaced(d.instanceId) && panel != null) panel.DiscardPlacedRune(d);
                if (inv.IsStaging(d.instanceId)) inv.DiscardFromStaging(d);
            }
            sb.AppendLine($"CHECK 합계 {pass}/{total}");
            File.WriteAllText("Temp/fb1022_rune.txt", sb.ToString());
            Debug.Log("[Fb1022Rune] 끝\n" + sb);
        }
    }

    /// <summary>
    /// 10-22 ⑫ 전설 룬 끌기(사용자 「전설 룬은 판과 대조할 때 크기가 안 맞아 보여 배치가 어렵다」) — 보관함 카드를 잡는 실제 길(SlotDragHandler)로
    /// ① 집을 때 블록 묶음 가운데가 커서 아래인가 ② 끄는 동안 블록 간격 = 판 칸 간격인가 ③ 놓기 직전 미리보기 칸 = 실제로 놓인 칸인가.
    /// 사진: Temp/ui_shots/RUNE22_drag_*.png · 결과: Temp/fb1022_drag.txt · 로그 「[Fb1022Drag] 끝」.
    /// </summary>
    [MenuItem("RelicFairy/Debug/10-22 전설 룬 끌기 크기 · 놓는 자리 실측 (런 중)")]
    private static void RunDrag()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning("[Fb1022Drag] 런 안에서"); return; }
        RunDragAsync().Forget();
    }

    private static async UniTaskVoid RunDragAsync()
    {
        var sb  = new StringBuilder("전설 룬 끌기 크기 · 놓는 자리 실측\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}"); }
        var inv = GameRunBootstrapper.Instance.Run.ItemInventory;
        var added = new List<RuntimeItemData>();
        UI_GridPanel panel = null;
        try
        {
            // 전설(9칸 이상) — 가장 넓은 것(7×2 등)과 5×3 하나씩
            var seen = new HashSet<int>();
            var legends = new List<RuntimeItemData>();
            foreach (var entry in Managers.ItemData.GetAllItems().Values)
            {
                var d = RuntimeItemData.FromServer(entry);
                if (d == null || d.shapeId <= 0 || RuneCardKit.CellCount(d) < 9 || !seen.Add(d.shapeId)) continue;
                legends.Add(d);
            }
            legends.Sort((a, b) => Width(b).CompareTo(Width(a)));
            // 속성은 그대로 — 전설은 자기 속성 존에만 놓인다(덮어쓰면 7칸 룬이 들어갈 존이 없었다 · 1차 실측)
            foreach (var d in legends.Take(2)) { if (string.IsNullOrEmpty(d.element)) d.element = Elems[added.Count]; if (inv.AddToStaging(d)) added.Add(d); }
            sb.AppendLine($"  전설 룬 {legends.Count}종 중 {string.Join(" · ", added.Select(a => $"{a.displayName}({a.element} · 모양 {a.shapeId} · {RuneCardKit.CellCount(a)}칸 · 폭 {Width(a)})"))}");

            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            panel = UI_GridPanel.Instance;
            panel.ShowWithNewItem(null);
            await UniTask.Delay(1500, ignoreTimeScale: true);

            var gm = GridManager.Instance;
            var squares = gm.grid.GetGridSquares().ToList();
            float boardStep = BoardStep(squares);
            sb.AppendLine($"  판 칸 간격(화면) {boardStep:0.0}px");
            var handlerType = typeof(StagingAreaView).GetNestedType("SlotDragHandler", BindingFlags.NonPublic);
            var move = typeof(GridManager).GetMethod("MoveShapeAnchorTo", BindingFlags.Static | BindingFlags.NonPublic);
            var previewField = typeof(GridManager).GetField("_previewSquares", Inst);
            int shot = 0;
            foreach (var it in added)
            foreach (bool old in new[] { true, false })
            {
                string mode = old ? "예전 1.1배" : "지금";
                var staging = typeof(UI_GridPanel).GetField("_stagingArea", Inst)?.GetValue(panel) as StagingAreaView;
                var shape = staging?.GetShapeForItem(it);
                var handler = Object.FindObjectsByType(handlerType, FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                                    .FirstOrDefault(h => (handlerType.GetField("item")?.GetValue(h) as RuntimeItemData) == it) as MonoBehaviour;
                if (shape == null || handler == null) { sb.AppendLine($"  {it.displayName}: 모양 · 카드 없음"); continue; }

                // 놓일 자리 하나 — 칸 누르기 길로 찾고 바로 걷는다
                GridSquare anchor = null;
                var host = BoardManager.Instance?.gridHost;
                if (host != null) { shape.transform.SetParent(host, false); shape.transform.localScale = Vector3.one; }
                foreach (var sq in squares)
                {
                    if (sq == null || sq.isOccupied) continue;
                    if (gm.TryPlaceShapeAt(shape, sq)) { anchor = sq; gm.ReleaseShape(shape); break; }
                }
                BoardManager.Instance?.ReSlotAndReturn(shape);
                await UniTask.Delay(300, ignoreTimeScale: true);
                if (anchor == null) { sb.AppendLine($"  {it.displayName}: 놓을 자리 없음"); continue; }

                // ① 카드를 잡는다 — 커서 = 판 가운데쯤
                Vector2 cursor = RectTransformUtility.WorldToScreenPoint(null, ((RectTransform)anchor.transform).position);
                var ped = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { position = cursor };
                ((UnityEngine.EventSystems.IBeginDragHandler)handler).OnBeginDrag(ped);
                if (old) shape.transform.localScale *= 1.1f;   // 예전 끌기 배율(ShapeDrag.asset 1.1) — 놓을 때 Shape가 원래 배율로 되돌린다
                await UniTask.Yield();
                var (center, step) = Blocks(shape);
                Vector2 centerScreen = RectTransformUtility.WorldToScreenPoint(null, center);
                float off = (centerScreen - cursor).magnitude;
                sb.AppendLine($"  [{it.displayName} · {mode}] 집음 — 묶음 가운데 ↔ 커서 {off:0}px · 블록 간격 {step:0.0}px(판 {boardStep:0.0}) · 배율 {shape.transform.localScale.x:0.##}");
                if (!old)
                {
                    Check($"집을 때 묶음 가운데가 커서 아래(±{boardStep * 0.5f:0}px) — {it.displayName}", off <= boardStep * 0.5f);
                    Check($"끄는 동안 블록 간격 = 판 칸 간격(±2%) — {it.displayName}", Mathf.Abs(step - boardStep) <= boardStep * 0.02f);
                }

                // ② 사람이 맞추듯 첫 블록을 그 칸에 → 미리보기 → 놓기
                move.Invoke(null, new object[] { shape, anchor });
                gm.TryPreviewAndSnap(shape, out _);
                var preview = new HashSet<GridSquare>((previewField.GetValue(gm) as List<GridSquare>) ?? new List<GridSquare>());
                await Shot($"RUNE22_drag_{shot++}_{it.shapeId}_{(old ? "old" : "new")}");
                ((UnityEngine.EventSystems.IEndDragHandler)handler).OnEndDrag(ped);
                await UniTask.Yield();
                var occupied = new HashSet<GridSquare>(squares.Where(s => s != null && s.isOccupied));
                bool placed = inv.IsPlaced(it.instanceId);
                sb.AppendLine($"  [{it.displayName} · {mode}] 미리보기 {preview.Count}칸 · 놓임 {placed} · 차지 {occupied.Count}칸 · 같은 칸 {preview.SetEquals(occupied)}");
                if (!old) Check($"놓기 직전 미리보기 칸 = 놓인 칸 — {it.displayName}", placed && preview.SetEquals(occupied));
                if (!old) await Shot($"RUNE22_drag_{shot++}_{it.shapeId}_placed");
                if (placed) typeof(UI_GridPanel).GetMethod("RemovePlacedItem", Inst)?.Invoke(panel, new object[] { it });
                await UniTask.Delay(300, ignoreTimeScale: true);
            }
            typeof(UI_GridPanel).GetMethod("ClosePanel", Inst)?.Invoke(panel, null);
            await UniTask.Delay(600, ignoreTimeScale: true);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var d in added)
            {
                if (inv.IsPlaced(d.instanceId) && panel != null) panel.DiscardPlacedRune(d);
                if (inv.IsStaging(d.instanceId)) inv.DiscardFromStaging(d);
            }
            sb.AppendLine($"CHECK 합계 {pass}/{total}");
            File.WriteAllText("Temp/fb1022_drag.txt", sb.ToString());
            Debug.Log("[Fb1022Drag] 끝\n" + sb);
        }
    }

    private static int Width(RuntimeItemData d)
    {
        var piece = Managers.RuneData?.GetPiece(d.shapeId);
        if (piece == null) return 0;
        var cells = RuneDataManager.ParseCellOffsets(piece);
        return cells.Length == 0 ? 0 : cells.Max(c => c.x) - cells.Min(c => c.x) + 1;
    }

    /// <summary>판 칸 간격(화면 px) — 같은 줄 옆 칸 둘의 거리 중 가장 작은 것.</summary>
    private static float BoardStep(List<GridSquare> squares)
    {
        float best = float.MaxValue;
        foreach (var a in squares)
        foreach (var b in squares)
        {
            if (a == null || b == null || a == b || a.row != b.row || Mathf.Abs(a.col - b.col) != 1) continue;
            best = Mathf.Min(best, Vector2.Distance(RectTransformUtility.WorldToScreenPoint(null, a.transform.position), RectTransformUtility.WorldToScreenPoint(null, b.transform.position)));
        }
        return best == float.MaxValue ? 0f : best;
    }

    /// <summary>블록 묶음 가운데(월드) · 블록 간격(화면 px — 가로로 붙은 블록 둘 중 가장 가까운 거리).</summary>
    private static (Vector3 center, float step) Blocks(Shape shape)
    {
        var pts = new List<Vector3>();
        for (int i = 0; i < shape.transform.childCount; i++) pts.Add(shape.transform.GetChild(i).position);
        if (pts.Count == 0) return (shape.transform.position, 0f);
        var min = pts[0]; var max = pts[0];
        foreach (var p in pts) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        float step = float.MaxValue;
        foreach (var a in pts)
        foreach (var b in pts)
        {
            if (a == b || Mathf.Abs(a.y - b.y) > 1f) continue;
            float d = Vector2.Distance(RectTransformUtility.WorldToScreenPoint(null, a), RectTransformUtility.WorldToScreenPoint(null, b));
            if (d > 1f) step = Mathf.Min(step, d);
        }
        return ((min + max) * 0.5f, step == float.MaxValue ? 0f : step);
    }

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }

    /// <summary>칸 누르기와 같은 길 — 판의 빈 칸마다 TryPlaceShapeAt를 시도해 처음 놓이는 곳에 놓는다.</summary>
    private static bool Place(UI_GridPanel panel, RuntimeItemData item)
    {
        var staging = typeof(UI_GridPanel).GetField("_stagingArea", Inst)?.GetValue(panel) as StagingAreaView;
        var shape = BoardManager.Instance?.GetSharedShapeByItem(item.instanceId) ?? staging?.GetShapeForItem(item);
        var gm = GridManager.Instance;
        if (shape == null || gm?.grid == null) return false;
        var host = BoardManager.Instance?.gridHost;
        if (host != null && shape.transform.parent != host) { shape.transform.SetParent(host, false); shape.transform.localScale = Vector3.one; }
        foreach (var sq in gm.grid.GetGridSquares())
        {
            if (sq == null || sq.isOccupied) continue;
            if (gm.TryPlaceShapeAt(shape, sq)) { BoardManager.Instance?.OnShapePlaced(shape); gm.ClearPreview(); return true; }
        }
        BoardManager.Instance?.ReSlotAndReturn(shape);
        return false;
    }

    private static int Occupied() => GridManager.Instance?.grid?.GetGridSquares().Count(s => s != null && s.isOccupied) ?? -1;

    private static int ZoneSum()
    {
        var counts = MerlinRuneBridge.Instance?.GetZoneOccupiedCounts();
        return counts == null ? -1 : counts.Values.Sum();
    }

    private static string Stats(GameRunSession run)
    {
        var st = run.Player.RuntimeStats;
        return $"체력 {st.MaxHp} · 근접 {st.MeleeAttack} · 방어 {st.Defense} · 공속 ×{st.AttackSpeedMultiplier:0.###} · 이동 ×{st.MoveSpeedMultiplier:0.###} · 치명 {st.CritChanceBonus:0.###} · 피해 {st.AllDamagePercent:0.###} · 속성 {st.AllElementBonus:0.###}";
    }
}
#endif
