#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 10-30 사용자 「룬들을 빼곡히 넣어서 검증해 보면 어떤 모양들이 들어간지 전혀 알기 힘든 구조 — 룬 선택이나 해제 · 다음 룬 누르고 돌아오는 등 UX 체크」 실측(테스트 허브 런 중).
/// ① 판을 룬 한도(20)까지 속성마다 맞붙여 채운다(2~6칸 모양 · 칸 누르기 경로) → 전체 · 가운데 3배 확대 사진.
/// ② UX 차례 — 놓인 룬 호버 · 놓인 룬 누르기 · 보관함 룬 A 고름 · A 다시 누름 · B 고름 · A로 돌아옴 · B 호버 뒤 떼기 · 빈 칸 누르기(판이 찼을 때) · 놓인 칸 누르기 · Esc · 다시 열기.
/// 차례마다 사진 + 상태(보관함에서 고른 룬 · 정보판 룬 · 판 강조 속성 · 밝은 조각 · 판 룬 수 · 알림 글).
/// 놓은 룬은 끝에 회수하고 넣은 룬은 보관함에서 뺀다. 결과: Temp/ui_shots/RD30_*.png · Temp/rune_dense1030.txt · 로그 「[RD30] 끝」.
/// </summary>
public static class RuneDense1030ProbeEditor
{
    // ── Constants ──
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string ShotDir = "Temp/ui_shots";
    private const string LogTag  = "[RD30]";
    private static readonly string[] Elems = { "FIRE", "ICE", "ELECTRIC", "GRASS", "LIGHT", "DARK" };

    // ── Static ──
    private static StringBuilder _sb;

    [MenuItem("RelicFairy/Debug/10-30 룬판 빼곡히 · 고르기 UX 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{LogTag} 플레이 중(테스트 허브)에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        _sb = new StringBuilder("10-30 룬판 빼곡히 · 고르기 UX\n");
        if (GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher != null) { launcher.SelectChapter(1); launcher.TryLaunch(); }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 60f && GameRunBootstrapper.Instance?.Run?.Player == null)
                await UniTask.Delay(500, DelayType.Realtime);
            if (GameRunBootstrapper.Instance?.Run?.Player == null) { Finish("런이 시작되지 않음(60초)"); return; }
            await UniTask.Delay(3000, DelayType.Realtime);
        }
        var inv    = GameRunBootstrapper.Instance.Run.ItemInventory;
        var added  = new List<RuntimeItemData>();
        var placed = new List<RuntimeItemData>();
        UI_GridPanel panel = null;
        Directory.CreateDirectory(ShotDir);
        try
        {
            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            panel = UI_GridPanel.Instance;
            panel.ShowWithNewItem(null);
            await UniTask.Delay(1200, ignoreTimeScale: true);

            // ── ① 빼곡히 — 속성마다 맞붙여 한도까지 ──
            foreach (var e in Elems)
            {
                if (RuneBlockBudget.Used >= RuneBlockBudget.Max) break;
                await FillElementAsync(panel, inv, e, added, placed, Mathf.CeilToInt(RuneBlockBudget.Max / (float)Elems.Length) + 1);
            }
            await UniTask.Delay(900, ignoreTimeScale: true);
            var pieces = placed.Select(it => BoardManager.Instance?.GetSharedShapeByItem(it.instanceId)).Where(s => s != null).ToList();
            _sb.AppendLine($"■ 놓은 룬 {placed.Count}개 · 판 룬 {RuneBlockBudget.Used}/{RuneBlockBudget.Max}");
            foreach (var g in placed.GroupBy(p => p.element))
                _sb.AppendLine($"  {g.Key}: " + string.Join(" · ", g.Select(p => $"{p.displayName}({Cells(p)}칸)")));
            int cellsUsed = pieces.Sum(s => s.GetOccupiedSquares().Count);
            var view = Object.FindFirstObjectByType<MerlinRuneHexGridView>();
            int cellsAll = view?.HexGrid?.GetGridSquares()?.Count(q => q != null) ?? 0;
            _sb.AppendLine($"  칸 {cellsUsed}/{cellsAll}");

            Call(panel, "RefreshInfoPanelDefault");
            await UniTask.Delay(400, ignoreTimeScale: true);
            var area = ZoomArea(pieces.SelectMany(s => s.GetOccupiedSquares()).ToList(), 0.35f);
            string shot = await Step(panel, inv, "RD30_0_dense", "빼곡히 · 고른 것 없음(정보판 기본)", pieces);
            SaveZoom(shot, area, "RD30_0_dense_zoom", 2);

            // ── ② UX 차례 ──
            var mid = pieces.OrderBy(s => (ScreenRect((RectTransform)s.transform).center - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)).sqrMagnitude).First();
            mid.OnPointerEnter(new PointerEventData(EventSystem.current));
            await UniTask.Delay(300, ignoreTimeScale: true);
            shot = await Step(panel, inv, "RD30_1_hover_placed", "놓인 룬에 커서", pieces);
            SaveZoom(shot, area, "RD30_1_hover_placed_zoom", 2);
            mid.OnPointerExit(new PointerEventData(EventSystem.current));

            mid.OnPointerClick(new PointerEventData(EventSystem.current));
            await UniTask.Delay(400, ignoreTimeScale: true);
            await Step(panel, inv, "RD30_2_click_placed", "놓인 룬 누름(정보판)", pieces);

            // 보관함 룬 셋(판에 안 들어가도 고르기는 된다)
            var spare = new List<RuntimeItemData>();
            foreach (var src in Pool())
            {
                if (spare.Count >= 3) break;
                var d = Fresh(src, Elems[spare.Count % Elems.Length]);
                if (d == null || !inv.AddToStagingOverflow(d)) continue;
                added.Add(d); spare.Add(d);
            }
            await UniTask.Delay(400, ignoreTimeScale: true);
            var stg = Staging(panel);
            if (spare.Count < 2 || stg == null) { _sb.AppendLine("  보관함 룬을 못 넣음 — UX 차례 건너뜀"); return; }
            var A = spare[0]; var B = spare[1];
            _sb.AppendLine($"  보관함 A = {A.displayName}({A.element}) · B = {B.displayName}({B.element})");

            CardClick(stg, A);
            await UniTask.Delay(400, ignoreTimeScale: true);
            await Step(panel, inv, "RD30_3_select_A", "보관함 A 누름", pieces);

            CardClick(stg, A);
            await UniTask.Delay(400, ignoreTimeScale: true);
            await Step(panel, inv, "RD30_4_select_A_again", "A 다시 누름(풀리나?)", pieces);

            CardClick(stg, B);
            await UniTask.Delay(400, ignoreTimeScale: true);
            await Step(panel, inv, "RD30_5_select_B", "B 누름", pieces);

            CardClick(stg, A);
            await UniTask.Delay(400, ignoreTimeScale: true);
            await Step(panel, inv, "RD30_6_back_A", "A로 돌아옴", pieces);

            Call(panel, "OnStagingItemHovered", B);
            await UniTask.Delay(300, ignoreTimeScale: true);
            await Step(panel, inv, "RD30_7_hover_B", "B에 커서(A 고른 채)", pieces);
            Call(panel, "OnStagingItemUnhovered");
            await UniTask.Delay(300, ignoreTimeScale: true);
            await Step(panel, inv, "RD30_8_unhover_B", "B에서 커서 뗌", pieces);

            var empty = view?.HexGrid?.GetGridSquares()?.FirstOrDefault(q => q != null && !q.isOccupied);
            if (empty != null)
            {
                Call(panel, "HandleSquareClicked", empty);
                await UniTask.Delay(500, ignoreTimeScale: true);
                await Step(panel, inv, "RD30_9_click_empty_full", "판이 찬 채 빈 칸 누름(A)", pieces);
            }

            var occSq = mid.GetOccupiedSquares().FirstOrDefault();
            if (occSq != null)
            {
                int before = RuneBlockBudget.Used;
                Call(panel, "HandleSquareClicked", occSq);
                await UniTask.Delay(500, ignoreTimeScale: true);
                _sb.AppendLine($"  놓인 칸 누름 → 판 룬 {before} → {RuneBlockBudget.Used}(줄면 누르기만으로 빠진다)");
                await Step(panel, inv, "RD30_10_click_occupied", "놓인 칸 누름", pieces);
            }

            panel.TryHandleEscape();
            await UniTask.Delay(900, ignoreTimeScale: true);
            bool open = typeof(UI_GridPanel).GetField("_isOpen", Inst)?.GetValue(panel) is bool o && o;
            _sb.AppendLine($"  Esc → 판 열림 {open}");
            panel.ShowWithNewItem(null);
            await UniTask.Delay(1200, ignoreTimeScale: true);
            await Step(panel, inv, "RD30_11_reopen", "Esc로 닫고 다시 엶(무엇이 골라져 있나)", pieces);
        }
        catch (System.Exception e) { _sb.AppendLine("예외: " + e); }
        finally
        {
            try
            {
                if (panel != null)
                {
                    foreach (var it in placed) if (inv.IsPlaced(it.instanceId)) Call(panel, "RemovePlacedItem", it);
                    await UniTask.Delay(400, ignoreTimeScale: true);
                    DiscardAdded(panel, inv, added);
                    Call(panel, "ClosePanel");
                    await UniTask.Delay(600, ignoreTimeScale: true);
                }
            }
            catch (System.Exception e) { _sb.AppendLine("정리 예외: " + e.Message); }
            DiscardAdded(panel, inv, added);
            Finish($"정리 뒤 판 룬 {RuneBlockBudget.Used}");
        }
    }

    private static void Finish(string tail)
    {
        _sb.AppendLine(tail);
        File.WriteAllText("Temp/rune_dense1030.txt", _sb.ToString());
        Debug.Log($"{LogTag} 끝\n" + _sb);
    }

    /// <summary>사진 + 상태 한 줄.</summary>
    private static async UniTask<string> Step(UI_GridPanel panel, RunItemInventory inv, string shot, string what, List<Shape> pieces)
    {
        string path = await Shot(shot);
        var stg  = Staging(panel);
        var info = typeof(UI_GridPanel).GetField("_itemInfoPanel", Inst)?.GetValue(panel) as ItemInfoPanel;
        var hl   = stg?.HighlightedItem;
        var cur  = info != null ? info.CurrentItem : null;
        int hot  = pieces.Count(s => s != null && (State(s, "_selected") || State(s, "_hovered")));
        string Where(RuntimeItemData d) => d == null ? "없음" : $"{d.displayName}({d.element}{(inv.IsPlaced(d.instanceId) ? " · 판" : inv.IsStaging(d.instanceId) ? " · 보관함" : "")})";
        var toast = typeof(UI_GridPanel).GetField("_synergyToastText", Inst)?.GetValue(panel) as TMPro.TMP_Text;
        string t = toast != null && toast.gameObject.activeInHierarchy ? toast.GetParsedText() : "";
        _sb.AppendLine($"\n## {shot} — {what}\n  보관함 고름 {Where(hl)} · 정보판 {Where(cur)} · 밝은 조각 {hot} · 판 룬 {RuneBlockBudget.Used}/{RuneBlockBudget.Max}{(t.Length > 0 ? $" · 알림 「{t}」" : "")}");
        return path;
    }

    private static void CardClick(StagingAreaView stg, RuntimeItemData item)
        => typeof(StagingAreaView).GetMethod("OnCardClicked", Inst)?.Invoke(stg, new object[] { item });

    /// <summary>한 속성 룬을 앞서 놓은 같은 속성 룬에 맞붙여 limit개까지(판 한도 안).</summary>
    private static async UniTask FillElementAsync(UI_GridPanel panel, RunItemInventory inv, string element,
                                                  List<RuntimeItemData> added, List<RuntimeItemData> placed, int limit)
    {
        var touch = new HashSet<Vector2Int>();
        int n = 0, tries = 0;
        foreach (var src in Pool())
        {
            if (n >= limit || ++tries > 24 || RuneBlockBudget.Used >= RuneBlockBudget.Max) return;
            var d = Fresh(src, element);
            if (d == null || !inv.AddToStagingOverflow(d)) continue;
            added.Add(d);
            await UniTask.Delay(200, ignoreTimeScale: true);
            var sq = FindFit(panel, d, n == 0 ? null : touch);
            if (sq == null) continue;
            int used = RuneBlockBudget.Used;
            Call(panel, "OnStagingItemSelected", d);
            Call(panel, "HandleSquareClicked", sq);
            await UniTask.Delay(120, ignoreTimeScale: true);
            if (RuneBlockBudget.Used <= used) continue;
            placed.Add(d); n++;
            var shape = BoardManager.Instance?.GetSharedShapeByItem(d.instanceId);
            if (shape != null) foreach (var q in shape.GetOccupiedSquares()) touch.Add(new Vector2Int(q.col, q.row));
        }
    }

    private static void DiscardAdded(UI_GridPanel panel, RunItemInventory inv, List<RuntimeItemData> added)
    {
        var stg = panel != null ? Staging(panel) : null;
        foreach (var d in added)
        {
            if (!inv.IsStaging(d.instanceId)) continue;
            stg?.RemoveShapeForItem(d);
            inv.DiscardFromStaging(d);
        }
    }

    private static bool State(Shape s, string field)
    {
        var o = s != null ? s.GetComponent<RunePieceOutline>() : null;
        return o != null && typeof(RunePieceOutline).GetField(field, Inst)?.GetValue(o) is bool b && b;
    }

    private static StagingAreaView Staging(UI_GridPanel panel)
        => typeof(UI_GridPanel).GetField("_stagingArea", Inst)?.GetValue(panel) as StagingAreaView;

    /// <summary>칸이 2~6개인 룬(모양마다 하나) — 큰 모양과 작은 모양이 섞이게 번갈아.</summary>
    private static List<RuntimeItemData> Pool()
    {
        var list = new List<RuntimeItemData>();
        var seen = new HashSet<int>();
        foreach (var entry in Managers.ItemData.GetAllItems().Values)
        {
            var d = RuntimeItemData.FromServer(entry);
            if (d == null || d.shapeId <= 0 || !seen.Add(d.shapeId)) continue;
            int c = Cells(d);
            if (c >= 2 && c <= 6) list.Add(d);
        }
        return list.OrderBy(d => (Cells(d) * 7919 + d.shapeId) % 13).ToList();
    }

    private static RuntimeItemData Fresh(RuntimeItemData src, string element)
    {
        var entries = Managers.ItemData.GetItem(src.itemId);
        var d = entries != null ? RuntimeItemData.FromServer(entries) : null;
        if (d != null) d.element = element;
        return d;
    }

    private static int Cells(RuntimeItemData d)
    {
        var e = Managers.RuneData?.GetShape(d.shapeId);
        return e != null ? RuneDataManager.ParseCellOffsets(e).Length : 0;
    }

    private static GridSquare FindFit(UI_GridPanel panel, RuntimeItemData item, HashSet<Vector2Int> touch)
    {
        var shape = Staging(panel)?.GetShapeForItem(item);
        var view  = Object.FindFirstObjectByType<MerlinRuneHexGridView>();
        var gm    = GridManager.Instance;
        var resolve = typeof(GridManager).GetMethod("ResolveFootprint", Inst);
        if (shape == null || view?.HexGrid == null || gm == null || resolve == null) return null;
        foreach (var sq in view.HexGrid.GetGridSquares())
        {
            if (sq == null || sq.isOccupied) continue;
            var args = new object[] { shape, sq, false };
            var foot = resolve.Invoke(gm, args) as List<GridSquare>;
            if (!(bool)args[2] || foot == null) continue;
            if (touch == null || foot.Any(f => Touches(new Vector2Int(f.col, f.row), touch))) return sq;
        }
        return null;
    }

    private static bool Touches(Vector2Int c, HashSet<Vector2Int> set)
        => set.Contains(c + Vector2Int.right) || set.Contains(c + Vector2Int.left) || set.Contains(c + Vector2Int.up) || set.Contains(c + Vector2Int.down);

    private static void Call(UI_GridPanel panel, string method, params object[] args)
        => typeof(UI_GridPanel).GetMethod(method, Inst)?.Invoke(panel, args.Length == 0 ? null : args);

    private static Rect ScreenRect(RectTransform rt)
    {
        var canvas = rt.GetComponentInParent<Canvas>()?.rootCanvas;
        var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]), b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    private static Rect ZoomArea(List<GridSquare> squares, float padCells)
    {
        var rs = squares.Where(q => q != null).Select(q => ScreenRect((RectTransform)q.transform)).ToList();
        if (rs.Count == 0) return new Rect(0f, 0f, Screen.width, Screen.height);
        float pad = rs[0].width * padCells;
        return Rect.MinMaxRect(rs.Min(r => r.xMin) - pad, rs.Min(r => r.yMin) - pad, rs.Max(r => r.xMax) + pad, rs.Max(r => r.yMax) + pad);
    }

    private static void SaveZoom(string png, Rect area, string name, int k)
    {
        var tex = LoadShot(png);
        if (tex == null) return;
        try
        {
            float sx = tex.width / (float)Screen.width, sy = tex.height / (float)Screen.height;
            int x0 = Mathf.Clamp(Mathf.RoundToInt(area.xMin * sx), 0, tex.width - 1), x1 = Mathf.Clamp(Mathf.RoundToInt(area.xMax * sx), 1, tex.width);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(area.yMin * sy), 0, tex.height - 1), y1 = Mathf.Clamp(Mathf.RoundToInt(area.yMax * sy), 1, tex.height);
            int w = x1 - x0, h = y1 - y0;
            if (w <= 0 || h <= 0) return;
            var src = tex.GetPixels32();
            var dst = new Color32[w * k * h * k];
            for (int y = 0; y < h * k; y++)
                for (int x = 0; x < w * k; x++)
                    dst[y * w * k + x] = src[(y0 + y / k) * tex.width + x0 + x / k];
            var outTex = new Texture2D(w * k, h * k, TextureFormat.RGBA32, false);
            try { outTex.SetPixels32(dst); outTex.Apply(); File.WriteAllBytes($"{ShotDir}/{name}.png", outTex.EncodeToPNG()); }
            finally { Object.Destroy(outTex); }
        }
        finally { Object.Destroy(tex); }
    }

    private static Texture2D LoadShot(string png)
    {
        if (string.IsNullOrEmpty(png) || !File.Exists(png)) return null;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (tex.LoadImage(File.ReadAllBytes(png))) return tex;
        Object.Destroy(tex);
        return null;
    }

    private static async UniTask<string> Shot(string name)
    {
        string path = $"{ShotDir}/{name}.png";
        if (File.Exists(path)) File.Delete(path);
        ScreenCapture.CaptureScreenshot(path);
        long last = -1;
        for (int i = 0; i < 60; i++)
        {
            await UniTask.Delay(50, ignoreTimeScale: true);
            if (!File.Exists(path)) continue;
            long len = new FileInfo(path).Length;
            if (len > 0 && len == last) break;
            last = len;
        }
        return path;
    }
}
#endif
