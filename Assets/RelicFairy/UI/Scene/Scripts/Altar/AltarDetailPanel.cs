using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 기억의 제단 오른쪽 상세 패널(09-29 개편) — 고른 노드의 이름 · 크기 등급 · 변화(A → B) · 값(할인 취소선) · 조건 진척 ·
/// 못 사는 이유 한 줄 · 해금 버튼. 누를 수 있을 때만 버튼에 불(<see cref="UIAffordGlow"/>).
/// </summary>
[DisallowMultipleComponent]
public sealed class AltarDetailPanel : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────
    private const float Pad       = 26f;
    private const float SwapSec   = 0.12f;

    // ── Static ───────────────────────────────────────────
    private static readonly Color WarnColor = new Color(0.93f, 0.62f, 0.55f, 1f);   // 못 사는 이유
    private static readonly Color InfoColor = new Color(0.80f, 0.76f, 0.88f, 1f);   // 안내(해금된 노드)

    // ── Private ──────────────────────────────────────────
    private RectTransform _rt;
    private CanvasGroup   _content;
    private TMP_Text _header, _name, _badge, _effect, _cost, _condition, _reason, _hint, _buttonLabel;
    private Image    _badgeBg, _buttonImg;
    private Button   _button;
    private string   _shownName;
    private CancellationTokenSource _swapCts;

    // ── Properties ───────────────────────────────────────
    public event Action ActionClicked;

    // ── Lifecycle ────────────────────────────────────────
    private void OnDestroy()
    {
        _swapCts?.Cancel();
        _swapCts?.Dispose();
    }

    // ── Public Methods ───────────────────────────────────

    public static AltarDetailPanel Create(RectTransform parent, float width, float rightMargin)
    {
        var go = new GameObject("AltarDetail", typeof(RectTransform), typeof(CanvasRenderer));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(width, -8f);
        rt.anchoredPosition = new Vector2(-rightMargin, 0f);
        var bg = go.AddComponent<Image>();
        UITheme.StylePanel(bg, UITheme.Glass, UITheme.GoldLine, 14f);
        var p = go.AddComponent<AltarDetailPanel>();
        p.Build();
        return p;
    }

    /// <summary>
    /// 내용을 바꾼다. 다른 노드로 넘어갈 때만 0.12초 교차 페이드(같은 노드 갱신은 즉시 — 숫자가 깜빡이지 않게).
    /// </summary>
    public void Show(in AltarDetailModel m)
    {
        bool swap = _shownName != null && _shownName != m.Name;
        _shownName = m.Name;
        if (swap) SwapAsync(m).Forget();
        else Fill(m);
    }

    // ── Private Methods ──────────────────────────────────

    private void Build()
    {
        _rt = (RectTransform)transform;
        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(_rt, false);
        var crt = (RectTransform)contentGo.transform;
        crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one; crt.offsetMin = crt.offsetMax = Vector2.zero;
        _content = contentGo.AddComponent<CanvasGroup>();

        _header    = Line(crt, "Header",    16f, FontStyles.Normal, UITheme.Mute,  24f,  22f);
        _name      = Line(crt, "Name",      26f, FontStyles.Bold,   UITheme.Ink,   50f,  36f);
        _badgeBg   = new GameObject("BadgeBg", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
        _badgeBg.transform.SetParent(crt, false);
        _badgeBg.sprite = UIProceduralSprites.RoundedRect(10f, 1.5f); _badgeBg.type = Image.Type.Sliced;
        _badgeBg.color = new Color(0.91f, 0.73f, 0.33f, 0.16f); _badgeBg.raycastTarget = false;
        TopLeft(_badgeBg.rectTransform, Pad, 94f, 90f, 24f);
        _badge     = Line(crt, "Badge",     16f, FontStyles.Bold,   UITheme.Gold,  94f,  24f);
        _badge.alignment = TextAlignmentOptions.Center;
        _effect    = Line(crt, "Effect",    20f, FontStyles.Bold,   UITheme.Gold,  132f, 56f);
        _effect.textWrappingMode = TextWrappingModes.Normal;

        var rule = new GameObject("Rule", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
        rule.transform.SetParent(crt, false);
        rule.color = new Color(1f, 1f, 1f, 0.08f); rule.raycastTarget = false;
        TopStretch(rule.rectTransform, 200f, 1f);

        _cost      = Line(crt, "Cost",      22f, FontStyles.Bold,   UITheme.Ink,   216f, 30f);
        _condition = Line(crt, "Condition", 16f, FontStyles.Normal, UITheme.Ink,   254f, 48f);
        _condition.textWrappingMode = TextWrappingModes.Normal;
        _reason    = Line(crt, "Reason",    16f, FontStyles.Normal, new Color(0.93f, 0.62f, 0.55f, 1f), 310f, 48f);
        _reason.textWrappingMode = TextWrappingModes.Normal;

        // 해금 버튼(아래) + 안내 한 줄
        var btnGo = new GameObject("Unlock", typeof(RectTransform), typeof(CanvasRenderer));
        btnGo.transform.SetParent(crt, false);
        var brt = (RectTransform)btnGo.transform;
        brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(1f, 0f); brt.pivot = new Vector2(0.5f, 0f);
        brt.offsetMin = new Vector2(Pad, 52f); brt.offsetMax = new Vector2(-Pad, 52f + 64f);
        _buttonImg = btnGo.AddComponent<Image>();
        _button    = btnGo.AddComponent<Button>();
        _button.targetGraphic = _buttonImg;
        _button.onClick.AddListener(() => ActionClicked?.Invoke());
        var lblGo = new GameObject("Label", typeof(RectTransform));
        lblGo.transform.SetParent(brt, false);
        _buttonLabel = lblGo.AddComponent<TextMeshProUGUI>();
        var lrt = _buttonLabel.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        _buttonLabel.fontSize = 22f; _buttonLabel.fontStyle = FontStyles.Bold;
        _buttonLabel.alignment = TextAlignmentOptions.Center; _buttonLabel.raycastTarget = false;
        TMPOutlineHelper.ApplySoftShadow(_buttonLabel);

        _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        _hint.transform.SetParent(crt, false);
        var hrt = _hint.rectTransform;
        hrt.anchorMin = new Vector2(0f, 0f); hrt.anchorMax = new Vector2(1f, 0f); hrt.pivot = new Vector2(0.5f, 0f);
        hrt.offsetMin = new Vector2(Pad, 18f); hrt.offsetMax = new Vector2(-Pad, 18f + 24f);
        _hint.fontSize = 16f; _hint.color = UITheme.Mute; _hint.alignment = TextAlignmentOptions.Center; _hint.raycastTarget = false;
        _hint.text = "노드를 한 번 더 누르면 해금";
    }

    private void Fill(in AltarDetailModel m)
    {
        _header.text    = m.Header ?? "";
        _name.text      = m.Name ?? "";
        _effect.text    = m.Effect ?? "";
        _cost.text      = m.CostLine ?? "";
        _condition.text = m.Condition ?? "";
        _reason.text    = m.Reason ?? "";
        _reason.color   = m.ReasonInfo ? InfoColor : WarnColor;

        bool badge = !string.IsNullOrEmpty(m.SizeBadge);
        _badge.gameObject.SetActive(badge); _badgeBg.enabled = badge;
        if (badge)
        {
            _badge.text = m.SizeBadge;
            float w = _badge.GetPreferredValues(m.SizeBadge).x + 22f;
            TopLeft(_badgeBg.rectTransform, Pad, 94f, w, 24f);
            TopLeft(_badge.rectTransform, Pad, 94f, w, 24f);
        }

        _button.interactable = m.ButtonEnabled;
        UITheme.StyleButton(_buttonImg, m.ButtonEnabled ? UITheme.CtaTint : UITheme.CtaTintOff);
        _buttonLabel.text  = m.ButtonLabel ?? "";
        _buttonLabel.color = m.ButtonEnabled ? UITheme.Ink : UITheme.Mute;
        UIAffordGlow.Set(_buttonImg, m.ButtonEnabled);
        _hint.gameObject.SetActive(m.ButtonEnabled);
    }

    private async UniTaskVoid SwapAsync(AltarDetailModel m)
    {
        _swapCts?.Cancel(); _swapCts?.Dispose();
        _swapCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        var ct = _swapCts.Token;
        try
        {
            for (float t = 0f; t < SwapSec * 0.5f; t += Time.unscaledDeltaTime)
            {
                _content.alpha = 1f - t / (SwapSec * 0.5f);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            Fill(m);
            for (float t = 0f; t < SwapSec; t += Time.unscaledDeltaTime)
            {
                _content.alpha = t / SwapSec;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally { if (_content != null) _content.alpha = 1f; }
    }

    private static TMP_Text Line(RectTransform parent, string name, float size, FontStyles style, Color color, float top, float height)
    {
        var t = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        t.transform.SetParent(parent, false);
        t.fontSize = size; t.fontStyle = style; t.color = color; t.raycastTarget = false; t.richText = true;
        t.alignment = TextAlignmentOptions.TopLeft;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.enableAutoSizing = true; t.fontSizeMax = size; t.fontSizeMin = Mathf.Min(size, 16f);
        TopStretch(t.rectTransform, top, height);
        TMPOutlineHelper.ApplySoftShadow(t);
        return t;
    }

    private static void TopStretch(RectTransform rt, float top, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(Pad, -top - height); rt.offsetMax = new Vector2(-Pad, -top);
    }

    private static void TopLeft(RectTransform rt, float left, float top, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(left, -top);
        rt.sizeDelta = new Vector2(w, h);
    }
}
