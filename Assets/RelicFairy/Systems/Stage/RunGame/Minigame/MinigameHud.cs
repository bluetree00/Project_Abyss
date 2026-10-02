using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 이벤트방 놀이 HUD(10-01 설계 §2-1) — 규칙 카드 · 카운트다운 · 진행 띠(시간 막대 · 등급 메달 · 상태 줄 · 라운드 점) · 등급 도장 · 떠오르는 글자.
/// 코드 생성(프리팹 없음, UI_ChallengeHud와 같은 방식). 모든 움직임은 실시간 — 팝업이 시간을 멈춰도 멈추지 않는다.
/// UI 톤 규칙: 튕김 없이(ease-out) · 원색 순간 등장 없이(페이드) · 글자 16px 이상.
/// </summary>
public sealed class MinigameHud : MonoBehaviour
{
    // ── Constants ──────────────────────────────────────────────
    private const float BandTop     = -124f;   // 오른쪽 위 재화 줄(위 74 · 높이 40) 아래 — 메달 · 라운드 점이 재화 줄과 겹쳤다(10-01)
    private const float BandHeight  = 118f;
    private const float BarWidth    = 520f;
    private const float MedalSize   = 84f;
    private const float StampSize   = 210f;
    private const float CardSlide   = 30f;
    private const float CardInSecs  = 0.3f;
    private const float CardOutSecs = 0.25f;
    private const float LowTimeSecs = 5f;

    private static readonly Color PlatinumColor = new Color(0.80f, 0.93f, 1.00f);
    private static readonly Color GoldColor     = new Color(1.00f, 0.82f, 0.30f);
    private static readonly Color SilverColor   = new Color(0.80f, 0.82f, 0.86f);
    private static readonly Color BronzeColor   = new Color(0.82f, 0.55f, 0.32f);
    private static readonly Color FailColor     = new Color(0.45f, 0.45f, 0.48f);
    private static readonly Color LowTimeColor  = new Color(0.90f, 0.30f, 0.25f);

    // ── Private ────────────────────────────────────────────────
    private RectTransform _root;
    private Color         _theme;

    private CanvasGroup     _band;
    private TextMeshProUGUI _bandTitle, _status, _dots;
    private RectTransform   _barFill;
    private Image           _barFillImg;
    private Image           _medal;
    private TextMeshProUGUI _medalText;
    private int             _shownGrade = -1;
    private bool            _lowTime;

    private CancellationToken _ct;

    // ── Public Methods ─────────────────────────────────────────

    public static MinigameHud Create(Color theme)
    {
        var canvasGO = new GameObject("MinigameHudCanvas", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = UISortingOrder.HudIndicator;

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        var hud = canvasGO.AddComponent<MinigameHud>();
        hud._root  = (RectTransform)canvasGO.transform;
        hud._theme = theme;
        hud._ct    = hud.GetCancellationTokenOnDestroy();   // 띠 · 메달의 작은 움직임은 HUD 수명에 묶는다
        hud.BuildBand();
        return hud;
    }

    public void Close()
    {
        if (this != null && gameObject != null) Destroy(gameObject);
    }

    /// <summary>② 규칙 카드 — 위에서 내려와 앉고(튕김 없이), <paramref name="seconds"/> 뒤 사라진다.</summary>
    public async UniTask ShowCardAsync(string title, string rule, string gradeHint, float seconds, CancellationToken ct)
    {
        // 위쪽 목표판(「▲보스 결전」 등)을 가리지 않게 그 아래에 앉힌다
        var card = Panel("Card", new Vector2(0f, -330f), new Vector2(820f, 212f), UITheme.Window, 16f);
        var cg   = card.gameObject.AddComponent<CanvasGroup>();
        Text(card, title,     new Vector2(0f, -30f),  46f, UITheme.Gold, 760f);
        Text(card, rule,      new Vector2(0f, -100f), 32f, UITheme.Ink,  760f);
        Text(card, gradeHint, new Vector2(0f, -154f), 22f, UITheme.Mute, 760f);

        MinigameFx.Sound(SoundKey.Sfx.UiButton, 0.8f, 0.8f);
        Vector2 basePos = card.anchoredPosition;
        await Tween(CardInSecs, t =>
        {
            float e = EaseOut(t);
            cg.alpha = e;
            card.anchoredPosition = basePos + new Vector2(0f, CardSlide * (1f - e));
        }, ct);
        await UIJuice.HoldAsync(Mathf.Max(0f, seconds - CardInSecs - CardOutSecs), ct);
        await Tween(CardOutSecs, t => cg.alpha = 1f - t, ct);
        Destroy(card.gameObject);
    }

    /// <summary>③ 카운트다운 — 3 · 2 · 1 → 「시작!」. 틱 음높이 0.9 → 1.0 → 1.12, 시작음 1.4.</summary>
    public async UniTask CountdownAsync(CancellationToken ct)
    {
        var num = Text(_root, "", new Vector2(0f, -400f), 120f, UITheme.Ink, 400f);
        var cg  = num.gameObject.AddComponent<CanvasGroup>();
        float[] pitches = { 0.9f, 1.0f, 1.12f };
        for (int i = 0; i < 3; i++)
        {
            num.text = (3 - i).ToString();
            num.color = UITheme.Ink;
            MinigameFx.Tick(pitches[i]);
            await Tween(0.5f, t =>
            {
                num.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.3f, 1f, EaseOut(Mathf.Min(1f, t * 2.5f)));
                cg.alpha = t < 0.8f ? 1f : Mathf.Lerp(1f, 0.3f, (t - 0.8f) / 0.2f);
            }, ct);
        }
        num.text  = "시작!";
        num.fontSize = 92f;
        num.color = _theme;
        MinigameFx.Tick(1.4f);
        MinigameFx.Flash(Color.white, 0.2f, 0.12f);
        await Tween(0.4f, t =>
        {
            num.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.15f, 1f, EaseOut(t));
            cg.alpha = 1f - t * t;
        }, ct);
        Destroy(num.gameObject);
    }

    /// <summary>④ 진행 띠를 보인다.</summary>
    public void ShowBand(string title)
    {
        _bandTitle.text = title;
        FadeBandAsync(1f).Forget();
    }

    public void HideBand() => FadeBandAsync(0f).Forget();

    /// <summary>시간 막대 — 남은 비율 · 남은 초. 5초 아래면 붉게 맥동한다.</summary>
    public void SetTime(float remaining01, float remainingSeconds)
    {
        if (_barFill == null) return;
        _barFill.anchorMax = new Vector2(Mathf.Clamp01(remaining01), 1f);
        _lowTime = remainingSeconds <= LowTimeSecs;
        if (_lowTime)
        {
            float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 4f);
            _barFillImg.color = Color.Lerp(LowTimeColor, Color.white, k * 0.35f);
        }
        else _barFillImg.color = _theme;
    }

    /// <summary>등급 메달 — 바뀌면 새 빛깔로 커졌다 돌아온다(0.18초) · 오를 때 밝은 음, 내릴 때 낮은 음.</summary>
    public void SetGrade(ChallengeGrade grade)
    {
        int g = (int)grade;
        if (g == _shownGrade) return;
        bool first = _shownGrade < 0;
        bool up    = g > _shownGrade;
        _shownGrade = g;

        _medal.color    = GradeColor(grade);
        _medalText.text = GradeName(grade);
        if (first) return;

        UIJuice.PunchAsync(_medal.rectTransform, 0.25f, 0.18f, _ct).Forget();
        MinigameFx.Sound(up ? SoundKey.Sfx.ItemPickup : SoundKey.Sfx.UiButton, 0.6f, up ? 1.3f : 0.7f);
    }

    public void SetStatus(string text) { if (_status != null) _status.text = text; }

    /// <summary>라운드 점(●●○) — null이면 숨김.</summary>
    public void SetDots(string text) { if (_dots != null) _dots.text = text ?? string.Empty; }

    /// <summary>메달 옆에서 떠오르며 사라지는 짧은 글자 — 「−1」 · 「간발!」 · 「안전!」.</summary>
    public void Float(string text, Color color)
    {
        if (_medal == null) return;
        var t = Text(_band.transform, text, _medal.rectTransform.anchoredPosition + new Vector2(90f, -10f), 30f, color, 240f);
        FloatAsync(t).Forget();
    }

    /// <summary>⑥ 등급 도장 — 메달이 가운데로 와서 찍힌다(0.98 → 1.0 눌림) + 등급 이름 + 성적 한 줄. 합 1.2초.</summary>
    public async UniTask StampAsync(ChallengeGrade grade, string resultLine, CancellationToken ct)
    {
        HideBand();
        var holder = new GameObject("Stamp", typeof(RectTransform)).GetComponent<RectTransform>();
        holder.SetParent(_root, false);
        holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0.5f);
        holder.anchoredPosition = new Vector2(0f, 90f);
        var cg = holder.gameObject.AddComponent<CanvasGroup>();

        // 큰 반지름 둥근 판은 절차 스프라이트가 마름모 넷으로 깨진다 — 메달 크기로 만들고 배율로 키운다
        var medal = Panel(holder, "Medal", Vector2.zero, new Vector2(MedalSize, MedalSize), GradeColor(grade), MedalSize * 0.5f, centerAnchor: true);
        Text(medal, GradeName(grade), new Vector2(0f, -MedalSize * 0.5f + 12f), 20f, new Color(0.08f, 0.07f, 0.1f), MedalSize + 30f);
        Text(holder, resultLine, new Vector2(0f, -StampSize * 0.5f - 24f), 28f, UITheme.Ink, 900f, centerAnchor: true);
        float big = StampSize / MedalSize;

        bool top = grade == ChallengeGrade.Platinum;
        if (top) FinisherEdgeService.Pulse();
        StampSound(grade);

        await Tween(0.22f, t =>
        {
            cg.alpha = Mathf.Clamp01(t * 2f);
            float s = t < 0.8f ? Mathf.Lerp(1.4f, 0.98f, EaseOut(t / 0.8f)) : Mathf.Lerp(0.98f, 1f, (t - 0.8f) / 0.2f);
            medal.localScale = Vector3.one * (s * big);
        }, ct);
        await UIJuice.HoldAsync(0.73f, ct);
        await Tween(0.25f, t => cg.alpha = 1f - t, ct);
        Destroy(holder.gameObject);
    }

    public static Color GradeColor(ChallengeGrade g) => g switch
    {
        ChallengeGrade.Platinum => PlatinumColor,
        ChallengeGrade.Gold     => GoldColor,
        ChallengeGrade.Silver   => SilverColor,
        ChallengeGrade.Bronze   => BronzeColor,
        _                       => FailColor,
    };

    public static string GradeName(ChallengeGrade g) => g switch
    {
        ChallengeGrade.Platinum => "플래티넘",
        ChallengeGrade.Gold     => "골드",
        ChallengeGrade.Silver   => "실버",
        ChallengeGrade.Bronze   => "브론즈",
        _                       => "실패",
    };

    // ── Private Methods ────────────────────────────────────────

    private void BuildBand()
    {
        var band = new GameObject("Band", typeof(RectTransform)).GetComponent<RectTransform>();
        band.SetParent(_root, false);
        band.anchorMin = band.anchorMax = new Vector2(0.5f, 1f);
        band.pivot     = new Vector2(0.5f, 1f);
        band.anchoredPosition = new Vector2(0f, BandTop);
        band.sizeDelta        = new Vector2(1300f, BandHeight);
        _band = band.gameObject.AddComponent<CanvasGroup>();
        _band.alpha = 0f;

        var bg = band.gameObject.AddComponent<Image>();
        bg.sprite        = UITheme.SoftBand;
        bg.color         = new Color(0.02f, 0.02f, 0.04f, 0.72f);
        bg.raycastTarget = false;

        _bandTitle = Text(band, "", new Vector2(-150f, -14f), 26f, UITheme.Gold, 520f);

        // 시간 막대
        var barBg = Panel(band, "TimeBar", new Vector2(-150f, -54f), new Vector2(BarWidth, 12f), new Color(1f, 1f, 1f, 0.12f), 6f);
        var fill  = new GameObject("Fill", typeof(RectTransform)).GetComponent<RectTransform>();
        fill.SetParent(barBg, false);
        fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one;
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        _barFillImg = fill.gameObject.AddComponent<Image>();
        _barFillImg.sprite = UIProceduralSprites.RoundedRect(radius: 6f, feather: 1.5f);
        _barFillImg.type   = Image.Type.Sliced;
        _barFillImg.color  = _theme;
        _barFillImg.raycastTarget = false;
        _barFill = fill;

        _status = Text(band, "", new Vector2(-150f, -76f), 22f, UITheme.Ink, 620f);

        // 등급 메달
        _medal = Panel(band, "Medal", new Vector2(250f, -14f), new Vector2(MedalSize, MedalSize), FailColor, MedalSize * 0.5f).GetComponent<Image>();
        _medalText = Text(_medal.rectTransform, "", new Vector2(0f, -MedalSize * 0.5f + 12f), 20f, new Color(0.08f, 0.07f, 0.1f), MedalSize + 30f);
        _dots = Text(band, "", new Vector2(380f, -40f), 26f, UITheme.Ink, 140f);
    }

    private async UniTaskVoid FadeBandAsync(float to)
    {
        if (_band == null) return;
        float from = _band.alpha;
        try { await Tween(0.25f, t => { if (_band != null) _band.alpha = Mathf.Lerp(from, to, t); }, _ct); }
        catch (System.OperationCanceledException) { }
    }

    private async UniTaskVoid FloatAsync(TextMeshProUGUI t)
    {
        var rt = t.rectTransform;
        Vector2 p0 = rt.anchoredPosition;
        try
        {
            await Tween(0.6f, k =>
            {
                if (rt == null) return;
                rt.anchoredPosition = p0 + new Vector2(0f, 40f * EaseOut(k));
                var c = t.color; c.a = 1f - k * k; t.color = c;
            }, _ct);
        }
        catch (System.OperationCanceledException) { }
        if (t != null) Destroy(t.gameObject);
    }

    private static void StampSound(ChallengeGrade g)
    {
        switch (g)
        {
            case ChallengeGrade.Platinum:
                PlayNotesAsync().Forget();
                break;
            case ChallengeGrade.Fail:
                MinigameFx.Sound(SoundKey.Sfx.UiButton, 0.7f, 0.55f);
                break;
            default:
                MinigameFx.Sound(SoundKey.Sfx.ItemPickup, 0.8f, 0.9f + 0.1f * (int)g);
                break;
        }
    }

    private static async UniTaskVoid PlayNotesAsync()
    {
        float[] pitches = { 1.0f, 1.15f, 1.3f };
        foreach (var p in pitches)
        {
            MinigameFx.Sound(SoundKey.Sfx.ItemPickup, 0.8f, p);
            await UniTask.Delay(150, DelayType.Realtime);
        }
    }

    private static async UniTask Tween(float seconds, System.Action<float> step, CancellationToken ct)
    {
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Time.unscaledDeltaTime / Mathf.Max(0.01f, seconds));
            step(t);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }

    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

    private RectTransform Panel(string name, Vector2 pos, Vector2 size, Color fill, float radius)
        => Panel(_root, name, pos, size, fill, radius);

    private static RectTransform Panel(Transform parent, string name, Vector2 pos, Vector2 size, Color fill, float radius, bool centerAnchor = false)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = centerAnchor ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 1f);
        rt.pivot     = centerAnchor ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = false;
        UITheme.StylePanel(img, fill, null, radius);
        return rt;
    }

    private static TextMeshProUGUI Text(Transform parent, string text, Vector2 pos, float size, Color color, float width, bool centerAnchor = false)
    {
        var rt = new GameObject("Text", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = centerAnchor ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 1f);
        rt.pivot     = centerAnchor ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = new Vector2(width, size * 1.4f);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text             = text;
        t.fontSize         = size;
        t.alignment        = TextAlignmentOptions.Center;
        t.raycastTarget    = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        UITheme.StyleText(t, color);
        return t;
    }
}
