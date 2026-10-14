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
/// 10-08 룬판 톤 v2 · 블록 예산 실측(런 중) — 큰 룬부터 칸 누르기와 같은 길(UI_GridPanel.HandleSquareClicked)로 판에 놓아
/// 블록 20을 채운 뒤 넘치는 룬을 한 번 더 놓아 본다. 촬영 5장(열림 · 고름 · 시너지 단계 켜짐 · 넘침 거절 · 룬 선택 카드).
/// 확인: 넘침 거절 · 토스트 「블록」 · 머리띠 「블록 n / 20」 · 「놓을 자리」 글자 없음 · 글자 Noto · 굵게 없음 · 시너지가 맨 위 ·
/// 단계 칸 금 테 · 룬 선택 카드 배지 「블록」. 놓은 룬은 끝에 회수, 넣은 룬은 보관함에서 뺀다.
/// 결과: Temp/ui_shots/RG_*.png · Temp/rune_grid_thin_probe.txt · 로그 「[RuneGridThin] 끝」.
/// </summary>
public static class RuneGridThinProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const int Budget = 20;
    private static readonly string[] Elems = { "FIRE", "ICE", "GRASS", "DARK", "LIGHT", "ELECTRIC" };

    [MenuItem("RelicFairy/Debug/10-08 룬판 톤 v2 · 블록 예산 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning("[RuneGridThin] 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb  = new StringBuilder("룬판 톤 v2 · 블록 예산\n");
        var inv = GameRunBootstrapper.Instance.Run.ItemInventory;
        var added  = new List<RuntimeItemData>();
        var placed = new List<RuntimeItemData>();
        UI_GridPanel panel = null;
        Directory.CreateDirectory("Temp/ui_shots");
        try
        {
            // 큰 모양부터(전설 포함) — 블록 20을 넘기려면 큰 룬이 필요하다
            var pool = new List<RuntimeItemData>();
            var seen = new HashSet<int>();
            foreach (var entry in Managers.ItemData.GetAllItems().Values)
            {
                var d = RuntimeItemData.FromServer(entry);
                if (d == null || d.shapeId <= 0 || !seen.Add(d.shapeId)) continue;
                pool.Add(d);
            }
            foreach (var d in pool.OrderByDescending(Cells).Where(d => Cells(d) >= 4 && Cells(d) <= 9).Take(6))
            {
                d.element = Elems[added.Count % Elems.Length];
                if (!inv.AddToStaging(d)) break;
                added.Add(d);
            }
            sb.AppendLine($"  넣은 룬 {added.Count} · 칸 {string.Join(",", added.Select(Cells))}");

            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            panel = UI_GridPanel.Instance;
            panel.ShowWithNewItem(null);
            await UniTask.Delay(1500, ignoreTimeScale: true);
            await Shot("RG_1_open", 0);

            // 고름 — 둘째 카드를 고른다(들림 · 테 밝아짐)
            if (added.Count > 1) Invoke(panel, "OnStagingItemSelected", added[1]);
            await Shot("RG_2_select", 120);

            // 놓기 — 예산 안에서 큰 것부터. 시너지 단계가 처음 켜지는 순간을 찍는다.
            bool traceShot = false;
            RuntimeItemData rejectCandidate = null;
            foreach (var it in added)
            {
                int used = Used();
                var sq = FindFit(panel, it);
                if (sq == null) { sb.AppendLine($"  자리 없음 {it.displayName}({Cells(it)}칸 {it.element})"); continue; }
                if (used + Cells(it) > Budget) { rejectCandidate ??= it; continue; }
                Invoke(panel, "OnStagingItemSelected", it);
                Invoke(panel, "HandleSquareClicked", sq);
                await UniTask.Delay(80, ignoreTimeScale: true);
                if (Used() > used) { placed.Add(it); sb.AppendLine($"  놓음 {it.displayName} {Cells(it)}칸 → 블록 {Used()}"); }
                if (!traceShot && MaxZone() >= 6) { ScreenCapture.CaptureScreenshot("Temp/ui_shots/RG_3_tier.png"); traceShot = true; }
                await UniTask.Delay(300, ignoreTimeScale: true);
            }

            // 넘침 — 자리는 있지만 블록 예산을 넘는 룬
            int before = Used();
            string toast = null;
            if (rejectCandidate != null)
            {
                var sq = FindFit(panel, rejectCandidate);
                Invoke(panel, "OnStagingItemSelected", rejectCandidate);
                Invoke(panel, "HandleSquareClicked", sq);
                await UniTask.Delay(150, ignoreTimeScale: true);
                toast = (typeof(UI_GridPanel).GetField("_synergyToastText", Inst)?.GetValue(panel) as TMP_Text)?.text;
                ScreenCapture.CaptureScreenshot("Temp/ui_shots/RG_4_reject.png");
                await UniTask.Delay(300, ignoreTimeScale: true);
            }
            sb.AppendLine($"  넘침 시도 {rejectCandidate?.displayName}({(rejectCandidate != null ? Cells(rejectCandidate) : 0)}칸) · 블록 {before} → {Used()} · 토스트 「{toast}」");
            sb.AppendLine($"  CHECK 블록 예산 — 넘치는 배치가 막힘: {rejectCandidate != null && Used() == before && before <= Budget}");
            sb.AppendLine($"  CHECK 거절 토스트에 「블록」: {toast != null && toast.Contains("블록")}");

            var root = panel.transform;
            var counter = FindDeep(root, "GuideHeader")?.Find("Desc")?.GetComponent<TMP_Text>();
            string want = $"블록 {Used()} / {Budget}";
            sb.AppendLine($"  CHECK 머리띠 「{want}」: {counter != null && counter.GetParsedText() == want} (지금 「{counter?.GetParsedText()}」)");

            var texts = root.GetComponentsInChildren<TMP_Text>(false);
            int fitWords = texts.Count(t => t.text != null && t.text.Contains("놓을 자리"));
            sb.AppendLine($"  CHECK 「놓을 자리」 글자 없음: {fitWords == 0} ({fitWords})");

            var thin = new[] { UITheme.Font(UIWeight.Light), UITheme.Font(UIWeight.Regular), UITheme.Font(UIWeight.Medium) };
            var bad = texts.Where(t => System.Array.IndexOf(thin, t.font) < 0 || (t.fontStyle & FontStyles.Bold) != 0 || t.text.Contains("<b>")).ToList();
            sb.AppendLine($"  CHECK 글자 전부 Noto · 굵게 없음: {bad.Count == 0} (어긋남 {bad.Count}: {string.Join(" ", bad.Take(6).Select(t => t.name))})");

            var syn  = FindDeep(root, "SynergyStatusRoot");
            var info = FindDeep(root, "CharacterInfoPanel");
            sb.AppendLine($"  CHECK 속성 시너지가 캐릭터 정보보다 위: {syn != null && info != null && syn.position.y > info.position.y}");

            var frames = syn != null ? syn.GetComponentsInChildren<UIThinFrame>(false) : new UIThinFrame[0];
            int ready = frames.Count(f => f.State == ThinFrameState.Ready);
            sb.AppendLine($"  CHECK 단계 도달 칸에 금 테(Ready): {ready > 0} ({ready}) · 단계 찍음 {traceShot}");

            // 룬 선택 카드 — 배지가 「블록 …」
            var small = pool.Where(d => Cells(d) >= 2 && Cells(d) <= 4 && d.rarity != ItemRarity.Legendary).Take(3).ToList();   // 전설은 개봉에서 멈춘다
            for (int i = 0; i < small.Count; i++) small[i].element = Elems[i];
            var cands = small.Select(d => (d, ItemSORegistry.Find(d.itemId))).ToList();
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
            popup.Setup(cands, inv);
            await UniTask.Delay(2600, ignoreTimeScale: true);   // 내려앉고 뒤집힐 때까지
            await Shot("RG_5_runeselect", 0);
            var badges = popup.GetComponentsInChildren<TMP_Text>(true)
                              .Where(t => t.transform.parent?.parent != null && t.transform.parent.parent.name.StartsWith("PlaceBadge")).ToList();   // 라벨 → Fill → PlaceBadge
            sb.AppendLine($"  CHECK 룬 선택 배지 = 「블록 …」: {badges.Count > 0 && badges.All(t => t.text.StartsWith("블록"))} ({string.Join(" | ", badges.Select(t => t.text))})");
            Managers.UI.CloseAllPopupUI();
            await UniTask.Delay(400, ignoreTimeScale: true);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            try
            {
                if (panel != null)
                {
                    foreach (var it in placed) typeof(UI_GridPanel).GetMethod("RemovePlacedItem", Inst)?.Invoke(panel, new object[] { it });
                    await UniTask.Delay(300, ignoreTimeScale: true);
                    typeof(UI_GridPanel).GetMethod("ClosePanel", Inst)?.Invoke(panel, null);
                    await UniTask.Delay(600, ignoreTimeScale: true);
                }
            }
            catch (System.Exception e) { sb.AppendLine("정리 예외: " + e.Message); }
            foreach (var d in added) if (inv.IsStaging(d.instanceId)) inv.DiscardFromStaging(d);
            File.WriteAllText("Temp/rune_grid_thin_probe.txt", sb.ToString());
            Debug.Log("[RuneGridThin] 끝\n" + sb);
        }
    }

    private static int Cells(RuntimeItemData d)
    {
        var e = Managers.RuneData?.GetShape(d.shapeId);
        return e != null ? RuneDataManager.ParseCellOffsets(e).Length : 0;
    }

    private static int Used()
    {
        int n = 0;
        var c = MerlinRuneBridge.Instance?.GetZoneOccupiedCounts();
        if (c != null) foreach (var kv in c) n += kv.Value;
        return n;
    }

    private static int MaxZone()
    {
        int m = 0;
        var c = MerlinRuneBridge.Instance?.GetZoneOccupiedCounts();
        if (c != null) foreach (var kv in c) if (kv.Key != "CENTER") m = Mathf.Max(m, kv.Value);
        return m;
    }

    /// <summary>미리보기 발자국(GridManager.ResolveFootprint)이 전부 맞는 첫 빈 칸 — 블록 예산은 보지 않는다.</summary>
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

    private static void Invoke(UI_GridPanel panel, string method, object arg)
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
