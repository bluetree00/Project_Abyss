#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-25 「룬 블록을 회전시키는 기능」 실측 — 보관함에 칸 모양이 가로 · 세로가 다른 룬 하나를 넣고 90° 돌려
/// 칸 모양 · 블록 자리 · 아이템 방향 · 커서 아래 머묾을 보고, 돌린 채 놓아 차지한 칸이 가로 · 세로가 바뀌었는지,
/// 세이브 JSON · 새로 만든 Shape(이어하기 · 보관함 다시 그림)가 방향을 지키는지, 회수 뒤에도 방향이 남는지,
/// 룬판에서 E(돌리기) · R · 우클릭을 눌러도 E · R 스킬 · 구르기가 정지 중에 시작되지 않는지 잰다.
/// 결과: Temp/rot1025.txt · Temp/ui_shots/ROT25_*.png · 로그 「[Rot25] 끝」.
/// </summary>
public static class Rot1025ProbeEditor
{
    private const string Tag = "[Rot25]";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-25 룬 돌리기 실측 — 모양 · 놓인 칸 · 세이브 · 정지 중 입력 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning($"{Tag} 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-25 룬 돌리기 실측\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}"); }
        var run = GameRunBootstrapper.Instance.Run;
        var inv = run.ItemInventory;
        RuntimeItemData item = null, other = null;
        UI_GridPanel panel = null;
        Shape probeShape = null;
        try
        {
            // 가로 · 세로가 다른 일반 룬(3~4칸) 하나 — 돌리면 차지하는 칸의 가로 · 세로가 바뀌어야 한다
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
            if (item == null) { sb.AppendLine("  가로 · 세로가 다른 룬 없음"); return; }

            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            panel = UI_GridPanel.Instance;
            panel.ShowWithNewItem(null);
            await UniTask.Delay(1500, ignoreTimeScale: true);

            var staging = typeof(UI_GridPanel).GetField("_stagingArea", Inst)?.GetValue(panel) as StagingAreaView;
            var shape = staging?.GetShapeForItem(item);
            if (shape == null) { sb.AppendLine("  보관함 모양 없음"); return; }
            var baseOffsets = new List<Vector2Int>(shape.cellOffsets);
            var (w0, h0) = Size(baseOffsets);
            sb.AppendLine($"  룬 「{item.displayName}」 모양 {item.shapeId} · {baseOffsets.Count}칸 · 가로 {w0} × 세로 {h0} · 칸 [{string.Join(" ", baseOffsets)}]");

            // ① 한 번 돌리기 — E와 같은 길(보관함에서 고름 → RotateTarget): 칸 모양 · 블록 자리 · 아이템 방향 · 묶음 가운데 머묾 · 카드 모양
            var (cw0, ch0) = CardPreviewSize(staging, item);
            Vector3 c0 = BlockCenterWorld(shape);
            staging.HighlightItem(item);
            typeof(UI_GridPanel).GetMethod("RotateTarget", Inst)?.Invoke(panel, null);
            float tilt0 = shape.transform.childCount > 0 ? Quaternion.Angle(shape.transform.GetChild(0).localRotation, Quaternion.identity) : 0f;
            await UniTask.Yield();
            Check($"돌릴 때 칸이 제자리에서 돈다(시작 기울기 {tilt0:0}°)", tilt0 > 45f);
            var (cw1, ch1) = CardPreviewSize(staging, item);
            sb.AppendLine($"  카드 작은 모양 — 돌리기 전 {cw0}열 × {ch0}줄 · 뒤 {cw1}열 × {ch1}줄");
            Check("카드 작은 모양도 돌아감(열 · 줄 바뀜)", cw0 > 0 && ch0 > 0 && cw1 == ch0 && ch1 == cw0);
            var (w1, h1) = Size(shape.cellOffsets);
            bool offsetsOk = shape.cellOffsets.Count == baseOffsets.Count
                          && baseOffsets.Select((o, i) => Shape.RotateCW(o, 1) == shape.cellOffsets[i]).All(x => x);
            bool blocksOk = shape.transform.childCount == shape.cellOffsets.Count
                         && Enumerable.Range(0, shape.cellOffsets.Count).All(i =>
                                Vector2.Distance(((RectTransform)shape.transform.GetChild(i)).anchoredPosition,
                                                 (Vector2)shape.cellOffsets[i] * shape.cellSize) < 0.5f);
            float drift = Vector3.Distance(c0, BlockCenterWorld(shape));
            sb.AppendLine($"  돌림 1 — 가로 {w1} × 세로 {h1} · 칸 [{string.Join(" ", shape.cellOffsets)}] · 묶음 가운데 이동 {drift:0.00}(월드)");
            Check("돌림 1 — Rotation 1 · 아이템 방향 1", shape.Rotation == 1 && item.rotation == 1);
            Check("돌림 1 — 칸 = 원래 칸을 시계 방향 90°", offsetsOk);
            Check("돌림 1 — 가로 · 세로가 바뀜", w1 == h0 && h1 == w0);
            Check("돌림 1 — 블록 수 그대로 · 블록 자리 = 칸 × 간격", blocksOk);
            Check("돌림 1 — 묶음 가운데가 제자리(커서 아래에서 돈다)", drift < 0.05f * Mathf.Max(1f, shape.cellSize * shape.transform.lossyScale.x));

            // ② 돌린 채 놓기 — 차지한 칸이 가로 · 세로가 바뀐 모양
            await UniTask.Delay(300, ignoreTimeScale: true);
            Check("돈 뒤 칸 기울기 0(그림만 돌고 끝난다)", Enumerable.Range(0, shape.transform.childCount).All(i => Quaternion.Angle(shape.transform.GetChild(i).localRotation, Quaternion.identity) < 0.5f));
            bool placed = Place(panel, item);
            await UniTask.Delay(300, ignoreTimeScale: true);
            var occ = GridManager.Instance.grid.GetGridSquares().Where(s => s != null && s.isOccupied && s.occupyingItem == item).ToList();
            int occW = occ.Count == 0 ? 0 : occ.Max(s => s.col) - occ.Min(s => s.col) + 1;
            int occH = occ.Count == 0 ? 0 : occ.Max(s => s.row) - occ.Min(s => s.row) + 1;
            sb.AppendLine($"  놓음 {placed} · 차지 {occ.Count}칸 · 칸 가로 {occW} × 세로 {occH} · [{string.Join(" ", occ.Select(s => $"({s.col},{s.row})"))}]");
            Check("돌린 채 놓임 · 차지 칸 수 = 룬 칸 수", placed && occ.Count == baseOffsets.Count);
            Check("놓인 칸 가로 · 세로 = 돌린 모양(원래와 바뀜)", occW == w1 && occH == h1);
            await Shot("ROT25_placed");

            // ③ 세이브 — 배치 스냅샷 · 아이템 JSON에 방향이 실린다
            var entryCap = BoardManager.Instance.CapturePlacements().FirstOrDefault(e => e.instanceId == item.instanceId);
            Check("배치 스냅샷 칸 수 = 룬 칸 수", entryCap != null && entryCap.cells.Count == baseOffsets.Count);
            string json = JsonUtility.ToJson(item);
            var back = JsonUtility.FromJson<RuntimeItemData>(json);
            Check("아이템 JSON 왕복 — 방향 1 유지", back != null && back.rotation == 1 && json.Contains("\"rotation\":1"));
            var old = JsonUtility.FromJson<RuntimeItemData>(json.Replace("\"rotation\":1,", "").Replace(",\"rotation\":1", ""));
            Check("옛 세이브(방향 칸 없음) — 방향 0", old != null && old.rotation == 0);

            // ④ 새로 만든 Shape(이어하기 RestorePlacedShape · 보관함 다시 그림과 같은 순서: ApplyAsset → BindItem)가 방향을 지킨다
            probeShape = Object.Instantiate(BoardManager.Instance.shapePrefab);
            probeShape.ApplyAsset(shape.shapeAsset);
            probeShape.BindItem(back);
            await UniTask.Yield();
            Check("새 Shape — 방향 1 · 칸 = 돌린 칸", probeShape.Rotation == 1 && probeShape.cellOffsets.SequenceEqual(shape.cellOffsets));
            Check("새 Shape — 블록 수 = 칸 수(옛 블록이 안 남음)", probeShape.transform.childCount == probeShape.cellOffsets.Count);

            // ⑤ 회수 — 보관함으로 돌아와도 방향이 남고, 네 번 돌리면 원래대로
            typeof(UI_GridPanel).GetMethod("RemovePlacedItem", Inst)?.Invoke(panel, new object[] { item });
            await UniTask.Delay(400, ignoreTimeScale: true);
            var back2 = staging.GetShapeForItem(item) ?? BoardManager.Instance.GetSharedShapeByItem(item.instanceId);
            Check("회수 뒤 보관함 — 방향 1 남음", inv.IsStaging(item.instanceId) && back2 != null && back2.Rotation == 1);
            if (back2 != null)
            {
                for (int i = 0; i < 3; i++) back2.RotateClockwise();
                Check("네 번 돌리면 원래 칸 · 방향 0", back2.Rotation == 0 && item.rotation == 0 && back2.cellOffsets.SequenceEqual(baseOffsets));
                back2.RotateClockwise();   // 카드 · 정보판 모양 사진용으로 한 번 돌려 둔다
            }

            // ⑥ 정보판 작은 모양 · 안내 줄
            var info = typeof(UI_GridPanel).GetField("_itemInfoPanel", Inst)?.GetValue(panel) as ItemInfoPanel;
            info?.ShowItem(item, isNew: false);
            await UniTask.Delay(400, ignoreTimeScale: true);
            var hint = panel.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "PlaceHint");
            if (hint != null) hint.ForceMeshUpdate();
            sb.AppendLine($"  안내 줄 「{hint?.text?.Replace("\n", " / ")}」 · 줄 {hint?.textInfo.lineCount} · 넓이 {hint?.preferredWidth:0}/{hint?.rectTransform.rect.width:0}");
            Check("안내 줄에 돌리기(E)", hint != null && hint.text.Contains("E = 돌리기"));
            Check("안내 줄 두 줄 안 · 넘치지 않음", hint != null && hint.textInfo.lineCount <= 2 && hint.preferredWidth <= hint.rectTransform.rect.width + 1f);
            await Shot("ROT25_info");

            // ⑥b 정보판 [돌리기 E] 버튼 · 고른 것 없이 돌리면 그 룬이 골라짐 · 놓인 룬을 보며 E → 다른 룬을 안 돌리고 안내(10-26 「회전은 UI에 잘 표현」)
            var rotBtn  = typeof(UI_GridPanel).GetField("_rotateButton", Inst)?.GetValue(panel) as UnityEngine.UI.Button;
            var rotNote = typeof(UI_GridPanel).GetField("_rotateNote", Inst)?.GetValue(panel) as TMP_Text;
            Check("보관함 룬이면 정보판에 [돌리기 E] 버튼 · 안내는 숨김", rotBtn != null && rotBtn.gameObject.activeInHierarchy && rotNote != null && !rotNote.gameObject.activeSelf);
            int rb = item.rotation;
            rotBtn?.onClick.Invoke();
            await UniTask.Yield();
            Check("[돌리기 E] 버튼을 누르면 그 룬이 돈다", item.rotation == (rb + 1) % 4);
            info?.ShowEmpty();
            staging.HighlightItem(null);
            int rf = item.rotation;
            typeof(UI_GridPanel).GetMethod("RotateTarget", Inst)?.Invoke(panel, null);
            await UniTask.Yield();
            Check("아무것도 안 고르고 E → 보관함 첫 룬이 돌고 그 룬이 골라지며 정보판에 뜬다",
                  item.rotation == (rf + 1) % 4 && staging.HighlightedItem == item && info != null && info.CurrentItem == item);

            foreach (var entry in Managers.ItemData.GetAllItems().Values)   // 다른 보관함 룬 하나(여러 칸)
            {
                var d = RuntimeItemData.FromServer(entry);
                if (d == null || d.shapeId <= 0 || d.rarity == ItemRarity.Legendary || RuneCardKit.CellCount(d) < 2 || d.itemId == item.itemId) continue;
                d.EnsureElement();
                if (inv.AddToStaging(d)) other = d;
                break;
            }
            await UniTask.Delay(300, ignoreTimeScale: true);
            bool placedAgain = Place(panel, item);
            await UniTask.Delay(300, ignoreTimeScale: true);
            info?.ShowItem(item, isNew: false);
            staging.HighlightItem(null);
            await UniTask.Delay(200, ignoreTimeScale: true);
            int rItem = item.rotation, rOther = other != null ? other.rotation : -1;
            Check("놓인 룬이면 정보판에 「집어 들면 E로 돌린다」 · 버튼은 숨김",
                  placedAgain && rotNote != null && rotNote.gameObject.activeInHierarchy && rotBtn != null && !rotBtn.gameObject.activeSelf);
            typeof(UI_GridPanel).GetMethod("RotateTarget", Inst)?.Invoke(panel, null);
            await UniTask.Delay(150, ignoreTimeScale: true);
            sb.AppendLine($"  놓인 룬을 보며 E — 놓인 룬 방향 {rItem} → {item.rotation} · 보관함 다른 룬 「{other?.displayName}」 {rOther} → {other?.rotation}");
            Check("놓인 룬을 보며 E → 아무 룬도 안 돈다(다른 룬을 몰래 돌리지 않음)", other != null && item.rotation == rItem && other.rotation == rOther);
            await Shot("ROT25_placed_note");
            typeof(UI_GridPanel).GetMethod("RemovePlacedItem", Inst)?.Invoke(panel, new object[] { item });
            await UniTask.Delay(300, ignoreTimeScale: true);
            info?.ShowItem(item, isNew: false);
            await UniTask.Delay(300, ignoreTimeScale: true);
            await Shot("ROT25_button");

            // ⑦ 정지 중(룬판) E(돌리기) · R · 우클릭 → E · R 스킬 · 구르기가 시작되지 않는다
            var player = run.Player;
            var actSM = typeof(PlayerController).GetField("_actSM", Inst)?.GetValue(player);
            var locoSM = typeof(PlayerController).GetField("_locoSM", Inst)?.GetValue(player);
            string Act() => actSM?.GetType().GetProperty("CurrentId")?.GetValue(actSM)?.ToString();
            string Loco() => locoSM?.GetType().GetProperty("CurrentId")?.GetValue(locoSM)?.ToString();
            string a0 = Act(), l0 = Loco();
            player.InputBuffer.Push(Command.ESkill);
            player.InputBuffer.Push(Command.RSkill);
            player.InputBuffer.Push(Command.Dodge);
            await UniTask.DelayFrame(3);
            sb.AppendLine($"  정지 중 E · R · 우클릭 넣음 — 시간 {Time.timeScale} · 동작 {a0} → {Act()} · 이동 {l0} → {Loco()} · 버퍼 {player.InputBuffer.Count}");
            Check("동작 · 이동 상태 읽힘(아래 두 칸이 헛참이 아님)", a0 != null && l0 != null);
            Check("정지 중 E · R 스킬 안 시작", Act() != "ESkill" && Act() != "RSkill");
            Check("정지 중 구르기 안 시작", Loco() != "Dodge");
            Check("정지 중 넣은 명령은 버려짐(닫은 뒤 안 나감)", player.InputBuffer.Count == 0);

            typeof(UI_GridPanel).GetMethod("ClosePanel", Inst)?.Invoke(panel, null);
            await UniTask.Delay(600, ignoreTimeScale: true);
            Check("판 닫은 뒤에도 E · R 스킬 · 구르기 안 나감", Act() != "ESkill" && Act() != "RSkill" && Loco() != "Dodge");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (probeShape != null) Object.Destroy(probeShape.gameObject);
            foreach (var d in new[] { item, other })
            {
                if (d == null) continue;
                if (inv.IsPlaced(d.instanceId) && panel != null) panel.DiscardPlacedRune(d);
                if (inv.IsStaging(d.instanceId)) inv.DiscardFromStaging(d);
            }
            if (panel != null && panel.IsOpen) typeof(UI_GridPanel).GetMethod("ClosePanel", Inst)?.Invoke(panel, null);
            sb.AppendLine($"CHECK 합계 {pass}/{total}");
            File.WriteAllText("Temp/rot1025.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }

    private static (int w, int h) Size(IList<Vector2Int> cells)
    {
        if (cells == null || cells.Count == 0) return (0, 0);
        return (cells.Max(c => c.x) - cells.Min(c => c.x) + 1, cells.Max(c => c.y) - cells.Min(c => c.y) + 1);
    }

    /// <summary>보관함 카드 「ShapePreview」 칸 묶음의 열 · 줄 수(칸 크기는 상자에 맞춰 바뀌어 px로는 못 견준다).</summary>
    private static (int cols, int rows) CardPreviewSize(StagingAreaView staging, RuntimeItemData item)
    {
        var slots = typeof(StagingAreaView).GetField("_slotGOs", Inst)?.GetValue(staging) as GameObject[];
        var items = typeof(StagingAreaView).GetField("_slotItems", Inst)?.GetValue(staging) as RuntimeItemData[];
        if (slots == null || items == null) return (0, 0);
        int idx = System.Array.IndexOf(items, item);
        if (idx < 0 || slots[idx] == null || !(slots[idx].transform.Find("ShapePreview") is RectTransform root)) return (0, 0);
        var xs = new HashSet<int>(); var ys = new HashSet<int>();
        for (int i = 0; i < root.childCount; i++)
        {
            var p = ((RectTransform)root.GetChild(i)).anchoredPosition;
            xs.Add(Mathf.RoundToInt(p.x)); ys.Add(Mathf.RoundToInt(p.y));
        }
        return (xs.Count, ys.Count);
    }

    private static Vector3 BlockCenterWorld(Shape shape)
    {
        if (shape.transform.childCount == 0) return shape.transform.position;
        Vector3 min = shape.transform.GetChild(0).position, max = min;
        for (int i = 1; i < shape.transform.childCount; i++)
        {
            var p = shape.transform.GetChild(i).position;
            min = Vector3.Min(min, p); max = Vector3.Max(max, p);
        }
        return (min + max) * 0.5f;
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

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
