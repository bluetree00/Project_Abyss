using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 톤 v2 구획 테(10-08, 룬판) — 1px 네 변 + 모서리 눈금 + 아주 옅은 바탕. 조각 이미지로 지어 칸 크기가 바뀌어도 선이 1px로 남는다
/// (한 장짜리 외곽선 스프라이트는 늘어나면 흐려진다). <see cref="UIThinFrame"/>이 상태 · 연출이 있는 칸이라면 이것은 가만히 있는 묶음 테다.
/// </summary>
public static class UIThinBox
{
    private static readonly Vector2[] Corners = { Vector2.zero, new(1f, 0f), new(0f, 1f), Vector2.one };

    /// <summary>
    /// <paramref name="host"/> 안 anchorMin~anchorMax에서 사방 <paramref name="inset"/>만큼 안쪽에 짓는다. 맨 앞 형제(내용 아래).
    /// 눈금 · 바탕은 알파가 0이면 짓지 않는다.
    /// </summary>
    public static RectTransform Create(RectTransform host, string name, Vector2 anchorMin, Vector2 anchorMax,
                                       float inset, Color line, Color tick, Color fill)
    {
        if (host == null) return null;
        var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        var rt = (RectTransform)go.transform;
        rt.SetParent(host, false);
        rt.SetAsFirstSibling();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);

        if (fill.a > 0f) Part(rt, "Fill", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, fill);
        const float s = UITheme.Thin.Stroke;
        Part(rt, "Top",    new Vector2(0f, 1f), Vector2.one,         new Vector2(0.5f, 1f), new Vector2(0f, s), line);
        Part(rt, "Bottom", Vector2.zero,        new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, s), line);
        Part(rt, "Left",   Vector2.zero,        new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(s, 0f), line);
        Part(rt, "Right",  new Vector2(1f, 0f), Vector2.one,         new Vector2(1f, 0.5f), new Vector2(s, 0f), line);
        if (tick.a > 0f)
        {
            const float l = UITheme.Thin.TickLen;
            foreach (var c in Corners)
            {
                Part(rt, "TickH", c, c, c, new Vector2(l, s), tick);
                Part(rt, "TickV", c, c, c, new Vector2(s, l), tick);
            }
        }
        return rt;
    }

    private static void Part(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                             Vector2 size, Color color)
    {
        var rt = ShopUIStyle.MakeImage(parent, name, color).rectTransform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot     = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
    }
}
