using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 조작 안내 카드(10-24 사용자 「인트로 시작 시 조작키랑 대화 넘기기키 등 안내가 필요해 · 근거리가 통하지 않는다의 레거시 튜토리얼 개선」).
/// 화면 왼쪽 아래에 얇은 판 하나 — 제목 한 줄 + [키] 하는 일 줄들. 한 번에 한 장(새로 띄우면 앞 카드를 바꾼다).
/// 조건(doneWhen)이 참이 되거나 최대 시간이 지나면 0.25초에 걸쳐 사라진다. 자기 캔버스(HUD 위 · 팝업 아래)를 갖는다.
/// </summary>
public sealed class KeyGuideCard : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────
    private const float Width    = 440f;
    private const float RowH     = 34f;
    private const float TitleH   = 30f;
    private const float Pad      = 16f;
    private const float KeyW     = 124f;
    private const float FadeSec  = 0.25f;
    private const float MinShown = 1.2f;   // 조건이 바로 참이어도 이만큼은 보여 준다

    // ── Static ──────────────────────────────────────────────
    private static KeyGuideCard s_instance;

    // ── Private ─────────────────────────────────────────────
    private RectTransform _panel;
    private CanvasGroup _group;
    private TMP_Text _title;
    private readonly System.Collections.Generic.List<(Image chip, TMP_Text key, TMP_Text act)> _rows = new();
    private Func<bool> _doneWhen;
    private float _age, _maxSeconds;
    private bool _showing, _closing;
    private Action _onClosed;

    // ── Lifecycle ───────────────────────────────────────────
    private void LateUpdate()
    {
        if (_group == null || (!_showing && !_closing)) return;
        float dt = Time.unscaledDeltaTime;
        if (_showing)
        {
            _age += dt;
            _group.alpha = Mathf.Min(1f, _group.alpha + dt / FadeSec);
            bool done = _age >= _maxSeconds || (_age >= MinShown && _doneWhen != null && SafeDone());
            if (done) { _showing = false; _closing = true; }
            return;
        }
        _group.alpha = Mathf.Max(0f, _group.alpha - dt / FadeSec);
        if (_group.alpha <= 0f)
        {
            _closing = false;
            var cb = _onClosed; _onClosed = null;
            cb?.Invoke();
        }
    }

    private void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
    }

    // ── Public Methods ──────────────────────────────────────
    /// <summary>카드를 띄운다. rows = (키, 하는 일). doneWhen이 참이 되면(최소 1.2초 뒤) · maxSeconds가 지나면 사라지고 onClosed를 부른다.</summary>
    public static void Show(string title, (string key, string action)[] rows, float maxSeconds, Func<bool> doneWhen = null, Action onClosed = null)
    {
        if (s_instance == null)
        {
            var go = new GameObject("@KeyGuideCard");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<KeyGuideCard>();
            s_instance.Build();
        }
        s_instance.Present(title, rows, maxSeconds, doneWhen, onClosed);
    }

    /// <summary>떠 있는 카드를 바로 걷는다(씬이 바뀔 때 등). 닫힘 콜백은 부르지 않는다.</summary>
    public static void HideNow()
    {
        if (s_instance == null) return;
        s_instance._showing = s_instance._closing = false;
        s_instance._onClosed = null;
        if (s_instance._group != null) s_instance._group.alpha = 0f;
    }

    // ── Private Methods ─────────────────────────────────────
    private bool SafeDone()
    {
        try { return _doneWhen(); }
        catch (Exception) { return true; }   // 대상이 사라졌다(씬 전환 등) — 카드를 걷는다
    }

    private void Present(string title, (string key, string action)[] rows, float maxSeconds, Func<bool> doneWhen, Action onClosed)
    {
        _title.text = title;
        for (int i = 0; i < _rows.Count; i++)
        {
            bool on = rows != null && i < rows.Length;
            _rows[i].chip.gameObject.SetActive(on);
            _rows[i].act.gameObject.SetActive(on);
            if (!on) continue;
            _rows[i].key.text = rows[i].key;
            _rows[i].act.text = rows[i].action;
        }
        int n = rows != null ? Mathf.Min(rows.Length, _rows.Count) : 0;
        _panel.sizeDelta = new Vector2(Width, Pad * 2f + TitleH + n * RowH);
        _doneWhen   = doneWhen;
        _maxSeconds = Mathf.Max(MinShown, maxSeconds);
        _onClosed   = onClosed;
        _age = 0f;
        _showing = true;
        _closing = false;
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = UISortingOrder.HudIndicator + 5;   // HUD 위 · 화면 연출 · 팝업 아래
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight  = 0.5f;

        var panelImg = ShopUIStyle.MakeImage(transform, "Panel", UITheme.Thin.GlassFill);
        panelImg.sprite = UIProceduralSprites.RoundedRect(radius: 10f, feather: 1.5f);
        panelImg.type   = Image.Type.Sliced;
        _panel = panelImg.rectTransform;
        _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0f, 0f);
        _panel.anchoredPosition = new Vector2(48f, 300f);
        _group = panelImg.gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable   = false;

        var line = ShopUIStyle.MakeImage(_panel, "TopLine", UITheme.Thin.LineGold);   // 위 금 1px — 「얇은 금선」
        var lrt = line.rectTransform;
        lrt.anchorMin = new Vector2(0f, 1f); lrt.anchorMax = new Vector2(1f, 1f); lrt.pivot = new Vector2(0.5f, 1f);
        lrt.offsetMin = new Vector2(Pad, -1f); lrt.offsetMax = new Vector2(-Pad, 0f);

        _title = NewText(_panel, "Title", UIWeight.Medium, UITheme.Thin.Lead, UITheme.Thin.Gold, TextAlignmentOptions.Left);
        Place(_title.rectTransform, Pad, Pad, Width - Pad * 2f, TitleH);

        for (int i = 0; i < 5; i++)
        {
            float top = Pad + TitleH + i * RowH + 4f;
            var chip = ShopUIStyle.MakeImage(_panel, $"Key{i}", UITheme.Thin.InkA(0.12f));
            chip.sprite = UIProceduralSprites.RoundedRect(radius: 6f, feather: 1.2f);
            chip.type   = Image.Type.Sliced;
            Place(chip.rectTransform, Pad, top + 2f, KeyW, RowH - 8f);
            var key = NewText(chip.rectTransform, "Label", UIWeight.Medium, UITheme.Thin.Small, UITheme.Thin.Ink, TextAlignmentOptions.Center);
            ShopUIStyle.Stretch(key.rectTransform);
            var act = NewText(_panel, $"Act{i}", UIWeight.Regular, UITheme.Thin.Body, UITheme.Thin.InkA(0.9f), TextAlignmentOptions.Left);
            Place(act.rectTransform, Pad + KeyW + 12f, top, Width - Pad * 2f - KeyW - 12f, RowH);
            _rows.Add((chip, key, act));
        }
    }

    private static TMP_Text NewText(Transform parent, string name, UIWeight weight, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        UITheme.StyleThinText(t, weight, size, color, shadow: true);
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        return t;
    }

    /// <summary>판 왼쪽 위 기준(x · 위에서 y) 자리.</summary>
    private static void Place(RectTransform rt, float x, float yFromTop, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -yFromTop);
        rt.sizeDelta = new Vector2(w, h);
    }
}
