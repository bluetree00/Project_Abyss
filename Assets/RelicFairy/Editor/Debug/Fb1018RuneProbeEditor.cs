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
/// 10-18 빌드 피드백 ② 「여전히 룬들이 붙어 있으면 식별하기가 어려워 — 어떤 모양들이 붙은 건지 정확한 라인 표기가 필요해 보여」 실측
/// (테스트 허브 런 중). 같은 속성 룬 3개를 서로 맞붙여 놓고(칸 누르기 경로) ① 고른 것 없음 ② 가운데 룬 고름 ③ 4번째 룬을 판 위로 끄는 중 —
/// 화면 사진 + 붙은 자리 3배 확대 사진. 숫자: 조각 사이 틈 · 조각 안 틈의 사진 밝기(사이가 밝아야), 조각마다 외곽선(RunePieceOutline) 칸 수 = 블록 수,
/// Shape 자식 수 = 블록 수(외곽선은 루트 그래픽 — 자식이 늘면 GridManager가 블록으로 센다), 고름 · 끌기 상태.
/// 놓은 룬은 끝에 회수하고 넣은 룬은 보관함에서 뺀다. 마우스가 Game 창 위 룬에 있으면 그 룬도 밝게 찍힌다(호버).
/// 결과: Temp/ui_shots/FB18_rune_*.png · Temp/fb1018_rune.txt · 로그 「[FB18rune] 끝」. Assets/에는 쓰지 않는다(플레이 중 도메인 리로드).
/// </summary>
public static class Fb1018RuneProbeEditor
{
    // ── Constants ──
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string ShotDir = "Temp/ui_shots";
    private const string LogTag  = "[FB18rune]";
    private const int    TriesPerElement = 12;
    private static readonly string[] Elems = { "ELECTRIC", "LIGHT", "FIRE", "ICE", "GRASS", "DARK" };   // 사진 02 = 번개 · 빛

    // ── Static ──
    private static StringBuilder _sb;
    private static int _pass, _fail;

    [MenuItem("RelicFairy/Debug/10-18 룬판 붙은 룬 외곽선 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{LogTag} 플레이 중(테스트 허브)에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        _sb = new StringBuilder("10-18 룬판 붙은 룬 외곽선 실측\n");
        _pass = _fail = 0;
        // 런이 없으면 테스트 허브에서 Ch1 런을 띄운다(다른 10-18 실측과 같은 길)
        if (GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher != null)
            {
                launcher.SelectChapter(1);
                launcher.TryLaunch();
            }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 60f && GameRunBootstrapper.Instance?.Run?.Player == null)
                await UniTask.Delay(500, DelayType.Realtime);
            if (GameRunBootstrapper.Instance?.Run?.Player == null)
            {
                _sb.AppendLine("런이 시작되지 않음(60초)");
                File.WriteAllText("Temp/fb1018_rune.txt", _sb.ToString());
                Debug.Log($"{LogTag} 끝\n{_sb}");
                return;
            }
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

            Check("판 쪽 옛 조각 변(_cellEdgeImages) 없음 — 외곽선은 조각이 그린다",
                  typeof(MerlinRuneHexGridView).GetField("_cellEdgeImages", Inst) == null, "");

            // ── 같은 속성 룬 3개를 맞붙인다 — 자리가 없으면 걷고 다음 속성 ──
            string element = null;
            foreach (var e in Elems)
            {
                await PlaceTouchingAsync(panel, inv, e, added, placed);
                if (placed.Count >= 3) { element = e; break; }
                await RemovePlacedAsync(panel, placed);
            }
            if (element == null)
            {
                Check("같은 속성 룬 3개 맞붙이기", false, $"어느 속성 존에도 자리가 없다(판 룬 {RuneBlockBudget.Used}/{RuneBlockBudget.Max})");
                return;
            }
            await UniTask.Delay(900, ignoreTimeScale: true);   // 놓임 연출 0.48초

            var pieces = placed.Select(it => BoardManager.Instance?.GetSharedShapeByItem(it.instanceId)).ToList();
            if (pieces.Any(s => s == null)) { Check("놓은 룬의 모양(Shape) 찾기", false, "BoardManager에서 못 찾음"); return; }
            _sb.AppendLine($"■ {element} 룬 {placed.Count}개 맞붙임 · 판 룬 {RuneBlockBudget.Used}/{RuneBlockBudget.Max}");
            for (int i = 0; i < placed.Count; i++)
            {
                var s = pieces[i];
                string cells = s != null ? string.Join(" ", s.GetOccupiedSquares().Select(q => $"({q.col},{q.row})")) : "모양 없음";
                _sb.AppendLine($"  {i + 1}. {placed[i].displayName} {s?.cellOffsets.Count ?? 0}칸 — {cells}");
            }
            CheckOutlines(pieces);

            // ① 고른 것 없음 — 정보판을 기본(보관함 첫 룬 · 빈 칸)으로 되돌린다
            Call(panel, "RefreshInfoPanelDefault", null);
            await UniTask.Delay(400, ignoreTimeScale: true);
            Check("고른 것 없음 = 밝은 조각 0", pieces.All(s => !State(s, "_selected")), Flags(pieces));
            var area = ZoomArea(pieces.SelectMany(s => s.GetOccupiedSquares()).ToList());
            string shot = await Shot("FB18_rune_board");
            SaveZoom(shot, area, "FB18_rune_board_zoom");
            MeasureSeams(shot, pieces, "고른 것 없음");

            // ② 가운데 룬 고름(판 룬 클릭과 같은 경로 — 정보판에 뜬다)
            Call(panel, "HandleItemSelected", placed[1]);
            await UniTask.Delay(400, ignoreTimeScale: true);
            Check("고른 룬만 밝다(정보판 = 2번)", State(pieces[1], "_selected") && !State(pieces[0], "_selected") && !State(pieces[2], "_selected"), Flags(pieces));
            shot = await Shot("FB18_rune_selected");
            SaveZoom(shot, area, "FB18_rune_selected_zoom");
            MeasureSeams(shot, pieces, "2번 고름");

            // ③ 4번째 룬을 맞붙는 자리 위로 끄는 중(배치 미리보기) — 놓지 않고 판 밖으로 빼서 끝낸다
            await DragPreviewAsync(panel, inv, element, added, placed, pieces);
        }
        catch (System.Exception e) { _sb.AppendLine("예외: " + e); _fail++; }
        finally
        {
            try
            {
                if (panel != null)
                {
                    await RemovePlacedAsync(panel, placed);
                    DiscardAdded(panel, inv, added);
                    Call(panel, "ClosePanel", null);
                    await UniTask.Delay(600, ignoreTimeScale: true);
                }
            }
            catch (System.Exception e) { _sb.AppendLine("정리 예외: " + e.Message); }
            DiscardAdded(panel, inv, added);
            _sb.AppendLine($"합계 통과 {_pass} · 실패 {_fail} · 정리 뒤 판 룬 {RuneBlockBudget.Used}");
            File.WriteAllText("Temp/fb1018_rune.txt", _sb.ToString());
            Debug.Log($"{LogTag} 끝\n" + _sb);
        }
    }

    // ── 놓기 ──

    /// <summary>같은 속성 룬을 하나씩 보관함에 넣고, 앞서 놓은 룬에 맞닿는 첫 자리에 칸 누르기로 놓는다(3개까지).</summary>
    private static async UniTask PlaceTouchingAsync(UI_GridPanel panel, RunItemInventory inv, string element,
                                                    List<RuntimeItemData> added, List<RuntimeItemData> placed)
    {
        var touch = new HashSet<Vector2Int>();
        int tries = 0;
        foreach (var src in Pool())
        {
            if (placed.Count >= 3 || ++tries > TriesPerElement) return;
            var d = Fresh(src, element);
            if (d == null || !inv.AddToStagingOverflow(d)) continue;
            added.Add(d);
            await UniTask.Delay(250, ignoreTimeScale: true);   // 보관함이 모양(Shape)을 만든다

            var sq = FindFit(panel, d, placed.Count == 0 ? null : touch);
            if (sq == null) continue;
            int used = RuneBlockBudget.Used;
            Call(panel, "OnStagingItemSelected", d);
            Call(panel, "HandleSquareClicked", sq);
            await UniTask.Delay(150, ignoreTimeScale: true);
            if (RuneBlockBudget.Used <= used) continue;

            placed.Add(d);
            var shape = BoardManager.Instance?.GetSharedShapeByItem(d.instanceId);
            if (shape != null) foreach (var q in shape.GetOccupiedSquares()) touch.Add(new Vector2Int(q.col, q.row));
        }
    }

    private static async UniTask RemovePlacedAsync(UI_GridPanel panel, List<RuntimeItemData> placed)
    {
        foreach (var it in placed) Call(panel, "RemovePlacedItem", it);
        placed.Clear();
        await UniTask.Delay(300, ignoreTimeScale: true);
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

    // ── ③ 끄는 중 ──

    private static async UniTask DragPreviewAsync(UI_GridPanel panel, RunItemInventory inv, string element,
                                                  List<RuntimeItemData> added, List<RuntimeItemData> placed, List<Shape> pieces)
    {
        if (EventSystem.current == null) { _sb.AppendLine("  끌기 촬영 건너뜀 — EventSystem 없음"); return; }
        var touch = new HashSet<Vector2Int>(pieces.SelectMany(s => s.GetOccupiedSquares()).Select(q => new Vector2Int(q.col, q.row)));

        RuntimeItemData d = null; Shape shape = null; GridSquare target = null;
        int tries = 0;
        foreach (var src in Pool())
        {
            if (++tries > TriesPerElement) break;
            if (placed.Any(p => p.itemId == src.itemId)) continue;
            var cand = Fresh(src, element);
            if (cand == null || !inv.AddToStagingOverflow(cand)) continue;
            added.Add(cand);
            await UniTask.Delay(250, ignoreTimeScale: true);
            var sq = FindFit(panel, cand, touch);
            if (sq == null) continue;
            d = cand; target = sq; shape = Staging(panel)?.GetShapeForItem(cand);
            break;
        }
        if (shape == null || target == null) { _sb.AppendLine("  끌기 촬영 건너뜀 — 맞닿는 빈 자리 없음"); return; }

        var ev = new PointerEventData(EventSystem.current);
        shape.OnBeginDrag(ev);
        var rt = (RectTransform)shape.transform;
        if (shape.transform.GetChild(0) is RectTransform firstBlock)
            rt.position += target.transform.position - firstBlock.position;   // 첫 블록을 맞닿는 자리에
        shape.OnDrag(ev);                                                      // 미리보기(초록 · 빨강)
        await UniTask.Delay(400, ignoreTimeScale: true);
        Check("끄는 룬 외곽선 = 밝게(끄는 중)", State(shape, "_dragging"), Flags(new List<Shape> { shape }));
        var area = ZoomArea(pieces.SelectMany(s => s.GetOccupiedSquares()).Concat(new[] { target }).ToList());
        string shot = await Shot("FB18_rune_drag");
        SaveZoom(shot, area, "FB18_rune_drag_zoom");

        rt.anchoredPosition += new Vector2(5000f, 5000f);   // 판 밖에서 손을 놓는다 → 보관함으로
        shape.OnEndDrag(ev);
        await UniTask.Delay(200, ignoreTimeScale: true);
        if (inv.IsPlaced(d.instanceId)) placed.Add(d);   // 혹시 놓였으면 정리 대상에
        Check("손 놓은 뒤 끌기 표시 꺼짐 · 판에 안 놓임", !State(shape, "_dragging") && !inv.IsPlaced(d.instanceId), "");
    }

    // ── 검사 ──

    private static void CheckOutlines(List<Shape> pieces)
    {
        for (int i = 0; i < pieces.Count; i++)
        {
            var s = pieces[i];
            var o = s != null ? s.GetComponent<RunePieceOutline>() : null;
            int blocks = s != null ? s.cellOffsets.Count : -1;
            int cells  = o != null && typeof(RunePieceOutline).GetField("_cells", Inst)?.GetValue(o) is HashSet<Vector2Int> h ? h.Count : -1;
            Check($"{i + 1}번 외곽선 — 루트 그래픽 · 칸 {blocks} · 자식 = 블록",
                  o != null && o.isActiveAndEnabled && !o.raycastTarget && cells == blocks && s.transform.childCount == blocks,
                  $"외곽선 칸 {cells} · 자식 {s?.transform.childCount ?? -1}");
        }
    }

    /// <summary>
    /// 조각 사이 틈 · 조각 안 틈의 사진 밝기 — 맞닿은 두 칸 중심의 가운데에서 틈 폭 5px × 칸 길이 60%를 잰다.
    /// 조각 사이는 두 조각의 금선(2px씩)이 들어가 밝고, 조각 안은 청동 받침이라 어둡다(예전엔 둘 다 판 바탕 ≈ 20).
    /// </summary>
    private static void MeasureSeams(string png, List<Shape> pieces, string label)
    {
        var owner = new Dictionary<Vector2Int, int>();
        var rects = new Dictionary<Vector2Int, Rect>();
        for (int i = 0; i < pieces.Count; i++)
            foreach (var q in pieces[i].GetOccupiedSquares())
            {
                var k = new Vector2Int(q.col, q.row);
                owner[k] = i;
                rects[k] = ScreenRect((RectTransform)q.transform);
            }

        var between = new List<Rect>();
        var inside  = new List<Rect>();
        foreach (var kv in owner)
            foreach (var dir in new[] { new Vector2Int(1, 0), new Vector2Int(0, 1) })
            {
                var n = kv.Key + dir;
                if (!owner.TryGetValue(n, out int other)) continue;
                Rect a = rects[kv.Key], b = rects[n];
                Vector2 m = (a.center + b.center) * 0.5f;
                var box = dir.x != 0 ? new Rect(m.x - 2.5f, m.y - a.height * 0.3f, 5f, a.height * 0.6f)
                                     : new Rect(m.x - a.width * 0.3f, m.y - 2.5f, a.width * 0.6f, 5f);
                (other == kv.Value ? inside : between).Add(box);
            }

        float lb = MeanLuma(png, between), li = MeanLuma(png, inside);
        _sb.AppendLine($"  [{label}] 조각 사이 틈 {between.Count}곳 밝기 {lb:0.#} · 조각 안 틈 {inside.Count}곳 밝기 {li:0.#}");
        if (between.Count > 0 && inside.Count > 0)
            Check($"[{label}] 조각 사이 틈이 조각 안 틈보다 밝다(+30 이상)", lb >= li + 30f, $"{lb:0.#} vs {li:0.#}");
    }

    // ── 도우미 ──

    private static bool State(Shape s, string field)
    {
        var o = s != null ? s.GetComponent<RunePieceOutline>() : null;
        return o != null && typeof(RunePieceOutline).GetField(field, Inst)?.GetValue(o) is bool b && b;
    }

    private static string Flags(List<Shape> pieces)
        => string.Join(" · ", pieces.Select((s, i) => $"{i + 1}번 고름 {(State(s, "_selected") ? 1 : 0)} 호버 {(State(s, "_hovered") ? 1 : 0)} 끌기 {(State(s, "_dragging") ? 1 : 0)}"));

    private static StagingAreaView Staging(UI_GridPanel panel)
        => typeof(UI_GridPanel).GetField("_stagingArea", Inst)?.GetValue(panel) as StagingAreaView;

    /// <summary>칸이 2~4개인 룬(모양이 겹치지 않게 한 모양에 하나) — 3칸을 먼저.</summary>
    private static List<RuntimeItemData> Pool()
    {
        var list = new List<RuntimeItemData>();
        var seen = new HashSet<int>();
        foreach (var entry in Managers.ItemData.GetAllItems().Values)
        {
            var d = RuntimeItemData.FromServer(entry);
            if (d == null || d.shapeId <= 0 || !seen.Add(d.shapeId)) continue;
            int c = Cells(d);
            if (c >= 2 && c <= 4) list.Add(d);
        }
        return list.OrderBy(d => Mathf.Abs(Cells(d) - 3)).ToList();
    }

    /// <summary>같은 룬을 새 인스턴스로(인스턴스 ID가 겹치면 판이 같은 룬으로 본다).</summary>
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

    /// <summary>미리보기 발자국(GridManager.ResolveFootprint)이 전부 맞고, <paramref name="touch"/>가 있으면 거기 맞닿는 첫 빈 칸.</summary>
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

    private static void Call(UI_GridPanel panel, string method, object arg)
        => typeof(UI_GridPanel).GetMethod(method, Inst)?.Invoke(panel, arg == null ? null : new[] { arg });

    private static void Check(string name, bool ok, string note)
    {
        if (ok) _pass++; else _fail++;
        _sb.AppendLine($"  {(ok ? "통과" : "실패")} — {name}{(string.IsNullOrEmpty(note) ? "" : " · " + note)}");
    }

    /// <summary>화면 좌표(왼쪽 아래 원점) 사각형 — 오버레이 캔버스면 카메라 없이.</summary>
    private static Rect ScreenRect(RectTransform rt)
    {
        var canvas = rt.GetComponentInParent<Canvas>()?.rootCanvas;
        var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]), b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    /// <summary>칸들을 감싸는 화면 사각형 + 칸 0.7개만큼 여백(확대 사진 범위).</summary>
    private static Rect ZoomArea(List<GridSquare> squares)
    {
        var rs = squares.Where(q => q != null).Select(q => ScreenRect((RectTransform)q.transform)).ToList();
        if (rs.Count == 0) return new Rect(0f, 0f, Screen.width, Screen.height);
        float pad = rs[0].width * 0.7f;
        return Rect.MinMaxRect(rs.Min(r => r.xMin) - pad, rs.Min(r => r.yMin) - pad, rs.Max(r => r.xMax) + pad, rs.Max(r => r.yMax) + pad);
    }

    /// <summary>사진 안 사각형들의 평균 밝기 0~255(BT.709). 사진을 못 읽거나 사각형이 없으면 -1.</summary>
    private static float MeanLuma(string png, List<Rect> rects)
    {
        if (rects.Count == 0) return -1f;
        var tex = LoadShot(png);
        if (tex == null) return -1f;
        try
        {
            var px = tex.GetPixels32();
            float sx = tex.width / (float)Screen.width, sy = tex.height / (float)Screen.height;
            double sum = 0; long n = 0;
            foreach (var r in rects)
            {
                int x0 = Mathf.Clamp(Mathf.RoundToInt(r.xMin * sx), 0, tex.width - 1), x1 = Mathf.Clamp(Mathf.RoundToInt(r.xMax * sx) - 1, 0, tex.width - 1);
                int y0 = Mathf.Clamp(Mathf.RoundToInt(r.yMin * sy), 0, tex.height - 1), y1 = Mathf.Clamp(Mathf.RoundToInt(r.yMax * sy) - 1, 0, tex.height - 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var p = px[y * tex.width + x];
                        sum += 0.2126 * p.r + 0.7152 * p.g + 0.0722 * p.b;
                        n++;
                    }
            }
            return n > 0 ? (float)(sum / n) : -1f;
        }
        finally { Object.Destroy(tex); }
    }

    /// <summary>사진의 한 부분을 3배(가장 가까운 픽셀)로 키워 따로 저장 — 1~2px 선을 눈으로 보기 위해.</summary>
    private static void SaveZoom(string png, Rect area, string name)
    {
        const int K = 3;
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
            var dst = new Color32[w * K * h * K];
            for (int y = 0; y < h * K; y++)
                for (int x = 0; x < w * K; x++)
                    dst[y * w * K + x] = src[(y0 + y / K) * tex.width + x0 + x / K];
            var outTex = new Texture2D(w * K, h * K, TextureFormat.RGBA32, false);
            try
            {
                outTex.SetPixels32(dst);
                outTex.Apply();
                File.WriteAllBytes($"{ShotDir}/{name}.png", outTex.EncodeToPNG());
            }
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

    /// <summary>찍고 파일이 다 쓰일 때까지 기다린다(사진에서 바로 잰다) — 지난 사진은 먼저 지운다. 경로를 돌려준다.</summary>
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
