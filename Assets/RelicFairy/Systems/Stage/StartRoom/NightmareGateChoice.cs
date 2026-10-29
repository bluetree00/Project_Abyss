using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 던전 입구 「해방기 / 악몽」 두 장 창(10-29 사용자 「악몽은 해방기 클리어 뒤 던전 입구에서 선택」 · 설계 §4).
/// 거점 심연 포탈 트리거(<see cref="BaseCampDungeonGate"/>)가 해금(<see cref="StoryProgress.IsNightmareModeUnlocked"/>) 뒤 내려가기 전에 부른다.
///   · 고르면 <c>onPicked(true = 악몽 · false = 해방기)</c>만 부른다 — 기록(SetNightmareMode) · 물듦 · 내려감은 부르는 쪽 몫.
///   · 닫으면(바깥 · [Esc]) <c>onCancel</c>만 부른다(부르는 쪽 = 「안 내려감」).
///   · 기본값 = 지금 기록(<see cref="StoryProgress.IsNightmareMode"/>) — 그 카드에 「지난번」.
/// 창은 코드로 짠다(옛 거점 갈림길 <see cref="BaseCampNightmareToggle"/>의 두 장 창과 같은 결) — 시간을 멈추고 플레이어 입력을 막는다.
/// </summary>
public static class NightmareGateChoice
{
    // ── Constants ─────────────────────────────────────────────
    private const float CardW = 380f;
    private const float CardH = 220f;
    private static readonly Color NightmareAccent = new(0.70f, 0.30f, 0.60f, 1f);   // 포탈 악몽 물듦과 같은 색

    // ── Static ────────────────────────────────────────────────
    private static readonly object s_timeOwner = new();

    /// <summary>창이 떠 있다 — 창이 [Esc]를 직접 받아 닫으므로 ESC 듣개는 그 밑에 일시정지 메뉴를 열지 않는다.</summary>
    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => IsOpen = false;

    // ── Public Methods ────────────────────────────────────────

    /// <summary>두 장 창의 글 — 던전 입구(<see cref="Show"/>) · 랭킹방 갈림(<see cref="RankingRoomChoice"/>)이 같은 창을 쓴다.</summary>
    public readonly struct Texts
    {
        public readonly string Title, Subtitle, LibTitle, LibDesc, NmTitle, NmDesc, Footer;
        public Texts(string title, string subtitle, string libTitle, string libDesc, string nmTitle, string nmDesc, string footer)
        {
            Title = title; Subtitle = subtitle; LibTitle = libTitle; LibDesc = libDesc; NmTitle = nmTitle; NmDesc = nmDesc; Footer = footer;
        }
    }

    /// <summary>창을 띄운다. 이미 떠 있으면 아무것도 안 한다.</summary>
    public static void Show(Action<bool> onPicked, Action onCancel = null)
    {
        if (IsOpen) return;
        var t = new Texts("어느 세계로 내려가는가", "해방기를 끝낸 자만 악몽을 고를 수 있다 · 고른 것은 다음에도 먼저 놓인다",
                          "해방된 세계로", "해방기 규칙 그대로\n클리어 시간이 해방기 랭킹에 오른다",
                          "악몽으로 들어간다", $"챕터마다 악몽 규칙 · 보스 악몽 강화 · 정수 ×{NightmareRules.ModeEssenceMultiplier:0.0#}\n클리어 시간이 악몽 랭킹에 오른다",
                          "카드를 눌러 고른다 · [Esc] 또는 바깥을 누르면 내려가지 않는다");
        ShowAsync(t, StoryProgress.IsNightmareMode, onPicked, onCancel).Forget();
    }

    /// <summary>같은 창을 다른 글로(<see cref="RankingRoomChoice"/>) — <paramref name="last"/>가 null이면 「지난번」 표시 없음.</summary>
    public static void Show(in Texts texts, bool? last, Action<bool> onPicked, Action onCancel = null)
    {
        if (IsOpen) return;
        ShowAsync(texts, last, onPicked, onCancel).Forget();
    }

    // ── Private Methods ───────────────────────────────────────

    private static async UniTaskVoid ShowAsync(Texts t, bool? last, Action<bool> onPicked, Action onCancel)
    {
        const int Undecided = -2, Closed = -1, PickLiberated = 0, PickNightmare = 1;
        int picked = Undecided;
        var player = GameRunBootstrapper.Instance?.Run?.Player ?? UnityEngine.Object.FindFirstObjectByType<PlayerController>();

        var go = new GameObject("@NightmareGateChoice", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        var ct = go.GetCancellationTokenOnDestroy();
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = UISortingOrder.RunChoice;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
        var group = go.GetComponent<CanvasGroup>();
        group.alpha = 0f;

        TimeScaleArbiter.Acquire(s_timeOwner, 0f, TimeScaleArbiter.Priority.Pause);
        if (player != null) player.SetInputEnabled(false);   // 카드를 누르는 클릭이 공격으로 새지 않게
        IsOpen = true;
        try
        {
            var veil = NewRect(go.transform, "Veil", Vector2.zero, Vector2.zero);
            veil.anchorMin = Vector2.zero; veil.anchorMax = Vector2.one;
            veil.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
            var veilBtn = veil.gameObject.AddComponent<Button>();
            veilBtn.transition = Selectable.Transition.None;
            veilBtn.onClick.AddListener(() => picked = Closed);

            NewText(go.transform, t.Title, 34f, UITheme.Gold, new Vector2(0f, 230f), new Vector2(900f, 46f));
            NewText(go.transform, t.Subtitle, 20f, UITheme.Mute, new Vector2(0f, 178f), new Vector2(1000f, 30f));

            NewCard(go.transform, new Vector2(-(CardW * 0.5f + 22f), -10f), t.LibTitle, t.LibDesc,
                    UITheme.Ink, last == false, () => picked = PickLiberated);
            NewCard(go.transform, new Vector2(CardW * 0.5f + 22f, -10f), t.NmTitle, t.NmDesc,
                    NightmareAccent, last == true, () => picked = PickNightmare);

            NewText(go.transform, t.Footer, 18f, UITheme.Mute, new Vector2(0f, -178f), new Vector2(1000f, 28f));

            FadeAsync(group, 1f, UIFader.OpenSec, ct).Forget();
            Managers.Sound?.PlayUiEvent(SoundEvent.UiOpen);   // 10-29 UI 소리 — 열기
            await UniTask.Yield(PlayerLoopTiming.Update, ct);   // 트리거에 들어선 프레임 입력이 닫기로 읽히지 않게
            while (picked == Undecided)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) picked = Closed;
                else await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            // 10-29 UI 소리 — 고르면 확정 · 바깥 · [Esc]면 닫기(어느 길이든 한 번)
            Managers.Sound?.PlayUiEvent(picked == Closed ? SoundEvent.UiClose : SoundEvent.UiConfirm);
            group.blocksRaycasts = false;
            await FadeAsync(group, 0f, UIFader.CloseSec, ct);
        }
        catch (OperationCanceledException) { picked = Closed; }
        finally
        {
            IsOpen = false;
            TimeScaleArbiter.Release(s_timeOwner);
            if (player != null) player.SetInputEnabled(true);
            if (go != null) UnityEngine.Object.Destroy(go);
        }

        if (picked == Closed) onCancel?.Invoke();
        else onPicked?.Invoke(picked == PickNightmare);
    }

    /// <summary>선택 카드 — 제목 + 설명, 지난번 고른 쪽이면 위에 「지난번」과 진한 테두리.</summary>
    private static void NewCard(Transform parent, Vector2 pos, string title, string desc, Color accent, bool last, Action onClick)
    {
        var rt = NewRect(parent, $"Card_{title}", new Vector2(CardW, CardH), pos);
        var img = rt.gameObject.AddComponent<Image>();
        UITheme.StylePanel(img, UITheme.Band, new Color(accent.r, accent.g, accent.b, last ? 0.9f : 0.45f));

        NewText(rt, title, 28f, accent, new Vector2(0f, 52f), new Vector2(CardW - 30f, 40f));
        var d = NewText(rt, desc, 17f, UITheme.Mute, new Vector2(0f, -28f), new Vector2(CardW - 40f, 96f));
        d.textWrappingMode = TextWrappingModes.Normal;
        if (last) NewText(rt, "· 지난번 ·", 18f, accent, new Vector2(0f, CardH * 0.5f + 18f), new Vector2(200f, 28f));

        var btn = rt.gameObject.AddComponent<Button>();
        btn.transition    = Selectable.Transition.ColorTint;
        btn.targetGraphic = img;
        var cb = btn.colors; cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f); cb.fadeDuration = 0.08f;
        btn.colors = cb;
        btn.onClick.AddListener(() => onClick?.Invoke());
        rt.gameObject.AddComponent<UIButtonFeedback>();
    }

    private static RectTransform NewRect(Transform parent, string name, Vector2 size, Vector2 pos)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return rt;
    }

    private static TextMeshProUGUI NewText(Transform parent, string text, float size, Color color, Vector2 pos, Vector2 box)
    {
        var rt = NewRect(parent, "Text", box, pos);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text             = text;
        t.fontSize         = size;
        t.color            = color;
        t.alignment        = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget    = false;
        TMPOutlineHelper.ApplySoftShadow(t);
        return t;
    }

    private static async UniTask FadeAsync(CanvasGroup g, float to, float seconds, CancellationToken ct)
    {
        float from = g.alpha;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            if (g == null) return;
            g.alpha = Mathf.Lerp(from, to, t / seconds);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        if (g != null) g.alpha = to;
    }
}
