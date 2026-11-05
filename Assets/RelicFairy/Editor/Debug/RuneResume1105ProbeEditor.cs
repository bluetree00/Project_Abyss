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
using UnityEngine.UI;

/// <summary>
/// 11-05 #15 사용자 「룬을 맞춰 놓고 끄고 다시 이어하기를 하면 룬들이 투명해지고 이미 넣은 룬을 뺄 수 없다」 — 끝까지 재현(테스트 허브 · 플레이 중).
/// 빈 2번 슬롯으로 런 → 첫 방 입장 저장 뒤 자동 실측을 멈추고 룬 셋(가로 · 세로가 다른 룬은 한 번 돌려)을 판 칸에 놓는다 → 그 상태로 저장 →
/// <b>룬판을 통째로 버린다</b>(MerlinRuneBridge.ClearBoard — 게임을 끄고 다시 켠 것과 같은 판 · 같은 플레이 안에서 그냥 이어하면 옛 판 조각이 남아 겹쳐 실측이 틀린다.
/// 실제 길은 ESC 「게임 종료」 = 앱 종료라 옛 판이 없다) → 로비 「이어하기」와 같은 길(AppBootstrapper.RequestRestoreRun) → 판을 열어 룬마다:
/// 판 조각 수 = 놓인 룬 수(겹침 0) · 회전 · 블록 수 · 블록이 보이나(그림 · 알파) · 차지한 칸 = 저장 전 · 블록이 그 칸 위 · 블록 가운데 맨 위가 그 룬(집을 수 있나)
/// → 끌어서 빼기(실제 끌기 이벤트) · 칸 눌러 빼기 · 다시 놓기. 결과 Temp/runeresume1105.txt · Temp/ui_shots/RS1105_before · after · end.png · 로그 「[RS1105] 끝」.
/// 끝나면 2번 슬롯을 비우고 원래 슬롯으로.
/// </summary>
public static class RuneResume1105ProbeEditor
{
    private const string Tag = "[RS1105]";
    private const int TestSlot = 2;
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly StringBuilder s_sb = new();
    private static int s_fail, s_origSlot;
    private static bool s_waiting;

    [MenuItem("RelicFairy/Debug/11-05 #15 이어하기 룬 끝까지 실측 (테스트 허브, 플레이 중)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning($"{Tag} 플레이 모드에서만(테스트 허브)"); return; }
        var rpm = RunProgressManager.Instance;
        if (rpm == null) { Debug.LogWarning($"{Tag} 런 진행 관리자가 없다"); return; }
        if (rpm.HasLocalRun(TestSlot)) { Debug.LogWarning($"{Tag} 2번 슬롯에 기록이 있다 — 지우지 않으려고 멈춘다"); return; }
        s_sb.Clear().AppendLine("11-05 #15 이어하기 룬 — 끝까지");
        s_fail = 0;
        s_origSlot = rpm.ActiveSlotIndex;
        rpm.ActiveSlotIndex = TestSlot;
        BackendGameData.Instance?.ApplyActiveSlot();
        s_waiting = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (!s_waiting || !msg.StartsWith("[RunFlow] 방 입장 저장")) return;
        s_waiting = false;
        Application.logMessageReceived -= OnLog;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            RunAsync().Forget();
        };
    }

    private static async UniTaskVoid RunAsync()
    {
        var rpm = RunProgressManager.Instance;
        try
        {
            await UniTask.Delay(2500, DelayType.Realtime);
            var run = GameRunBootstrapper.Instance?.Run;
            if (run?.Player == null || RunFlowController.Active == null) { Check(false, "런 · 절차 흐름"); return; }
            var inv = run.ItemInventory;

            // ── 1 룬 셋 — 가로 · 세로가 다른 룬(돌림 1) · 한 칸 룬 · 그 밖
            var all = Managers.ItemData.GetAllItems().Values.Select(RuntimeItemData.FromServer)
                         .Where(d => d != null && d.shapeId > 0 && d.rarity != ItemRarity.Legendary).ToList();
            RuntimeItemData Pick(System.Func<List<Vector2Int>, bool> pred, IEnumerable<string> not)
                => all.FirstOrDefault(d => !not.Contains(d.itemId) && Offs(d) is { } o && pred(o));
            var taken = new List<string>();
            var rot = Pick(o => o.Count >= 3 && o.Count <= 4 && W(o) != H(o), taken); if (rot != null) taken.Add(rot.itemId);
            var one = Pick(o => o.Count == 1, taken);                                 if (one != null) taken.Add(one.itemId);
            var other = Pick(o => o.Count >= 2 && o.Count <= 3, taken);
            var runes = new[] { rot, one, other }.Where(r => r != null).ToList();
            foreach (var r in runes) { r.EnsureElement(); inv.AddToStagingOverflow(r); }

            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            var panel = UI_GridPanel.Instance;
            panel.ShowWithNewItem(null);
            await UniTask.Delay(1500, DelayType.Realtime);
            var staging = typeof(UI_GridPanel).GetField("_stagingArea", Any)?.GetValue(panel) as StagingAreaView;
            if (rot != null && staging != null)
            {
                staging.HighlightItem(rot);
                typeof(UI_GridPanel).GetMethod("RotateTarget", Any)?.Invoke(panel, null);
                await UniTask.Delay(400, DelayType.Realtime);
            }
            foreach (var r in runes) { Place(panel, r); await UniTask.Delay(250, DelayType.Realtime); }
            var before = runes.ToDictionary(r => r.instanceId, r => Occupied(r));
            foreach (var r in runes)
                s_sb.AppendLine($"  놓음 「{r.displayName}」 모양 {r.shapeId} · 돌림 {r.rotation} · 칸 {before[r.instanceId].Count} [{string.Join(" ", before[r.instanceId])}]");
            Check(runes.Count == 3 && runes.All(r => inv.IsPlaced(r.instanceId) && before[r.instanceId].Count == (Offs(r)?.Count ?? -1)), "룬 셋을 판 칸에 놓음");
            Check(rot == null || rot.rotation == 1, "가로 · 세로가 다른 룬은 돌린 채(돌림 1)");
            await Shot("RS1105_before");
            typeof(UI_GridPanel).GetMethod("ClosePanel", Any)?.Invoke(panel, null);
            await UniTask.Delay(600, DelayType.Realtime);

            // ── 2 저장(지금 판 그대로 · 피해 잠금은 풀고) → 이어하기
            var flow = RunFlowController.Active;
            typeof(RunFlowController).GetMethod("UnlockCombat", Any)?.Invoke(flow, null);
            var plan = typeof(RunFlowController).GetField("_lastPlan", Any).GetValue(flow);
            var edge = typeof(RunFlowController).GetField("_lastEdge", Any).GetValue(flow);
            var mirror = typeof(RunFlowController).GetField("_lastMirror", Any).GetValue(flow);
            typeof(RunFlowController).GetMethod("SaveRunState", Any).Invoke(flow, new[] { plan, edge, mirror, false, false });
            var save = rpm.LoadLocalRun(TestSlot);
            var pw = save != null && !string.IsNullOrEmpty(save.runePlacementsJson) ? JsonUtility.FromJson<RunePlacementListWrapper>(save.runePlacementsJson) : null;
            Check(pw?.items != null && runes.All(r => pw.items.Any(p => p.instanceId == r.instanceId)), $"저장에 배치 {pw?.items?.Count ?? 0}건 · 시험 룬 셋 다 실림 · 잠금 {save?.combatLocked}");

            // 게임을 끄고 다시 켠 것과 같은 판 — 판 조각 · 칸 점유를 통째로 버린다(새 세션이 판을 새로 짓는다)
            MerlinRuneBridge.Instance?.ClearBoard();
            await UniTask.Delay(300, DelayType.Realtime);
            var expRot = runes.ToDictionary(r => r.instanceId, r => r.rotation);
            bool resumed = await RestoreAndWaitLog("[RunFlow] 이어하기 완료", 90f);
            Check(resumed, "로비 「이어하기」와 같은 길로 다시 열림");
            await UniTask.Delay(2000, DelayType.Realtime);

            // ── 3 이어한 뒤 — 판을 열어 룬마다
            var run2 = GameRunBootstrapper.Instance?.Run;
            var inv2 = run2?.ItemInventory;
            var items = inv2 == null ? new List<RuntimeItemData>() : runes.Select(r => inv2.PlacedItems.FirstOrDefault(p => p.instanceId == r.instanceId)).ToList();
            Check(items.All(i => i != null), $"이어한 뒤 놓인 목록에 셋 다 [{string.Join(", ", items.Select(i => i != null ? i.displayName + " 돌림 " + i.rotation : "없음"))}]");
            if (items.Any(i => i == null)) return;
            var panel2 = UI_GridPanel.Instance;
            {   // 진단 — 판을 열기 전: 배치 기록 · 기록의 판 · BoardManager 지금 판
                var bm0 = BoardManager.Instance;
                var gps0 = typeof(BoardManager).GetField("_globalPlacements", Any)?.GetValue(bm0) as System.Collections.IDictionary;
                var act0 = typeof(BoardManager).GetField("activeAsset", Any)?.GetValue(bm0) as Object;
                var view0 = Object.FindFirstObjectByType<MerlinRuneHexGridView>(FindObjectsInactive.Include);
                var hg0 = view0 != null ? view0.HexGrid : null;
                var ids = new List<string>();
                if (gps0 != null) foreach (System.Collections.DictionaryEntry kv in gps0) ids.Add(((Object)kv.Value.GetType().GetField("grid")?.GetValue(kv.Value))?.GetInstanceID().ToString() ?? "null");
                s_sb.AppendLine($"  [진단·열기 전] 배치 기록 {gps0?.Count} · 기록의 판 [{string.Join(", ", ids)}] · activeAsset {(act0 != null ? act0.GetInstanceID().ToString() + " " + act0.name : "null")} · 헥사 판 asset {(hg0 != null && hg0.gridAsset != null ? hg0.gridAsset.GetInstanceID().ToString() : "null")}");
            }
            panel2.Open();
            await UniTask.Delay(1500, DelayType.Realtime);
            await Shot("RS1105_after");
            var host = BoardManager.Instance != null ? BoardManager.Instance.gridHost : null;
            int pieces = host == null ? -1 : host.GetComponentsInChildren<Shape>(false).Count(s => s.gameObject.activeInHierarchy);
            Check(pieces == inv2.PlacedItems.Count, $"판 조각 수 {pieces} = 놓인 룬 수 {inv2.PlacedItems.Count}(겹친 옛 조각 0)");
            // 진단 — 조각마다 부모 길 · 그룹 알파 · 판 배치 기록(needsRealign) · gridHost 길
            var bm = BoardManager.Instance;
            var gps = typeof(BoardManager).GetField("_globalPlacements", Any)?.GetValue(bm) as System.Collections.IDictionary;
            s_sb.AppendLine($"  [진단] gridHost {Path(bm != null ? bm.gridHost : null)} · bridge.boardManager == Instance {ReferenceEquals(typeof(MerlinRuneBridge).GetField("boardManager", Any)?.GetValue(MerlinRuneBridge.Instance), bm)} · 배치 기록 {gps?.Count}");
            foreach (var it in items)
            {
                var sh = bm?.GetSharedShapeByItem(it.instanceId);
                if (sh == null) { s_sb.AppendLine($"  [진단] 「{it.displayName}」 조각 없음"); continue; }
                object gp = gps != null && gps.Contains(sh) ? gps[sh] : null;
                string gpTxt = gp == null ? "기록 없음" : $"기록 있음 · realign {gp.GetType().GetField("needsRealign")?.GetValue(gp)} · 판 {((Object)gp.GetType().GetField("grid")?.GetValue(gp))?.GetInstanceID()}";
                s_sb.AppendLine($"  [진단] 「{it.displayName}」 {Path(sh.transform)} · {gpTxt} · 판 asset {GridManager.Instance?.grid?.gridAsset?.GetInstanceID()} · 위치 {((RectTransform)sh.transform).anchoredPosition}");
            }

            foreach (var it in items)
            {
                var sh = BoardManager.Instance?.GetSharedShapeByItem(it.instanceId);
                string n = $"「{it.displayName}」";
                if (sh == null || !sh.gameObject.activeInHierarchy) { Check(false, $"{n} 판에 모양이 있다"); continue; }
                var occ = Occupied(it);
                var was = before[it.instanceId];
                Check(sh.Rotation == it.rotation && it.rotation == expRot[it.instanceId], $"{n} 회전 {sh.Rotation} · 아이템 {it.rotation} · 저장 전 {expRot[it.instanceId]}");
                Check(sh.transform.childCount == sh.cellOffsets.Count && sh.cellOffsets.Count == was.Count, $"{n} 블록 {sh.transform.childCount} = 칸 {sh.cellOffsets.Count} = 저장 전 {was.Count}");
                Check(occ.SequenceEqual(was), $"{n} 차지한 칸 = 저장 전 [{string.Join(" ", occ)}]");

                // 보이나 — 블록 그림 · 색 알파 · 위 CanvasGroup 알파
                float alpha = 1f;
                for (var t = sh.transform; t != null; t = t.parent) if (t.TryGetComponent<CanvasGroup>(out var cg)) alpha *= cg.alpha;
                var imgs = Enumerable.Range(0, sh.transform.childCount).Select(i => sh.transform.GetChild(i).GetComponent<Image>()).ToList();
                bool visible = imgs.All(i => i != null && i.enabled && i.sprite != null && i.color.a >= 0.9f) && alpha >= 0.9f;
                Check(visible, $"{n} 블록이 보인다(그림 · 알파 {imgs.Select(i => i != null ? i.color.a : 0f).DefaultIfEmpty(0f).Min():0.00} · 그룹 {alpha:0.00})");

                // 블록이 그 칸 위에 — 블록 가운데마다 가장 가까운 칸이 이 룬이 차지한 칸
                var squares = GridManager.Instance.grid.GetGridSquares().Where(s => s != null).ToList();
                int onCell = 0;
                for (int i = 0; i < sh.transform.childCount; i++)
                {
                    var bp = sh.transform.GetChild(i).position;
                    var near = squares.OrderBy(s => (s.transform.position - bp).sqrMagnitude).First();
                    if (near.isOccupied && near.occupyingItem == it) onCell++;
                }
                Check(onCell == sh.transform.childCount, $"{n} 블록 {onCell}/{sh.transform.childCount}이 자기 칸 위");

                // 집을 수 있나 — 블록 가운데 맨 위가 이 룬(끌기 받는 곳)인가
                var top = TopAt(sh.transform.GetChild(0).position);
                var dragTo = top != null ? ExecuteEvents.GetEventHandler<IBeginDragHandler>(top) : null;
                Check(dragTo == sh.gameObject, $"{n} 블록 가운데 맨 위 = 이 룬(「{(top != null ? top.name : "-")}」 → 끌기 {(dragTo != null ? dragTo.name : "없음")})");
            }

            // ── 4 빼기 — 첫 룬은 실제 끌기 이벤트로 판 밖에 놓기 · 둘째는 칸 누르기 · 그리고 첫 룬 다시 놓기
            var a = items[0];
            var sa = BoardManager.Instance.GetSharedShapeByItem(a.instanceId);
            var ped = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, sa.transform.GetChild(0).position) };
            ExecuteEvents.Execute(sa.gameObject, ped, ExecuteEvents.beginDragHandler);
            ((RectTransform)sa.transform).anchoredPosition += new Vector2(4000f, 0f);   // 판 밖으로 끌고 감
            ExecuteEvents.Execute(sa.gameObject, ped, ExecuteEvents.endDragHandler);
            await UniTask.Delay(500, DelayType.Realtime);
            Check(inv2.IsStaging(a.instanceId) && Occupied(a).Count == 0, $"「{a.displayName}」 끌어서 판 밖 → 보관함으로 · 칸 비움");

            var b = items[1];
            var bSq = GridManager.Instance.grid.GetGridSquares().FirstOrDefault(s => s != null && s.isOccupied && s.occupyingItem == b);
            if (bSq != null)
            {
                var gm = GridManager.Instance;
                var ev = typeof(GridManager).GetField("OnSquareClicked", Any)?.GetValue(gm) as System.Delegate;
                ev?.DynamicInvoke(bSq);   // 칸 누르기 = GridManager.OnSquareClicked(→ 판의 회수)
                await UniTask.Delay(500, DelayType.Realtime);
            }
            Check(inv2.IsStaging(b.instanceId) && Occupied(b).Count == 0, $"「{b.displayName}」 칸 눌러 빼기 → 보관함으로");

            bool again = Place(panel2, a);
            await UniTask.Delay(400, DelayType.Realtime);
            Check(again && inv2.IsPlaced(a.instanceId), $"「{a.displayName}」 다시 놓기");
            await Shot("RS1105_end");
            typeof(UI_GridPanel).GetMethod("ClosePanel", Any)?.Invoke(panel2, null);
        }
        catch (System.Exception e) { s_fail++; s_sb.AppendLine("예외: " + e); }
        finally
        {
            if (rpm != null)
            {
                rpm.ActiveSlotIndex = TestSlot;
                rpm.ResetSlot(TestSlot);
                IntroCompletionTracker.ClearForSlot(TestSlot);
                rpm.ActiveSlotIndex = s_origSlot;
                BackendGameData.Instance?.ApplyActiveSlot();
                s_sb.AppendLine($"  정리 — 2번 슬롯 비움 · 원래 슬롯 {s_origSlot}");
            }
            s_sb.Insert(0, s_fail == 0 ? "PASS\n" : $"FAIL {s_fail}\n");
            File.WriteAllText("Temp/runeresume1105.txt", s_sb.ToString());
            Debug.Log($"{Tag} 끝\n{s_sb}");
        }
    }

    // ── 조각 ──────────────────────────────────────────────────
    private static List<Vector2Int> Offs(RuntimeItemData d) => RuneDataManager.ParseCellOffsets(Managers.RuneData.GetPiece(d.shapeId))?.ToList();
    private static int W(List<Vector2Int> o) => o.Max(v => v.x) - o.Min(v => v.x);
    private static int H(List<Vector2Int> o) => o.Max(v => v.y) - o.Min(v => v.y);

    /// <summary>이 룬이 차지한 칸(열, 줄) — 정렬.</summary>
    private static List<Vector2Int> Occupied(RuntimeItemData it)
        => GridManager.Instance?.grid == null ? new List<Vector2Int>()
         : GridManager.Instance.grid.GetGridSquares().Where(s => s != null && s.isOccupied && s.occupyingItem != null && s.occupyingItem.instanceId == it.instanceId)
                      .Select(s => new Vector2Int(s.col, s.row)).OrderBy(v => v.x).ThenBy(v => v.y).ToList();

    /// <summary>부모 길 — 이름(그룹 알파 · 꺼짐)을 위로.</summary>
    private static string Path(Transform t)
    {
        if (t == null) return "(없음)";
        var parts = new List<string>();
        for (; t != null; t = t.parent)
        {
            string a = t.TryGetComponent<CanvasGroup>(out var cg) ? $"[α{cg.alpha:0.##}]" : "";
            parts.Add(t.name + a + (t.gameObject.activeSelf ? "" : "(꺼짐)"));
        }
        return string.Join(" < ", parts.Take(7));
    }

    private static GameObject TopAt(Vector3 world)
    {
        var ped = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, world) };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(ped, hits);
        return hits.Count > 0 ? hits[0].gameObject : null;
    }

    /// <summary>칸 누르기와 같은 길 — 판의 빈 칸마다 TryPlaceShapeAt를 시도해 처음 놓이는 곳에 놓는다(Rot1025와 같다).</summary>
    private static bool Place(UI_GridPanel panel, RuntimeItemData item)
    {
        var staging = typeof(UI_GridPanel).GetField("_stagingArea", Any)?.GetValue(panel) as StagingAreaView;
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

    /// <summary>로비 「이어하기」와 같은 길(RequestRestoreRun)로 다시 열고, 기대 로그가 나올 때까지 기다린다(HadesSave와 같다).</summary>
    private static async UniTask<bool> RestoreAndWaitLog(string needle, float timeout)
    {
        bool seen = false;
        void Hook(string m, string s, LogType t) { if (m.Contains(needle)) seen = true; }
        Application.logMessageReceived += Hook;
        try
        {
            AppBootstrapper.Instance?.RequestRestoreRun();
            float t0 = Time.unscaledTime;
            while (!seen && Time.unscaledTime - t0 < timeout) await UniTask.Yield();
            await UniTask.Delay(1500, DelayType.Realtime);
            return seen;
        }
        finally { Application.logMessageReceived -= Hook; }
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) s_fail++;
        s_sb.AppendLine($"{(ok ? "PASS" : "FAIL")} {what}");
    }

    private static async UniTask Shot(string name)
    {
        Directory.CreateDirectory("Temp/ui_shots");
        ScreenCapture.CaptureScreenshot($"Temp/ui_shots/{name}.png");
        await UniTask.Delay(400, DelayType.Realtime);
    }
}
#endif
