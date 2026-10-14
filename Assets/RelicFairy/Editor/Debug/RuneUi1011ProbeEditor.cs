#if UNITY_EDITOR
using System.Collections;
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
/// 10-11 룬 선택 팝업 · 룬판 위계 · 가독성 개편 실측(테스트 허브에서 런 중) — 비교 리서치 B §2 · §3 「고칠 값」.
/// ① 팝업(일반 · 영웅 · 전설 한 장씩 — 없으면 다른 등급) ② 가운데 카드를 고른 뒤
/// ③ 룬판 — 작은 룬 3개를 놓고 보관함을 비운 상태(빈 칸 · 조각 외곽선 · 정보판의 놓인 룬 = 금 외곽선)
/// ④ 룬판 배치 중 — 보관함에 1개를 넣고 고른 상태(같은 속성 존 강조 · 맥박 상한).
/// 사진에서 직접 잰다: 팝업 창 면적 비율 · 채도 높은 픽셀 비율, 빈 칸 평균 밝기. 놓은 룬은 끝에 회수, 넣은 룬은 보관함에서 뺀다.
/// 결과: Temp/ui_shots/U11_*.png · Temp/rune_ui_1011_probe.txt · 로그 「[RuneUi1011] 끝」. Assets/에는 쓰지 않는다(플레이 중 도메인 리로드).
/// </summary>
public static class RuneUi1011ProbeEditor
{
    // ── Constants ──
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string ShotDir = "Temp/ui_shots";
    private static readonly string[] Elems = { "FIRE", "ICE", "GRASS", "DARK", "LIGHT", "ELECTRIC" };

    // ── Static ──
    private static StringBuilder _sb;
    private static int _pass, _fail;

    [MenuItem("RelicFairy/Debug/10-11 룬 선택·룬판 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning("[RuneUi1011] 런 안에서(테스트 허브 → 챕터 진입 뒤)"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        _sb = new StringBuilder("10-11 룬 선택 · 룬판 실측\n");
        _pass = _fail = 0;
        var run    = GameRunBootstrapper.Instance.Run;
        var inv    = run.ItemInventory;
        var added  = new List<RuntimeItemData>();
        var placed = new List<RuntimeItemData>();
        UI_GridPanel panel = null;
        Directory.CreateDirectory(ShotDir);
        try
        {
            await ProbePopupAsync(inv);
            Managers.UI.CloseAllPopupUI();
            await UniTask.Delay(500, ignoreTimeScale: true);

            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            panel = UI_GridPanel.Instance;
            await ProbeGridAsync(panel, inv, added, placed);
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
            File.WriteAllText("Temp/rune_ui_1011_probe.txt", _sb.ToString());
            Debug.Log("[RuneUi1011] 끝\n" + _sb);
        }
    }

    // ── ①② 팝업 ──

    private static async UniTask ProbePopupAsync(RunItemInventory inv)
    {
        _sb.AppendLine("■ 룬 선택 팝업");
        var pool  = Pool().Where(d => Cells(d) >= 2 && Cells(d) <= 5).ToList();
        var picks = new List<RuntimeItemData>();
        foreach (var r in new[] { ItemRarity.Common, ItemRarity.Epic, ItemRarity.Legendary })
        {
            var d = pool.FirstOrDefault(x => x.rarity == r && !picks.Contains(x)) ?? pool.FirstOrDefault(x => !picks.Contains(x));
            if (d != null) picks.Add(d);
        }
        for (int i = 0; i < picks.Count; i++) picks[i].element = Elems[i];
        _sb.AppendLine($"  후보: {string.Join(" · ", picks.Select(p => $"{p.displayName}({p.rarity} · {Cells(p)}칸)"))}");

        var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
        popup.Setup(picks.Select(d => (d, ItemSORegistry.Find(d.itemId))).ToList(), inv);
        await UniTask.Delay(4000, ignoreTimeScale: true);   // 내려앉고 뒤집힐 때까지(전설은 0.55초 멈춘다)
        string shot = await Shot("U11_popup");

        // 창 — 맞춤 상한 1.3 · 화면 면적
        var window = FindDeep(popup.transform, "Window") as RectTransform;
        var fitter = window != null ? window.GetComponent<UIWindowFitter>() : null;
        float maxScale = fitter != null ? (float)typeof(UIWindowFitter).GetField("maxScale", Inst).GetValue(fitter) : -1f;
        float applied  = window != null && window.TryGetComponent<UIFontScaleMemo>(out var memo) ? memo.Applied : -1f;
        var wr = window != null ? ScreenRect(window) : default;
        float share = wr.width * wr.height / (Screen.width * (float)Screen.height);
        Check("창 맞춤 상한 1.3", Mathf.Approximately(maxScale, 1.3f) && applied > 0f && applied <= 1.301f, $"상한 {maxScale:0.##} · 실효 {applied:0.###}");
        Check("창 면적 화면의 60% 이하(예전 88%)", share > 0.2f && share <= 0.60f, $"{share * 100f:0.#}% · {wr.width:0}×{wr.height:0}px");
        float sat = SatShare(shot, new Rect(0f, 0f, Screen.width, Screen.height));
        _sb.AppendLine($"  채도 높은 픽셀(S≥0.45 · V≥0.35) 화면의 {sat * 100f:0.#}% — 리서치 예전 12.0% · 목표 6% 안팎");

        // 글자 — Noto · 가짜 굵게 · 기울임 없음(확률 막대는 정제소 작업자 몫이라 따로 적는다)
        var odds  = popup.GetComponentInChildren<OddsBarView>(true);
        var texts = popup.GetComponentsInChildren<TMP_Text>(false).Where(t => odds == null || !t.transform.IsChildOf(odds.transform)).ToList();
        var dnf   = texts.Where(t => t.font != null && t.font.name.Contains("DNF")).Select(t => t.name).ToList();
        var faux  = texts.Where(t => (t.fontStyle & (FontStyles.Bold | FontStyles.Italic)) != 0).Select(t => t.name).ToList();
        Check("팝업 글자 DNF 0", dnf.Count == 0, $"{texts.Count}개 중 DNF {dnf.Count} {string.Join(",", dnf.Distinct())}");
        Check("가짜 굵게 · 기울임 0", faux.Count == 0, string.Join(",", faux.Distinct()));
        var title = window != null ? FindDeep(window, "Title")?.GetComponent<TMP_Text>() : null;
        Check("제목 Noto Light 24", title != null && title.font.name.Contains("Light") && Mathf.Abs(title.fontSize / Mathf.Max(0.01f, applied) - 24f) < 0.6f,
              title != null ? $"{title.font.name} {title.fontSize / Mathf.Max(0.01f, applied):0.#}" : "없음");
        LogSizes(texts.Where(t => t.transform.parent != null && t.transform.parent.name == "Face").ToList(), "Name", "이름 Noto Medium 22", 22f, "Medium");
        LogSizes(texts.Where(t => t.transform.parent != null && t.transform.parent.name == "EffectRow").ToList(), "Label", "효과 Noto Regular 18(넘치면 줄임)", 18f, "Regular");

        // 카드 — 후광 전설만 · 보석 테 영웅 이상 · 등급색 선 1~2 · 룬 수는 글자만 · 속성 칩 테 없음
        var cards = Cards(popup);
        int legend = cards.Count(c => c.rarity == ItemRarity.Legendary);
        int epicUp = cards.Count(c => c.rarity >= ItemRarity.Epic);
        int halos  = popup.GetComponentsInChildren<Image>(true).Count(i => i.name == "RarityHalo");
        int gems   = popup.GetComponentsInChildren<Image>(true).Count(i => i.name == "Gem_TL");
        Check("후광 = 전설 카드 수", halos == legend, $"후광 {halos} · 전설 {legend}");
        Check("보석 테 = 영웅 이상 카드 수", gems == epicUp, $"보석 {gems} · 영웅 이상 {epicUp}");
        var bars = cards.Select(c => $"{c.rarity} {(c.rt.Find("RarityBar0") as RectTransform)?.sizeDelta.y:0.#}").ToList();
        _sb.AppendLine($"  등급색 선 두께(목업 px): {string.Join(" · ", bars)} — 목표 일반 · 희귀 1 / 영웅 1.5 / 전설 2");
        var badges = popup.GetComponentsInChildren<TMP_Text>(true).Where(t => t.name == "PlaceBadge").ToList();
        Check("룬 수 = 글자 한 줄(칠 바 없음)", badges.Count == cards.Count && badges.All(b => b.GetComponent<Image>() == null && b.transform.Find("Fill") == null),
              string.Join(" | ", badges.Select(b => b.text)));
        var chips = popup.GetComponentsInChildren<Image>(true).Where(i => i.name == "ElementChip").ToList();
        Check("속성 칩 테 · 채움 없음", chips.Count > 0 && chips.All(i => i.color.a < 0.01f && (i.transform.Find("Fill")?.GetComponent<Image>()?.color.a ?? 0f) < 0.01f), $"칩 {chips.Count}");
        var confirm = window != null ? FindDeep(window, "ConfirmBtn") as RectTransform : null;
        var skip    = window != null ? FindDeep(window, "SkipBtn") as RectTransform : null;
        float k = applied > 0f ? applied : 1f;
        Check("[선택] 220×44 · [넘기기] 높이 44", confirm != null && skip != null
              && Mathf.Abs(confirm.rect.width / k - 220f) < 3f && Mathf.Abs(confirm.rect.height / k - 44f) < 2f && Mathf.Abs(skip.rect.height / k - 44f) < 2f,
              confirm != null && skip != null ? $"선택 {confirm.rect.width / k:0}×{confirm.rect.height / k:0} · 넘기기 {skip.rect.width / k:0}×{skip.rect.height / k:0}" : "없음");

        // ② 가운데 카드를 고른다 — 금 테(UIThinFrame Ready) 하나 · 1.03배 · 나머지 0.55
        int pick = Mathf.Min(1, cards.Count - 1);
        typeof(UI_RuneSelectPopup).GetMethod("SetSelected", Inst)?.Invoke(popup, new object[] { pick });
        await UniTask.Delay(600, ignoreTimeScale: true);   // 테가 그려지는 0.25초 뒤
        await Shot("U11_popup_sel");
        cards = Cards(popup);
        var sel = cards.Count > pick ? cards[pick] : null;
        bool frameOk = sel?.select != null && sel.select.gameObject.activeSelf && sel.select.State == ThinFrameState.Ready
                       && cards.Where(c => c != sel).All(c => c.select == null || !c.select.gameObject.activeSelf);
        Check("고른 표시 = 금 테 하나(다른 카드는 꺼짐)", frameOk, sel?.select != null ? $"{sel.select.State}" : "없음");
        Check("고른 카드 1.03배 · 나머지 알파 0.55", sel != null && Mathf.Abs(sel.rt.localScale.x - 1.03f) < 0.005f
              && cards.Where(c => c != sel).All(c => Mathf.Abs(c.group.alpha - 0.55f) < 0.01f),
              sel != null ? $"배율 {sel.rt.localScale.x:0.###}" : "");
        var mini = popup.GetComponentsInChildren<Image>(true).Where(i => i.transform.parent != null && i.transform.parent.name == "BoardPanel" && i.name.StartsWith("C") && i.sprite == null).ToList();
        _sb.AppendLine($"  미니 판 빈 칸 알파(고르기 전 0.18 · 고른 뒤 들어갈 칸 0.60 · 못 들어갈 칸 0.08): {string.Join(" ", mini.Select(i => i.color.a.ToString("0.00")).Distinct().OrderBy(s => s))}");
    }

    // ── ③④ 룬판 ──

    private static async UniTask ProbeGridAsync(UI_GridPanel panel, RunItemInventory inv, List<RuntimeItemData> added, List<RuntimeItemData> placed)
    {
        _sb.AppendLine("■ 룬판");
        var small = Pool().Where(d => Cells(d) >= 1 && Cells(d) <= 2).ToList();
        for (int k = 0; k < 3 && small.Count > 0; k++)
        {
            var d = Fresh(small[k % small.Count], Elems[k]);
            if (d != null && inv.AddToStaging(d)) added.Add(d);
        }
        panel.ShowWithNewItem(null);
        await UniTask.Delay(1200, ignoreTimeScale: true);
        foreach (var it in added)
        {
            int used = RuneBlockBudget.Used;
            var sq = FindFit(panel, it);
            if (sq == null) { _sb.AppendLine($"  자리 없음 {it.displayName}({it.element})"); continue; }
            Call(panel, "OnStagingItemSelected", it);
            Call(panel, "HandleSquareClicked", sq);
            await UniTask.Delay(120, ignoreTimeScale: true);
            if (RuneBlockBudget.Used > used) placed.Add(it);
        }
        await UniTask.Delay(900, ignoreTimeScale: true);   // 놓임 연출 0.48초
        _sb.AppendLine($"  놓은 룬 {placed.Count} · 판 룬 {RuneBlockBudget.Used} / {RuneBlockBudget.Max} · 보관함 {inv.StagingCount}");
        string shot = await Shot("U11_grid");

        var view    = typeof(UI_GridPanel).GetField("_hexGridView", Inst)?.GetValue(panel) as MerlinRuneHexGridView;
        var cells   = view != null ? typeof(MerlinRuneHexGridView).GetField("_cellImages", Inst)?.GetValue(view) as Dictionary<Vector2Int, Image> : null;
        var bases   = view != null ? typeof(MerlinRuneHexGridView).GetField("_cellBaseColors", Inst)?.GetValue(view) as Dictionary<Vector2Int, Color> : null;
        var occ     = view != null ? typeof(MerlinRuneHexGridView).GetField("_occupiedPositions", Inst)?.GetValue(view) as HashSet<Vector2Int> : null;
        var edges   = view != null ? typeof(MerlinRuneHexGridView).GetField("_cellEdgeImages", Inst)?.GetValue(view) as Dictionary<Vector2Int, Image[]> : null;
        string selId = view != null ? typeof(MerlinRuneHexGridView).GetField("_selectedPieceId", Inst)?.GetValue(view) as string : null;
        if (cells == null || bases == null || occ == null) { Check("판 읽기", false, "리플렉션 실패"); return; }

        // 빈 칸 — 색 ×0.30 · 알파 0.35 · 사진 밝기(예전 평균 97). 보관함에 원래 룬이 있으면 놓을 자리 힌트가 켜져 있다 — 힌트 칸(알파 0.6 · 1)은 뺀다.
        if (inv.StagingCount > 0) _sb.AppendLine($"  주의: 보관함에 룬 {inv.StagingCount}개 — 놓을 자리 힌트가 켜져 있어 힌트 칸은 빼고 잰다");
        var empties = cells.Where(kv => !occ.Contains(kv.Key) && kv.Value != null && kv.Value.color.a < 0.5f).ToList();
        int dimOk = empties.Count(kv => bases.TryGetValue(kv.Key, out var b) && Mathf.Abs(kv.Value.color.a - 0.35f) < 0.01f && Mathf.Abs(kv.Value.color.r - b.r * 0.30f) < 0.01f);
        Check("빈 칸 = 존 색 ×0.30 · 알파 0.35(놓을 룬 없음)", empties.Count > 0 && dimOk == empties.Count, $"{dimOk}/{empties.Count}");
        float luma = MeanLuma(shot, empties.Select(kv => ScreenRect(kv.Value.rectTransform)).ToList());
        Check("빈 칸 평균 밝기 50 이하(예전 97)", luma >= 0f && luma <= 50f, $"{luma:0.#}");
        var occRects = cells.Where(kv => occ.Contains(kv.Key) && kv.Value != null).Select(kv => ScreenRect(kv.Value.rectTransform)).ToList();
        if (occRects.Count > 0) _sb.AppendLine($"  놓인 칸 평균 밝기 {MeanLuma(shot, occRects):0.#}(빈 칸보다 밝아야 한다)");

        // 조각 외곽선 — 2px · 채도 반 · 정보판에 뜬 놓인 룬만 금
        var anyEdge = edges?.Values.FirstOrDefault(e => e != null && e[0] != null)?[0];
        Check("조각 외곽선 2px", anyEdge != null && Mathf.Approximately(anyEdge.rectTransform.sizeDelta.y, 2f), anyEdge != null ? $"{anyEdge.rectTransform.sizeDelta.y}" : "없음");
        var lastPlaced = placed.LastOrDefault();
        bool goldOk = lastPlaced != null && selId == lastPlaced.instanceId && edges != null && occ.Any(p =>
                          edges.TryGetValue(p, out var e) && e.Any(img => img != null && img.color == UITheme.Thin.LineReady));
        Check("정보판의 놓인 룬 = 금 외곽선", goldOk, $"고른 조각 {selId ?? "없음"} · 마지막에 놓은 {lastPlaced?.instanceId ?? "없음"}");

        // 글자 — 시너지 이름 18 · 단계 17 · 활성 효과 18 · 오른쪽 안내 17
        var root = panel.transform;
        var syn  = FindDeep(root, "SynergyStatusRoot");
        LogSizes(syn != null ? syn.GetComponentsInChildren<TMP_Text>(true).ToList() : new List<TMP_Text>(), "Name", "시너지 이름 18", 18f, null);
        LogSizes(syn != null ? syn.GetComponentsInChildren<TMP_Text>(true).ToList() : new List<TMP_Text>(), "Lbl", "시너지 단계 17", 17f, null);
        var eff = FindDeep(root, "EffectsRoot");
        var bodies = eff != null ? eff.GetComponentsInChildren<TMP_Text>(true).Where(t => t.name == "Body").ToList() : new List<TMP_Text>();
        if (bodies.Count > 0) LogSizes(bodies, "Body", "활성 효과 18", 18f, null);
        else _sb.AppendLine("  활성 효과 줄 없음(켜진 효과 0) — 18 확인 못 함");
        var hint = FindDeep(root, "PlaceHint")?.GetComponent<TMP_Text>();
        Check("오른쪽 안내 17", hint != null && Mathf.Approximately(hint.fontSize, 17f), hint != null ? $"{hint.fontSize}" : "없음");

        // ④ 배치 중 — 보관함에 불 룬 하나 · 고름 → 불 존 강조 + 맥박(상한 0.12)
        var extra = small.Count > 0 ? Fresh(small[0], "FIRE") : null;
        if (extra == null || !inv.AddToStaging(extra)) { _sb.AppendLine("  배치 중 촬영 건너뜀 — 보관함에 넣지 못함"); return; }
        added.Add(extra);
        Close(panel);
        panel.ShowWithNewItem(null);
        await UniTask.Delay(1200, ignoreTimeScale: true);
        Call(panel, "OnStagingItemSelected", extra);
        var pulse = typeof(MerlinRuneHexGridView).GetField("_pulseImages", Inst)?.GetValue(view) as List<Image>;
        float peak = 0f;
        for (int i = 0; i < 40; i++)   // 맥박 한 주기(≈2초) 동안 가장 밝은 값
        {
            await UniTask.Delay(50, ignoreTimeScale: true);
            if (pulse != null) foreach (var p in pulse) if (p != null) peak = Mathf.Max(peak, p.color.a);
        }
        await Shot("U11_grid_placing");
        Check("배치 유도 칸 있음 · 맥박 상한 0.12", pulse != null && pulse.Count > 0 && peak <= 0.121f, $"칸 {pulse?.Count ?? 0} · 정점 {peak:0.###}");
    }

    // ── 도우미 ──

    private sealed class CardInfo
    {
        public RectTransform rt;
        public CanvasGroup group;
        public UIThinFrame select;
        public ItemRarity rarity;
    }

    /// <summary>팝업의 카드 목록(private 중첩 클래스) — 리플렉션으로 필요한 칸만 꺼낸다.</summary>
    private static List<CardInfo> Cards(UI_RuneSelectPopup popup)
    {
        var list = new List<CardInfo>();
        if (typeof(UI_RuneSelectPopup).GetField("_cards", Inst)?.GetValue(popup) is not IList raw) return list;
        foreach (var c in raw)
        {
            var t = c.GetType();
            list.Add(new CardInfo
            {
                rt     = t.GetField("Rt")?.GetValue(c) as RectTransform,
                group  = t.GetField("Group")?.GetValue(c) as CanvasGroup,
                select = t.GetField("Select")?.GetValue(c) as UIThinFrame,
                rarity = t.GetField("Rarity")?.GetValue(c) is ItemRarity r ? r : ItemRarity.Common,
            });
        }
        return list;
    }

    private static void LogSizes(List<TMP_Text> texts, string name, string label, float want, string weight)
    {
        var hit = texts.Where(t => t != null && t.name == name).ToList();
        if (hit.Count == 0) { Check(label, false, "글자 없음"); return; }
        bool ok = hit.All(t => Mathf.Abs(t.fontSize - want) < 0.6f || (t.enableAutoSizing && t.fontSize <= want))
               && (weight == null || hit.All(t => t.font != null && t.font.name.Contains(weight)));
        Check(label, ok, $"{hit.Count}개 · 크기 {string.Join("/", hit.Select(t => t.fontSize.ToString("0.#")).Distinct())} · {hit[0].font?.name}");
    }

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

    /// <summary>사진 안 사각형들(안쪽 80%)의 평균 밝기 0~255(BT.709). 사진을 못 읽으면 -1.</summary>
    private static float MeanLuma(string png, List<Rect> rects)
    {
        var tex = LoadShot(png);
        if (tex == null || rects.Count == 0) return -1f;
        try
        {
            var px = tex.GetPixels32();
            float sx = tex.width / (float)Screen.width, sy = tex.height / (float)Screen.height;
            double sum = 0; long n = 0;
            foreach (var r in rects)
            {
                int x0 = Mathf.Clamp(Mathf.RoundToInt((r.xMin + r.width * 0.1f) * sx), 0, tex.width - 1);
                int x1 = Mathf.Clamp(Mathf.RoundToInt((r.xMax - r.width * 0.1f) * sx), 0, tex.width - 1);
                int y0 = Mathf.Clamp(Mathf.RoundToInt((r.yMin + r.height * 0.1f) * sy), 0, tex.height - 1);
                int y1 = Mathf.Clamp(Mathf.RoundToInt((r.yMax - r.height * 0.1f) * sy), 0, tex.height - 1);
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

    /// <summary>사각형 안에서 채도 0.45 이상 · 명도 0.35 이상인 픽셀 비율(리서치 B의 「강조색 면적」과 같은 기준).</summary>
    private static float SatShare(string png, Rect area)
    {
        var tex = LoadShot(png);
        if (tex == null) return -1f;
        try
        {
            var px = tex.GetPixels32();
            float sx = tex.width / (float)Screen.width, sy = tex.height / (float)Screen.height;
            int x0 = Mathf.Clamp(Mathf.RoundToInt(area.xMin * sx), 0, tex.width - 1), x1 = Mathf.Clamp(Mathf.RoundToInt(area.xMax * sx), 0, tex.width - 1);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(area.yMin * sy), 0, tex.height - 1), y1 = Mathf.Clamp(Mathf.RoundToInt(area.yMax * sy), 0, tex.height - 1);
            long hit = 0, n = 0;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Color.RGBToHSV(px[y * tex.width + x], out _, out float s, out float v);
                    if (s >= 0.45f && v >= 0.35f) hit++;
                    n++;
                }
            return n > 0 ? hit / (float)n : -1f;
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
