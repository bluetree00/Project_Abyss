using System;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 기억의 제단 오른쪽 상세 패널(09-29 개편) — 고른 노드의 이름 · 크기 등급 · 변화(A → B) · 값(할인 취소선) · 조건 진척 ·
/// 못 사는 이유 한 줄 · 해금 버튼. 누를 수 있을 때만 버튼에 불(<see cref="UIAffordGlow"/>).
/// <para><b>10-11 글자(v2)</b> — Noto 300/400/500만(가짜 굵게 없음). 효과 문구는 금이 아니라 <b>잉크</b>(금 = 누를 것)이고
/// 수치만 Noto 500으로 세운다. 크기 단계 15 · 17 · 20 · 24.</para>
/// <para><b>10-13 Artsystack</b> — 판 = description_box_05 남색 재색칠(글래스 위에 덮고 금 테는 걷는다) · 구분선 = Line 1 ·
/// 버튼 위 빈 가운데 = 고른 노드 아이콘 96 잉크 15%. 그림이 없으면 지금 모양.</para>
/// </summary>
[DisallowMultipleComponent]
public sealed class AltarDetailPanel : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────
    private const float Pad       = 26f;
    private const float SwapSec   = 0.12f;
    private const float BigIcon   = 96f;    // 빈 가운데 큰 아이콘(10-13)
    private const float MidTop    = 360f;   // 못 사는 이유 줄 아래(310 + 50)
    private const float MidBottom = 116f;   // 해금 버튼 위(52 + 64)

    // ── Static ───────────────────────────────────────────
    private static readonly Color WarnColor = new Color(0.93f, 0.62f, 0.55f, 1f);   // 못 사는 이유
    private static readonly Color InfoColor = new Color(0.80f, 0.76f, 0.88f, 1f);   // 안내(해금된 노드)
    // 효과 문구의 수치(부호 · 곱 기호 · 단위 포함). 앞 글자가 낱말이면 그 안의 숫자라 건드리지 않는다(UIKeywordInk와 같은 규칙).
    private static readonly Regex s_number = new(@"(?<![\w#])([+\-−×]?\d+(?:\.\d+)?(?:%p|%|칸|장|종|절|개|회|초)?)", RegexOptions.Compiled);
    private static string s_mediumOpen;    // 수치를 Noto 500으로 바꾸는 여는 태그(글꼴을 TMP에 올린 뒤에 만든다)
    private static bool   s_mediumBroken;  // 태그가 글자로 찍혔다 — 이번 실행 동안은 수치도 400으로 둔다

    // ── Private ──────────────────────────────────────────
    private RectTransform _rt;
    private CanvasGroup   _content;
    private TMP_Text _header, _name, _badge, _effect, _cost, _condition, _reason, _hint, _buttonLabel;
    private Image    _badgeBg, _buttonImg, _bigIcon;
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
        ApplyPanelArt(bg);
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

        _header    = Line(crt, "Header",    15f, UIWeight.Medium,  UITheme.Mute,  24f,  22f);
        _name      = Line(crt, "Name",      24f, UIWeight.Medium,  UITheme.Ink,   50f,  36f);
        _badgeBg   = new GameObject("BadgeBg", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
        _badgeBg.transform.SetParent(crt, false);
        _badgeBg.sprite = UIProceduralSprites.RoundedRect(10f, 1.5f); _badgeBg.type = Image.Type.Sliced;
        // 크기 등급은 정보다 — 금(누를 것)이 아니라 잉크 바탕 · 잉크 글자(10-11 v2 강조색)
        _badgeBg.color = UITheme.Thin.InkA(0.08f); _badgeBg.raycastTarget = false;
        TopLeft(_badgeBg.rectTransform, Pad, 94f, 90f, 24f);
        _badge     = Line(crt, "Badge",     15f, UIWeight.Medium,  UITheme.Thin.InkA(0.8f), 94f, 24f);
        _badge.alignment = TextAlignmentOptions.Center;
        // 효과 문구 — 20 Bold 금 → Noto 400 20 잉크(금 = 누를 것). 수치만 500(SetEffect). 두 줄(29px × 2)이 들어가게 60.
        _effect    = Line(crt, "Effect",    20f, UIWeight.Regular, UITheme.Ink,   132f, 60f);
        _effect.textWrappingMode = TextWrappingModes.Normal;

        var rule = new GameObject("Rule", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
        rule.transform.SetParent(crt, false);
        rule.color = new Color(1f, 1f, 1f, 0.08f); rule.raycastTarget = false;
        TopStretch(rule.rectTransform, 200f, 1f);
        var ruleArt = UISkin.Achievement != null ? UISkin.Achievement.AltarDetailRule : null;
        if (ruleArt != null)
        {
            // 붓 결 선(Line 1) — 무채 + 잉크 35%. 획 가운데가 예전 1px 선 자리(200)에 오게
            rule.sprite = UISpriteMaster.Neutral(ruleArt);
            rule.color  = UITheme.Thin.InkA(0.35f);
            TopStretch(rule.rectTransform, 197f, 8f);
        }

        // 빈 가운데(이유 줄 아래 ~ 버튼 위) — 고른 노드 아이콘을 흐리게. 판이 낮으면 함께 줄어든다(preserveAspect).
        _bigIcon = new GameObject("BigIcon", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
        _bigIcon.transform.SetParent(crt, false);
        var irt = _bigIcon.rectTransform;
        irt.anchorMin = new Vector2(0.5f, 0f); irt.anchorMax = new Vector2(0.5f, 1f); irt.pivot = new Vector2(0.5f, 0.5f);
        irt.offsetMin = new Vector2(-BigIcon * 0.5f, MidBottom); irt.offsetMax = new Vector2(BigIcon * 0.5f, -MidTop);
        _bigIcon.preserveAspect = true; _bigIcon.raycastTarget = false;
        _bigIcon.color = UITheme.Thin.InkA(0.15f);
        _bigIcon.enabled = false;

        _cost      = Line(crt, "Cost",      20f, UIWeight.Medium,  UITheme.Ink,   216f, 30f);
        _condition = Line(crt, "Condition", 17f, UIWeight.Regular, UITheme.Ink,   254f, 50f);
        _condition.textWrappingMode = TextWrappingModes.Normal;
        _reason    = Line(crt, "Reason",    17f, UIWeight.Regular, new Color(0.93f, 0.62f, 0.55f, 1f), 310f, 50f);
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
        // 버튼 글자는 갈색 판 위라 그림자를 남긴다(Noto 500 20 — 가짜 굵게 없음)
        UITheme.StyleThinText(_buttonLabel, UIWeight.Medium, 20f, UITheme.Ink, shadow: true);
        _buttonLabel.alignment = TextAlignmentOptions.Center; _buttonLabel.raycastTarget = false;

        _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        _hint.transform.SetParent(crt, false);
        var hrt = _hint.rectTransform;
        hrt.anchorMin = new Vector2(0f, 0f); hrt.anchorMax = new Vector2(1f, 0f); hrt.pivot = new Vector2(0.5f, 0f);
        hrt.offsetMin = new Vector2(Pad, 18f); hrt.offsetMax = new Vector2(-Pad, 18f + 24f);
        UITheme.StyleThinText(_hint, UIWeight.Regular, 15f, UITheme.Mute);
        _hint.alignment = TextAlignmentOptions.Center; _hint.raycastTarget = false;
        _hint.text = "누르면 확인 창이 뜬다 — 바로 쓰이지 않는다";
    }

    private void Fill(in AltarDetailModel m)
    {
        _header.text    = m.Header ?? "";
        _name.text      = m.Name ?? "";
        SetEffect(m.Effect ?? "");
        _cost.text      = m.CostLine ?? "";
        _condition.text = m.Condition ?? "";
        _reason.text    = m.Reason ?? "";
        _reason.color   = m.ReasonInfo ? InfoColor : WarnColor;

        var icon = UISkin.Achievement != null ? UISkin.Achievement.NodeIcon(m.NodeId) : null;
        _bigIcon.sprite  = icon;
        _bigIcon.enabled = icon != null;

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

    /// <summary>
    /// 효과 문구 — 본문 Noto 400 · 수치만 Noto 500(TMP &lt;font&gt; 태그). 글꼴이 TMP에 올라가 있지 않으면 태그가 글자로 찍히므로,
    /// 한 번 그려 보고(파싱 결과에 태그가 남았는지) 남았으면 그 실행 동안은 400 한 벌로 둔다.
    /// </summary>
    private void SetEffect(string text)
    {
        _effect.text = text;
        if (text.Length == 0 || s_mediumBroken || !EnsureMediumTag()) return;
        string tagged = s_number.Replace(text, mt => s_mediumOpen + mt.Value + "</font>");
        if (tagged == text) return;
        _effect.text = tagged;
        _effect.ForceMeshUpdate(true, true);
        if (_effect.GetParsedText().Contains("<font"))
        {
            s_mediumBroken = true;
            _effect.text = text;
            Debug.LogWarning("[AltarDetailPanel] 수치 굵기 태그가 먹지 않아 효과 문구를 한 굵기로 둔다.");
        }
    }

    /// <summary>Noto 500을 TMP 글꼴 목록에 올리고 여는 태그를 만든다. 글꼴이 아직 안 올라왔으면 false(다음에 다시).</summary>
    private static bool EnsureMediumTag()
    {
        if (s_mediumOpen != null) return true;
        var medium = UISkin.ThemeFonts != null ? UISkin.ThemeFonts.Medium : null;
        if (medium == null) return false;
        MaterialReferenceManager.AddFontAsset(medium);
        s_mediumOpen = $"<font=\"{medium.name}\">";
        return true;
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

    /// <summary>
    /// 판 그림(description_box_05 재색칠) — 안쪽 알파가 0.27이라 판에 바로 입히면 비친다. 글래스 판은 두고 그 위에 덮는다.
    /// 그림이 자기 가는 테를 가져 금 테는 걷는다(금 = 누를 것). 그림이 없으면 지금 글래스 + 금 테.
    /// </summary>
    private static void ApplyPanelArt(Image bg)
    {
        var art = UISkin.Achievement != null ? UISkin.Achievement.AltarDetailPanel : null;
        if (art == null) return;
        var img = new GameObject("Art", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
        var rt  = img.rectTransform;
        rt.SetParent(bg.transform, false);
        rt.SetAsFirstSibling();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        img.raycastTarget = false;
        ShopUIStyle.Skin(img, art, sliced: true);
        var edge = bg.transform.Find("Edge");
        if (edge != null) edge.gameObject.SetActive(false);
    }

    /// <summary>판 위 한 줄 — Noto(굵기 글꼴) · 가짜 굵게 · 그림자 없음(10-11 v2). 넘치면 15까지 줄어든다.</summary>
    private static TMP_Text Line(RectTransform parent, string name, float size, UIWeight weight, Color color, float top, float height)
    {
        var t = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        t.transform.SetParent(parent, false);
        UITheme.StyleThinText(t, weight, size, color);
        t.raycastTarget = false; t.richText = true;
        t.alignment = TextAlignmentOptions.TopLeft;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.enableAutoSizing = true; t.fontSizeMax = size; t.fontSizeMin = Mathf.Min(size, 15f);
        TopStretch(t.rectTransform, top, height);
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
