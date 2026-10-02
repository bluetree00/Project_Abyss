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
/// [실측 · 런 안에서] 10-01 룬 선택지 3장 고정 + 룬 획득 창 「지금 룬판」.
/// <para>① 규칙 — 정예 후보 수 · 제단 「룬 선택지 +1」 폐지 · 룬 갈래 입구.</para>
/// <para>② 화면 — 지금 판 그대로 3장 · 칸을 채운 판(가짜 점유)으로 3장 + 카드 고르기(들어갈 빈 칸 강조) · 1장 지급.
/// 판 숫자(채운 칸 · 속성 줄)가 브릿지 집계와 같은지, 판 자리가 카드 · 창과 겹치지 않는지 잰다.</para>
/// <para>가짜 점유는 판 뷰(<see cref="MerlinRuneHexGridView.RestoreOccupiedCells"/>)에 넣었다가 끝나면 원래 점유로 되돌린다.
/// 세이브 · 보관함은 건드리지 않는다.</para>
/// 결과: Temp/rune3_probe.txt · Temp/ui_shots/RS3_*.png
/// </summary>
public static class RuneSelectBoardProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags StaticNonPub = BindingFlags.Static | BindingFlags.NonPublic;

    private static readonly (string id, string elem)[] Offer =
    {
        ("item_t2_calm_crit", "ICE"), ("item_t3_fire_ember", "FIRE"), ("item_t1_weight", "GRASS"),
    };

    [MenuItem("RelicFairy/UI/10-01 룬 획득 3장 · 지금 룬판 실측 (런 안에서)")]
    private static void Run()
    {
        if (!Application.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            Debug.LogWarning("[Rune3] 런 안에서 실행해야 한다.");
            return;
        }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb  = new StringBuilder();
        var run = GameRunBootstrapper.Instance.Run;
        MerlinRuneHexGridView view = null;
        List<Vector2Int> prior = null;
        Invoke("HideTestHubGui");
        try
        {
            // ① 규칙
            sb.AppendLine($"정예 후보 {RoomRewardTable.For(RoomPlanKind.Elite).ChoiceCount} · 일반 {RoomRewardTable.For(RoomPlanKind.Normal).ChoiceCount} · 이벤트 {RoomRewardTable.For(RoomPlanKind.Event).ChoiceCount} (전부 3이어야)");
            sb.AppendLine($"제단 rune_choice_4 노드: {(MemoryAltarCatalog.Get("rune_choice_4") == null ? "없음(정상)" : "남아 있음 ✗")} · 노드 수 {MemoryAltarCatalog.All.Count}");
            foreach (var n in MemoryAltarCatalog.GetBranch(AltarBranch.Rune))
                sb.AppendLine($"  룬 갈래 {n.DisplayName} · 깊이 {MemoryAltarCatalog.Depth(n)} · 부모 [{string.Join(",", n.Parents)}]");

            var cands = new List<(RuntimeItemData, ItemSO)>();
            foreach (var (id, elem) in Offer)
            {
                var entries = Managers.ItemData?.GetItem(id);
                var d = entries != null ? RuntimeItemData.FromServer(entries) : null;
                if (d == null) { sb.AppendLine($"룬 없음 {id}"); continue; }
                d.element = elem;
                cands.Add((d, ItemSORegistry.Find(id)));
            }

            // ② A — 지금 판 그대로
            await OpenAndShoot(sb, "RS3_A", new List<(RuntimeItemData, ItemSO)>(cands), run, selectIndex: 1);

            // ② B — 칸을 채운 판(불 존 전부 · 물 절반 · 빛 1/4 · 중앙 3칸)
            view = UnityEngine.Object.FindFirstObjectByType<MerlinRuneHexGridView>(FindObjectsInactive.Include);
            if (view == null) sb.AppendLine("B 생략 — 판 뷰 없음");
            else
            {
                prior = view.GetOccupiedCells();
                view.RestoreOccupiedCells(FakeCells());
                await UniTask.Delay(300, ignoreTimeScale: true);
                var counts = MerlinRuneBridge.Instance?.GetZoneOccupiedCounts();
                var line = new StringBuilder("B 브릿지 집계:");
                if (counts != null) foreach (var kv in counts) line.Append($" {kv.Key}={kv.Value}");
                sb.AppendLine(line.ToString());
                // 카드는 정렬 뒤 순서가 바뀐다(등급 오름차순) — 불 · 물 룬을 id로 찾아 고른다.
                await OpenAndShoot(sb, "RS3_B", new List<(RuntimeItemData, ItemSO)>(cands), run, selectId: "item_t3_fire_ember");
                await OpenAndShoot(sb, "RS3_B2", new List<(RuntimeItemData, ItemSO)>(cands), run, selectId: "item_t2_calm_crit");
            }

            // ② C — 1장 지급
            await OpenAndShoot(sb, "RS3_C", new List<(RuntimeItemData, ItemSO)> { cands[0] }, run);
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            if (view != null && prior != null)
            {
                view.RestoreOccupiedCells(prior);
                sb.AppendLine($"판 점유 되돌림 → {view.GetOccupiedCells().Count}칸(원래 {prior.Count})");
            }
            Invoke("RestoreTestHubGui");
            File.WriteAllText(Path.Combine("Temp", "rune3_probe.txt"), sb.ToString());
            Debug.Log("[Rune3] 끝 — Temp/rune3_probe.txt\n" + sb);
        }
    }

    private static async UniTask OpenAndShoot(StringBuilder sb, string name, List<(RuntimeItemData, ItemSO)> cands,
                                              GameRunSession run, int selectIndex = -1, string selectId = null)
    {
        var pick = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
        pick.Setup(cands, run.ItemInventory);
        await UniTask.Delay(2400, ignoreTimeScale: true);   // 뒤집기 연출이 끝나게
        await Shot(name);
        Measure(sb, name, pick);

        if (selectId != null)
            for (int i = 0; i < cands.Count; i++) if (cands[i].Item1.itemId == selectId) selectIndex = i;
        if (selectIndex >= 0)
        {
            typeof(UI_RuneSelectPopup).GetMethod("SetSelected", Inst)?.Invoke(pick, new object[] { selectIndex });
            await UniTask.Delay(300, ignoreTimeScale: true);
            await Shot(name + "_sel");
            var group = FindDeep(pick.transform, "BoardPanel");
            int bright = 0, dim = 0;
            if (group != null)
                foreach (var img in group.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                {
                    if (!img.name.StartsWith("C") || img.sprite != null) continue;
                    if (img.color.a > 0.5f) bright++; else if (img.color.a < 0.12f) dim++;
                }
            sb.AppendLine($"  {name} 선택 {cands[selectIndex].Item1.displayName}({cands[selectIndex].Item1.element}) → 밝힌 빈 칸 {bright} · 누른 빈 칸 {dim}");
        }
        Managers.UI.CloseAllPopupUI();
        await UniTask.Delay(500, ignoreTimeScale: true);
    }

    /// <summary>카드 · 판 자리 사각형(창 좌표) — 겹침 · 창 밖 · 판 숫자.</summary>
    private static void Measure(StringBuilder sb, string name, UI_RuneSelectPopup pick)
    {
        var window = FindDeep(pick.transform, "Window") as RectTransform;
        var rects = new List<(string, Rect)>();
        foreach (Transform t in pick.GetComponentsInChildren<Transform>(true))
            if ((t.name.StartsWith("Card") && t.name.Length <= 5) || t.name == "BoardPanel")
                rects.Add((t.name, ScreenRect((RectTransform)t)));

        sb.AppendLine($"── {name}: 칸 {rects.Count}개");
        var win = window != null ? ScreenRect(window) : new Rect();
        for (int i = 0; i < rects.Count; i++)
        {
            var (n, r) = rects[i];
            bool inside = window == null || (r.xMin >= win.xMin - 1f && r.xMax <= win.xMax + 1f && r.yMin >= win.yMin - 1f && r.yMax <= win.yMax + 1f);
            sb.AppendLine($"  {n}: x {r.xMin:0}~{r.xMax:0} · 폭 {r.width:0} · 높이 {r.height:0}{(inside ? "" : " ✗ 창 밖")}");
            for (int j = i + 1; j < rects.Count; j++)
                if (r.Overlaps(rects[j].Item2)) sb.AppendLine($"  ✗ 겹침 {n} ↔ {rects[j].Item1}");
        }

        var board = FindDeep(pick.transform, "BoardPanel");
        if (board == null) { sb.AppendLine("  지금 룬판 없음"); return; }
        foreach (var t in board.GetComponentsInChildren<TMP_Text>(true))
            if (t.name == "Title" || t.name == "Total" || t.name == "Count" || t.name == "Name")
                sb.Append($" [{t.transform.parent.name}/{t.name}] {t.text}");
        sb.AppendLine();
        int cells = 0;
        foreach (var img in board.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            if (img.name.StartsWith("C") && img.name.Contains("_")) cells++;
        var cg = board.GetComponent<CanvasGroup>();
        sb.AppendLine($"  판 칸 {cells}개 · 알파 {(cg != null ? cg.alpha : -1f):0.##}");
    }

    /// <summary>불 존 전부 · 물 존 절반 · 빛 존 1/4 · 중앙 3칸.</summary>
    private static List<Vector2Int> FakeCells()
    {
        var rows = Managers.RuneData.GetZoneMapRows();
        int maxLen = 0;
        foreach (var r in rows) if (r?.pattern != null) maxLen = Mathf.Max(maxLen, r.pattern.Length);
        var seen = new Dictionary<char, int>();
        var list = new List<Vector2Int>();
        foreach (var r in rows)
        {
            if (r?.pattern == null) continue;
            int off = (maxLen - r.pattern.Length) / 2;
            for (int c = 0; c < r.pattern.Length; c++)
            {
                char code = r.pattern[c];
                seen.TryGetValue(code, out int k); seen[code] = k + 1;
                bool take = code == 'F' || (code == 'I' && k % 2 == 0) || (code == 'L' && k % 4 == 0) || (code == '+' && k < 3);
                if (take) list.Add(new Vector2Int(c + off, r.hex_row));
            }
        }
        return list;
    }

    private static Rect ScreenRect(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
    }

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    private static async UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", StaticNonPub);
        if (m != null) await (UniTask)m.Invoke(null, new object[] { name, 0 });
    }

    private static void Invoke(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, StaticNonPub)?.Invoke(null, null);
}
