#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 글자 겹침 · 말줄임 · 작은 글자 공용 점검(10-15 사용자 「텍스트 겹침도 문제도 확인」).
/// 실측 도구가 사진을 찍을 때마다(<c>UILayoutRuntimeProbeEditor.ShotAsync</c>) 그 순간 화면에 보이는 UI 글자를 훑어
/// Temp/text_overlap_audit.txt에 한 덩어리씩 덧붙인다 — 실측 도구 마흔여 개가 그대로 겹침 점검기가 된다.
/// <list type="bullet">
/// <item>보임 = 켜져 있고 · 알파(CanvasGroup 사슬 포함) 0.05 이상 · 글이 있고 · 같은 캔버스에서 뒤에 그려지는 불투명 판(알파 0.9 이상)이 덮지 않는다.</item>
/// <item>겹침 = 같은 캔버스의 두 글자 상자(그려진 글자 기준)가 가로 2px · 세로 「작은 쪽 높이의 35%」 넘게 겹친다(부모 · 자식 사이는 뺀다).
/// 글자 상자는 줄 높이(위 · 아래 여백)를 품어, 위아래로 붙인 이름 · 수치 줄은 몇 px 겹쳐도 글자는 닿지 않는다(10-15 첫 실측 오탐).</item>
/// <item>작은 글자 = 화면 1080 기준 15px 미만(본문 최소 — 톤 v2).</item>
/// </list>
/// </summary>
public static class TextOverlapAudit
{
    public const string OutPath = "Temp/text_overlap_audit.txt";
    private const float OverlapPx = 2f;
    private const float OverlapYK = 0.35f;
    private const float MinPx     = 15f;

    /// <summary>지금 화면을 훑어 한 덩어리를 덧붙인다. 반환 = 문제 수(겹침 + 말줄임 + 작은 글자).</summary>
    public static int Record(string shot)
    {
        var sb = new StringBuilder();
        int overlaps = 0, cut = 0, small = 0, seen = 0;
        var byCanvas = new Dictionary<Canvas, List<TMP_Text>>();
        foreach (var t in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None))
        {
            if (!Visible(t) || t.canvas == null) continue;
            if (!byCanvas.TryGetValue(t.canvas, out var list)) byCanvas[t.canvas] = list = new List<TMP_Text>();
            list.Add(t);
        }

        var lines = new StringBuilder();
        foreach (var kv in byCanvas)
        {
            var canvas = kv.Key;
            var order  = RenderOrder(canvas);
            var texts  = new List<TMP_Text>();
            foreach (var t in kv.Value) if (!Covered(t, order)) texts.Add(t);
            seen += texts.Count;
            float k = canvas.rootCanvas != null && canvas.rootCanvas.transform.lossyScale.y > 0f ? 1f / canvas.rootCanvas.transform.lossyScale.y : 1f;
            var rects = new Rect[texts.Count];
            for (int i = 0; i < texts.Count; i++)
            {
                var t = texts[i];
                t.ForceMeshUpdate();
                rects[i] = WorldRect(t);
                if (t.isTextTruncated) { cut++; lines.AppendLine($"   말줄임 {Path(t)} 「{Short(t)}」"); }
                else if (OutOfBox(t, rects[i], k, out string side)) { cut++; lines.AppendLine($"   칸 밖 {side} {Path(t)} 「{Short(t)}」"); }
                float px = t.fontSize * t.rectTransform.lossyScale.y * k;
                if (px < MinPx - 0.05f) { small++; lines.AppendLine($"   작은 글자 {px:0.#}px(글자 {t.fontSize:0.#} × 배율 {t.rectTransform.lossyScale.y * k:0.###}{(t.enableAutoSizing ? $" · 자동 크기 {t.fontSizeMin:0.#}~{t.fontSizeMax:0.#}" : "")} · 칸 {t.rectTransform.rect.width:0}×{t.rectTransform.rect.height:0}) {Path(t)} 「{Short(t)}」"); }
            }
            for (int i = 0; i < texts.Count; i++)
                for (int j = i + 1; j < texts.Count; j++)
                {
                    if (texts[i].transform.IsChildOf(texts[j].transform) || texts[j].transform.IsChildOf(texts[i].transform)) continue;
                    var a = rects[i]; var b = rects[j];
                    float ox = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
                    float oy = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                    if (ox * k <= OverlapPx || oy * k <= Mathf.Max(OverlapPx, OverlapYK * Mathf.Min(a.height, b.height) * k)) continue;
                    overlaps++;
                    lines.AppendLine($"   겹침 {ox * k:0}×{oy * k:0}px {Path(texts[i])} 「{Short(texts[i])}」 × {Path(texts[j])} 「{Short(texts[j])}」");
                }
        }
        sb.AppendLine($"[{shot}] 보이는 글 {seen} · 겹침 {overlaps} · 말줄임 · 칸 밖 {cut} · 15px 미만 {small}");
        sb.Append(lines);
        try { File.AppendAllText(OutPath, sb.ToString()); } catch (IOException) { }
        return overlaps + cut + small;
    }

    /// <summary>새로 잰다 — 실측 묶음을 돌리기 전에 지운다.</summary>
    public static void Clear()
    {
        try { File.WriteAllText(OutPath, string.Empty); } catch (IOException) { }
    }

    // ── 도우미 ──

    /// <summary>그려진 글자가 제 칸 밖으로 3px 넘게 나갔나(넘침 모드 Overflow — 말줄임 없이 테 밖에 그린다).</summary>
    private static bool OutOfBox(TMP_Text t, Rect drawn, float k, out string side)
    {
        side = "";
        var box = RectOf(t.rectTransform);
        float l = (box.xMin - drawn.xMin) * k, r = (drawn.xMax - box.xMax) * k;
        float b = (box.yMin - drawn.yMin) * k, u = (drawn.yMax - box.yMax) * k;
        if (r > 3f || l > 3f) { side = $"가로 {Mathf.Max(l, r):0}px"; return true; }
        if (u > 4f || b > 4f) { side = $"세로 {Mathf.Max(u, b):0}px"; return true; }
        return false;
    }

    private static bool Visible(TMP_Text t)
    {
        if (t == null || !t.isActiveAndEnabled || t.color.a < 0.05f || t.alpha < 0.05f) return false;
        if (t.GetParsedText().Trim().Length == 0) return false;
        float a = 1f;
        foreach (var g in t.GetComponentsInParent<CanvasGroup>(false))
        {
            a *= g.alpha;
            if (g.ignoreParentGroups) break;
        }
        return a >= 0.05f;
    }

    /// <summary>캔버스 안 그리기 순서(UGUI = 깊이 우선 순서) — 뒤에 그려지는 불투명 판이 글을 덮는지 본다.</summary>
    private static List<Graphic> RenderOrder(Canvas canvas)
    {
        var all = new List<Graphic>();
        foreach (var g in canvas.GetComponentsInChildren<Graphic>(false))
            if (g.canvas == canvas && g.isActiveAndEnabled) all.Add(g);
        return all;
    }

    private static bool Covered(TMP_Text t, List<Graphic> order)
    {
        int at = order.IndexOf(t);
        if (at < 0) return false;
        var r = WorldRect(t);
        for (int i = at + 1; i < order.Count; i++)
        {
            var g = order[i];
            if (g is TMP_Text || g.transform.IsChildOf(t.transform)) continue;
            if (g.color.a < 0.9f || g is Image img && img.sprite == null && g.color.a < 0.95f) continue;
            float a = 1f;
            foreach (var cg in g.GetComponentsInParent<CanvasGroup>(false)) { a *= cg.alpha; if (cg.ignoreParentGroups) break; }
            if (a < 0.9f) continue;
            var c = RectOf(g.rectTransform);
            if (c.Contains(r.min) && c.Contains(r.max)) return true;
        }
        return false;
    }

    private static Rect WorldRect(TMP_Text t)
    {
        var b = t.textBounds;
        if (b.size.x <= 0f) return RectOf(t.rectTransform);
        Vector3 a = t.transform.TransformPoint(b.min), c = t.transform.TransformPoint(b.max);
        return UnityEngine.Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
    }

    private static Rect RectOf(RectTransform rt)
    {
        var w = new Vector3[4];
        rt.GetWorldCorners(w);
        return UnityEngine.Rect.MinMaxRect(Mathf.Min(w[0].x, w[2].x), Mathf.Min(w[0].y, w[2].y), Mathf.Max(w[0].x, w[2].x), Mathf.Max(w[0].y, w[2].y));
    }

    private static string Path(TMP_Text t)
    {
        var p = t.transform;
        string s = p.name;
        for (int i = 0; i < 2 && p.parent != null; i++) { p = p.parent; s = p.name + "/" + s; }
        return s;
    }

    private static string Short(TMP_Text t)
    {
        string s = t.GetParsedText().Replace('\n', ' ');
        return s.Length > 16 ? s.Substring(0, 16) + "…" : s;
    }
}
#endif
