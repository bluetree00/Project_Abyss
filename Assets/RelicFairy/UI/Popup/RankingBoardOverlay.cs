using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 랭킹방 비석 화면(10-29 기획 설계 §5-3) — 11-02부터 ESC 메뉴 「랭킹」과 같은 쪽(<see cref="UI_RankingPage"/> · 탭 · 실시간 · 연출)을 그 시기 탭으로 연다.
/// 랭킹방(2a 프리팹)이 도착할 때 · 비석 [F]에서 <see cref="ShowAsync"/>를 부른다. 창은 코드로 짠다(시간을 멈추고 입력을 막는다) — [Esc] · [F] · 바깥 · 돌아가기로 닫는다.
/// 남은 올리기(서버가 안 받던 기록)는 쪽이 열릴 때 먼저 올린다.
/// </summary>
public static class RankingBoardOverlay
{
    // ── Static ────────────────────────────────────────────────
    private static readonly object s_timeOwner = new();

    /// <summary>비석 창이 떠 있다(ESC 듣개 · 상호작용 막기).</summary>
    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => IsOpen = false;

    // ── Public Methods ────────────────────────────────────────

    /// <summary>비석 창을 열고 닫힐 때까지 기다린다. 이미 떠 있으면 바로 돌아온다.</summary>
    public static async UniTask ShowAsync(StoryEra era)
    {
        if (IsOpen || era == StoryEra.Sealed) return;
        var player = Object.FindFirstObjectByType<PlayerController>();
        var go = new GameObject("@RankingBoard", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
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

        bool closed = false;
        TimeScaleArbiter.Acquire(s_timeOwner, 0f, TimeScaleArbiter.Priority.Pause);
        if (player != null) player.SetInputEnabled(false);
        IsOpen = true;
        try
        {
            var veil = Rect(go.transform, "Veil", Vector2.zero, Vector2.zero);
            veil.anchorMin = Vector2.zero; veil.anchorMax = Vector2.one;
            veil.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.74f);
            var veilBtn = veil.gameObject.AddComponent<Button>();
            veilBtn.transition = Selectable.Transition.None;
            veilBtn.onClick.AddListener(() => closed = true);

            var page = UI_RankingPage.Create(go.transform, () => closed = true);
            page.Open(era);

            FadeAsync(group, 1f, UIFader.OpenSec, ct).Forget();
            Managers.Sound?.PlayUiEvent(SoundEvent.UiOpen);   // 10-29 UI 소리 — 열기

            await UniTask.Yield(PlayerLoopTiming.Update, ct);   // 연 [F]가 닫기로 읽히지 않게
            while (!closed)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F)) closed = true;
                else await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            Managers.Sound?.PlayUiEvent(SoundEvent.UiClose);  // 10-29 UI 소리 — 닫기([F] · [Esc] · 바깥 · 돌아가기 어느 길이든 한 번)
            group.blocksRaycasts = false;
            await FadeAsync(group, 0f, UIFader.CloseSec, ct);
        }
        catch (System.OperationCanceledException) { }
        finally
        {
            IsOpen = false;
            TimeScaleArbiter.Release(s_timeOwner);
            if (player != null) player.SetInputEnabled(true);
            if (go != null) Object.Destroy(go);
        }
    }

    /// <summary>「이번 기록 38:12.40 — 「날랜 해방자」 · 다음 칭호 30분 안」 · 이번 기록이 없으면 「내 칭호 …」. 둘 다 없으면 null(랭킹 쪽 아래 한 줄).</summary>
    internal static string RecordLine(StoryEra era)
    {
        bool now = RankingFlow.LastEra == era && RankingFlow.LastCentis > 0;
        int tier = now ? RankingFlow.TitleTier(era, RankingFlow.LastCentis) : RankingFlow.BestTitleTier(era);
        if (tier < 0) return null;
        string title = RankingFlow.TitleName(era, tier);
        string head  = now ? Loc.F("이번 기록  {0}  —  「{1}」", RankingService.Format(RankingFlow.LastCentis), title) : Loc.F("내 칭호  「{0}」", title);
        int next = RankingFlow.NextTierMinutes(era, tier);
        return next > 0 ? Loc.F("{0}   <size=72%><color=#{1}>다음 칭호 {2}분 안</color></size>", head, ColorUtility.ToHtmlStringRGB(UITheme.Mute), next) : head;
    }

    // ── Private Methods ───────────────────────────────────────

    private static RectTransform Rect(Transform parent, string name, Vector2 size, Vector2 pos)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return rt;
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
