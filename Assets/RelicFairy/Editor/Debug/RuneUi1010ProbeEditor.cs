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

/// <summary>
/// 10-10 UI · 룬 실측(런 중) — 사용자 「HUD 장비 · 포션 · 스킬 칸 크게 · 룬 획득에 어떤 룬인지 · 모양은 판과 같은 리소스 ·
/// 룬판 위 정보 · 가운데 시너지 · 아래 총합 · 20칸이 아니라 20개 · 확률 UI 얇고 깔끔하게」.
/// ① HUD 원 크기(Q 114 · E/R 88 · 포션 64 · 무기 78/44) ② 룬 획득 카드(룬 그림 · 판과 같은 타일 · 배지 「룬 n + 1 / 20」) · 확률 막대(네 등급 · 올린 이유)
/// ③ 룬판 왼쪽 열 순서 · 머리띠 「룬 n / 20」 ④ 작은 룬으로 20개를 채운 뒤 21번째가 막히는지(토스트 「룬이 가득」).
/// 놓은 룬은 끝에 회수, 넣은 룬은 보관함에서 뺀다. 결과: Temp/ui_shots/U10_*.png · Temp/rune_ui_1010_probe.txt · 로그 「[RuneUi1010] 끝」.
/// </summary>
public static class RuneUi1010ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly string[] Elems = { "FIRE", "ICE", "GRASS", "DARK", "LIGHT", "ELECTRIC" };
    private static StringBuilder _sb;
    private static int _pass, _fail;

    [MenuItem("RelicFairy/Debug/10-10 UI · 룬 20개 상한 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning("[RuneUi1010] 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        _sb = new StringBuilder("10-10 UI · 룬\n");
        _pass = _fail = 0;
        var run    = GameRunBootstrapper.Instance.Run;
        var inv    = run.ItemInventory;
        var added  = new List<RuntimeItemData>();
        var placed = new List<RuntimeItemData>();
        UI_GridPanel panel = null;
        Directory.CreateDirectory("Temp/ui_shots");
        try
        {
            // ① HUD
            await Shot("U10_hud", 300);
            var hud = Object.FindFirstObjectByType<CombatPanelView>(FindObjectsInactive.Include);
            Check("HUD Q 114",      Size(hud, "HUD_QSkile")    == 114f, Size(hud, "HUD_QSkile").ToString());
            Check("HUD E 88",       Size(hud, "HUD_ESkile")    == 88f,  Size(hud, "HUD_ESkile").ToString());
            Check("HUD R 88",       Size(hud, "HUD_RSkile")    == 88f,  Size(hud, "HUD_RSkile").ToString());
            Check("HUD 포션 64",    Size(hud, "HUD_Active_01") == 64f,  Size(hud, "HUD_Active_01").ToString());
            var wr = hud != null ? typeof(CombatPanelView).GetField("_weaponSlotRoot", Inst)?.GetValue(hud) as RectTransform[] : null;
            var ws = wr != null ? wr.Where(r => r != null).Select(r => r.sizeDelta.x).OrderByDescending(v => v).ToArray() : new float[0];
            Check("HUD 무기 78 / 44", ws.Length == 2 && ws[0] == 78f && ws[1] == 44f, string.Join("/", ws));

            // ② 룬 획득 카드 · 확률 막대
            var pool = Pool();
            var cards = pool.Where(d => Cells(d) >= 2 && Cells(d) <= 5 && d.rarity != ItemRarity.Legendary)
                            .GroupBy(Cells).Select(g => g.First()).Take(3).ToList();   // 전설은 개봉에서 멈춘다
            for (int i = 0; i < cards.Count; i++) cards[i].element = Elems[i];
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
            popup.Setup(cards.Select(d => (d, ItemSORegistry.Find(d.itemId))).ToList(), inv);
            await UniTask.Delay(2600, ignoreTimeScale: true);   // 내려앉고 뒤집힐 때까지
            await Shot("U10_popup", 0);
            var icons = popup.GetComponentsInChildren<Image>(true).Where(i => i.name == "RuneIcon" && i.sprite != null).ToList();
            Check("카드마다 룬 그림", icons.Count == cards.Count, $"{icons.Count}/{cards.Count}");
            int tileOk = 0, tileAll = 0;
            foreach (var shape in popup.GetComponentsInChildren<RectTransform>(true).Where(t => t.name == "Shape"))
                foreach (var img in shape.GetComponentsInChildren<Image>(true).Where(i => i.name.StartsWith("C") && i.name.Contains("_")))
                {
                    tileAll++;
                    if (img.sprite != null && Elems.Any(e => RuneArt.GetBlockTile(e) == img.sprite)) tileOk++;
                }
            Check("모양 칸 = 판과 같은 속성 타일", tileAll > 0 && tileOk == tileAll, $"{tileOk}/{tileAll}");
            var badges = popup.GetComponentsInChildren<TMP_Text>(true)
                              .Where(t => t.transform.parent?.parent != null && t.transform.parent.parent.name.StartsWith("PlaceBadge")).ToList();
            Check("배지 「룬 n + 1 / 20」", badges.Count > 0 && badges.All(t => t.text.StartsWith("룬 ") && t.text.Contains("+ 1 / 20")),
                  string.Join(" | ", badges.Select(t => t.text)));
            var odds = popup.GetComponentInChildren<OddsBarView>(true);
            var segs = odds != null ? odds.GetComponentsInChildren<Image>(true).Count(i => i.name.StartsWith("Seg")) : 0;
            var col0 = odds != null ? odds.transform.Find("Col0") : null;
            var boost = odds != null ? odds.transform.Find("Boost")?.GetComponent<TMP_Text>() : null;
            var kind  = run.CurrentRoomKind;
            Check("확률 막대 네 구간 · Common 칸", segs == 4 && col0 != null && col0.gameObject.activeSelf, $"구간 {segs} · Common {(col0 != null && col0.gameObject.activeSelf)}");
            Check("확률 막대 올린 이유", boost != null && boost.text == RoomRewardTable.DescribeBoost(kind),
                  $"「{boost?.text}」 · 챕터 {run.CurrentChapter} · 방 {kind} · 단계 {RoomRewardTable.ChapterStep}");
            Check("옛 막대 줄(L0 · T0 · P0) 걷힘", odds != null && odds.transform.Find("L0") == null && odds.GetComponentsInChildren<Transform>(true).All(t => t.name != "T0"), "");
            Managers.UI.CloseAllPopupUI();
            await UniTask.Delay(500, ignoreTimeScale: true);

            // ③ 룬판 열기 — 왼쪽 열 순서 · 머리띠
            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            panel = UI_GridPanel.Instance;
            panel.ShowWithNewItem(null);
            await UniTask.Delay(1500, ignoreTimeScale: true);
            await Shot("U10_grid_open", 0);
            var root = panel.transform;
            var info = FindDeep(root, "CharacterInfoPanel") as RectTransform;
            var syn  = FindDeep(root, "SynergyStatusRoot") as RectTransform;
            var eff  = FindDeep(root, "EffectsRoot") as RectTransform;
            Check("왼쪽 열 순서 — 정보 · 시너지 · 활성 효과", info != null && syn != null && eff != null &&
                  info.position.y > syn.position.y && syn.position.y > eff.position.y,
                  info != null && syn != null && eff != null ? $"{info.position.y:0} > {syn.position.y:0} > {eff.position.y:0}" : "없음");
            Check("활성 효과 머리띠가 아래 칸에", eff != null && FindDeep(eff, "EffectsHdr") != null, "");
            CheckHeader(root);

            // ④ 20개 채우기 — 작은 룬(1~2칸)을 보관함에 넣고 놓기를 되풀이
            var small = pool.Where(d => Cells(d) >= 1 && Cells(d) <= 2).ToList();
            int guard = 0;
            while (RuneBlockBudget.Used < RuneBlockBudget.Max && guard++ < 8 && small.Count > 0)
            {
                var batch = new List<RuntimeItemData>();
                for (int k = 0; k < 4 && RuneBlockBudget.Used + batch.Count < RuneBlockBudget.Max; k++)
                {
                    var d = Fresh(small[(added.Count + k) % small.Count], Elems[(added.Count + k) % Elems.Length]);
                    if (d == null || !inv.AddToStaging(d)) break;
                    batch.Add(d);
                }
                added.AddRange(batch);
                if (batch.Count == 0) break;
                Close(panel);
                panel.ShowWithNewItem(null);
                await UniTask.Delay(1200, ignoreTimeScale: true);
                foreach (var it in batch)
                {
                    int used = RuneBlockBudget.Used;
                    var sq = FindFit(panel, it);
                    if (sq == null) { _sb.AppendLine($"  자리 없음 {it.displayName}({it.element})"); continue; }
                    Call(panel, "OnStagingItemSelected", it);
                    Call(panel, "HandleSquareClicked", sq);
                    await UniTask.Delay(80, ignoreTimeScale: true);
                    if (RuneBlockBudget.Used > used) placed.Add(it);
                }
            }
            _sb.AppendLine($"  놓은 룬 {placed.Count} · 판 룬 {RuneBlockBudget.Used} / {RuneBlockBudget.Max}");
            Check("20개까지 놓임", RuneBlockBudget.Used == RuneBlockBudget.Max, $"{RuneBlockBudget.Used}");

            // 21번째 — 자리는 있지만 상한
            var extra = Fresh(small[0], Elems[0]);
            string toast = null;
            int before = RuneBlockBudget.Used;
            if (extra != null && inv.AddToStaging(extra))
            {
                added.Add(extra);
                Close(panel);
                panel.ShowWithNewItem(null);
                await UniTask.Delay(1200, ignoreTimeScale: true);
                var sq = FindFit(panel, extra);
                Call(panel, "OnStagingItemSelected", extra);
                if (sq != null) Call(panel, "HandleSquareClicked", sq);
                await UniTask.Delay(150, ignoreTimeScale: true);
                toast = (typeof(UI_GridPanel).GetField("_synergyToastText", Inst)?.GetValue(panel) as TMP_Text)?.text;
                await Shot("U10_grid_full", 0);
                Check("21번째 막힘 · 토스트 「룬이 가득」", sq != null && RuneBlockBudget.Used == before && toast != null && toast.StartsWith(RuneBlockBudget.FullPrefix),
                      $"자리 {(sq != null)} · {before} → {RuneBlockBudget.Used} · 「{toast}」");
            }
            CheckHeader(root);
            Check("카드 문구 넘침(가득일 때)", RuneBlockBudget.Label().EndsWith("· 넘침"), RuneBlockBudget.Label());
        }
        catch (System.Exception e) { _sb.AppendLine("예외: " + e); _fail++; }
        finally
        {
            try
            {
                if (panel != null)
                {
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
            File.WriteAllText("Temp/rune_ui_1010_probe.txt", _sb.ToString());
            Debug.Log("[RuneUi1010] 끝\n" + _sb);
        }
    }

    private static void CheckHeader(Transform root)
    {
        var counter = FindDeep(root, "GuideHeader")?.Find("Desc")?.GetComponent<TMP_Text>();
        string want = $"룬 {RuneBlockBudget.Used} / {RuneBlockBudget.Max}";
        Check($"머리띠 「{want}」", counter != null && counter.GetParsedText() == want, counter?.GetParsedText());
    }

    private static void Check(string name, bool ok, string note)
    {
        if (ok) _pass++; else _fail++;
        _sb.AppendLine($"  {(ok ? "통과" : "실패")} — {name}{(string.IsNullOrEmpty(note) ? "" : " · " + note)}");
    }

    private static float Size(CombatPanelView hud, string name)
        => hud != null && FindDeep(hud.transform, name) is RectTransform rt ? Mathf.Round(rt.sizeDelta.x) : -1f;

    private static List<RuntimeItemData> Pool()
    {
        var list = new List<RuntimeItemData>();
        var seen = new HashSet<int>();
        foreach (var entry in Managers.ItemData.GetAllItems().Values)
        {
            var d = RuntimeItemData.FromServer(entry);
            if (d == null || d.shapeId <= 0 || !seen.Add(d.shapeId)) continue;
            list.Add(d);
        }
        return list;
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

    /// <summary>미리보기 발자국(GridManager.ResolveFootprint)이 전부 맞는 첫 빈 칸 — 상한은 보지 않는다.</summary>
    private static GridSquare FindFit(UI_GridPanel panel, RuntimeItemData item)
    {
        var stg   = typeof(UI_GridPanel).GetField("_stagingArea", Inst)?.GetValue(panel) as StagingAreaView;
        var shape = stg?.GetShapeForItem(item);
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

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++) { var hit = FindDeep(t.GetChild(i), name); if (hit != null) return hit; }
        return null;
    }

    private static async UniTask Shot(string name, int waitMs)
    {
        if (waitMs > 0) await UniTask.Delay(waitMs, ignoreTimeScale: true);
        ScreenCapture.CaptureScreenshot($"Temp/ui_shots/{name}.png");
        await UniTask.Yield();
    }
}
#endif
