using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 엔딩 카드 + 크레딧 — 검은 화면에 카드 문장을 한 줄씩 띄우고, 크레딧을 올려 보낸다.
/// 런타임 생성(ScreenSpaceOverlay, 프리팹/Addressable 불필요 — UI_ChallengeHud와 같은 패턴).
///
/// 문장은 대사 CSV(DIALOGUE_DATA)의 시퀀스에서 읽는다. 일러스트 칸을 서식 태그로 쓴다:
///   <c>title</c> = 큰 제목 · <c>header</c> = 금색 소제목 · 비움 = 본문.
/// F · Space · Enter · Esc · 클릭으로 지금 단계를 건너뛴다. 시간 배율과 무관하게(unscaled) 흐른다.
/// </summary>
public sealed class EndingCardOverlay : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const float FadeSeconds        = 1.2f;
    private const float CardLineSeconds    = 3.2f;
    private const float CardLineFade       = 0.6f;
    private const float CreditLineHeight   = 64f;
    private const float CardBodySize       = 44f;
    private const float HeaderSize         = 40f;   // 크레딧 본문(34)보다 커야 소제목으로 읽힌다
    private const float CreditBodySize     = 34f;
    private const float CreditScrollSpeed  = 80f;    // px/s (1080 기준)
    private const float CreditsTailSeconds = 1.5f;
    private const float SkipGuardSeconds   = 0.8f;   // 대사창을 넘기던 입력이 곧바로 건너뛰지 않게
    private const float ScreenHeight       = 1080f;
    private const string TitleTag          = "title";
    private const string HeaderTag         = "header";

    private static readonly Color BodyColor = new Color(0.92f, 0.92f, 0.95f);

    // ── Private ───────────────────────────────────────────────────
    private CanvasGroup     _group;
    private TextMeshProUGUI _card;
    private RectTransform   _credits;
    private float           _phaseStart;

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>카드 → 크레딧을 튼다. 끝나면 화면을 검게 덮은 채(ScreenFade) 오버레이를 거둔다 — 뒤이은 귀환 연출이 이어받는다.</summary>
    public static async UniTask PlayAsync(DialogueLine[] cardLines, DialogueLine[] creditLines, CancellationToken ct)
    {
        var overlay = Create();
        try
        {
            await overlay.RunAsync(cardLines, creditLines, ct);
            await ScreenFade.Out(0.4f, ct);
        }
        finally
        {
            if (overlay != null) Destroy(overlay.gameObject);
        }
    }

    // ── Private Methods ───────────────────────────────────────────
    private static EndingCardOverlay Create()
    {
        var canvasGO = new GameObject("EndingCardCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = UISortingOrder.Cinematic;

        // 프로젝트 캔버스 기준 준수 — 1920×1080, Scale With Screen Size, Match 0.5
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, ScreenHeight);
        scaler.matchWidthOrHeight  = 0.5f;

        var overlay = canvasGO.AddComponent<EndingCardOverlay>();
        overlay._group = canvasGO.GetComponent<CanvasGroup>();
        overlay._group.alpha          = 0f;
        overlay._group.blocksRaycasts = true;   // 뒤의 UI를 누르지 않게

        var bg = new GameObject("Black", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(canvasGO.transform, false);
        Stretch((RectTransform)bg.transform);
        bg.GetComponent<Image>().color = Color.black;

        overlay._card = MakeText(canvasGO.transform, "Card", CardBodySize, BodyColor);
        var cardRt = overlay._card.rectTransform;
        cardRt.sizeDelta = new Vector2(1500f, 200f);
        overlay._card.textWrappingMode = TextWrappingModes.Normal;

        var credits = new GameObject("Credits", typeof(RectTransform));
        credits.transform.SetParent(canvasGO.transform, false);
        overlay._credits = (RectTransform)credits.transform;
        overlay._credits.anchorMin = overlay._credits.anchorMax = new Vector2(0.5f, 0.5f);
        overlay._credits.pivot     = new Vector2(0.5f, 1f);
        return overlay;
    }

    private async UniTask RunAsync(DialogueLine[] cardLines, DialogueLine[] creditLines, CancellationToken ct)
    {
        await FadeAsync(_group, 1f, FadeSeconds, ct);

        if (cardLines != null && cardLines.Length > 0)
        {
            BeginPhase();
            for (int i = 0; i < cardLines.Length; i++)
            {
                var line = cardLines[i];
                if (line == null || string.IsNullOrEmpty(line.text)) continue;
                ApplyStyle(_card, line.illustrationKey, CardBodySize);
                _card.text  = line.text;
                // 제목 바로 뒤의 소제목은 한 장면으로 — 따로 띄우면 검은 화면에 작은 금색 글자 하나만 남는다.
                if (IsTag(line, TitleTag) && i + 1 < cardLines.Length && IsTag(cardLines[i + 1], HeaderTag))
                    _card.text += $"\n<size={HeaderSize}><color={UIPalette.GoldHex}>{cardLines[++i].text}</color></size>";
                _card.alpha = 0f;
                if (await ShowCardLineAsync(ct)) break;   // 건너뛰면 카드 단계 전체를 넘긴다
            }
            _card.text = string.Empty;
        }

        if (creditLines != null && creditLines.Length > 0)
            await RollCreditsAsync(creditLines, ct);
    }

    /// <summary>카드 한 줄 — 페이드 인 → 유지 → 페이드 아웃. 건너뛰었으면 true.</summary>
    private async UniTask<bool> ShowCardLineAsync(CancellationToken ct)
    {
        float t = 0f;
        while (t < CardLineSeconds)
        {
            if (SkipPressed()) return true;
            t += Time.unscaledDeltaTime;
            float fadeIn  = Mathf.Clamp01(t / CardLineFade);
            float fadeOut = Mathf.Clamp01((CardLineSeconds - t) / CardLineFade);
            _card.alpha = Mathf.Min(fadeIn, fadeOut);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        return false;
    }

    private async UniTask RollCreditsAsync(DialogueLine[] lines, CancellationToken ct)
    {
        int count = 0;
        foreach (var line in lines)
        {
            if (line == null) continue;
            var text = MakeText(_credits, "Line", CreditBodySize, BodyColor);
            ApplyStyle(text, line.illustrationKey, CreditBodySize);
            text.text = line.text;
            var rt = text.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -count * CreditLineHeight);
            count++;
        }

        // 화면 아래에서 시작해 마지막 줄이 화면 위로 빠질 때까지 올린다.
        float totalHeight = count * CreditLineHeight;
        float startY      = -ScreenHeight * 0.5f;
        float endY        = ScreenHeight * 0.5f + totalHeight;
        _credits.anchoredPosition = new Vector2(0f, startY);

        BeginPhase();
        float y = startY;
        while (y < endY)
        {
            if (SkipPressed()) return;
            y += CreditScrollSpeed * Time.unscaledDeltaTime;
            _credits.anchoredPosition = new Vector2(0f, y);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        await UniTask.Delay(TimeSpan.FromSeconds(CreditsTailSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
    }

    private void BeginPhase() => _phaseStart = Time.unscaledTime;

    private bool SkipPressed()
    {
        if (Time.unscaledTime - _phaseStart < SkipGuardSeconds) return false;
        return Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.Space)
            || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Escape)
            || Input.GetMouseButtonDown(0);
    }

    private static bool IsTag(DialogueLine line, string tag)
        => line != null && string.Equals(line.illustrationKey, tag, StringComparison.OrdinalIgnoreCase);

    private static void ApplyStyle(TextMeshProUGUI text, string tag, float bodySize)
    {
        if (string.Equals(tag, TitleTag, StringComparison.OrdinalIgnoreCase))
        {
            text.fontSize = 72f;
            text.color    = Color.white;
        }
        else if (string.Equals(tag, HeaderTag, StringComparison.OrdinalIgnoreCase))
        {
            text.fontSize = HeaderSize;
            text.color    = UIPalette.Gold;
        }
        else
        {
            text.fontSize = bodySize;
            text.color    = BodyColor;
        }
    }

    private static async UniTask FadeAsync(CanvasGroup group, float target, float seconds, CancellationToken ct)
    {
        float start = group.alpha;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(start, target, t / seconds);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        group.alpha = target;
    }

    private static TextMeshProUGUI MakeText(Transform parent, string name, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1400f, CreditLineHeight);

        var t = go.AddComponent<TextMeshProUGUI>();
        var f = TMP_Settings.defaultFontAsset;
        if (f != null) t.font = f;
        t.fontSize         = size;
        t.alignment        = TextAlignmentOptions.Center;
        t.color            = color;
        t.raycastTarget    = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        TMPOutlineHelper.ApplyDefault(t);
        return t;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
