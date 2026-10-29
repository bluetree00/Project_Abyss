using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 랭킹방 비석 화면(10-29 기획 설계 §5-3) — 해방기 · 악몽 클리어 시간 상위 10 · 내 줄 · 이번 기록.
/// 랭킹방(2a 프리팹)이 도착할 때 · 비석 [F]에서 <see cref="ShowAsync"/>를 부른다. 창은 코드로 짠다(시간을 멈추고 입력을 막는다) — [Esc] · [F] · 바깥으로 닫는다.
/// 서버를 못 읽으면 그 줄만 「불러오지 못했다」로 두고 이번 기록은 그대로 보인다.
/// </summary>
public static class RankingBoardOverlay
{
    // ── Constants ─────────────────────────────────────────────
    private const float BoardW = 900f, BoardH = 700f, RowH = 40f;
    // 10-29 석판 그림 — 그림 비율(1.39) 그대로 · 위 사슬 문장(판 높이 11%)을 비켜 글을 내린다(128 → 138: 제목이 사슬 끝에 붙어 넘쳐 보였다)
    private const float ArtW = 1040f, ArtH = 748f, ArtRowH = 36f, ArtTopPad = 138f, MedalSize = 38f;
    private const string TabletKey = "UI/RankingBoard/Tablet";
    private static readonly string[] MedalKeys = { "UI/RankingBoard/Medal1", "UI/RankingBoard/Medal2", "UI/RankingBoard/Medal3" };
    private static readonly Color MedalInk = new(0.12f, 0.09f, 0.06f, 1f);   // 메달 위 순위 숫자
    private static readonly Color LibAccent = new(0.95f, 0.82f, 0.45f, 1f);   // 해방기 = 밝은 성소 금빛
    private static readonly Color NmAccent  = new(0.80f, 0.36f, 0.66f, 1f);   // 악몽 = 포탈 악몽 물듦 쪽

    // ── Static ────────────────────────────────────────────────
    private static readonly object s_timeOwner = new();
    private static Sprite   s_tablet;
    private static readonly Sprite[] s_medals = new Sprite[3];
    private static bool     s_artTried;

    /// <summary>비석 창이 떠 있다(ESC 듣개 · 상호작용 막기).</summary>
    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { IsOpen = false; s_tablet = null; s_artTried = false; System.Array.Clear(s_medals, 0, s_medals.Length); }

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
        Color accent = era == StoryEra.NightmareMode ? NmAccent : LibAccent;
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

            await LoadArtAsync();
            bool art = s_tablet != null;
            float bw = art ? ArtW : BoardW, bh = art ? ArtH : BoardH, rowH = art ? ArtRowH : RowH, pad = art ? ArtTopPad : 46f;
            var board = Rect(go.transform, "Board", new Vector2(bw, bh), Vector2.zero);
            var bg = board.gameObject.AddComponent<Image>();
            if (art)
            {
                bg.sprite = s_tablet;
                bg.type   = Image.Type.Simple;
                bg.color  = era == StoryEra.NightmareMode ? new Color(0.92f, 0.84f, 0.96f, 1f) : Color.white;   // 악몽은 살짝 보랏빛
            }
            else UITheme.StylePanel(bg, UITheme.Band, new Color(accent.r, accent.g, accent.b, 0.85f));
            bg.raycastTarget = true;   // 판 위 클릭은 닫지 않는다

            Text(board, era == StoryEra.NightmareMode ? "악몽 — 클리어 시간" : "해방기 — 클리어 시간", 34f, accent, new Vector2(0f, bh * 0.5f - pad), new Vector2(bw - 160f, 46f));
            var status = Text(board, "기록을 불러오는 중…", 19f, UITheme.Mute, new Vector2(0f, bh * 0.5f - pad - 42f), new Vector2(bw - 160f, 28f));

            // 머리 줄
            float top = bh * 0.5f - pad - 90f;
            Row(board, bw, rowH, top, "순위", "이름", "시간", "무기", UITheme.Mute, 18f);

            // 이번 기록 · 칭호(10-29 F6) — 이번에 깬 기록이 없으면 이 시기 가장 좋은 칭호
            string record = RecordLine(era);
            if (record != null)
                Text(board, record, 24f, UITheme.Gold, new Vector2(0f, -bh * 0.5f + (art ? 108f : 92f)), new Vector2(bw - 160f, 32f));
            // 닫기 안내 — 석판이면 판 밖 아래(금 테에 얹히지 않게)
            Text(board, "[F] · [Esc] 또는 바깥을 누르면 닫는다", 17f, UITheme.Mute, new Vector2(0f, art ? -bh * 0.5f - 26f : -bh * 0.5f + 40f), new Vector2(bw - 60f, 26f));

            FadeAsync(group, 1f, UIFader.OpenSec, ct).Forget();
            Managers.Sound?.PlayUiEvent(SoundEvent.UiOpen);   // 10-29 UI 소리 — 열기

            // 서버 — 리더보드를 찾고, 남은 기록을 먼저 올리고 읽는다
            bool ready = await RankingService.ResolveAsync(era);
            if (go == null) return;
            if (!ready) status.text = "랭킹 서버 준비 중 — 기록은 기기에 남겨 두었다";
            else
            {
                await RankingService.RetryPendingAsync();
                var (list, mine) = await RankingService.LoadAsync(era);
                if (go == null) return;
                if (list.Count == 0) status.text = "아직 기록이 없거나 불러오지 못했다";
                else
                {
                    status.text = "";
                    for (int i = 0; i < list.Count; i++)
                    {
                        var e = list[i];
                        Row(board, bw, rowH, top - rowH * (i + 1), $"{e.Rank}", e.Nickname, RankingService.Format(e.Centis), e.Weapon,
                            e.IsMine ? UITheme.Gold : UITheme.Ink, 20f, e.Rank <= 3 ? e.Rank : 0);
                    }
                    if (mine is { } me && me.Rank > RankingService.TopCount)   // 상위 10 밖이면 내 줄을 따로
                        Row(board, bw, rowH, top - rowH * (list.Count + 1.4f), $"{me.Rank}", me.Nickname, RankingService.Format(me.Centis), me.Weapon, UITheme.Gold, 20f);
                }
            }

            await UniTask.Yield(PlayerLoopTiming.Update, ct);   // 연 [F]가 닫기로 읽히지 않게
            while (!closed)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F)) closed = true;
                else await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            Managers.Sound?.PlayUiEvent(SoundEvent.UiClose);  // 10-29 UI 소리 — 닫기([F] · [Esc] · 바깥 어느 길이든 한 번)
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

    // ── Private Methods ───────────────────────────────────────

    /// <summary>「이번 기록 38:12.40 — 「날랜 해방자」 · 다음 칭호 30분 안」 · 이번 기록이 없으면 「내 칭호 …」. 둘 다 없으면 null.</summary>
    private static string RecordLine(StoryEra era)
    {
        bool now = RankingFlow.LastEra == era && RankingFlow.LastCentis > 0;
        int tier = now ? RankingFlow.TitleTier(era, RankingFlow.LastCentis) : RankingFlow.BestTitleTier(era);
        if (tier < 0) return null;
        string title = RankingFlow.TitleName(era, tier);
        string head  = now ? $"이번 기록  {RankingService.Format(RankingFlow.LastCentis)}  —  「{title}」" : $"내 칭호  「{title}」";
        int next = RankingFlow.NextTierMinutes(era, tier);
        return next > 0 ? $"{head}   <size=72%><color=#{ColorUtility.ToHtmlStringRGB(UITheme.Mute)}>다음 칭호 {next}분 안</color></size>" : head;
    }

    /// <summary>한 줄 — <paramref name="medal"/> 1 · 2 · 3이면 순위 숫자를 금 · 은 · 동 메달 위에(그림이 없으면 숫자만).</summary>
    private static void Row(RectTransform board, float boardW, float rowH, float y, string rank, string name, string time, string weapon,
                            Color color, float size, int medal = 0)
    {
        float w = boardW - 160f;
        Vector2 rankPos = new(-w * 0.5f + 50f, y);
        var medalArt = medal >= 1 && medal <= 3 ? s_medals[medal - 1] : null;
        if (medalArt != null)
        {
            var m = Rect(board, "Medal", new Vector2(MedalSize, MedalSize), rankPos).gameObject.AddComponent<Image>();
            m.sprite = medalArt;
            m.preserveAspect = true;
            m.raycastTarget = false;
        }
        Text(board, rank,   size, medalArt != null ? MedalInk : color, rankPos, new Vector2(90f, rowH), TextAlignmentOptions.Center);
        Text(board, name,   size, color, new Vector2(-w * 0.5f + 300f, y), new Vector2(380f, rowH), TextAlignmentOptions.Left);
        Text(board, time,   size, color, new Vector2(w * 0.5f - 300f,  y), new Vector2(160f, rowH), TextAlignmentOptions.Right);
        Text(board, weapon, size - 2f, UITheme.Mute, new Vector2(w * 0.5f - 90f, y), new Vector2(170f, rowH), TextAlignmentOptions.Left);
    }

    /// <summary>석판 · 메달 그림을 한 번 읽어 둔다(10-29 힉스필드) — 없으면 예전 판으로.</summary>
    private static async UniTask LoadArtAsync()
    {
        if (s_artTried || Managers.AddressableManager == null) return;
        s_artTried = true;
        s_tablet = await Managers.AddressableManager.TryLoadAssetAsync<Sprite>(TabletKey);
        for (int i = 0; i < MedalKeys.Length; i++)
            s_medals[i] = await Managers.AddressableManager.TryLoadAssetAsync<Sprite>(MedalKeys[i]);
    }

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

    private static TextMeshProUGUI Text(Transform parent, string text, float size, Color color, Vector2 pos, Vector2 box,
                                        TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var rt = Rect(parent, "Text", box, pos);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text             = text;
        t.fontSize         = size;
        t.color            = color;
        t.alignment        = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode     = TextOverflowModes.Ellipsis;
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
