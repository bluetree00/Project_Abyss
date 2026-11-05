using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 스탯 상세 창(11-05 사용자 「스탯 상세보기 팝업을 열 수 있게 잘 보이게 · 지금 적용된 모든 룬 기능 · 수치를 최대한 보기 좋게」) —
/// 룬판 왼쪽 위 [스탯 상세] 단추로 연다. 왼쪽 = 능력치(최종 값 · 룬 몫) · 오른쪽 = 속성 시너지 · 발동 계열 각인 · 놓인 룬 · 지금 켜진 조건부 효과(둘 다 스크롤).
/// 데이터는 <see cref="StatDetailReport"/> — 열 때 한 번 짓는다(룬판이 시간을 멈춘 동안이라 값이 안 바뀐다).
/// 코드로 짓는 덮개 — 멀린 룬 보관 · 환전소와 같은 틀. Esc · Tab · [닫기]로 닫는다(Tab은 이 창만 닫고 룬판은 남는다 · 룬판이 닫히면 같이 닫힌다).
/// </summary>
public static class StatDetailView
{
    // ── Constants ─────────────────────────────────────────────
    private const float PanelW = 1640f, PanelH = 900f, ColTop = 336f, ColBottom = -398f;
    private const float LeftL = -796f, LeftR = -246f, RightL = -216f, RightR = 796f;
    private const float ColHeadH = 46f, Inset = 14f, BarW = 4f;
    private const float RowH = 34f, GroupHeadH = 38f, GroupGap = 10f;
    private const float LabelW = 196f, ValueW = 150f, NoteGap = 18f;
    private const float SectionHeadH = 44f, SectionGap = 18f, ItemPadY = 9f, ItemGap = 6f, IconSize = 40f, IconGap = 14f;
    private const float FadeIn = 0.18f, FadeOut = 0.14f;
    // 판은 불투명 — 선형 색 공간에선 4%만 비쳐도 뒤 룬판의 밝은 글이 회색 유령으로 떴다(11-05 실측 사진)
    private static readonly Color PanelC = new(0.05f, 0.05f, 0.07f, 1f);
    private static readonly Color VeilC  = new(0f, 0f, 0f, 0.82f);
    private static readonly Color ColC   = new(1f, 1f, 1f, 0.025f);
    private static readonly Color PlateC = new(1f, 1f, 1f, 0.03f);
    private static readonly Color RuleC  = new(1f, 1f, 1f, 0.10f);
    private static readonly Color HeadC  = new(0.96f, 0.90f, 0.72f, 1f);
    private static readonly Color NoteC  = new(0.60f, 0.82f, 0.72f, 1f);   // 룬 몫 — 옅은 녹청(값과 섞이지 않게)
    private static readonly Color DimC   = new(0.55f, 0.55f, 0.60f, 1f);

    /// <summary>창이 열려 있나 — ESC 처리기 · 룬판 Tab이 비킨다.</summary>
    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => IsOpen = false;

    // ── Public Methods ────────────────────────────────────────
    public static async UniTask OpenAsync(GameRunSession run, CancellationToken outer)
    {
        if (IsOpen || run == null) return;
        var report = StatDetailReport.Build(run);
        var (go, root, group) = UICodeOverlay.Create("@StatDetail", UISortingOrder.RunChoice);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer, go.GetCancellationTokenOnDestroy());
        var ct = cts.Token;
        IsOpen = true;
        bool close = false;
        try
        {
            UICodeOverlay.Img(root, "Veil", Vector2.zero, Vector2.zero, VeilC, stretch: true);
            var panel = (RectTransform)UICodeOverlay.Img(root, "Panel", Vector2.zero, new Vector2(PanelW, PanelH), PanelC).transform;
            UICodeOverlay.Line(panel, new Color(UIPalette.Gold.r, UIPalette.Gold.g, UIPalette.Gold.b, 0.45f));
            UICodeOverlay.Txt(panel, "스탯 상세", 32f, HeadC, new Vector2(0f, 404f), 900f);
            UICodeOverlay.Txt(panel, "지금 적용 중인 값 — 룬 · 유물 · 버프 · 각인을 모두 합친 결과", 17f, UICodeOverlay.Mist, new Vector2(0f, 366f), 1200f);
            UICodeOverlay.Btn(panel, "닫기", new Vector2(PanelW * 0.5f - 84f, 404f), new Vector2(120f, 42f), 19f, () => close = true);

            BuildStats(Column(panel, "능력치", LeftL, LeftR), report);
            BuildSections(Column(panel, "룬 효과", RightL, RightR), report);

            UICodeOverlay.Txt(panel, "한 타에 붙는 보너스(룬 · 증강 · 전설)는 서로 곱해진다   ·   Esc · Tab 닫기", 15f, DimC,
                              new Vector2(0f, -PanelH * 0.5f + 26f), 1500f);

            Managers.Sound?.PlayUiEvent(SoundEvent.UiOpen);
            for (float t = 0f; t < FadeIn; t += Time.unscaledDeltaTime) { group.alpha = t / FadeIn; await UniTask.Yield(PlayerLoopTiming.Update, ct); }
            group.alpha = 1f;

            while (!close && !Input.GetKeyDown(KeyCode.Escape) && !Input.GetKeyDown(KeyCode.Tab) && UI_GridPanel.Instance != null && UI_GridPanel.Instance.IsOpen)
                await UniTask.Yield(PlayerLoopTiming.Update, ct);

            Managers.Sound?.PlayUiEvent(SoundEvent.UiClose);
            for (float t = 0f; t < FadeOut; t += Time.unscaledDeltaTime) { group.alpha = 1f - t / FadeOut; await UniTask.Yield(PlayerLoopTiming.Update, ct); }
        }
        catch (System.OperationCanceledException) { }
        finally
        {
            IsOpen = false;
            if (go != null) Object.Destroy(go);
        }
    }

    // ── Private Methods: 틀 ───────────────────────────────────
    /// <summary>한 열 — 옅은 판 · 머리(제목 + 선) · 아래 스크롤(얇은 막대). 돌려주는 것 = 스크롤 내용(위 왼쪽 기준).</summary>
    private static RectTransform Column(RectTransform panel, string title, float left, float right)
    {
        float w = right - left, h = ColTop - ColBottom;
        var col = (RectTransform)UICodeOverlay.Img(panel, "Col_" + title, new Vector2((left + right) * 0.5f, (ColTop + ColBottom) * 0.5f), new Vector2(w, h), ColC).transform;
        var head = Text(col, Loc.T(title), 20f, HeadC, Inset, 10f, w - Inset * 2f);
        head.rectTransform.sizeDelta = new Vector2(w - Inset * 2f, ColHeadH - 14f);
        head.alignment = TextAlignmentOptions.MidlineLeft;
        Rule(col, Inset, ColHeadH - 2f, w - Inset * 2f);

        var view = (RectTransform)new GameObject("View", typeof(RectTransform), typeof(RectMask2D)).transform;
        view.SetParent(col, false);
        view.anchorMin = Vector2.zero; view.anchorMax = Vector2.one;
        view.offsetMin = new Vector2(0f, 8f); view.offsetMax = new Vector2(-(BarW + 6f), -ColHeadH);
        var hit = view.gameObject.AddComponent<Image>();   // 빈 곳에서도 휠이 먹게(투명)
        hit.color = new Color(0f, 0f, 0f, 0f);

        var content = (RectTransform)new GameObject("Content", typeof(RectTransform)).transform;
        content.SetParent(view, false);
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;

        var scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.content = content; scroll.viewport = view; scroll.horizontal = false;
        scroll.scrollSensitivity = 30f; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.inertia = false;
        scroll.verticalScrollbar = Bar(col);
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return content;
    }

    /// <summary>얇은 세로 막대(4 px) — 넘칠 때만 보인다(AutoHide).</summary>
    private static Scrollbar Bar(RectTransform col)
    {
        var track = UICodeOverlay.Img(col, "Bar", Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.05f));
        var trt = track.rectTransform;
        trt.anchorMin = new Vector2(1f, 0f); trt.anchorMax = Vector2.one; trt.pivot = new Vector2(1f, 0.5f);
        trt.offsetMin = new Vector2(-(BarW + 4f), 10f); trt.offsetMax = new Vector2(-4f, -(ColHeadH + 2f));
        var handle = UICodeOverlay.Img(trt, "Handle", Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.32f), stretch: true);
        var bar = track.gameObject.AddComponent<Scrollbar>();
        bar.handleRect = handle.rectTransform;
        bar.targetGraphic = handle;
        bar.direction = Scrollbar.Direction.BottomToTop;
        return bar;
    }

    // ── Private Methods: 왼쪽 능력치 ──────────────────────────
    private static void BuildStats(RectTransform content, StatDetailReport report)
    {
        float w = RightOf(content), y = 6f;
        float valueX = Inset + LabelW, noteX = valueX + ValueW + NoteGap;
        for (int g = 0; g < report.Stats.Count; g++)
        {
            var grp = report.Stats[g];
            if (g > 0) y += GroupGap;
            var gh = Text(content, grp.Title, 16f, new Color(HeadC.r, HeadC.g, HeadC.b, 0.75f), Inset, y + 8f, w - Inset * 2f);
            gh.characterSpacing = 4f;
            Rule(content, Inset, y + GroupHeadH - 4f, w - Inset * 2f);
            y += GroupHeadH;
            foreach (var row in grp.Rows)
            {
                Cell(content, row.Label, 18f, UICodeOverlay.Mist, Inset, y, LabelW, TextAlignmentOptions.MidlineLeft);
                Cell(content, row.Value, 20f, UICodeOverlay.Ink, valueX, y, ValueW, TextAlignmentOptions.MidlineRight);
                if (!string.IsNullOrEmpty(row.Note))
                    Cell(content, row.Note, 15f, NoteC, noteX, y, w - noteX - Inset, TextAlignmentOptions.MidlineLeft);
                y += RowH;
            }
        }
        content.sizeDelta = new Vector2(0f, y + 12f);
    }

    // ── Private Methods: 오른쪽 룬 효과 ───────────────────────
    private static void BuildSections(RectTransform content, StatDetailReport report)
    {
        float w = RightOf(content), y = 6f;
        for (int s = 0; s < report.Sections.Count; s++)
        {
            var sec = report.Sections[s];
            if (s > 0) y += SectionGap;
            string count = string.IsNullOrEmpty(sec.Count) ? "" : $"   <size=80%><color=#9A98A0>{sec.Count}</color></size>";
            Text(content, sec.Title + count, 19f, HeadC, Inset, y + 8f, w - Inset * 2f);
            Rule(content, Inset, y + SectionHeadH - 4f, w - Inset * 2f);
            y += SectionHeadH;

            if (sec.Items.Count == 0)
            {
                var empty = Text(content, sec.Empty, 16f, DimC, Inset + 6f, y + 4f, w - Inset * 2f - 12f);
                y += empty.rectTransform.sizeDelta.y + 10f;
                continue;
            }
            foreach (var item in sec.Items)
                y += ItemBlock(content, item, y, w) + ItemGap;
        }
        content.sizeDelta = new Vector2(0f, y + 12f);
    }

    /// <summary>한 칸 — 옅은 받침 · 그림(있으면) · 머리 · 본문(숫자 강조). 돌려주는 것 = 높이.</summary>
    private static float ItemBlock(RectTransform content, StatDetailReport.Item item, float y, float w)
    {
        var plate = UICodeOverlay.Img(content, "Item", Vector2.zero, Vector2.zero, PlateC);
        plate.raycastTarget = false;
        bool icon = item.Icon != null;
        float tx = Inset + 10f + (icon ? IconSize + IconGap : 0f), tw = w - tx - Inset - 10f;

        var head = Text(content, item.Head, 18f, UICodeOverlay.Ink, tx, y + ItemPadY, tw);
        float h = head.rectTransform.sizeDelta.y;
        if (!string.IsNullOrEmpty(item.Body))
        {
            var body = Text(content, UIKeywordInk.Values(item.Body), 16f, new Color(0.86f, 0.84f, 0.80f, 1f), tx, y + ItemPadY + h + 3f, tw);
            body.lineSpacing = 6f;
            body.rectTransform.sizeDelta = new Vector2(tw, Mathf.Ceil(body.GetPreferredValues(body.text, tw, 0f).y));
            h += 3f + body.rectTransform.sizeDelta.y;
        }
        h = Mathf.Max(h, icon ? IconSize : 0f) + ItemPadY * 2f;

        if (icon)
        {
            var img = UICodeOverlay.Img(content, "Icon", Vector2.zero, Vector2.one * IconSize, Color.white);
            img.sprite = item.Icon;
            img.preserveAspect = true;
            img.raycastTarget = false;
            TopLeft(img.rectTransform, Inset + 10f, y + ItemPadY);
        }
        var prt = plate.rectTransform;
        TopLeft(prt, Inset, y);
        prt.sizeDelta = new Vector2(w - Inset * 2f, h);
        prt.SetAsFirstSibling();   // 받침은 글 · 그림 밑
        return h;
    }

    // ── Private Methods: 조각 ─────────────────────────────────
    /// <summary>위 왼쪽 기준 글 — 폭을 정하고 높이는 글에 맞춘다(줄바꿈 포함).</summary>
    private static TMP_Text Text(RectTransform parent, string text, float size, Color color, float x, float y, float w)
    {
        var t = UICodeOverlay.Txt(parent, text, size, color, Vector2.zero, w, align: TextAlignmentOptions.TopLeft);
        TopLeft(t.rectTransform, x, y);
        t.rectTransform.sizeDelta = new Vector2(w, Mathf.Ceil(t.GetPreferredValues(t.text, w, 0f).y));
        return t;
    }

    /// <summary>한 줄 칸(높이 = 줄 높이) — 넘치면 말줄임.</summary>
    private static void Cell(RectTransform parent, string text, float size, Color color, float x, float y, float w, TextAlignmentOptions align)
    {
        var t = UICodeOverlay.Txt(parent, text, size, color, Vector2.zero, w, align: align);
        TopLeft(t.rectTransform, x, y);
        t.rectTransform.sizeDelta = new Vector2(w, RowH);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
    }

    private static void Rule(RectTransform parent, float x, float y, float w)
    {
        var r = UICodeOverlay.Img(parent, "Rule", Vector2.zero, new Vector2(w, 1f), RuleC);
        r.raycastTarget = false;
        TopLeft(r.rectTransform, x, y);
    }

    private static void TopLeft(RectTransform rt, float x, float y)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
    }

    /// <summary>내용 폭 — 스크롤 보기 폭(열 폭 − 막대 자리).</summary>
    private static float RightOf(RectTransform content)
    {
        var view = (RectTransform)content.parent;
        var col = (RectTransform)view.parent;
        return col.sizeDelta.x + view.offsetMax.x - view.offsetMin.x;
    }
}
