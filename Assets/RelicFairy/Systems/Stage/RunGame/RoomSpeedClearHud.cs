using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-29 F1 속도 보상 한 줄 — 해방기 · 악몽 세는 방에서 싸움이 붙으면 위 가운데에 「빠른 정리 0:12 / 0:19」.
/// 정해진 시간을 넘기면 흐려진다(그 방 보상은 평소대로). 방이 끝나도 결과는 다음 방에 들어설 때까지 남는다
/// (RoomSpeedClear가 방 순번이 바뀔 때만 비운다). 판정은 RoomSpeedClear(2a) — 이 칸은 보여 주기만 한다.
/// 빠르게 끝내 룬 첫 장이 올랐으면(<see cref="MarkRewarded"/>) 같은 줄 끝에 「첫 룬 한 단계 위」를 붙이고 한 번 살짝 커진다 —
/// 알림 칸(화면 가운데 아래)에 띄우면 그 자리에 서는 보상 빛과 겹쳐 읽히지 않았다(10-29 실측).
/// 띠 없이 얇은 글 한 줄(UI 모던 · 얇게). 고르기 창(시간 정지) · 컷신 동안엔 숨긴다.
/// RunCombatClock처럼 앱 시작 때 한 번 세운다 — 방 · HUD 프리팹을 건드리지 않는다.
/// </summary>
public sealed class RoomSpeedClearHud : MonoBehaviour
{
    // ── Constants ──
    private const float Top       = 150f;   // 1080 기준 위에서 — 보스 체력바(44~126) 아래
    private const float FadeSec   = 0.25f;
    private const float OverAlpha = 0.4f;   // 정해진 시간을 넘겼다
    private const float PulseSec  = 0.6f;
    private const float PulseGrow = 0.15f;  // 「살짝」

    // ── Static ──
    private static bool s_rewarded;   // 이 방 룬 첫 장이 한 단계 올랐다 — 다음 방(IsActive가 꺼질 때)에 비운다
    private static bool s_pulse;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { s_rewarded = false; s_pulse = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindFirstObjectByType<RoomSpeedClearHud>() != null) return;
        var go = new GameObject("RoomSpeedClearHud", typeof(Canvas), typeof(CanvasScaler));
        DontDestroyOnLoad(go);
        go.AddComponent<RoomSpeedClearHud>();
    }

    // ── Private ──
    private CanvasGroup     _group;
    private TextMeshProUGUI _text;
    private int   _shownFight = -1, _shownLimit = -1;
    private bool  _shownOver, _shownRewarded;
    private float _pulseT = -1f;

    // ── Lifecycle ──
    private void Awake()
    {
        var canvas = GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = UISortingOrder.HudIndicator;
        // 프로젝트 캔버스 기준 — 1920×1080, Scale With Screen Size, Match 0.5
        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha          = 0f;
        _group.blocksRaycasts = false;
        _group.interactable   = false;

        var go = new GameObject("Line", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -Top);
        rt.sizeDelta        = new Vector2(900f, 36f);
        _text = go.AddComponent<TextMeshProUGUI>();
        _text.alignment        = TextAlignmentOptions.Center;
        _text.textWrappingMode = TextWrappingModes.NoWrap;
        _text.raycastTarget    = false;
        UITheme.StyleThinText(_text, UIWeight.Regular, UITheme.Thin.Lead, UITheme.Gold, shadow: true);
    }

    private void Update()
    {
        if (!RoomSpeedClear.IsActive) s_rewarded = false;   // 다음 방에 들어섰다
        bool  over   = RoomSpeedClear.FightSeconds > RoomSpeedClear.LimitSeconds;
        float target = Visible() ? (over ? OverAlpha : 1f) : 0f;
        if (target > 0f) Refresh(over);
        if (!Mathf.Approximately(_group.alpha, target))
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.unscaledDeltaTime / FadeSec);
        Pulse();
    }

    // ── Public Methods ──

    /// <summary>RoomClearGate — 빠른 정리로 이 방 룬 첫 장을 한 단계 올렸다. 줄 끝에 알리고 한 번 살짝 커진다.</summary>
    public static void MarkRewarded()
    {
        s_rewarded = true;
        s_pulse    = true;
        // 10-29 UI 소리 — 「첫 룬 한 단계 위」. 같은 프레임에 보상 예고(RewardObjectPresenter)도 RewardGrade를 내므로 살짝 높여 겹친다
        Managers.Sound?.PlayUiEvent(SoundEvent.RewardGrade, 1.2f);
    }

    // ── Private Methods ──
    private static bool Visible()
    {
        var run = GameRunBootstrapper.Instance != null ? GameRunBootstrapper.Instance.Run : null;
        if (run == null || !run.IsRunning || Time.timeScale <= 0f) return false;
        if (run.TryGetHudMode(out var mode) && (mode == HUDIds.Mode.Cutscene || mode == HUDIds.Mode.BossCutscene)) return false;
        return RoomSpeedClear.IsActive && RoomSpeedClear.FightSeconds > 0f;
    }

    /// <summary>초가 바뀔 때만 글을 새로 쓴다(매 프레임 문자열을 만들지 않게).</summary>
    private void Refresh(bool over)
    {
        int fight = Mathf.FloorToInt(RoomSpeedClear.FightSeconds);
        int limit = Mathf.FloorToInt(RoomSpeedClear.LimitSeconds);
        if (fight == _shownFight && limit == _shownLimit && over == _shownOver && s_rewarded == _shownRewarded) return;
        _shownFight    = fight;
        _shownLimit    = limit;
        _shownOver     = over;
        _shownRewarded = s_rewarded;
        _text.color = over ? UITheme.Mute : UITheme.Gold;
        _text.text  = s_rewarded
            ? $"빠른 정리  {RunResultPresenter.FormatTime(fight)} / {RunResultPresenter.FormatTime(limit)}  ·  첫 룬 한 단계 위"
            : $"빠른 정리  {RunResultPresenter.FormatTime(fight)} / {RunResultPresenter.FormatTime(limit)}";
    }

    /// <summary>한 단계 위가 붙는 순간 한 번 살짝 커졌다 돌아온다(보상 고르기 창이 시간을 멈춰도 돌게 unscaled).</summary>
    private void Pulse()
    {
        if (s_pulse) { s_pulse = false; _pulseT = 0f; }
        if (_pulseT < 0f) return;
        _pulseT += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(_pulseT / PulseSec);
        _text.rectTransform.localScale = Vector3.one * (1f + PulseGrow * Mathf.Sin(k * Mathf.PI));
        if (k >= 1f) _pulseT = -1f;
    }
}
