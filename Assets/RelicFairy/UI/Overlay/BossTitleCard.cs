using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보스 「이름 + 칭호」 카드(10-14 보스 연출 몰입 리서치 권장 1 · 84 요청) — 등장 클로즈업 순간 화면 아래 3분의 1에 2~3초.
/// 칭호가 시기(봉인기 · 해방기 · 악몽)를 알린다(Risk of Rain 2 Mithrix 부제 · Enter the Gungeon 인트로 카드).
/// 톤 v2 얇은 금선: 칭호(금 20) · 이름(Light 76) · 가운데 마름모가 있는 금선 한 줄 · 뒤는 좌우가 흐린 먹색 띠.
/// 등장 자막(UI_BossBark BossIntro · 화면 가운데 -160)보다 아래에 앉고, 레터박스 위에 그린다.
/// 씬 오브젝트라 씬이 바뀌면 같이 사라진다. 실시간 시계(시간정지에도 흐른다).
/// 호출: <c>BossTitleCard.Show("숲의 수호자", "봉인의 숲", 2.6f);</c> · 연출을 건너뛰면 <c>BossTitleCard.Hide();</c>
/// </summary>
public sealed class BossTitleCard : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────────────
    private const int   SortingOrder     = UISortingOrder.BossTitle;
    private const float CenterFromBottom = 212f;    // 1080 기준 — 금선 152 ~ 칭호 위 끝 ≈ 290 · 레터박스 아래 띠(16:9에서 138) 위 · 등장 자막 두 줄 아래 끝(≈ 330) 아래
    private const float CardWidth        = 1100f;
    private const float CardHeight       = 200f;
    private const float BandHeight       = 300f;    // 띠 그림은 위아래 바깥 55%가 흐려진다 — 칭호(+66) · 금선(−60)이 짙은 가운데에 들게
    private const float BandAlpha        = 0.8f;
    private const float TitleY           = 66f;
    private const float RuleY            = -60f;
    private const float RuleWidth        = 520f;
    private const float DiamondSize      = 8f;
    private const float NameSpacingFrom  = 28f;     // 이름 자간이 넓게 퍼졌다가 모인다
    private const float NameSpacingTo    = 8f;
    private const float TitleRise        = 8f;
    private const float MinSeconds       = 1.2f;
    private const float FadeInTime       = 0.3f;
    private const float FadeOutTime      = 0.5f;
    private const float HideTime         = 0.25f;

    // ── Static ───────────────────────────────────────────────────────
    private static BossTitleCard _instance;

    // ── Private ──────────────────────────────────────────────────────
    private CanvasGroup   _group;
    private RectTransform _rule;
    private Image         _diamond;
    private TMP_Text      _name;
    private TMP_Text      _title;
    private float         _start;
    private float         _hideAt;
    private float         _fadeOut = FadeOutTime;

    // ── Lifecycle ────────────────────────────────────────────────────
    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // ── Public Methods ───────────────────────────────────────────────
    /// <summary>카드를 띄운다. 떠 있던 카드는 바로 바뀐다. 칭호가 비면 이름만.</summary>
    public static void Show(string bossName, string title, float seconds = 2.6f)
    {
        if (string.IsNullOrEmpty(bossName)) return;
        if (_instance != null) Destroy(_instance.gameObject);
        var card = Create(bossName, title);
        card._start  = Time.unscaledTime;
        card._hideAt = card._start + Mathf.Max(MinSeconds, seconds) - FadeOutTime;
        card.PlayAsync(card.destroyCancellationToken).Forget();
    }

    /// <summary>짧게 페이드 아웃하고 걷는다(연출 건너뛰기).</summary>
    public static void Hide()
    {
        if (_instance == null) return;
        _instance._hideAt  = Mathf.Min(_instance._hideAt, Time.unscaledTime);
        _instance._fadeOut = Mathf.Min(_instance._fadeOut, HideTime);
    }

    // ── Private Methods ──────────────────────────────────────────────
    private async UniTaskVoid PlayAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                float now = Time.unscaledTime;
                float t   = now - _start;
                float out01 = Mathf.Clamp01((now - _hideAt) / _fadeOut);
                _group.alpha = Mathf.Min(Mathf.Clamp01(t / FadeInTime), 1f - out01);

                // 금선이 가운데서 양옆으로 그어지고 → 마름모 · 이름(자간이 모인다) → 칭호가 살짝 올라앉는다
                _rule.sizeDelta = new Vector2(RuleWidth * EaseOut(Mathf.Clamp01(t / 0.4f)), _rule.sizeDelta.y);
                _diamond.color  = UITheme.Thin.WithA(UITheme.Thin.Gold, Mathf.Clamp01((t - 0.25f) / 0.2f));
                float n = Mathf.Clamp01((t - 0.1f) / 0.6f);
                _name.alpha            = Mathf.Clamp01((t - 0.1f) / 0.5f);
                _name.characterSpacing = Mathf.Lerp(NameSpacingFrom, NameSpacingTo, EaseOut(n));
                if (_title.gameObject.activeSelf)
                {
                    float k = Mathf.Clamp01((t - 0.3f) / 0.4f);
                    _title.alpha = k;
                    _title.rectTransform.anchoredPosition = new Vector2(0f, TitleY - TitleRise * (1f - EaseOut(k)));
                }

                if (out01 >= 1f) break;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { return; }
        Destroy(gameObject);
    }

    private static BossTitleCard Create(string bossName, string title)
    {
        var go     = new GameObject("@BossTitleCard");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        // 프로젝트 캔버스 기준 — 1920×1080, Scale With Screen Size, Match 0.5
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        var card = go.AddComponent<BossTitleCard>();
        card._group = go.AddComponent<CanvasGroup>();
        card._group.alpha          = 0f;
        card._group.interactable   = false;
        card._group.blocksRaycasts = false;

        var root = NewRect("Card", go.transform);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
        root.sizeDelta        = new Vector2(CardWidth, CardHeight);
        root.anchoredPosition = new Vector2(0f, CenterFromBottom);

        // 밝은 장면(화룡 불길 · 숲 아레나 베이지)에서도 읽히게 — 대사 자막과 같은 좌우 · 위아래가 흐린 띠.
        // 카드 높이(200)에 맞추면 칭호 줄이 흐린 곳에 걸려 숲 아레나에서 칭호가 거의 안 보였다(10-14 5회차) → 띠만 키 300 · 알파 0.8.
        var band = NewImage("Band", root, UITheme.SoftBand, new Color(0.02f, 0.02f, 0.04f, BandAlpha));
        band.rectTransform.sizeDelta = new Vector2(CardWidth + 200f, BandHeight);

        card._title = NewText("Title", root, title, UIWeight.Medium, UITheme.Thin.Lead, UITheme.Thin.Gold, TitleY, shadow: true);
        card._title.characterSpacing = 10f;
        card._title.gameObject.SetActive(!string.IsNullOrEmpty(title));

        // 그림자 없음 — 76 크기에선 부드러운 그림자가 글자 칸 여백을 넘어 모서리에 어두운 네모가 남았다(10-14 촬영). 뒤 띠가 대비를 맡는다.
        card._name = NewText("Name", root, bossName, UIWeight.Light, UITheme.Thin.Display, UITheme.Thin.Ink, 0f, shadow: false);
        card._name.enableAutoSizing = true;     // 긴 이름은 H1까지 줄인다
        card._name.fontSizeMin      = UITheme.Thin.H1;
        card._name.fontSizeMax      = UITheme.Thin.Display;

        float stroke = UITheme.Thin.PixelStroke(UITheme.Thin.CanvasScale(Screen.width, Screen.height));
        var rule = NewImage("Rule", root, null, UITheme.Thin.LineReady);   // 민 선 — 흐린 띠 그림을 1px로 줄이면 흐려져 회색으로 보였다
        card._rule = rule.rectTransform;
        card._rule.sizeDelta        = new Vector2(0f, stroke);
        card._rule.anchoredPosition = new Vector2(0f, RuleY);

        card._diamond = NewImage("Diamond", root, null, UITheme.Thin.WithA(UITheme.Thin.Gold, 0f));
        var dr = card._diamond.rectTransform;
        dr.sizeDelta        = new Vector2(DiamondSize, DiamondSize);
        dr.anchoredPosition = new Vector2(0f, RuleY);
        dr.localRotation    = Quaternion.Euler(0f, 0f, 45f);

        _instance = card;
        return card;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
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

    private static TMP_Text NewText(string name, Transform parent, string text, UIWeight weight, float size, Color color, float y, bool shadow)
    {
        var rt = NewRect(name, parent);
        rt.sizeDelta        = new Vector2(CardWidth - 100f, size * 1.6f);   // 1.4배면 Light 76의 줄 높이가 넘쳐 자동 크기가 73으로 줄었다
        rt.anchoredPosition = new Vector2(0f, y);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.alignment        = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget    = false;
        t.text             = text ?? string.Empty;
        UITheme.StyleThinText(t, weight, size, color, shadow);
        t.alpha = 0f;
        return t;
    }

    private static float EaseOut(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
}
