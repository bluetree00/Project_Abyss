using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 랭킹 쪽(11-02 사용자 「실시간 랭킹 팝업도 제대로 된 연출과 구성으로 플레이어들에게 공유될 수 있게」) — 모든 플레이어의 기록을 뒤끝 리더보드에서 바로 읽는다.
/// 고르기 = 시기(해방기 · 악몽) × 종류(클리어 시간 · 보스전 · 챕터 전투 — 챕터 1~4)(11-03 사용자 「나눌 거고」 — 챕터 기록도 시기별 표). 상위 10(1~3위 금 · 은 · 동 메달) · 내 줄(금 띠) · 10위 밖이면 아래 따로 · 전체 인원.
/// 여는 곳 = ESC 메뉴 「랭킹」(런 · 거점 어디서나 · 같은 정지) · 랭킹방 비석(<see cref="RankingBoardOverlay"/> — 그 시기 탭으로).
/// 연출: 판이 살짝 커지며 뜨고 · 줄이 위에서부터 차례로 밀려 들어오고 · 메달이 맺히고 · 내 줄 금 띠가 숨 쉰다(0.2초 안팎 · 튕김 없음).
/// 30초마다 · [새로고침]으로 다시 읽는다. 그림 = 랭킹방 석판 · 메달(10-29 힉스필드) — 없으면 얇은 창. 시간은 멈춰 있어도 돈다(실제 시간).
/// </summary>
public sealed class UI_RankingPage : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────
    private const float BoardW = 1040f, BoardH = 748f, BoardY = 34f;   // 석판 그림 비율(1.39) · 아래 단추 자리만큼 위로
    private const float ColW = 880f, RowH = 31f, RowTop = 48f, MedalSize = 32f;
    // 칸(가운데 x · 폭) — 11-05 사용자 「텍스트 위치가 각각 내용에 맞게 중앙 정렬」: 머리글 · 값 모두 칸 가운데. 폭 합 = ColW(100 + 360 + 220 + 200)
    private const float RankX = -390f, RankW = 100f, NameX = -160f, NameW = 360f, TimeX = 130f, TimeW = 220f, WeaponX = 340f, WeaponW = 200f;
    private const float BandW = ColW - 24f;   // 내 줄 금 띠 — 석판 안쪽 금테(안 폭 ≈ 872) 안에 들게(11-03 904는 금테를 덮었다)
    private const float OpenSec = 0.22f, RowIn = 0.2f, RowStagger = 0.035f, RefreshSec = 30f, LoadTimeout = 6f;
    private const string TabletKey = "UI/RankingBoard/Tablet";
    private static readonly string[] MedalKeys = { "UI/RankingBoard/Medal1", "UI/RankingBoard/Medal2", "UI/RankingBoard/Medal3" };
    private static readonly string[] EraNames  = { "해방기", "악몽" };
    private static readonly string[] KindNames = { "클리어 시간", "보스전", "챕터 전투" };
    private const float TabW = 150f, TabStep = 158f;
    private const int ChapterCount = 4;

    private static readonly Color MedalInk  = new(0.12f, 0.09f, 0.06f, 1f);
    private static readonly Color LibAccent = new(0.95f, 0.82f, 0.45f, 1f);   // 해방기 = 성소 금빛(비석과 같다)
    private static readonly Color NmAccent  = new(0.80f, 0.36f, 0.66f, 1f);   // 악몽
    private static readonly Color MineInk   = new(1f, 0.82f, 0.30f, 1f);
    private static readonly Color BandGold  = new(1f, 0.80f, 0.30f, 0.12f);
    private static readonly Color TabOn     = new(1f, 0.82f, 0.30f, 1f);

    // ── Static ────────────────────────────────────────────────
    private static Sprite s_tablet;
    private static readonly Sprite[] s_medals = new Sprite[3];
    private static bool s_artTried;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { s_tablet = null; s_artTried = false; Array.Clear(s_medals, 0, s_medals.Length); }

    private struct RowAnim { public CanvasGroup Group; public RectTransform Rt, Medal; public float At, Y; }

    // ── Private ───────────────────────────────────────────────
    private readonly List<RowAnim> _anims = new(12);
    private readonly TMP_Text[] _eraLabels  = new TMP_Text[2];
    private readonly TMP_Text[] _kindLabels = new TMP_Text[3];
    private readonly TMP_Text[] _chipLabels = new TMP_Text[ChapterCount];
    private Action        _onBack;
    private CanvasGroup   _group;
    private RectTransform _board, _rows, _chips;
    private Image         _boardImg, _myBand;
    private TMP_Text      _title, _live, _status, _footer, _clockNote;
    private StoryEra _era = StoryEra.Liberated;
    private int   _kind, _chapter = 1, _loadSeq;   // 종류 0 클리어 시간 · 1 보스전 · 2 챕터 전투
    private float _openedAt, _nextRefresh = -1f;
    private bool  _loading;

    // ── Lifecycle ─────────────────────────────────────────────
    private void Update()
    {
        float now = Time.unscaledTime;
        float k = Mathf.Clamp01((now - _openedAt) / OpenSec);
        float e = 1f - (1f - k) * (1f - k);
        _group.alpha = e;
        _board.localScale = Vector3.one * Mathf.Lerp(0.965f, 1f, e);

        for (int i = 0; i < _anims.Count; i++)
        {
            var a = _anims[i];
            if (a.Rt == null) continue;
            float kk = Mathf.Clamp01((now - a.At) / RowIn);
            float ee = 1f - (1f - kk) * (1f - kk);
            a.Group.alpha = kk;
            a.Rt.anchoredPosition = new Vector2(Mathf.Lerp(-16f, 0f, ee), a.Y);
            if (a.Medal != null) a.Medal.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, ee);
        }

        if (_myBand != null)
        {
            var c = BandGold;
            c.a = 0.08f + 0.07f * (0.5f + 0.5f * Mathf.Sin(now * 3.2f));   // 내 줄 — 천천히 숨 쉰다
            _myBand.color = c;
        }

        if (!_loading && _nextRefresh > 0f && now >= _nextRefresh) LoadAsync(false).Forget();
    }

    // ── Public Methods ────────────────────────────────────────
    /// <summary><paramref name="canvasRoot"/>(1920×1080 캔버스)를 꽉 채우는 쪽을 짓는다 — 꺼진 채로 돌려준다.</summary>
    public static UI_RankingPage Create(Transform canvasRoot, Action onBack)
    {
        var go = new GameObject("RankingPage", typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(canvasRoot, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        var page = go.AddComponent<UI_RankingPage>();
        page._onBack = onBack;
        page._group  = go.GetComponent<CanvasGroup>();
        page._era    = StoryProgress.Era == StoryEra.NightmareMode ? StoryEra.NightmareMode : StoryEra.Liberated;   // 처음 = 지금 시기(봉인기면 해방기)
        page.Build();
        go.SetActive(false);
        return page;
    }

    /// <summary>연다 — <paramref name="era"/>를 주면 그 시기 클리어 시간(비석), 없으면 마지막에 보던 판. 남은 올리기(해방기 · 악몽)를 먼저 올린 뒤 다시 읽는다.</summary>
    public void Open(StoryEra? era = null)
    {
        if (era is { } e && e != StoryEra.Sealed) { _era = e; _kind = 0; }
        gameObject.SetActive(true);
        _openedAt = Time.unscaledTime;
        Apply();
        ShowTab(_era, _kind, _chapter);
        PrepareAsync().Forget();
    }

    public void Close()
    {
        _nextRefresh = -1f;
        _loadSeq++;   // 읽던 것은 버린다
        gameObject.SetActive(false);
    }

    /// <summary>지금 탭을 다시 읽는다(줄 연출 없이).</summary>
    public void Refresh() => LoadAsync(false).Forget();

    // ── Private Methods ───────────────────────────────────────
    private void Build()
    {
        var root = (RectTransform)transform;
        _board = (RectTransform)UICodeOverlay.Img(root, "Board", new Vector2(0f, BoardY), new Vector2(BoardW, BoardH), UITheme.Window).transform;
        _boardImg = _board.GetComponent<Image>();
        _boardImg.raycastTarget = true;   // 판 위 클릭은 뒤로 새지 않는다
        UICodeOverlay.Line(_board, new Color(UIPalette.Gold.r, UIPalette.Gold.g, UIPalette.Gold.b, 0.45f));

        float h = BoardH * 0.5f;
        _title = UICodeOverlay.Txt(_board, "랭킹", 34f, LibAccent, new Vector2(0f, h - 136f), 820f);
        _title.textWrappingMode = TextWrappingModes.NoWrap;   // 영어 「Chapter 1 Boss Fight — time fighting …」가 넘쳤다 — 한 줄로 줄인다
        _title.enableAutoSizing = true;
        _title.fontSizeMin = 22f;
        _title.fontSizeMax = 34f;
        _live  = UICodeOverlay.Txt(_board, "", 16f, UICodeOverlay.Mist, new Vector2(0f, h - 170f), 860f);

        // 시기 둘 | 종류 셋 — 한 줄(사이를 옅은 세로줄로 가른다)
        for (int i = 0; i < EraNames.Length; i++)
        {
            var era = i == 0 ? StoryEra.Liberated : StoryEra.NightmareMode;
            var b = UICodeOverlay.Btn(_board, EraNames[i], new Vector2(-332f + i * TabStep, h - 210f), new Vector2(TabW, 38f), 18f, () => PickEra(era), quiet: true);
            _eraLabels[i] = b.GetComponentInChildren<TMP_Text>();
        }
        var mist = UICodeOverlay.Mist;
        UICodeOverlay.Img(_board, "TabRule", new Vector2(-79f, h - 210f), new Vector2(1f, 26f), new Color(mist.r, mist.g, mist.b, 0.35f)).raycastTarget = false;
        for (int i = 0; i < KindNames.Length; i++)
        {
            int kind = i;
            var b = UICodeOverlay.Btn(_board, KindNames[i], new Vector2(16f + i * TabStep, h - 210f), new Vector2(TabW, 38f), 18f, () => PickKind(kind), quiet: true);
            _kindLabels[i] = b.GetComponentInChildren<TMP_Text>();
        }

        _chips = (RectTransform)new GameObject("Chapters", typeof(RectTransform)).transform;
        _chips.SetParent(_board, false);
        _chips.anchoredPosition = new Vector2(0f, h - 246f);
        for (int c = 1; c <= ChapterCount; c++)
        {
            int ch = c;
            var b = UICodeOverlay.Btn(_chips, Loc.F("챕터 {0}", c), new Vector2(-165f + (c - 1) * 110f, 0f), new Vector2(100f, 30f), 16f, () => PickChapter(ch), quiet: true);
            _chipLabels[c - 1] = b.GetComponentInChildren<TMP_Text>();
        }
        // 11-05 사용자 「시간도 제대로 파악할 수 없는 숫자」 — 클리어 시간 판은 챕터 칩 자리에 무엇을 센 시간인지 한 줄
        _clockNote = UICodeOverlay.Txt(_board, "런 시작부터 리치 처치까지 실제로 흐른 시간 — 일시정지 · 고르기 창 · 대사 · 컷신 · 로딩은 뺀다",
                                       15f, UICodeOverlay.Mist, new Vector2(0f, h - 246f), 860f);
        _clockNote.textWrappingMode = TextWrappingModes.NoWrap;
        _clockNote.enableAutoSizing = true;
        _clockNote.fontSizeMin = 11f;
        _clockNote.fontSizeMax = 15f;

        float headY = RowTop + RowH;
        Cell(_board, "순위", RankX,   headY, RankW,   15f, UICodeOverlay.Mist, TextAlignmentOptions.Center);
        Cell(_board, "이름", NameX,   headY, NameW,   15f, UICodeOverlay.Mist, TextAlignmentOptions.Center);
        Cell(_board, "시간", TimeX,   headY, TimeW,   15f, UICodeOverlay.Mist, TextAlignmentOptions.Center);
        Cell(_board, "무기", WeaponX, headY, WeaponW, 15f, UICodeOverlay.Mist, TextAlignmentOptions.Center);

        _rows = (RectTransform)new GameObject("Rows", typeof(RectTransform)).transform;
        _rows.SetParent(_board, false);
        _status = UICodeOverlay.Txt(_board, "", 19f, UICodeOverlay.Mist, new Vector2(0f, -70f), 820f);

        // 판 밖 아래 — 내 기록 · 칭호 한 줄 / 단추
        _footer = UICodeOverlay.Txt(root, "", 21f, UITheme.Gold, new Vector2(0f, BoardY - h - 26f), 1100f);
        UICodeOverlay.Btn(root, "새로고침", new Vector2(-120f, BoardY - h - 74f), new Vector2(180f, 42f), 18f, Refresh, quiet: true);
        UICodeOverlay.Btn(root, "돌아가기", new Vector2(110f, BoardY - h - 74f), new Vector2(200f, 46f), 20f, () => _onBack?.Invoke());
    }

    /// <summary>석판 · 메달 그림 — 처음 열 때 한 번 읽는다(없으면 얇은 창 그대로).</summary>
    private async UniTaskVoid LoadArtAsync()
    {
        if (s_artTried || Managers.AddressableManager == null) return;
        s_artTried = true;
        try
        {
            s_tablet = await Managers.AddressableManager.TryLoadAssetAsync<Sprite>(TabletKey);
            for (int i = 0; i < MedalKeys.Length; i++)
                s_medals[i] = await Managers.AddressableManager.TryLoadAssetAsync<Sprite>(MedalKeys[i]);
        }
        catch (OperationCanceledException) { return; }
        if (this != null) Apply();
    }

    private void Apply()
    {
        if (!s_artTried) LoadArtAsync().Forget();
        if (s_tablet == null || _boardImg.sprite == s_tablet) return;
        _boardImg.sprite = s_tablet;
        _boardImg.type   = Image.Type.Simple;
        _boardImg.color  = Color.white;
        var line = _board.Find("Line");
        if (line != null) line.gameObject.SetActive(false);   // 석판에 금 테가 있다
    }

    /// <summary>남은 올리기(해방기 · 악몽)를 먼저 올리고 다시 읽는다 — 기다리지 않고 판부터 보여 준다.</summary>
    private async UniTaskVoid PrepareAsync()
    {
        try
        {
            bool lib = await RankingService.ResolveAsync(StoryEra.Liberated);
            bool nm  = await RankingService.ResolveAsync(StoryEra.NightmareMode);
            if (lib || nm) await RankingService.RetryPendingAsync();
        }
        catch (OperationCanceledException) { return; }
        if (this != null && gameObject.activeInHierarchy && !_loading) LoadAsync(false).Forget();
    }

    private void PickEra(StoryEra era)
    {
        if (era == _era) return;
        Managers.Sound?.PlayUiEvent(SoundEvent.UiSelect);
        ShowTab(era, _kind, _chapter);
    }

    private void PickKind(int kind)
    {
        if (kind == _kind) return;
        Managers.Sound?.PlayUiEvent(SoundEvent.UiSelect);
        ShowTab(_era, kind, _chapter);
    }

    private void PickChapter(int chapter)
    {
        if (chapter == _chapter) return;
        Managers.Sound?.PlayUiEvent(SoundEvent.UiSelect);
        ShowTab(_era, _kind, chapter);
    }

    private void ShowTab(StoryEra era, int kind, int chapter)
    {
        _era  = era;
        _kind = kind;
        _chapter = Mathf.Clamp(chapter, 1, ChapterCount);
        _chips.gameObject.SetActive(kind > 0);
        _clockNote.gameObject.SetActive(kind == 0);
        bool nm = era == StoryEra.NightmareMode;
        for (int i = 0; i < _eraLabels.Length; i++)
            if (_eraLabels[i] != null) _eraLabels[i].color = (i == 1) == nm ? TabOn : UICodeOverlay.Mist;
        for (int i = 0; i < _kindLabels.Length; i++)
            if (_kindLabels[i] != null) _kindLabels[i].color = i == kind ? TabOn : UICodeOverlay.Mist;
        for (int c = 0; c < _chipLabels.Length; c++)
            if (_chipLabels[c] != null) _chipLabels[c].color = c + 1 == _chapter ? TabOn : UICodeOverlay.Mist;
        _title.text  = TitleOf(era, kind, _chapter);
        _title.color = nm ? NmAccent : LibAccent;
        LoadAsync(true).Forget();
    }

    private static string EraName(StoryEra era) => era == StoryEra.NightmareMode ? Loc.T("악몽") : Loc.T("해방기");

    private static string TitleOf(StoryEra era, int kind, int chapter) => kind switch
    {
        0 => era == StoryEra.NightmareMode ? Loc.T("악몽 — 클리어 시간") : Loc.T("해방기 — 클리어 시간"),
        1 => Loc.F("{0} · 챕터 {1} 보스전 — 보스방에서 싸운 시간", EraName(era), chapter),
        _ => Loc.F("{0} · 챕터 {1} 전투 — 그 챕터 방들에서 싸운 시간", EraName(era), chapter),
    };

    private static string TableOf(StoryEra era, int kind, int chapter) => kind switch
    {
        0 => era == StoryEra.NightmareMode ? RankingService.NightmareTable : RankingService.LiberationTable,
        _ => RecordBoard.TableOf(kind == 1, chapter, era),
    };

    /// <summary>지금 탭을 뒤끝에서 읽어 그린다 — 늦게 온 답(탭을 바꾼 뒤)은 버린다 · 6초 넘으면 「닿지 않는다」.</summary>
    private async UniTaskVoid LoadAsync(bool animate)
    {
        int seq = ++_loadSeq;
        var era  = _era;
        int kind = _kind, chapter = _chapter;
        _loading = true;
        if (animate)
        {
            ClearRows();
            _status.text = Loc.T("기록을 불러오는 중…");
            _footer.text = FooterOf(era, kind, chapter, null);
        }
        RankingService.Board board;
        bool answered;
        try
        {
            (answered, board) = await UniTask.WhenAny(RankingService.LoadBoardAsync(TableOf(era, kind, chapter)),
                                                      UniTask.Delay(TimeSpan.FromSeconds(LoadTimeout), ignoreTimeScale: true, cancellationToken: destroyCancellationToken));
        }
        catch (OperationCanceledException) { return; }
        if (this == null || seq != _loadSeq) return;
        _loading = false;
        _nextRefresh = Time.unscaledTime + RefreshSec;
        if (!answered) { if (animate || _anims.Count == 0) _status.text = Loc.T("랭킹 서버에 닿지 않는다 — 잠시 뒤 다시 읽는다"); return; }
        Render(board, era, kind, chapter, animate);
    }

    private void Render(RankingService.Board board, StoryEra era, int kind, int chapter, bool animate)
    {
        ClearRows();
        _footer.text = FooterOf(era, kind, chapter, board);
        // 11-03 사용자 「실시간일 때 현재 몇 위인지도」 — 다시 읽을 때마다(30초 · 새로고침) 지금 내 순위
        string at = DateTime.Now.ToString("HH:mm:ss");
        _live.text = !board.Ready ? Loc.T("모든 플레이어의 기록이 모이는 곳")
            : board.Mine is { } mine ? Loc.F("실시간 · 전체 {0}명 · <color=#FFD147>지금 내 순위 {1}위</color> · {2} 갱신", Mathf.Max(board.Total, mine.Rank), mine.Rank, at)
            : Loc.F("실시간 · 전체 {0}명 · 내 기록 없음 · {1} 갱신", board.Total, at);
        if (!board.Ready) { _status.text = Loc.T("이 랭킹은 아직 준비 중 — 내 기록은 기기에 남아 있다"); return; }
        if (board.Top.Count == 0) { _status.text = Loc.T("아직 아무도 기록을 남기지 않았다 — 첫 이름이 되어 보자"); return; }
        _status.text = "";

        float now = Time.unscaledTime;
        for (int i = 0; i < board.Top.Count; i++)
            AddRow(board.Top[i], RowTop - RowH * i, animate ? now + 0.05f + i * RowStagger : now - RowIn);
        if (board.Mine is { } me && me.Rank > board.Top.Count)
        {
            float y = RowTop - RowH * board.Top.Count - 10f;
            Cell(_rows, "⋯", RankX, y + 6f, RankW, 16f, UICodeOverlay.Mist, TextAlignmentOptions.Center);
            AddRow(me, y - RowH * 0.7f, animate ? now + 0.05f + board.Top.Count * RowStagger + 0.08f : now - RowIn);
        }
    }

    /// <summary>판 밖 아래 한 줄 — 클리어 시간은 칭호(이번 기록 · 다음 칭호), 보스전 · 챕터 전투는 그 시기 내 최고 · 내 순위.</summary>
    private string FooterOf(StoryEra era, int kind, int chapter, RankingService.Board? board)
    {
        if (kind == 0) return RankingBoardOverlay.RecordLine(era) ?? "";
        int best = RecordBoard.PersonalBest(kind == 1, chapter, era);
        if (best <= 0) return Loc.F("<color=#9A98A0>아직 내 기록이 없다 — 챕터 {0} 보스를 쓰러뜨리면 남는다</color>", chapter);
        if (board is { Ready: true, Mine: { } me })
            return Loc.F("내 최고  {0}   ·   {1}위 / {2}명", RankingService.Format(best), me.Rank, Mathf.Max(board.Value.Total, me.Rank));
        return Loc.F("내 최고  {0}", RankingService.Format(best));
    }

    private void AddRow(RankingService.Entry e, float y, float at)
    {
        var go = new GameObject("Row", typeof(RectTransform), typeof(CanvasGroup));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_rows, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(ColW, RowH);
        rt.anchoredPosition = new Vector2(0f, y);
        var group = go.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;

        Color ink = e.IsMine ? MineInk : UICodeOverlay.Ink;
        if (e.IsMine)
        {
            _myBand = UICodeOverlay.Img(rt, "MineBand", Vector2.zero, new Vector2(BandW, RowH - 2f), BandGold);
            _myBand.raycastTarget = false;
        }
        RectTransform medal = null;
        var art = e.Rank >= 1 && e.Rank <= 3 ? s_medals[e.Rank - 1] : null;
        if (art != null)
        {
            var m = UICodeOverlay.Img(rt, "Medal", new Vector2(RankX, 0f), new Vector2(MedalSize, MedalSize), Color.white);
            m.sprite = art;
            m.preserveAspect = true;
            m.raycastTarget = false;
            medal = m.rectTransform;
        }
        float size = 19f;
        Cell(rt, e.Rank.ToString(), RankX, 0f, RankW, art != null ? 17f : size, art != null ? MedalInk : ink, TextAlignmentOptions.Center);
        Cell(rt, DisplayName(e.Nickname), NameX, 0f, NameW, size, ink, TextAlignmentOptions.Center);
        Cell(rt, RankingService.Format(e.Centis), TimeX, 0f, TimeW, size, ink, TextAlignmentOptions.Center);
        Cell(rt, e.Weapon, WeaponX, 0f, WeaponW, size - 3f, UICodeOverlay.Mist, TextAlignmentOptions.Center);
        _anims.Add(new RowAnim { Group = group, Rt = rt, Medal = medal, At = at, Y = y });
    }

    /// <summary>같은 스팀 이름이 있어 붙은 번호(「bg000727#4257」)는 옅고 작게 — 이름이 먼저 읽히게(11-05).</summary>
    private static string DisplayName(string nick)
    {
        int i = nick != null ? nick.LastIndexOf('#') : -1;
        if (i <= 0 || i != nick.Length - 5) return nick;
        for (int k = i + 1; k < nick.Length; k++) if (!char.IsDigit(nick[k])) return nick;
        return nick.Substring(0, i) + "<size=80%><color=#8A8894>" + nick.Substring(i) + "</color></size>";
    }

    private void ClearRows()
    {
        for (int i = _rows.childCount - 1; i >= 0; i--) Destroy(_rows.GetChild(i).gameObject);
        _anims.Clear();
        _myBand = null;
    }

    /// <summary>한 칸 글 — 한 줄 · 넘치면 「…」.</summary>
    private static TMP_Text Cell(RectTransform parent, string text, float x, float y, float w, float size, Color color, TextAlignmentOptions align)
    {
        var t = UICodeOverlay.Txt(parent, text, size, color, new Vector2(x, y), w, align: align);
        t.rectTransform.sizeDelta = new Vector2(w, RowH);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }
}
