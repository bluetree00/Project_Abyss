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
/// 10-11 원거리 장비 선택(모루) 나란히 2열 · 룬판 빈 칸 코드 타일 실측(테스트 허브 → 챕터 진입 뒤, 플레이 중).
/// 사진 3장: Temp/ui_shots/UX3_1_Forge(열자마자 — 첫 카드 고름) · UX3_2_ForgeSelected(둘째 카드를 누른 뒤) · UX3_3_RuneGrid(작은 룬 3개를 놓은 판).
/// 사진 없이 수치만: 3개 = 3열 · 4개 = 넘기기, 룬판 「놓을 수 있음」 힌트 칸은 타일 그림 그대로.
/// 결과 Temp/ux3_forge_rune_probe.txt(빈 칸 평균 밝기 포함) · 로그 「[UX3ForgeRune] 끝」. Assets/에는 쓰지 않는다(플레이 중 도메인 리로드).
/// </summary>
public static class ForgeRuneEmpty1011ProbeEditor
{
    // ── Constants ──
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string Tag     = "[UX3ForgeRune]";
    private const string ShotDir = "Temp/ui_shots";
    private const string OutTxt  = "Temp/ux3_forge_rune_probe.txt";
    private static readonly string[] Elems = { "FIRE", "ICE", "GRASS" };

    // ── Static ──
    private static StringBuilder _sb;
    private static int _pass, _fail;
    private static TestHubLauncher s_hiddenHub;

    [MenuItem("RelicFairy/Debug/10-11 모루 2열 · 룬판 빈 칸 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning($"{Tag} 런 안에서(테스트 허브 → 챕터 진입 뒤)"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        _sb = new StringBuilder("10-11 모루 2열 · 룬판 빈 칸 실측\n");
        _pass = _fail = 0;
        var inv    = GameRunBootstrapper.Instance.Run.ItemInventory;
        var added  = new List<RuntimeItemData>();
        var placed = new List<RuntimeItemData>();
        UI_GridPanel panel = null;
        Directory.CreateDirectory(ShotDir);
        HideTestHubGui();
        try
        {
            await ForgeAsync();

            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            panel = UI_GridPanel.Instance;
            await RuneGridAsync(panel, inv, added, placed);
        }
        catch (System.Exception e) { _sb.AppendLine("예외: " + e); _fail++; }
        finally
        {
            try
            {
                Managers.UI.CloseAllPopupUI();
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
            RestoreTestHubGui();
            _sb.AppendLine($"합계 통과 {_pass} · 실패 {_fail} · 정리 뒤 판 룬 {RuneBlockBudget.Used}");
            File.WriteAllText(OutTxt, _sb.ToString());
            Debug.Log($"{Tag} 끝\n" + _sb);
        }
    }

    // ── 모루 ──

    private static async UniTask ForgeAsync()
    {
        _sb.AppendLine("■ 원거리 장비 선택(모루)");
        var (bow, crossbow) = RangedWeapons();
        if (bow == null || crossbow == null) { Check("원거리 무기 2종 찾기", false, "활 · 석궁 에셋을 못 찾음"); return; }
        var unlocked = new UI_RangedForgePopup.Entry { Weapon = bow };
        var locked   = new UI_RangedForgePopup.Entry { Weapon = crossbow, Locked = true, LockReason = "기억의 제단 — 1,200 ◆" };

        // ① 2개(실제 모루와 같은 구성) — 열자마자
        var popup = await OpenForgeAsync(unlocked, locked);
        if (popup == null) return;
        await UniTask.Delay(500, ignoreTimeScale: true);   // 여는 연출 0.15초 뒤
        await Shot("UX3_1_Forge");

        var cards = Cards(popup);
        var slider = Field<RectTransform>(popup, "_slider");
        var dots   = Field<RectTransform>(popup, "_dots");
        Check("나란히 판 — 카드 2장 · 넘기기 카드 · 점 숨김", Field<bool>(popup, "_multi") && cards.Count(c => c.Root.gameObject.activeSelf) == 2
              && slider != null && !slider.gameObject.activeSelf && dots != null && !dots.gameObject.activeSelf,
              $"카드 {cards.Count(c => c.Root.gameObject.activeSelf)}");
        LogCardGeometry(cards, 2);
        float scale = PanelScale(popup);
        CheckSelection(cards, 0, scale, "처음 고른 카드 = 첫 후보");
        var confirm = Field<Button>(popup, "_confirm");
        Check("[장착하기] 켜짐(첫 후보 해금)", confirm != null && confirm.interactable, "");
        CheckArrows(popup, prevOn: false, nextOn: true);
        CheckCardText(cards, 2);

        // 화살표 · ← → 와 같은 길(Step) — 고르는 카드만 옮기고 넘기지 않는다
        var step = typeof(UI_RangedForgePopup).GetMethod("Step", Inst);
        step?.Invoke(popup, new object[] { +1 });
        int afterNext = Field<int>(popup, "_index");
        step?.Invoke(popup, new object[] { -1 });
        int afterPrev = Field<int>(popup, "_index");
        Check("→ 다음 카드 고름 · ← 되돌림(넘기기 없음)", afterNext == 1 && afterPrev == 0 && !slider.gameObject.activeSelf, $"→ {afterNext} · ← {afterPrev}");
        await UniTask.Delay(400, ignoreTimeScale: true);

        // ② 둘째 카드를 누른다(잠긴 후보) — 고른 표시가 옮겨 가고 [장착하기]가 잠긴다
        cards[1].Root.GetComponent<Button>()?.onClick.Invoke();
        await UniTask.Delay(450, ignoreTimeScale: true);   // 0.18초 감속 뒤
        await Shot("UX3_2_ForgeSelected");
        CheckSelection(cards, 1, scale, "둘째 카드를 누름");
        Check("잠긴 후보를 고르면 [장착하기] 잠김(예전과 같음)", confirm != null && !confirm.interactable, "");
        CheckArrows(popup, prevOn: true, nextOn: false);
        Managers.UI.CloseAllPopupUI();
        await UniTask.Delay(400, ignoreTimeScale: true);

        // ③ 3개 = 3열(사진 없음)
        popup = await OpenForgeAsync(unlocked, locked, unlocked);
        if (popup == null) return;
        await UniTask.Delay(300, ignoreTimeScale: true);
        cards = Cards(popup);
        Check("3개 = 나란히 3열", Field<bool>(popup, "_multi") && cards.Count(c => c.Root.gameObject.activeSelf) == 3, $"카드 {cards.Count(c => c.Root.gameObject.activeSelf)}");
        LogCardGeometry(cards, 3);
        CheckCardText(cards, 3);
        Managers.UI.CloseAllPopupUI();
        await UniTask.Delay(400, ignoreTimeScale: true);

        // ④ 4개 = 넘기기(사진 없음)
        popup = await OpenForgeAsync(unlocked, locked, unlocked, locked);
        if (popup == null) return;
        await UniTask.Delay(300, ignoreTimeScale: true);
        slider = Field<RectTransform>(popup, "_slider");
        dots   = Field<RectTransform>(popup, "_dots");
        var count = popup.transform.Find("Panel/Dots/Count")?.GetComponent<TMP_Text>();
        Check("4개 = 넘기기(한 장 · 점 · 「1 / 4」)", !Field<bool>(popup, "_multi") && slider != null && slider.gameObject.activeSelf
              && dots != null && dots.gameObject.activeSelf && count != null && count.text == "1 / 4", count != null ? count.text : "없음");
        Managers.UI.CloseAllPopupUI();
        await UniTask.Delay(400, ignoreTimeScale: true);
    }

    private static async UniTask<UI_RangedForgePopup> OpenForgeAsync(params UI_RangedForgePopup.Entry[] entries)
    {
        var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RangedForgePopup>();
        if (popup == null) { Check("모루 팝업 열기", false, "로드 실패"); return null; }
        popup.Setup(entries);
        return popup;
    }

    /// <summary>고른 카드 = 금 85% · 2px(화면) · 1.03배 · 알파 1 / 나머지 = 잉크 18% · 1px · 알파 0.55.</summary>
    private static void CheckSelection(List<CardInfo> cards, int sel, float scale, string label)
    {
        var on = cards.Count > sel ? cards[sel] : null;
        if (on == null) { Check(label, false, "카드 없음"); return; }
        float onPx = on.Edge[0].rectTransform.sizeDelta.y * scale;
        bool okOn = Mathf.Abs(on.Root.localScale.x - 1.03f) < 0.005f && Mathf.Abs(on.Group.alpha - 1f) < 0.01f
                    && on.Edge.All(e => e.color == UITheme.Thin.LineReady) && Mathf.Abs(onPx - 2f) < 0.05f;
        var others = cards.Where((c, i) => i != sel && c.Root.gameObject.activeSelf).ToList();
        bool okOff = others.All(c => Mathf.Abs(c.Group.alpha - 0.55f) < 0.01f && Mathf.Abs(c.Root.localScale.x - 1f) < 0.005f
                                     && c.Edge.All(e => e.color == UITheme.Thin.LineIdle) && Mathf.Abs(c.Edge[0].rectTransform.sizeDelta.y * scale - 1f) < 0.05f);
        Check($"{label} — 금 85% 2px 테 · 1.03배 · 나머지 0.55", okOn && okOff,
              $"고른 카드 배율 {on.Root.localScale.x:0.###} · 테 {onPx:0.##}px · 나머지 알파 {string.Join("/", others.Select(c => c.Group.alpha.ToString("0.##")))} · 창 배율 {scale:0.###}");
    }

    private static void CheckArrows(UI_RangedForgePopup popup, bool prevOn, bool nextOn)
    {
        var prev = Field<Button>(popup, "_prev");
        var next = Field<Button>(popup, "_next");
        Check($"화살표 ◀ {(prevOn ? "켜짐" : "꺼짐")} · ▶ {(nextOn ? "켜짐" : "꺼짐")}", prev != null && next != null && prev.interactable == prevOn && next.interactable == nextOn,
              prev != null && next != null ? $"◀ {prev.interactable} · ▶ {next.interactable}" : "없음");
    }

    /// <summary>카드 폭이 같고 사이가 16인지, 내용(수치표 아래 끝)이 카드 안에 드는지 — 무대 좌표(배율 무시).</summary>
    private static void LogCardGeometry(List<CardInfo> cards, int n)
    {
        var on = cards.Where(c => c.Root.gameObject.activeSelf).Take(n).ToList();
        if (on.Count < 2) return;
        float w0 = on[0].Root.rect.width;
        bool same = on.All(c => Mathf.Abs(c.Root.rect.width - w0) < 0.5f);
        var gaps = new List<float>();
        for (int i = 1; i < on.Count; i++)
            gaps.Add((on[i].Root.localPosition.x + on[i].Root.rect.xMin) - (on[i - 1].Root.localPosition.x + on[i - 1].Root.rect.xMax));
        Check($"{n}열 카드 폭 같음 · 사이 16", same && gaps.All(g => Mathf.Abs(g - 16f) < 0.5f),
              $"폭 {w0:0.#} × 높이 {on[0].Root.rect.height:0.#} · 사이 {string.Join("/", gaps.Select(g => g.ToString("0.#")))}");

        bool inside = true;
        var note = new StringBuilder();
        float minIcon = n >= 3 ? 84f : 96f;   // 3열은 그림 84 · 태그라인 15 네 줄(10-11 2차 — 159 폭에서 17 세 줄도 잘렸다)
        foreach (var c in on)
        {
            var box = c.Root.Find("IconBox") as RectTransform;
            float iconH = box != null ? box.rect.height : -1f;
            float statsBottom = c.Stats.rectTransform.localPosition.y + c.Stats.rectTransform.rect.yMin;   // 카드 좌표
            float nameTop     = c.Name.rectTransform.localPosition.y + c.Name.rectTransform.rect.yMax;
            bool fit = iconH >= minIcon - 0.5f && statsBottom >= c.Root.rect.yMin - 0.5f && nameTop <= c.Root.rect.yMax + 0.5f;
            inside &= fit;
            note.Append($"[그림 {iconH:0} · 아래 여유 {statsBottom - c.Root.rect.yMin:0.#}] ");
        }
        Check($"{n}열 카드 내용이 카드 안(그림 칸 ≥ {minIcon:0})", inside, note.ToString());
    }

    /// <summary>태그라인 두 줄(3열은 네 줄) 안 · 말줄임 없음, 글자는 Noto(DNF 0) · 가짜 굵게 · 기울임 0.</summary>
    private static void CheckCardText(List<CardInfo> cards, int n)
    {
        var on = cards.Where(c => c.Root.gameObject.activeSelf).Take(n).ToList();
        int maxLines = n >= 3 ? 4 : 2;
        var lines = on.Select(c => { c.Tag.ForceMeshUpdate(); return (lines: c.Tag.textInfo.lineCount, cut: c.Tag.preferredHeight > c.Tag.rectTransform.rect.height + 1f); }).ToList();
        Check($"{n}열 태그라인 {maxLines}줄 안 · 말줄임 없음", lines.All(l => l.lines <= maxLines && !l.cut),
              string.Join(" · ", lines.Select(l => $"{l.lines}줄{(l.cut ? "(잘림)" : "")}")) + $" · 폭 {on[0].Tag.rectTransform.rect.width:0}");
        var texts = on.SelectMany(c => c.Root.GetComponentsInChildren<TMP_Text>(false)).ToList();
        var dnf   = texts.Where(t => t.font != null && t.font.name.Contains("DNF")).Select(t => t.name).Distinct().ToList();
        var faux  = texts.Where(t => (t.fontStyle & (FontStyles.Bold | FontStyles.Italic)) != 0).Select(t => t.name).Distinct().ToList();
        Check($"{n}열 카드 글자 DNF 0 · 가짜 굵게 · 기울임 0", dnf.Count == 0 && faux.Count == 0, string.Join(",", dnf.Concat(faux)));
        var name = on[0].Name;
        _sb.AppendLine($"  이름 {name.font?.name} {name.fontSize:0.#} · 태그 {on[0].Tag.fontSize:0.#} · 수치 {on[0].Stats.fontSize:0.#}(설계 크기 — 창 배율 ×{PanelScaleOf(on[0].Root):0.##}이 곱해진다)");
    }

    // ── 룬판 ──

    private static async UniTask RuneGridAsync(UI_GridPanel panel, RunItemInventory inv, List<RuntimeItemData> added, List<RuntimeItemData> placed)
    {
        _sb.AppendLine("■ 룬판 빈 칸");
        if (panel == null) { Check("룬판 열기", false, "UI_GridPanel 없음"); return; }
        var small = Pool().Where(d => Cells(d) >= 1 && Cells(d) <= 2).ToList();
        for (int k = 0; k < Elems.Length && small.Count > 0; k++)
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
        if (inv.StagingCount > 0) _sb.AppendLine($"  주의: 보관함에 룬 {inv.StagingCount}개 — 놓을 자리 힌트가 켜져 있어 힌트 칸(타일 그림)은 빼고 잰다");
        string shot = await Shot("UX3_3_RuneGrid");

        var view  = typeof(UI_GridPanel).GetField("_hexGridView", Inst)?.GetValue(panel) as MerlinRuneHexGridView;
        var cells = Field<Dictionary<Vector2Int, Image>>(view, "_cellImages");
        var faces = Field<Dictionary<Vector2Int, Image>>(view, "_cellEmptyFaces");
        var inner = Field<Dictionary<Vector2Int, Image>>(view, "_cellInnerImages");
        var zones = Field<Dictionary<Vector2Int, char>>(view, "_cellZones");
        var occ   = Field<HashSet<Vector2Int>>(view, "_occupiedPositions");
        if (cells == null || faces == null || inner == null || zones == null || occ == null) { Check("판 읽기", false, "리플렉션 실패(새 빌드가 아닌가)"); return; }

        // 빈 칸 — 타일 그림 끔(알파 0) · 흰 겹판 끔 · 코드 타일 켬(존 색 ×0.30 · 0.35 / 테 ×0.45 · 0.5) · 스프라이트 붙음
        var empties = cells.Where(kv => !occ.Contains(kv.Key) && faces.TryGetValue(kv.Key, out var f) && f != null && f.gameObject.activeSelf).ToList();
        int lookOk = 0;
        foreach (var kv in empties)
        {
            var face = faces[kv.Key];
            var line = face.transform.childCount > 0 ? face.transform.GetChild(0).GetComponent<Image>() : null;
            var z = ZoneColor(zones[kv.Key]);
            bool ok = kv.Value.color.a < 0.001f && inner.TryGetValue(kv.Key, out var ii) && ii != null && !ii.enabled
                      && face.sprite != null && line != null && line.sprite != null
                      && Near(face.color, new Color(z.r * 0.30f, z.g * 0.30f, z.b * 0.30f, 0.35f))
                      && Near(line.color, new Color(z.r * 0.45f, z.g * 0.45f, z.b * 0.45f, 0.50f));
            if (ok) lookOk++;
        }
        Check("빈 칸 = 코드 타일(문양 없음 · 존 색 ×0.30 · 0.35 · 1px 테 ×0.45 · 0.5) · 흰 겹판 끔", empties.Count > 0 && lookOk == empties.Count, $"{lookOk}/{empties.Count}");
        float luma = MeanLuma(shot, empties.Select(kv => ScreenRect(kv.Value.rectTransform)).ToList());
        Check("빈 칸 평균 밝기 50 이하(10-11 실측 83 · 예전 97)", luma >= 0f && luma <= 50f, $"{luma:0.#}");
        var occCells = cells.Where(kv => occ.Contains(kv.Key) && kv.Value != null).ToList();
        if (occCells.Count > 0)
        {
            float occLuma = MeanLuma(shot, occCells.Select(kv => ScreenRect(kv.Value.rectTransform)).ToList());
            Check("놓인 칸이 빈 칸보다 밝다", occLuma > luma, $"놓인 칸 {occLuma:0.#} · 빈 칸 {luma:0.#}");
            bool occOk = occCells.All(kv => kv.Value.color.a > 0.99f && !faces[kv.Key].gameObject.activeSelf && inner[kv.Key].enabled);
            Check("놓인 칸 = 타일 그림 그대로(코드 타일 끔 · 흰 겹판 켬)", occOk, $"{occCells.Count}칸");
        }
        _sb.AppendLine($"  빈 칸 평균 밝기: {luma:0.#}");

        // 힌트(사진 없음) — 보관함에 불 룬 하나를 넣고 고르면 불 존 빈 칸은 타일 그림(강조), 나머지 빈 칸은 코드 타일 그대로
        var extra = small.Count > 0 ? Fresh(small[0], "FIRE") : null;
        if (extra == null || !inv.AddToStaging(extra)) { _sb.AppendLine("  힌트 확인 건너뜀 — 보관함에 넣지 못함"); return; }
        added.Add(extra);
        Close(panel);
        panel.ShowWithNewItem(null);
        await UniTask.Delay(1200, ignoreTimeScale: true);
        Call(panel, "OnStagingItemSelected", extra);
        await UniTask.Delay(300, ignoreTimeScale: true);
        char fire = ElementDef.IdToCode("FIRE");
        var fireEmpty  = cells.Where(kv => !occ.Contains(kv.Key) && zones[kv.Key] == fire).ToList();
        var otherEmpty = cells.Where(kv => !occ.Contains(kv.Key) && zones[kv.Key] != fire && zones[kv.Key] != ElementDef.CenterCode).ToList();
        int fireArt  = fireEmpty.Count(kv => kv.Value.color.a > 0.99f && !faces[kv.Key].gameObject.activeSelf);
        int otherDim = otherEmpty.Count(kv => kv.Value.color.a < 0.001f && faces[kv.Key].gameObject.activeSelf);
        Check("힌트 칸(불 존) = 타일 그림 그대로 · 다른 존 빈 칸 = 코드 타일", fireEmpty.Count > 0 && fireArt > 0 && otherDim == otherEmpty.Count,
              $"불 존 빈 칸 {fireEmpty.Count}개 중 그림 {fireArt}(놓을 자리 밖은 코드 타일) · 다른 존 {otherDim}/{otherEmpty.Count}");
    }

    // ── 도우미 ──

    private sealed class CardInfo
    {
        public RectTransform Root;
        public CanvasGroup   Group;
        public Image[]       Edge;
        public TMP_Text      Name, Tag, Stats;
    }

    /// <summary>팝업의 나란히 카드 목록(private 중첩 클래스) — 리플렉션으로 필요한 칸만 꺼낸다.</summary>
    private static List<CardInfo> Cards(UI_RangedForgePopup popup)
    {
        var list = new List<CardInfo>();
        if (typeof(UI_RangedForgePopup).GetField("_cards", Inst)?.GetValue(popup) is not IList raw) return list;
        foreach (var c in raw)
        {
            var t = c.GetType();
            list.Add(new CardInfo
            {
                Root  = t.GetField("Root")?.GetValue(c) as RectTransform,
                Group = t.GetField("Group")?.GetValue(c) as CanvasGroup,
                Edge  = t.GetField("Edge")?.GetValue(c) as Image[],
                Name  = t.GetField("Name")?.GetValue(c) as TMP_Text,
                Tag   = t.GetField("Tag")?.GetValue(c) as TMP_Text,
                Stats = t.GetField("Stats")?.GetValue(c) as TMP_Text,
            });
        }
        return list;
    }

    private static T Field<T>(object o, string name)
        => o != null && o.GetType().GetField(name, Inst)?.GetValue(o) is T v ? v : default;

    private static float PanelScale(UI_RangedForgePopup popup)
    {
        var p = popup.transform.Find("Panel");
        return p != null ? p.localScale.x : 1f;
    }

    private static float PanelScaleOf(Transform t)
    {
        for (var p = t; p != null; p = p.parent) if (p.name == "Panel") return p.localScale.x;
        return 1f;
    }

    private static (WeaponSO bow, WeaponSO crossbow) RangedWeapons()
    {
        var altar = Object.FindFirstObjectByType<WeaponForgeAltar>(FindObjectsInactive.Include);
        WeaponSO bow = null, crossbow = null;
        if (altar != null)
        {
            bow      = typeof(WeaponForgeAltar).GetField("bowOption", Inst)?.GetValue(altar) as WeaponSO;
            crossbow = typeof(WeaponForgeAltar).GetField("crossbowOption", Inst)?.GetValue(altar) as WeaponSO;
        }
        if (bow == null)      bow      = LoadWeapon("T1_Bow");
        if (crossbow == null) crossbow = LoadWeapon("T1_Crossbow");
        return (bow, crossbow);
    }

    private static WeaponSO LoadWeapon(string assetName)
    {
        foreach (var guid in AssetDatabase.FindAssets(assetName + " t:MainWeaponSO"))
        {
            var w = AssetDatabase.LoadAssetAtPath<MainWeaponSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (w != null && w.name == assetName) return w;
        }
        return null;
    }

    private static Color ZoneColor(char code) => ElementDef.TryGetCodeColor(code, out var c) ? c : new Color(0.15f, 0.15f, 0.20f, 0.30f);

    private static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f && Mathf.Abs(a.a - b.a) < 0.01f;

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
        if (string.IsNullOrEmpty(png) || !File.Exists(png) || rects.Count == 0) return -1f;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!tex.LoadImage(File.ReadAllBytes(png))) return -1f;
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

    private static void HideTestHubGui()
    {
        var hub = Object.FindFirstObjectByType<TestHubLauncher>();
        if (hub == null || !hub.enabled) return;
        hub.enabled = false;
        s_hiddenHub = hub;
    }

    private static void RestoreTestHubGui()
    {
        if (s_hiddenHub != null) s_hiddenHub.enabled = true;
        s_hiddenHub = null;
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
