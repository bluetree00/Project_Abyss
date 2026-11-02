using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 코드로 짓는 덮개 창 도우미(11-02 — 멀린 룬 보관 · 환전소). 1920×1080 · Scale With Screen Size · Match 0.5 캔버스와
/// 그림 · 얇은 글(NotoSansKR) · 얇은 금선 단추를 같은 모양으로 짓는다.
/// </summary>
public static class UICodeOverlay
{
    public static readonly Color Ink  = new(0.94f, 0.91f, 0.85f, 1f);
    public static readonly Color Mist = new(0.72f, 0.72f, 0.76f, 1f);

    /// <summary>화면 덮개 캔버스(맨 앞 정렬 <paramref name="sortingOrder"/>) — 투명으로 시작(CanvasGroup).</summary>
    public static (GameObject go, RectTransform root, CanvasGroup group) Create(string name, int sortingOrder)
    {
        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
        var group = go.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        return (go, (RectTransform)go.transform, group);
    }

    public static Image Img(RectTransform parent, string name, Vector2 pos, Vector2 size, Color color, bool stretch = false)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        if (stretch) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
        else { rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = size; }
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    /// <summary>얇은 1px 남짓 테두리(둥근 모서리) — 부모를 꽉 채운다.</summary>
    public static Image Line(RectTransform parent, Color color)
    {
        var line = Img(parent, "Line", Vector2.zero, Vector2.zero, color, stretch: true);
        line.sprite = UIProceduralSprites.RoundedOutline(radius: 6f, stroke: 1.2f);
        line.type   = Image.Type.Sliced;
        line.raycastTarget = false;
        return line;
    }

    public static TMP_Text Txt(RectTransform parent, string text, float size, Color color, Vector2 pos, float width,
                               FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(width, size * 2.6f);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        var thin = ThinFont();
        if (thin != null) tmp.font = thin;
        tmp.text = Loc.T(text);   // 11-02 Q11 — 표에 있으면 지금 언어로(없으면 그대로)
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = align;
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
        return tmp;
    }

    /// <summary>단추 — 주(어두운 바탕 + 금선) · 조용함(바탕 없음 · 옅은 글).</summary>
    public static Button Btn(RectTransform parent, string label, Vector2 pos, Vector2 size, float fontSize, System.Action onClick, bool quiet = false)
    {
        var bg = Img(parent, "Btn_" + label, pos, size, quiet ? new Color(0f, 0f, 0f, 0f) : new Color(0.10f, 0.09f, 0.07f, 0.95f));
        if (!quiet) Line((RectTransform)bg.transform, UIPalette.Gold);
        var btn = bg.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(() => onClick());
        Txt((RectTransform)bg.transform, label, fontSize, quiet ? Mist : new Color(0.96f, 0.90f, 0.72f), Vector2.zero, size.x);
        return btn;
    }

    /// <summary>얇은 한글 글꼴(NotoSansKR) — 기본 글꼴이 굵어 덮개 글에 쓰지 않는다(UI 톤).</summary>
    public static TMP_FontAsset ThinFont()
    {
        var def = TMP_Settings.defaultFontAsset;
        if (def == null) return null;
        if (def.name.Contains("NotoSansKR")) return def;
        var table = def.fallbackFontAssetTable;
        if (table == null) return null;
        for (int i = 0; i < table.Count; i++)
            if (table[i] != null && table[i].name.Contains("NotoSansKR")) return table[i];
        return null;
    }
}
