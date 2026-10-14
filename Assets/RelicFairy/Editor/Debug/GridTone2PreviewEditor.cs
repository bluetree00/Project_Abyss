#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-08 룬판 톤 v2 시안 촬영(게임 코드 무변경) — 플레이 중 열린 룬판에 톤 v2를 「그 자리에서」 입혀 전후를 찍는다.
/// 글자 = Noto 300/400/500 · 장식 테 = 선만 깎은 사본(UISpriteMaster.Thinned, 색 유지) · 베벨 판 = 평평한 글래스 ·
/// 버튼 = 얇은 테(완료만 금) · 겹침 2건(NEW 배지 · 안내 문구) 정리. 룬판을 닫으면 원래대로 다시 지어진다(저장 없음).
/// 결과: Temp/ui_shots/GridV2_0_before.png · GridV2_1_after.png · Temp/grid_v2_dump.txt · 로그 「[GridV2] 끝」.
/// </summary>
public static class GridTone2PreviewEditor
{
    private static readonly string[] ButtonNames = { "ResetBtn", "ConfirmBtn", "SalvageBtn", "KeepBtn", "YesBtn", "NoBtn", "CancelBtn" };

    [MenuItem("RelicFairy/Debug/10-08 룬판 톤 v2 시안 촬영 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) return;
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb  = new StringBuilder("룬판 톤 v2 시안\n");
        var inv = GameRunBootstrapper.Instance.Run.ItemInventory;
        var added = new List<RuntimeItemData>();
        Directory.CreateDirectory("Temp/ui_shots");
        try
        {
            string[] elems = { "FIRE", "ICE", "LIGHT" };
            foreach (var entry in Managers.ItemData.GetAllItems().Values.Take(40).Where((_, i) => i % 13 == 5).Take(3))
            {
                var d = RuntimeItemData.FromServer(entry);
                if (d == null) continue;
                d.element = elems[added.Count % elems.Length];
                if (inv.AddToStaging(d)) added.Add(d);
            }
            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            var panel = UI_GridPanel.Instance;
            panel?.ShowWithNewItem(added.Count > 0 ? added[^1] : null);
            await UniTask.Delay(1600, ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/GridV2_0_before.png");
            await UniTask.Delay(200, ignoreTimeScale: true);

            Dump(panel.transform, "Temp/grid_v2_dump.txt");
            Restyle(panel.transform, sb);
            Canvas.ForceUpdateCanvases();
            await UniTask.Delay(400, ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/GridV2_1_after.png");
            await UniTask.Delay(300, ignoreTimeScale: true);
            typeof(UI_GridPanel).GetMethod("ClosePanel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                                ?.Invoke(panel, null);
            await UniTask.Delay(600, ignoreTimeScale: true);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var d in added) inv.DiscardFromStaging(d);
            File.WriteAllText("Temp/grid_v2.txt", sb.ToString());
            Debug.Log("[GridV2] 끝\n" + sb);
        }
    }

    // ── 톤 v2를 그 자리에서 입힌다 ──
    private static void Restyle(Transform root, StringBuilder sb)
    {
        int thinned = 0, plates = 0, texts = 0, buttons = 0, clipped = 0;

        // 0) 판 외곽 — 2px + 6px 빛 → 1px 금선(빛 없음)
        foreach (var o in root.GetComponentsInChildren<UIRectilinearOutline>(true))
        {
            var f = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(UIRectilinearOutline).GetField("_lineWidth", f)?.SetValue(o, 1f);
            typeof(UIRectilinearOutline).GetField("_glowWidth", f)?.SetValue(o, 0f);
            typeof(UIRectilinearOutline).GetField("_glowAlpha", f)?.SetValue(o, 0f);
            o.color = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.55f);
            o.SetVerticesDirty();
        }

        // 1) 버튼 — 베벨 → 글래스 + 얇은 테(완료만 금 · 준비됨)
        foreach (var btn in root.GetComponentsInChildren<Button>(true))
        {
            if (System.Array.IndexOf(ButtonNames, btn.name) < 0) continue;
            var rt = (RectTransform)btn.transform;
            foreach (var img in btn.GetComponentsInChildren<Image>(true))
            {
                if (img.transform == btn.transform) { img.sprite = null; img.type = Image.Type.Simple; img.color = UITheme.Thin.GlassFill; }
                else img.enabled = false;
            }
            var frame = UIThinFrame.Create(rt, ThinShape.Square, rt.rect.size);
            bool primary = btn.name == "ConfirmBtn";
            frame.SetState(primary ? ThinFrameState.Ready : ThinFrameState.Active, animate: false);
            foreach (var t in btn.GetComponentsInChildren<TMP_Text>(true))
                UITheme.StyleThinText(t, UIWeight.Medium, primary ? 22f : 20f, primary ? UITheme.Thin.Gold : UITheme.Thin.Ink);
            buttons++;
        }

        // 2) 이미지 — 장식 테는 선만 깎고(색 유지) 베벨 판은 평평한 글래스로
        foreach (var img in root.GetComponentsInChildren<Image>(true))
        {
            if (img == null || img.sprite == null || !img.enabled) continue;
            if (img.GetComponentInParent<Button>(true) is { } b && System.Array.IndexOf(ButtonNames, b.name) >= 0) continue;
            if (img.GetComponent<UIThinFrame>() != null || img.transform.parent?.GetComponent<UIThinFrame>() != null) continue;
            string n = img.name, p = img.transform.parent != null ? img.transform.parent.name : "";
            if (n == "Border" && p.StartsWith("Row_"))
            {
                // 시너지 줄 테 — 걷는다(줄 강조를 줄인 2판이 사용자 확인 · 구획 테가 묶음을 맡는다)
                img.enabled = false;
            }
            else if (p == "GemFrame")
            {
                // 등급 보석 테 — 리소스는 쓰되 얇고 작게(사용자 10-08): 선을 깎고 조각을 0.62배로
                img.sprite = UISpriteMaster.Thinned(img.sprite, keepColor: true);
                img.rectTransform.sizeDelta *= 0.62f;
                var c = img.color; c.a *= 0.85f; img.color = c;
                thinned++;
            }
            else if (n == "Frame" && p.StartsWith("Slot_"))
            {
                // 빈 칸 테(룬 배치 테두리)는 원래 가는 선이라 깎으면 사라진다 — 깎지 않고 옅게만
                var c = img.color; c.a *= 0.5f; img.color = c;
            }
            else if (IsFrame(n, p))
            {
                img.sprite = UISpriteMaster.Thinned(img.sprite, keepColor: true);
                var c = img.color; c.a *= 0.85f; img.color = c;
                thinned++;
            }
            else if (n == "Header" || p == "RowContainer" || IsRowBg(img))
            {
                img.sprite = null; img.type = Image.Type.Simple; img.color = UITheme.Thin.GlassFill;
                plates++;
            }
        }

        // 3) 색 띠 — 능력치 칸 · 시너지 줄의 굵은 세로 띠를 2px로
        foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
        {
            if ((rt.name == "AccentBar" || rt.name == "Accent") && rt.rect.width > 2f && rt.rect.width < 12f)
                rt.sizeDelta = new Vector2(rt.sizeDelta.x - (rt.rect.width - 2f), rt.sizeDelta.y);
            if (rt.name == "TopLine" && rt.TryGetComponent<Image>(out var line)) { var c = line.color; c.a = 0.25f; line.color = c; }
        }

        // 4) 글자 — 큰 글자 300 · 긴 본문 400 · 라벨 · 숫자 500, 굵게 없음
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (t.GetComponentInParent<Button>(true) is { } b && System.Array.IndexOf(ButtonNames, b.name) >= 0) continue;
            var w = t.fontSize >= 26f ? UIWeight.Light
                  : (t.text != null && t.text.Length >= 12 && t.fontSize <= 19f) ? UIWeight.Regular
                  : UIWeight.Medium;
            UITheme.StyleThinText(t, w, t.fontSize, t.color, shadow: true);
            // Noto는 줄 높이가 DNF보다 커서(≈1.45em) 칸 높이에 맞춘 한 줄 글자가 잘림(Ellipsis · Truncate)으로 통째 사라진다
            if (t.overflowMode == TextOverflowModes.Ellipsis || t.overflowMode == TextOverflowModes.Truncate)
            {
                float need = t.fontSize * 1.5f, h = t.rectTransform.rect.height;
                if (h < need) { t.rectTransform.sizeDelta += new Vector2(0f, need - h); clipped++; }
            }
            texts++;
        }

        // 4b) 왼쪽 정보 구획 — 캐릭터 · 활성 효과 · 속성 시너지를 얇은 잉크 테 + 금 모서리 눈금으로 나눈다(머리띠 판은 걷는다)
        var left = FindDeep(root, "LeftPanel") as RectTransform;
        if (left != null)
        {
            // 속성 시너지를 맨 위로(판과 같이 보이게) — 캐릭터 정보 · 활성 효과는 그 아래로 내린다
            var syn  = FindDeep(root, "SynergyStatusRoot") as RectTransform;
            var info = FindDeep(root, "CharacterInfoPanel") as RectTransform;
            if (syn != null && info != null)
            {
                var sr = LocalBounds(left, syn); var cr = LocalBounds(left, info);
                const float gap = 10f;
                syn.anchoredPosition  += new Vector2(0f, cr.yMax - sr.yMax);
                info.anchoredPosition += new Vector2(0f, -(sr.height + gap));
            }
            // 「블록 n / 20」 — 판에 놓인 칸 수 / 최대(설계 중)
            int cells = 0;
            var counts = MerlinRuneBridge.Instance?.GetZoneOccupiedCounts();
            if (counts != null) foreach (var kv in counts) cells += kv.Value;
            if (FindDeep(root, "GuideHeader")?.Find("Desc")?.GetComponent<TMP_Text>() is { } desc)
            {
                desc.SetText($"블록 <color=#FFCC4D>{cells}</color> / 20");
                UITheme.StyleThinText(desc, UIWeight.Medium, 17f, UITheme.Thin.Ink, shadow: true);
                desc.alignment = TextAlignmentOptions.MidlineRight;
            }
            foreach (var band in new[] { "EffectsHdr", "GuideHeader" })
                if (FindDeep(root, band) is RectTransform b && b.TryGetComponent<Image>(out var bi)) bi.color = Color.clear;
            Section(left, "Sec_Char",   FindDeep(root, "CharName")?.parent as RectTransform, FindDeep(root, "StatsGrid") as RectTransform);
            Section(left, "Sec_Effect", FindDeep(root, "EffectsHdr") as RectTransform, FindDeep(root, "EffectsScroll") as RectTransform);
            Section(left, "Sec_Syn",    FindDeep(root, "SynergyStatusRoot") as RectTransform);
        }

        // 4c) 「놓을 자리」 배지 — 모든 화면에서 뺀다(사용자 10-08: 판 전체를 완벽히 배치하지 않는 한 맞히기 어렵다)
        foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
            if (rt.name.StartsWith("FitBadge")) rt.gameObject.SetActive(false);

        // 4d) 보관함 — 옛 남색 띠 · 돌판 바탕 → 글래스 + 위 1px 금선, 빈 칸 = 얇은 잉크 테
        if (FindDeep(root, "Footer") is RectTransform footer && footer.TryGetComponent<Image>(out var fImg))
        { fImg.sprite = null; fImg.color = UITheme.Thin.GlassFill; }
        if (FindDeep(root, "TopLine") is RectTransform top && top.TryGetComponent<Image>(out var tl))
        { tl.color = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.35f); top.sizeDelta = new Vector2(top.sizeDelta.x, 1f); }
        foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
        {
            if (!rt.name.StartsWith("Slot_") || rt.parent == null || rt.parent.name != "StagingBar") continue;
            if (rt.TryGetComponent<Image>(out var slotBg)) { slotBg.sprite = null; slotBg.type = Image.Type.Simple; slotBg.color = new Color(1f, 1f, 1f, 0.035f); }
            if (rt.Find("Frame") is RectTransform fr && fr.TryGetComponent<Image>(out var frImg))
            {
                frImg.sprite = UIThinShapes.Outline(ThinShape.Square, rt.rect.width, rt.rect.height);
                frImg.color  = UITheme.Thin.InkA(0.3f);
                ShopUIStyle.Stretch(fr);
            }
        }

        // 5) 겹침 2건 — NEW 배지가 카드 테에 가림 · 안내 문구가 테 아래 장식에 겹침
        foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
        {
            if (rt.name == "NewBadge") rt.SetAsLastSibling();
            if (rt.name == "PlaceHint" && rt.TryGetComponent<TMP_Text>(out var hint)) hint.fontSize = 15f;
        }

        sb.AppendLine($"  버튼 {buttons} · 깎은 테 {thinned} · 글래스 판 {plates} · 글자 {texts} · 줄 높이 늘림 {clipped}");
    }

    private static Rect LocalBounds(RectTransform space, RectTransform rt)
    {
        var c = new Vector3[4]; rt.GetWorldCorners(c);
        Vector2 a = space.InverseTransformPoint(c[0]), b = space.InverseTransformPoint(c[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    /// <summary>space 안에서 rts를 감싸는 얇은 구획 테(1px 잉크 28% + 8px 금 모서리 눈금 60%).</summary>
    private static void Section(RectTransform space, string name, params RectTransform[] rts)
    {
        var corners = new Vector3[4];
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        int n = 0;
        foreach (var rt in rts)
        {
            if (rt == null) continue;
            rt.GetWorldCorners(corners);
            foreach (var c in corners)
            {
                var l = space.InverseTransformPoint(c);
                x0 = Mathf.Min(x0, l.x); y0 = Mathf.Min(y0, l.y); x1 = Mathf.Max(x1, l.x); y1 = Mathf.Max(y1, l.y);
            }
            n++;
        }
        if (n == 0) return;
        const float padX = 4f, inY = 4f;   // 위아래는 4px 안으로 — 붙어 있는 구획 사이에 8px 틈
        var r = Rect.MinMaxRect(x0 - padX, y0 + inY, x1 + padX, y1 - inY);
        var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        var grt = (RectTransform)go.transform;
        grt.SetParent(space, false);
        grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(0.5f, 0.5f);
        grt.anchoredPosition = r.center - space.rect.center;
        grt.sizeDelta = r.size;
        var fill = ShopUIStyle.MakeImage(grt, "Fill", new Color(1f, 1f, 1f, 0.025f));
        ShopUIStyle.Stretch(fill.rectTransform);
        var line = ShopUIStyle.MakeImage(grt, "Line", UITheme.Thin.InkA(0.35f));
        line.sprite = UIThinShapes.Outline(ThinShape.Square, r.width, r.height);
        ShopUIStyle.Stretch(line.rectTransform);
        var ticks = ShopUIStyle.MakeImage(grt, "Ticks", UITheme.Thin.WithA(UITheme.Thin.Gold, 0.7f));
        ticks.sprite = UIThinShapes.Ticks(r.width + 8f, r.height + 8f);
        ShopUIStyle.Stretch(ticks.rectTransform);
        ticks.rectTransform.offsetMin = new Vector2(-4f, -4f); ticks.rectTransform.offsetMax = new Vector2(4f, 4f);
    }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++) { var hit = FindDeep(t.GetChild(i), name); if (hit != null) return hit; }
        return null;
    }

    private static bool IsFrame(string n, string p) =>
        n == "Border" || n == "Frame" || n == "SkinFrame" || n.StartsWith("Frame_") || n.StartsWith("Gem_") ||
        n.StartsWith("Edge") || n.StartsWith("Corner") || p == "GemFrame" || p == "BoardFrame";

    /// <summary>시너지 줄 바탕(9-slice 알약 아트) — 「Border」 형제를 가진 줄 뿌리.</summary>
    private static bool IsRowBg(Image img) => img.type == Image.Type.Sliced && img.transform.Find("Border") != null && img.transform.Find("Count") != null;

    private static void Dump(Transform root, string path)
    {
        var sb = new StringBuilder();
        foreach (var img in root.GetComponentsInChildren<Image>(true))
        {
            if (img.sprite == null) continue;
            var r = ((RectTransform)img.transform).rect;
            sb.AppendLine($"IMG {PathOf(img.transform, root)} · {img.sprite.name} · {r.width:0}×{r.height:0} · {img.type} · a{img.color.a:0.00} · on {img.enabled && img.gameObject.activeInHierarchy}");
        }
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            sb.AppendLine($"TXT {PathOf(t.transform, root)} · {t.fontSize:0} · {t.fontStyle} · {t.font?.name} · 「{(t.text ?? "").Replace("\n", " ").Substring(0, Mathf.Min(18, (t.text ?? "").Length))}」");
        File.WriteAllText(path, sb.ToString());
    }

    private static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (var c = t; c != null && c != root; c = c.parent) parts.Add(c.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
}
#endif
