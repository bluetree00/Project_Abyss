using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보스 처치 기록 카드(11-02 사용자 「실시간 랭킹 팝업도 제대로 된 연출과 구성으로 플레이어들에게 공유될 수 있게」) — 유물 기억 카드 뒤
/// 오른쪽에서 밀려 들어와 이번 보스전 · 챕터 전투 시간(숫자가 차오른다) · 신기록 도장 · 모든 플레이어 중 내 순위(전체 인원) · 1위를 보여 주고 물러난다.
/// 예전엔 HUD 알림 줄 둘(「보스전 0:52.41 신기록 · 1등 …」)이라 지나가 버렸다. 조작을 막지 않는다(입력 · 클릭 통과) · 실제 시간으로 돈다.
/// 순위는 올리기(<see cref="RecordBoard"/>)가 끝난 뒤 뒤끝에서 읽어 채운다 — 표가 없거나 닿지 않으면 「내 기록은 기기에」.
/// </summary>
public sealed class UI_RecordCard : MonoBehaviour
{
    /// <summary>카드 한 줄 — 이름 · 이번 시간(1/100초 · 0이면 줄을 뺀다) · 신기록 · 지난 내 최고 · 뒤끝 표 이름.</summary>
    public readonly struct Line
    {
        public readonly string Label;
        public readonly int    Centis, Best;
        public readonly bool   IsNew;
        public readonly string Table;
        public Line(string label, int centis, bool isNew, int best, string table)
        { Label = label; Centis = centis; IsNew = isNew; Best = best; Table = table; }
    }

    // ── Constants ─────────────────────────────────────────────
    private const float CardW = 470f, HeadH = 46f, BlockH = 104f, FootH = 36f, EdgeX = 28f, CardY = 70f;
    private const float SlideIn = 0.35f, CountUp = 0.6f, StampAt = 0.75f, StampDur = 0.16f, SlideOut = 0.3f;
    private const float HoldMin = 7f, HoldAfterRank = 3.5f, HoldMax = 11f, RankTimeout = 4f;
    private static readonly Color CardC  = new(0.055f, 0.051f, 0.086f, 0.94f);
    private static readonly Color GoldC  = new(1f, 0.82f, 0.30f, 1f);
    private static readonly Color LabelC = new(0.94f, 0.91f, 0.85f, 0.78f);

    // ── Static ────────────────────────────────────────────────
    private static UI_RecordCard s_current;

    private struct Block { public Line Data; public TMP_Text Time, Rank; public RectTransform Stamp; }

    // ── Private ───────────────────────────────────────────────
    private readonly Block[] _blocks = new Block[2];
    private int           _count;
    private RectTransform _card;
    private CanvasGroup   _group;
    private float         _ranksAt = -1f;   // 순위를 채운 때(실제 시간) — 그 뒤 조금 더 머문다

    // ── Lifecycle ─────────────────────────────────────────────
    private void OnDestroy() { if (s_current == this) s_current = null; }

    // ── Public Methods ────────────────────────────────────────
    /// <summary>카드를 띄운다(앞 카드는 걷는다). <paramref name="submitted"/>가 끝나면(최대 4초) 순위를 읽는다.</summary>
    public static void Show(int chapter, Line boss, Line rooms, UniTask submitted)
    {
        if (boss.Centis <= 0 && rooms.Centis <= 0) return;
        if (s_current != null) Destroy(s_current.gameObject);
        var (go, root, group) = UICodeOverlay.Create("@RecordCard", UISortingOrder.HudIndicator + 10);
        group.blocksRaycasts = false;   // 조작 · 클릭을 막지 않는다
        group.interactable   = false;
        DontDestroyOnLoad(go);          // 보스방 → 출구 계단으로 씬이 바뀌어도 끝까지
        var card = go.AddComponent<UI_RecordCard>();
        card._group = group;
        card.Build(root, chapter, boss, rooms);
        s_current = card;
        card.RunAsync(submitted).Forget();
    }

    // ── Private Methods ───────────────────────────────────────
    private void Build(RectTransform root, int chapter, Line boss, Line rooms)
    {
        _count = 0;
        if (boss.Centis > 0)  _blocks[_count++] = new Block { Data = boss };
        if (rooms.Centis > 0) _blocks[_count++] = new Block { Data = rooms };
        float h = HeadH + BlockH * _count + FootH;

        _card = (RectTransform)UICodeOverlay.Img(root, "Card", Vector2.zero, new Vector2(CardW, h), CardC).transform;
        _card.anchorMin = _card.anchorMax = new Vector2(1f, 0.5f);
        _card.pivot = new Vector2(1f, 0.5f);
        _card.anchoredPosition = new Vector2(-EdgeX, CardY);
        _card.GetComponent<Image>().raycastTarget = false;
        UICodeOverlay.Line(_card, new Color(GoldC.r, GoldC.g, GoldC.b, 0.5f));

        float top = h * 0.5f;
        var head = UICodeOverlay.Txt(_card, Loc.F("챕터 {0} · 기록", chapter), 17f, UICodeOverlay.Mist, new Vector2(0f, top - 24f), CardW - 48f, align: TextAlignmentOptions.Left);
        head.rectTransform.sizeDelta = new Vector2(CardW - 48f, 30f);
        var rule = UICodeOverlay.Img(_card, "Rule", new Vector2(0f, top - HeadH + 2f), new Vector2(CardW - 48f, 1f), new Color(GoldC.r, GoldC.g, GoldC.b, 0.25f));
        rule.raycastTarget = false;

        for (int i = 0; i < _count; i++)
        {
            float y = top - HeadH - BlockH * i;
            ref var b = ref _blocks[i];
            Left(_card, b.Data.Label, 18f, LabelC, new Vector2(0f, y - 20f), 26f);
            b.Time = Left(_card, RankingService.Format(0), 40f, b.Data.IsNew ? GoldC : UICodeOverlay.Ink, new Vector2(0f, y - 54f), 48f);
            b.Rank = Left(_card, Loc.T("순위 불러오는 중…"), 16f, UICodeOverlay.Mist, new Vector2(0f, y - 88f), 24f);
            if (b.Data.IsNew)
            {
                // 신기록 도장 — 금선 상자 + 금 글(맺힐 때 한 번 커졌다 제자리)
                var box = UICodeOverlay.Img(_card, "Stamp", new Vector2(CardW * 0.5f - 24f - 66f, y - 52f), new Vector2(132f, 40f), new Color(GoldC.r, GoldC.g, GoldC.b, 0.10f));
                box.raycastTarget = false;
                UICodeOverlay.Line(box.rectTransform, GoldC);
                UICodeOverlay.Txt(box.rectTransform, "신기록", 20f, GoldC, Vector2.zero, 132f);
                b.Stamp = box.rectTransform;
                b.Stamp.localScale = Vector3.zero;
            }
            else if (b.Data.Best > 0)
            {
                var best = UICodeOverlay.Txt(_card, Loc.F("내 최고  {0}", RankingService.Format(b.Data.Best)), 16f, UICodeOverlay.Mist,
                                             new Vector2(CardW * 0.5f - 24f - 90f, y - 54f), 180f, align: TextAlignmentOptions.Right);
                best.rectTransform.sizeDelta = new Vector2(180f, 26f);
            }
        }
        var foot = UICodeOverlay.Txt(_card, "Esc › 랭킹 — 모든 플레이어의 기록", 14f, UICodeOverlay.Mist, new Vector2(0f, -h * 0.5f + 20f), CardW - 48f,
                                     align: TextAlignmentOptions.Left);
        foot.rectTransform.sizeDelta = new Vector2(CardW - 48f, 22f);
        _group.alpha = 0f;
    }

    /// <summary>들어옴 → 숫자 차오름 → 도장 → 순위 채움 → 머묾 → 물러남.</summary>
    private async UniTaskVoid RunAsync(UniTask submitted)
    {
        var ct = destroyCancellationToken;
        try
        {
            bool anyNew = _blocks[0].Data.IsNew || (_count > 1 && _blocks[1].Data.IsNew);
            Managers.Sound?.PlayUiEvent(SoundEvent.UiOpen);
            FillRanksAsync(submitted).Forget();

            float t0 = Time.unscaledTime;
            bool stamped = false;
            while (true)
            {
                float t = Time.unscaledTime - t0;
                float k = Mathf.Clamp01(t / SlideIn);
                float e = 1f - (1f - k) * (1f - k) * (1f - k);
                _card.anchoredPosition = new Vector2(-EdgeX + (1f - e) * (CardW + EdgeX), CardY);
                _group.alpha = e;

                float c = Mathf.Clamp01((t - SlideIn * 0.5f) / CountUp);
                float ce = 1f - (1f - c) * (1f - c);
                for (int i = 0; i < _count; i++) _blocks[i].Time.text = RankingService.Format(Mathf.RoundToInt(_blocks[i].Data.Centis * ce));

                if (anyNew && t >= StampAt)
                {
                    if (!stamped) { stamped = true; Managers.Sound?.PlayUiEvent(SoundEvent.QuestDone); }
                    float s = Mathf.Clamp01((t - StampAt) / StampDur);
                    for (int i = 0; i < _count; i++)
                        if (_blocks[i].Stamp != null) _blocks[i].Stamp.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, s) * Mathf.Min(1f, s * 4f);
                }
                if (k >= 1f && c >= 1f && (!anyNew || t >= StampAt + StampDur)) break;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            // 머묾 — 순위가 채워지면 조금 더(최소 7초 · 최대 11초)
            while (true)
            {
                float t = Time.unscaledTime - t0;
                if (t >= HoldMax || (t >= HoldMin && (_ranksAt > 0f ? Time.unscaledTime - _ranksAt >= HoldAfterRank : t >= HoldMin + 2f))) break;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            float o0 = Time.unscaledTime;
            while (true)
            {
                float k = Mathf.Clamp01((Time.unscaledTime - o0) / SlideOut);
                float e = k * k;
                _card.anchoredPosition = new Vector2(-EdgeX + e * (CardW + EdgeX), CardY);
                _group.alpha = 1f - e;
                if (k >= 1f) break;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { return; }
        if (this != null) Destroy(gameObject);
    }

    /// <summary>올리기가 끝나면(최대 4초) 표마다 내 순위 · 전체 · 1위를 읽어 줄을 채운다.</summary>
    private async UniTaskVoid FillRanksAsync(UniTask submitted)
    {
        var ct = destroyCancellationToken;
        try
        {
            await UniTask.WhenAny(submitted, UniTask.Delay(TimeSpan.FromSeconds(RankTimeout), ignoreTimeScale: true, cancellationToken: ct));
            for (int i = 0; i < _count; i++)
            {
                var (answered, board) = await UniTask.WhenAny(RankingService.LoadBoardAsync(_blocks[i].Data.Table, 1),
                                                              UniTask.Delay(TimeSpan.FromSeconds(RankTimeout), ignoreTimeScale: true, cancellationToken: ct));
                if (this == null) return;
                _blocks[i].Rank.text = RankLine(answered, board, _blocks[i].Data.IsNew);
            }
            _ranksAt = Time.unscaledTime;
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>「23위 / 128명 · 1위 이름 0:31.00」 · 내가 1위면 「1위 / 128명 — 모든 플레이어 중 가장 빠르다」.</summary>
    private static string RankLine(bool answered, RankingService.Board board, bool isNew)
    {
        if (!answered) return Loc.T("랭킹 서버에 닿지 않는다 — 내 기록은 기기에 남았다");
        if (!board.Ready) return Loc.T("이 랭킹은 아직 준비 중 — 내 기록은 기기에 남았다");
        if (board.Mine is not { } me)
            return isNew ? Loc.T("랭킹에 올리지 못했다 — 다음 신기록 때 다시 올린다") : Loc.T("랭킹에 아직 내 기록이 없다 — 신기록을 세우면 오른다");
        int total = Mathf.Max(board.Total, me.Rank);
        if (me.Rank == 1) return Loc.F("<color=#FFD147>1위</color> / {0}명 — 모든 플레이어 중 가장 빠르다", total);
        if (board.Top.Count > 0)
            return Loc.F("{0}위 / {1}명  ·  1위 {2} {3}", me.Rank, total, board.Top[0].Nickname, RankingService.Format(board.Top[0].Centis));
        return Loc.F("{0}위 / {1}명", me.Rank, total);
    }

    /// <summary>
    /// 카드 왼쪽 맞춤 한 줄(안쪽 여백 24). 칸 높이는 글꼴 줄 높이(≈ 크기 × 1.45)보다 넉넉히 — 낮으면 TMP가 줄을 통째로 숨긴다
    /// (11-02 사진: 이름 18/26 · 시간 40/48이 안 보였다). 넘치는 영어는 줄여 한 줄에(최소 12).
    /// </summary>
    private static TMP_Text Left(RectTransform card, string text, float size, Color color, Vector2 pos, float h)
    {
        var t = UICodeOverlay.Txt(card, text, size, color, new Vector2(0f, pos.y), CardW - 48f, align: TextAlignmentOptions.Left);
        t.rectTransform.sizeDelta = new Vector2(CardW - 48f, Mathf.Max(h, size * 1.7f));
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.enableAutoSizing = true;
        t.fontSizeMin = Mathf.Min(size, 12f);
        t.fontSizeMax = size;
        return t;
    }
}
