using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 바 옷(10-18 ⑲) — 사용자 「체력바나 바 같은 경우 깔끔한 디자인의 바를 힉스필드로 뽑아 봐도 좋겠다」 → 힉스필드 시안 A(얇은 겹 금선 · 화살촉 끝 · 위 반사선 결).
/// 그림을 HUD 두께(10px)로 줄이면 금선이 0.5px로 뭉개져, 시안의 모양만 코드로 또렷하게 그린다(그림 파일 없음).
///   채움 결: 왼쪽이 조금 어둡고 오른쪽이 밝은 무채색 + 위쪽 얇은 반사선 — Image 색을 곱해 쓴다(체력 초록 · 위험 빨강 · 보스 붉은색 그대로).
///   옷: 바 위아래 1px 금선(바에서 2px 띄움) + 양 끝 마름모 끝장식(짙은 판 + 1px 금 테, Q 마름모와 같은 부품).
/// </summary>
public static class UIBarDress
{
    // ── Constants ──
    private const string DressName = "BarDress";
    private const float  LineGap   = 2f;   // 바 가장자리 → 금선(1080 기준 px)
    private static readonly Color LineColor = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.40f);
    private static readonly Color CapLine   = UITheme.Thin.LineActive;
    private static readonly Color CapFill   = new(0.06f, 0.055f, 0.08f, 0.92f);

    // ── Static ──
    private static Sprite s_grain;

    // ── Properties ──
    /// <summary>채움 결(무채색 64×16) — Filled · Simple 어느 쪽이든 Image 색과 곱해진다.</summary>
    public static Sprite FillGrain => s_grain != null ? s_grain : (s_grain = BakeGrain());

    // ── Public Methods ──

    /// <summary>
    /// <paramref name="host"/> 맨 위(마지막 자식)에 옷을 입힌다 — 자리는 <paramref name="match"/>(트랙)의 앵커 · 오프셋을 그대로 따른다.
    /// 끝장식이 채움 끝을 덮도록 채움보다 뒤에 그린다. 두 번 불러도 하나만 있다.
    /// </summary>
    public static RectTransform Dress(RectTransform host, RectTransform match, float capW, float capH)
    {
        if (host == null) return null;
        if (host.Find(DressName) is RectTransform old) { old.SetAsLastSibling(); return old; }

        var root = NewRect(DressName, host);
        if (match != null)
        {
            root.anchorMin = match.anchorMin; root.anchorMax = match.anchorMax;
            root.offsetMin = match.offsetMin; root.offsetMax = match.offsetMax;
        }
        else
        {
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
        }
        root.SetAsLastSibling();

        float inset = capW * 0.5f;
        var top = NewImage("LineTop", root, UIThinShapes.Solid, LineColor);
        top.rectTransform.anchorMin = new Vector2(0f, 1f); top.rectTransform.anchorMax = Vector2.one;
        top.rectTransform.offsetMin = new Vector2(inset, LineGap); top.rectTransform.offsetMax = new Vector2(-inset, LineGap + UITheme.Thin.Stroke);
        var bottom = NewImage("LineBottom", root, UIThinShapes.Solid, LineColor);
        bottom.rectTransform.anchorMin = Vector2.zero; bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
        bottom.rectTransform.offsetMin = new Vector2(inset, -LineGap - UITheme.Thin.Stroke); bottom.rectTransform.offsetMax = new Vector2(-inset, -LineGap);

        MakeCap("CapL", root, 0f, capW, capH);
        MakeCap("CapR", root, 1f, capW, capH);
        return root;
    }

    // ── Private Methods ──

    private static void MakeCap(string name, RectTransform root, float x, float w, float h)
    {
        var plate = NewImage(name, root, UIThinShapes.Fill(ThinShape.Diamond, w, h), CapFill);
        var rt = plate.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(x, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = new Vector2(w, h);
        var edge = NewImage("Edge", rt, UIThinShapes.Outline(ThinShape.Diamond, w, h), CapLine);
        edge.rectTransform.anchorMin = Vector2.zero; edge.rectTransform.anchorMax = Vector2.one;
        edge.rectTransform.offsetMin = edge.rectTransform.offsetMax = Vector2.zero;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var img = NewRect(name, parent).gameObject.AddComponent<Image>();
        img.sprite        = sprite;
        img.color         = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>왼쪽 0.74 → 오른쪽 0.94 밝기 · 위쪽 얇은 반사선(+0.08) · 아래 가장자리 그늘(×0.86) — 시안 A 채움 결.</summary>
    private static Sprite BakeGrain()
    {
        const int W = 64, H = 16;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
        {
            name = "BarGrain", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                float g = Mathf.Lerp(0.74f, 0.94f, u);
                if (v > 0.66f && v < 0.84f) g += 0.08f;   // 위 반사선
                else if (v < 0.18f)         g *= 0.86f;   // 아래 그늘
                byte b = (byte)(Mathf.Clamp01(g) * 255f);
                px[y * W + x] = new Color32(b, b, b, 255);
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        var sprite = Sprite.Create(tex, new Rect(0f, 0f, W, H), new Vector2(0.5f, 0.5f), 100f);
        sprite.name      = "BarGrain";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
