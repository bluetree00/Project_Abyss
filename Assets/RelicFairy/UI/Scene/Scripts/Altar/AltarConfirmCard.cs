using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 기억의 제단 해금 확인 카드(10-14 사용자 「클릭하면 바로 해금된다 — 정보를 확실히 본 뒤 결정할 수 있게」).
/// 노드를 고른 뒤 [해금](또는 같은 노드를 한 번 더)을 누르면 트리 위에 크게 뜬다 — 이름 · 무엇이 바뀌나 · 정수(쓰는 값 → 남는 값) ·
/// 할인 · 이어서 열리는 것을 한 장에 보여 주고, <b>[해금한다]를 눌러야만</b> 산다. [취소] · 바깥 클릭 · ESC는 닫기만(정수 그대로).
/// <para>판 · 글은 상세 패널과 같은 톤(Noto 300/400/500 · 잉크 본문 · 금 = 누를 것). 글자 최소 17.</para>
/// </summary>
[DisallowMultipleComponent]
public sealed class AltarConfirmCard : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────
    private const float CardW   = 760f;
    private const float CardH   = 560f;   // 500이면 「이어서 열 수 있는 것」 줄이 버튼에 닿았다(10-14 사진)
    private const float Pad     = 40f;
    private const float IconPx  = 88f;
    private const float BtnH    = 60f;
    private const float FadeSec = 0.14f;

    // ── Static ───────────────────────────────────────────
    private static readonly Color Dim       = new(0.02f, 0.02f, 0.04f, 0.62f);
    private static readonly Color InfoColor = new(0.80f, 0.76f, 0.88f, 1f);

    // ── Private ──────────────────────────────────────────
    private CanvasGroup _group;
    private TMP_Text _name, _header, _effect, _cost, _condition, _next, _confirmLabel, _eyebrow;
    private Image    _icon;
    private Button   _confirm, _cancel;
    private float    _shownAt = -1f;

    // ── Properties ───────────────────────────────────────
    public event Action Confirmed;
    public event Action Cancelled;
    public bool IsOpen => _group != null && _group.blocksRaycasts;

    // ── Public Methods ───────────────────────────────────

    /// <summary>해금 영역 전체를 덮는 확인 층을 짓는다(꺼진 채). 바깥(어두운 막)을 누르면 취소.</summary>
    public static AltarConfirmCard Create(RectTransform area)
    {
        var go = new GameObject("AltarConfirm", typeof(RectTransform), typeof(CanvasGroup), typeof(Canvas), typeof(GraphicRaycaster));
        var rt = (RectTransform)go.transform;
        rt.SetParent(area, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        // 트리 · 상세 패널 · 배너 위 — 같은 팝업 캔버스 안에서 정렬만 올린다
        var cv = go.GetComponent<Canvas>();
        cv.overrideSorting = true;
        var parentCanvas = area.GetComponentInParent<Canvas>();
        cv.sortingOrder = (parentCanvas != null ? parentCanvas.sortingOrder : 0) + 5;
        var card = go.AddComponent<AltarConfirmCard>();
        card.Build(rt);
        card.SetOpen(false, instant: true);
        return card;
    }

    /// <summary>고른 노드의 확인 카드를 띄운다 — 살 수 있을 때만 부른다(<see cref="AltarDetailModel.ButtonEnabled"/>).</summary>
    public void Show(in AltarDetailModel m, int essence)
    {
        _header.text = string.IsNullOrEmpty(m.LevelLine) ? m.Header ?? "" : $"{m.Header} · {m.LevelLine}";
        _eyebrow.text = string.IsNullOrEmpty(m.LevelLine) ? "해금하기 — 정수를 써서 영구히 연다" : $"강화하기 — {m.LevelLine}";
        _name.text   = m.Name ?? "";
        _effect.text = UIKeywordInk.Values(m.Effect ?? "");   // 수치만 Noto 500(10-15 설명 글 강조)
        int after    = Mathf.Max(0, essence - m.Cost);
        _cost.text   = $"정수 <color=#FFCC4D>◆ {m.Cost:N0}</color> 사용     →     남는 정수 ◆ {after:N0}";
        _condition.text = m.Condition ?? "";
        _next.text   = string.IsNullOrEmpty(m.NextNames) ? "" : "이어서 열 수 있는 것 · " + m.NextNames;
        _confirmLabel.text = string.IsNullOrEmpty(m.LevelLine) ? $"해금한다   ◆ {m.Cost:N0}" : $"강화한다   ◆ {m.Cost:N0}";

        var icon = UISkin.Achievement != null ? UISkin.Achievement.NodeIcon(m.NodeId) : null;
        _icon.sprite  = icon;
        _icon.enabled = icon != null;

        SetOpen(true, instant: false);
        _shownAt = Time.unscaledTime;
        // 패드 · 키보드 — 확인 버튼에 초점(Enter = 해금한다). 마우스는 같은 클릭이 연 직후의 눌림을 거른다(OnConfirm).
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_confirm.gameObject);
    }

    public void Hide() => SetOpen(false, instant: false);

    // ── Private Methods ──────────────────────────────────

    private void Build(RectTransform root)
    {
        _group = root.GetComponent<CanvasGroup>();

        var dim = NewImage(root, "Dim", Dim);
        Stretch(dim.rectTransform);
        dim.raycastTarget = true;
        var dimBtn = dim.gameObject.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.navigation = new Navigation { mode = Navigation.Mode.None };
        dimBtn.onClick.AddListener(OnCancel);

        var card = NewImage(root, "Card", UITheme.Glass);
        var crt  = card.rectTransform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(CardW, CardH);
        UITheme.StylePanel(card, UITheme.Window, UITheme.GoldLine, 16f);
        card.raycastTarget = true;   // 카드 위 클릭은 막(취소)으로 새지 않는다

        _eyebrow = Text(crt, "Eyebrow", UIWeight.Medium, UITheme.Thin.Lead, UITheme.Thin.Gold, TextAlignmentOptions.MidlineLeft);
        TopLeft(_eyebrow.rectTransform, Pad, 28f, CardW - Pad * 2f, 28f);
        _eyebrow.text = "해금하기 — 정수를 써서 영구히 연다";

        _icon = NewImage(crt, "Icon", Color.white);
        _icon.preserveAspect = true;
        TopLeft(_icon.rectTransform, Pad, 72f, IconPx, IconPx);

        float tx = Pad + IconPx + 20f;
        _header = Text(crt, "Header", UIWeight.Regular, UITheme.Thin.Body, UITheme.Thin.InkA(0.62f), TextAlignmentOptions.MidlineLeft);
        TopLeft(_header.rectTransform, tx, 78f, CardW - tx - Pad, 26f);
        _name = Text(crt, "Name", UIWeight.Medium, UITheme.Thin.H2, UITheme.Thin.Ink, TextAlignmentOptions.MidlineLeft);
        TopLeft(_name.rectTransform, tx, 108f, CardW - tx - Pad, 44f);

        Rule(crt, 178f);
        var what = Text(crt, "WhatLabel", UIWeight.Medium, UITheme.Thin.Body, UITheme.Thin.InkA(0.62f), TextAlignmentOptions.MidlineLeft);
        TopLeft(what.rectTransform, Pad, 194f, 300f, 24f);
        what.text = "무엇이 바뀌나";
        _effect = Text(crt, "Effect", UIWeight.Regular, UITheme.Thin.Lead, UITheme.Thin.Ink, TextAlignmentOptions.TopLeft);
        _effect.textWrappingMode = TextWrappingModes.Normal;
        _effect.enableAutoSizing = true;
        _effect.fontSizeMax = UITheme.Thin.Lead;
        _effect.fontSizeMin = 17f;
        TopLeft(_effect.rectTransform, Pad, 224f, CardW - Pad * 2f, 84f);

        Rule(crt, 318f);
        _cost = Text(crt, "Cost", UIWeight.Medium, UITheme.Thin.Lead, UITheme.Thin.Ink, TextAlignmentOptions.MidlineLeft);
        TopLeft(_cost.rectTransform, Pad, 332f, CardW - Pad * 2f, 30f);
        _condition = Text(crt, "Condition", UIWeight.Regular, UITheme.Thin.Body, UITheme.Thin.InkA(0.8f), TextAlignmentOptions.MidlineLeft);
        TopLeft(_condition.rectTransform, Pad, 364f, CardW - Pad * 2f, 26f);
        _next = Text(crt, "Next", UIWeight.Regular, UITheme.Thin.Body, InfoColor, TextAlignmentOptions.MidlineLeft);
        TopLeft(_next.rectTransform, Pad, 390f, CardW - Pad * 2f, 26f);

        // 버튼 — 오른쪽 = 지금 누를 것(금 · 해금한다), 왼쪽 = 취소
        _cancel = MakeButton(crt, "Cancel", "취소", UITheme.SecondaryTint, UITheme.Thin.Ink, new Vector2(Pad, 28f), 220f);
        _cancel.onClick.AddListener(OnCancel);
        _confirm = MakeButton(crt, "Confirm", "해금한다", UITheme.CtaTint, UITheme.Thin.Ink, new Vector2(Pad + 240f, 28f), CardW - Pad * 2f - 240f);
        _confirmLabel = _confirm.GetComponentInChildren<TMP_Text>(true);
        _confirm.onClick.AddListener(OnConfirm);
        UIAffordGlow.Set(_confirm.targetGraphic, true);
    }

    private void SetOpen(bool on, bool instant)
    {
        _group.blocksRaycasts = on;
        _group.interactable   = on;
        if (instant) { _group.alpha = on ? 1f : 0f; return; }
        FadeAsync(_group.alpha, on ? 1f : 0f, this.GetCancellationTokenOnDestroy()).Forget();
    }

    private async UniTaskVoid FadeAsync(float from, float to, CancellationToken ct)
    {
        try { await UIMotion.Tween(FadeSec, k => _group.alpha = Mathf.Lerp(from, to, k), ct); }
        catch (OperationCanceledException) { }
    }

    private Button MakeButton(RectTransform parent, string name, string label, Color tint, Color ink, Vector2 bottomLeft, float width)
    {
        var img = NewImage(parent, name, Color.white);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.anchoredPosition = bottomLeft;
        rt.sizeDelta = new Vector2(width, BtnH);
        UITheme.StyleButton(img, tint);
        img.raycastTarget = true;
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        ShopUIStyle.ApplyButtonColors(btn, img);
        var t = Text(rt, "Label", UIWeight.Medium, UITheme.Thin.Lead, ink, TextAlignmentOptions.Center);
        Stretch(t.rectTransform);
        t.text = label;
        return btn;
    }

    private void Rule(RectTransform parent, float top)
    {
        var r = NewImage(parent, "Rule", UITheme.Thin.InkA(0.12f));
        TopLeft(r.rectTransform, Pad, top, CardW - Pad * 2f, 1f);
    }

    private static TMP_Text Text(RectTransform parent, string name, UIWeight weight, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        UITheme.StyleThinText(t, weight, size, color, shadow: true);
        t.alignment = align;
        t.richText = true;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    private static Image NewImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static void TopLeft(RectTransform rt, float x, float top, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -top);
        rt.sizeDelta = new Vector2(w, h);
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // ── Event Handlers ───────────────────────────────────

    /// <summary>[해금한다] — 카드가 뜬 직후 0.25초의 눌림(카드를 연 그 클릭 · 연타)은 받지 않는다.</summary>
    private void OnConfirm()
    {
        if (!IsOpen || Time.unscaledTime - _shownAt < 0.25f) return;
        Hide();
        Confirmed?.Invoke();
    }

    private void OnCancel()
    {
        if (!IsOpen) return;
        Hide();
        Cancelled?.Invoke();
    }
}
