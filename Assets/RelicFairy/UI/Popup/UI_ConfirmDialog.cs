using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 작은 확인 창 — 「게임을 종료하시겠습니까?」 · 요정의 샘처럼 <b>한 번 더 묻고 결정</b>하는 곳에 쓴다(10-16 사용자 빌드 피드백).
/// 자체 화면 캔버스(정렬 높음)라 로비 · 게임 · ESC 메뉴 위 어디서나 맨 위에 뜬다. 코드로 지어 프리팹이 없다.
/// 오른쪽 버튼 = 지금 누를 것(금 · 위험이면 붉은 톤), 왼쪽 = 취소. 바깥(어두운 막) · ESC(<see cref="EscKeyListener"/>)는 취소.
/// 판 · 글은 제단 확인 카드와 같은 톤(Noto 300/400/500 · 잉크 본문 · 얇은 금선).
/// 열려 있는 동안 시간을 멈춘다(ESC 메뉴와 같은 <see cref="TimeScaleArbiter"/> Pause) — 샘 앞에서 「마신다」를 누르는 클릭에 몸이 움직이거나 휘두르지 않게.
/// </summary>
[DisallowMultipleComponent]
public sealed class UI_ConfirmDialog : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────
    private const float CardW     = 640f;
    private const float Pad       = 40f;
    private const float BtnH      = 58f;
    private const float FadeSec   = 0.14f;
    private const float GuardSec  = 0.25f;   // 연 클릭 · 연타가 바로 결정으로 새지 않게
    private const int   SortOrder = 32000;   // 로딩(900) · ESC 메뉴 · 설정보다 위

    // ── Static ───────────────────────────────────────────
    private static readonly Color Dim = new(0.02f, 0.02f, 0.04f, 0.62f);
    private static UI_ConfirmDialog s_open;

    // ── Private ──────────────────────────────────────────
    private CanvasGroup _group;
    private TMP_Text    _title, _body;
    private Button      _confirm, _cancel;
    private Action      _onYes, _onNo, _onAlt;
    private float       _shownAt;
    private bool        _closing;

    // ── Properties ───────────────────────────────────────
    public static bool IsOpen => s_open != null && !s_open._closing;

    // ── Public Methods ───────────────────────────────────

    /// <summary>확인 창을 띄운다. 이미 열려 있으면 그것을 닫고(취소 아님) 새로 연다.
    /// <paramref name="no"/>가 비면 버튼 하나(<paramref name="yes"/> = 「닫기」 같은 알림용 · 무채 버튼)만 가운데에 둔다.</summary>
    /// <param name="body">본문 — 리치 텍스트 · 여러 줄 가능(수치 강조는 부르는 쪽에서 UIKeywordInk).</param>
    /// <param name="danger">결정이 되돌릴 수 없는 일(종료 등)이면 오른쪽 버튼을 붉은 톤으로.</param>
    public static UI_ConfirmDialog Show(string title, string body, string yes, string no, Action onYes, Action onNo = null, bool danger = false)
    {
        if (s_open != null) Destroy(s_open.gameObject);

        var go = new GameObject("@ConfirmDialog", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        DontDestroyOnLoad(go);
        var cv = go.GetComponent<Canvas>();
        cv.renderMode   = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = SortOrder;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        var d = go.AddComponent<UI_ConfirmDialog>();
        d._onYes = onYes;
        d._onNo  = onNo;
        d.Build((RectTransform)go.transform, title, body, yes, no, danger);
        s_open = d;
        d.Open();
        return d;
    }

    /// <summary>
    /// 결정 둘을 나란히 비교하는 창(10-18 쉼터 「체력 회복 / 무기 강화」) — 왼쪽 · 오른쪽 모두 결정(금)이고,
    /// 바깥 · ESC만 「고르지 않고 닫기」다. 키보드 초점은 비운다(Enter 한 번에 어느 쪽도 결정되지 않게).
    /// </summary>
    public static UI_ConfirmDialog ShowChoice(string title, string body, string left, string right, Action onLeft, Action onRight)
    {
        var d = Show(title, body, right, left, onRight);
        d._onAlt = onLeft;
        d._cancel.onClick.RemoveAllListeners();
        d._cancel.onClick.AddListener(d.OnAlt);
        if (d._cancel.targetGraphic is Image img) UITheme.StyleButton(img, UITheme.CtaTint);
        UIAffordGlow.Set(d._cancel.targetGraphic, true);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        return d;
    }

    /// <summary>ESC — 열려 있으면 취소로 닫고 true(ESC를 소비했다).</summary>
    public static bool CloseIfOpen()
    {
        if (!IsOpen) return false;
        s_open.OnCancel();
        return true;
    }

    // ── Lifecycle ────────────────────────────────────────
    private void OnDestroy()
    {
        TimeScaleArbiter.Release(this);
        if (s_open == this) s_open = null;
    }

    // ── Private Methods ──────────────────────────────────

    private void Build(RectTransform root, string title, string body, string yes, string no, bool danger)
    {
        _group = root.GetComponent<CanvasGroup>();
        _group.alpha = 0f;

        var dim = NewImage(root, "Dim", Dim);
        Stretch(dim.rectTransform);
        dim.raycastTarget = true;
        var dimBtn = dim.gameObject.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.navigation = new Navigation { mode = Navigation.Mode.None };
        dimBtn.onClick.AddListener(OnCancel);

        // 본문 높이에 맞춘 카드 — 줄 수만큼 늘어난다(글이 판 밖으로 나가지 않게)
        _body = Text(root, "Body", UIWeight.Regular, UITheme.Thin.Lead, UITheme.Thin.Ink, TextAlignmentOptions.Top);
        _body.textWrappingMode = TextWrappingModes.Normal;
        _body.lineSpacing = 8f;
        _body.text = body ?? "";
        float bodyH = Mathf.Max(30f, _body.GetPreferredValues(_body.text, CardW - Pad * 2f, 0f).y);
        bool hasTitle = !string.IsNullOrEmpty(title);
        float top = hasTitle ? 92f : 48f;
        float cardH = top + bodyH + 36f + BtnH + 32f;

        var card = NewImage(root, "Card", UITheme.Glass);
        var crt  = card.rectTransform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(CardW, cardH);
        UITheme.StylePanel(card, UITheme.Window, UITheme.GoldLine, 16f);
        card.raycastTarget = true;   // 카드 위 클릭은 막(취소)으로 새지 않는다

        if (hasTitle)
        {
            _title = Text(crt, "Title", UIWeight.Medium, UITheme.Thin.Title, UITheme.Thin.Ink, TextAlignmentOptions.Center);
            TopLeft(_title.rectTransform, Pad, 34f, CardW - Pad * 2f, 34f);
            _title.text = title;
            var rule = NewImage(crt, "Rule", UITheme.Thin.InkA(0.12f));
            TopLeft(rule.rectTransform, Pad, 80f, CardW - Pad * 2f, 1f);
        }

        _body.transform.SetParent(crt, false);
        TopLeft(_body.rectTransform, Pad, top, CardW - Pad * 2f, bodyH);

        float half = (CardW - Pad * 2f - 20f) * 0.5f;
        if (string.IsNullOrEmpty(no))
        {
            // 알림 창 — 결정할 것이 없으니 금(지금 할 일) 대신 무채 버튼 하나
            _confirm = MakeButton(crt, "Confirm", yes, UITheme.SecondaryTint, new Vector2((CardW - half) * 0.5f, 32f), half);
            _confirm.onClick.AddListener(OnConfirm);
            return;
        }
        _cancel = MakeButton(crt, "Cancel", no, UITheme.SecondaryTint, new Vector2(Pad, 32f), half);
        _cancel.onClick.AddListener(OnCancel);
        _confirm = MakeButton(crt, "Confirm", yes, danger ? UITheme.DangerTint : UITheme.CtaTint, new Vector2(Pad + half + 20f, 32f), half);
        _confirm.onClick.AddListener(OnConfirm);
        if (!danger) UIAffordGlow.Set(_confirm.targetGraphic, true);
    }

    private void Open()
    {
        _shownAt = Time.unscaledTime;
        TimeScaleArbiter.Acquire(this, 0f, TimeScaleArbiter.Priority.Pause);
        _group.blocksRaycasts = true;
        _group.interactable   = true;
        FadeAsync(0f, 1f, destroyAfter: false).Forget();
        // 키보드 · 패드 — 취소에 초점(Enter 한 번으로 되돌릴 수 없는 결정이 나지 않게)
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject((_cancel != null ? _cancel : _confirm).gameObject);
        Managers.Sound?.PlayUiEvent(SoundEvent.UiOpen);   // 10-29 UI 소리 — 열기(누름 소리였다)
    }

    private void Close()
    {
        if (_closing) return;
        _closing = true;
        TimeScaleArbiter.Release(this);
        _group.blocksRaycasts = false;
        _group.interactable   = false;
        FadeAsync(_group.alpha, 0f, destroyAfter: true).Forget();
    }

    private async UniTaskVoid FadeAsync(float from, float to, bool destroyAfter)
    {
        var ct = this.GetCancellationTokenOnDestroy();
        try { await UIMotion.Tween(FadeSec, k => _group.alpha = Mathf.Lerp(from, to, k), ct); }
        catch (OperationCanceledException) { return; }
        if (destroyAfter && this != null) Destroy(gameObject);
    }

    private Button MakeButton(RectTransform parent, string name, string label, Color tint, Vector2 bottomLeft, float width)
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
        var t = Text(rt, "Label", UIWeight.Medium, UITheme.Thin.Lead, UITheme.Thin.Ink, TextAlignmentOptions.Center);
        Stretch(t.rectTransform);
        t.text = label;
        return btn;
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

    private void OnConfirm()
    {
        if (_closing || Time.unscaledTime - _shownAt < GuardSec) return;
        Managers.Sound?.PlayUiEvent(SoundEvent.UiClick);
        Close();
        _onYes?.Invoke();
    }

    private void OnCancel()
    {
        if (_closing) return;
        Managers.Sound?.PlayUiEvent(SoundEvent.UiClose);   // 10-29 UI 소리 — 취소 · 바깥 · ESC
        Close();
        _onNo?.Invoke();
    }

    /// <summary><see cref="ShowChoice"/>의 왼쪽 결정.</summary>
    private void OnAlt()
    {
        if (_closing || Time.unscaledTime - _shownAt < GuardSec) return;
        Managers.Sound?.PlayUiEvent(SoundEvent.UiClick);
        Close();
        _onAlt?.Invoke();
    }
}
