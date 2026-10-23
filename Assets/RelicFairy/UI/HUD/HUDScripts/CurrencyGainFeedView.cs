//============================================================
// CurrencyGainFeedView.cs
// - 재화 획득 목록(10-22 사용자 「원신 · 명조 같은 기법으로 획득한 재화를 테두리랑 내용을 실시간으로 가까운 순 보여주고 사라지게」)
// - 재화 칸(오른쪽 위) 바로 아래 「획득」 머리 + 줄마다 [재화색 테 아이콘] 「이름 × n」 · 새 줄이 맨 위 · 2.5초 뒤 사라진다
// - 재화 하나에 줄 하나 — 같은 재화가 또 들어오면 숫자만 더하고 시간을 다시 잰다(금화가 마리마다 줄을 쌓지 않게)
//============================================================
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum CurrencyGainKind { Gold, EnhanceMaterial, RuneOre, Essence }

public sealed class CurrencyGainFeedView : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────────────
    private const int   Kinds      = 4;
    private const float Width      = 300f;
    private const float RowH       = 38f;
    private const float RowGap     = 6f;
    private const float HeaderH    = 26f;
    private const float IconSize   = 28f;
    private const float TextSize   = 20f;
    private const float HeaderSize = 15f;
    private const float SlideIn    = 0.2f;     // 오른쪽에서 미끄러져 들어온다
    private const float SlideFrom  = 28f;
    private const float Hold       = 2.5f;
    private const float FadeOut    = 0.4f;
    private const float MoveSpeed  = 14f;      // 줄이 빠지면 남은 줄이 올라가는 빠르기
    private const float BandAlpha  = 0.62f;
    private static readonly string[] Names = { "골드", "강화재료", "원석", "심연의 정수" };

    // ── Private ──────────────────────────────────────────────────────
    private sealed class Row
    {
        public RectTransform Rect;
        public CanvasGroup   Group;
        public Image         Icon, Frame;
        public TMP_Text      Text;
        public int           Amount;
        public float         ShownAt = -1f, LastPushAt, Y;
        public int           Order;   // 들어온 순서 — 큰 값이 최근(맨 위)
        public bool Alive => ShownAt >= 0f;
    }

    private readonly Row[]   _rows   = new Row[Kinds];
    private readonly Color[] _tints  = new Color[Kinds];
    private CanvasGroup _headerGroup;
    private int _order;

    // ── Lifecycle ────────────────────────────────────────────────────
    private void Update()
    {
        float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
        int slot = 0;
        float headerAlpha = 0f;
        // 최근 순으로 자리를 매긴다(줄 4개 — 정렬 없이 큰 순서부터 고른다)
        int taken = 0;
        while (true)
        {
            int best = -1;
            for (int i = 0; i < Kinds; i++)
            {
                var r = _rows[i];
                if (r == null || !r.Alive || (taken & (1 << i)) != 0) continue;
                if (best < 0 || r.Order > _rows[best].Order) best = i;
            }
            if (best < 0) break;
            taken |= 1 << best;
            var pick = _rows[best];

            float age = now - pick.ShownAt, left = pick.LastPushAt + Hold - now;
            float a = left >= 0f ? Mathf.Clamp01(age / SlideIn) : Mathf.Clamp01(1f + left / FadeOut);
            if (left < -FadeOut) { Hide(pick); continue; }

            float targetY = -(HeaderH + slot * (RowH + RowGap));
            pick.Y = age < dt * 1.5f ? targetY : Mathf.Lerp(pick.Y, targetY, 1f - Mathf.Exp(-MoveSpeed * dt));
            float slide = (1f - EaseOut(Mathf.Clamp01(age / SlideIn))) * SlideFrom;
            pick.Rect.anchoredPosition = new Vector2(slide, pick.Y);
            pick.Group.alpha = a;
            headerAlpha = Mathf.Max(headerAlpha, a);
            slot++;
        }
        if (_headerGroup != null) _headerGroup.alpha = headerAlpha;
    }

    // ── Public Methods ───────────────────────────────────────────────
    /// <summary>재화 칸 아래에 목록을 세운다 — 부모 = 재화 줄의 부모(같은 배율), 오른쪽 위 기준 <paramref name="topRight"/>.</summary>
    public static CurrencyGainFeedView Create(RectTransform parent, Vector2 topRight)
    {
        var go = new GameObject("CurrencyGainFeed(Runtime)", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = topRight;
        rt.sizeDelta = new Vector2(Width, HeaderH + Kinds * (RowH + RowGap));
        var view = go.AddComponent<CurrencyGainFeedView>();
        view.Build();
        return view;
    }

    /// <summary>재화 아이콘 · 테 색(HUD 재화 칸과 같은 그림 · 같은 색).</summary>
    public void SetLook(CurrencyGainKind kind, Sprite icon, Color tint)
    {
        int i = (int)kind;
        _tints[i] = tint;
        var r = _rows[i];
        if (r == null) return;
        r.Icon.sprite  = icon;
        r.Icon.enabled = icon != null;
        r.Frame.color  = new Color(tint.r, tint.g, tint.b, 0.9f);
    }

    /// <summary>얻은 양 하나 — 떠 있는 같은 재화 줄이 있으면 더하고 시간을 다시 잰다, 없으면 맨 위에 새 줄.</summary>
    public void Push(CurrencyGainKind kind, int amount)
    {
        if (amount <= 0) return;
        var r = _rows[(int)kind];
        if (r == null) return;
        float now = Time.unscaledTime;
        if (r.Alive && now - r.LastPushAt <= Hold + FadeOut)
        {
            r.Amount += amount;
            if (now - r.LastPushAt > Hold) r.ShownAt = now - SlideIn;   // 사라지던 줄은 다시 또렷하게(미끄러짐 없이)
        }
        else
        {
            r.Amount  = amount;
            r.ShownAt = now;
            r.Order   = ++_order;
            r.Rect.gameObject.SetActive(true);
        }
        r.LastPushAt = now;
        var t = _tints[(int)kind];
        r.Text.text = $"{Names[(int)kind]} <color=#{ColorUtility.ToHtmlStringRGB(t)}>× {r.Amount:N0}</color>";
    }

    // ── Private Methods ──────────────────────────────────────────────
    private void Build()
    {
        var header = new GameObject("Header", typeof(RectTransform), typeof(CanvasGroup));
        var hrt = (RectTransform)header.transform;
        hrt.SetParent(transform, false);
        hrt.anchorMin = new Vector2(0f, 1f); hrt.anchorMax = Vector2.one; hrt.pivot = new Vector2(1f, 1f);
        hrt.sizeDelta = new Vector2(0f, HeaderH);
        _headerGroup = header.GetComponent<CanvasGroup>();
        _headerGroup.alpha = 0f; _headerGroup.blocksRaycasts = false; _headerGroup.interactable = false;
        var label = MakeText(hrt, "Label", HeaderSize, UITheme.Thin.Ink);
        label.text = "획득";
        label.alignment = TextAlignmentOptions.BottomRight;
        label.rectTransform.offsetMax = new Vector2(-4f, 0f);
        // 머리 밑 얇은 금선(톤 v2 · 1px)
        var rule = new GameObject("Rule", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        rule.rectTransform.SetParent(hrt, false);
        rule.rectTransform.anchorMin = new Vector2(0.45f, 0f); rule.rectTransform.anchorMax = new Vector2(1f, 0f);
        rule.rectTransform.pivot = new Vector2(1f, 0f);
        rule.rectTransform.sizeDelta = new Vector2(0f, UITheme.Thin.Stroke);
        rule.rectTransform.anchoredPosition = new Vector2(0f, -2f);
        rule.color = new Color(UITheme.Thin.Gold.r, UITheme.Thin.Gold.g, UITheme.Thin.Gold.b, 0.55f);
        rule.raycastTarget = false;

        for (int i = 0; i < Kinds; i++) _rows[i] = MakeRow(i);
    }

    private Row MakeRow(int i)
    {
        var go = new GameObject($"Row_{Names[i]}", typeof(RectTransform), typeof(CanvasGroup));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(Width, RowH);
        var group = go.GetComponent<CanvasGroup>();
        group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;

        // 띠 — 좌우가 흐린 먹색(자막 띠와 같은 그림). 오른쪽 끝(화면 가장자리) 쪽이 짙게 오른쪽으로 치우친다.
        var band = new GameObject("Band", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        band.rectTransform.SetParent(rt, false);
        band.rectTransform.anchorMin = Vector2.zero; band.rectTransform.anchorMax = Vector2.one;
        band.rectTransform.offsetMin = new Vector2(-40f, 0f); band.rectTransform.offsetMax = new Vector2(60f, 0f);
        band.sprite = UITheme.SoftBand; band.type = Image.Type.Simple;
        band.color  = new Color(0.02f, 0.02f, 0.04f, BandAlpha);
        band.raycastTarget = false;

        // 아이콘 칸 — 1px 재화색 테 + 아이콘(오른쪽 끝)
        var frame = new GameObject("Frame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        var frt = frame.rectTransform;
        frt.SetParent(rt, false);
        frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(1f, 0.5f);
        frt.sizeDelta = new Vector2(IconSize + 6f, IconSize + 6f);
        frt.anchoredPosition = new Vector2(-4f, 0f);
        frame.sprite = UIProceduralSprites.Ring(0.04f, 64);
        frame.color  = UITheme.Thin.Gold;
        frame.raycastTarget = false;
        var icon = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        icon.rectTransform.SetParent(frt, false);
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        icon.rectTransform.sizeDelta = new Vector2(IconSize - 4f, IconSize - 4f);
        icon.preserveAspect = true; icon.raycastTarget = false; icon.enabled = false;

        var text = MakeText(rt, "Text", TextSize, UITheme.Thin.Ink);
        text.alignment = TextAlignmentOptions.MidlineRight;
        text.rectTransform.offsetMax = new Vector2(-(IconSize + 16f), 0f);

        go.SetActive(false);
        return new Row { Rect = rt, Group = group, Icon = icon, Frame = frame, Text = text };
    }

    private static TMP_Text MakeText(RectTransform parent, string name, float size, Color color)
    {
        var t = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        t.rectTransform.SetParent(parent, false);
        t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one;
        t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
        UITheme.StyleThinText(t, UIWeight.Medium, size, color, shadow: true);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode     = TextOverflowModes.Overflow;
        t.raycastTarget    = false;
        return t;
    }

    private static void Hide(Row r)
    {
        r.ShownAt = -1f;
        r.Amount  = 0;
        r.Group.alpha = 0f;
        r.Rect.gameObject.SetActive(false);
    }

    private static float EaseOut(float k) => 1f - (1f - k) * (1f - k);
}
