using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 런 결과창(10-07, 사용자 「든 시간 · 얻은 재화 · 어떤 빌드를 사용했는지 등 종합적으로」) — 톤 v2 「얇은 금선」 첫 화면.
/// 자기 캔버스를 짓는다 — 클리어 문구 · 멀린 부활과 같은 런 경계 층(<see cref="UISortingOrder.RunBoundary"/>, 팝업 400 · 암전 850 위).
/// 시간정지 중에도 돈다(unscaled). 1초 뒤부터 [계속] · Space · Enter로 넘어간다.
/// 등장: 머리 페이드 → 챕터 띠가 왼→오로 그려짐 0.8초 → 숫자 카운트업 0.6초 → 세 단 0.08초 간격 → [계속] 테가 그려지며 켜짐.
/// 데이터 = <see cref="RunResultSnapshot"/>(Provider), 글자 = <see cref="RunResultPresenter"/>.
/// </summary>
public sealed class RunResultView : MonoBehaviour
{
    // ── Constants ──
    private const float InputDelay = 1.0f;
    private const float RevealEnd  = 1.7f;
    private const float FadeOutSec = 0.2f;
    private const float CountSoundAt = 0.5f;   // 카운트업 소리 — ApplyReveal의 숫자 카운트업 시작(0.5초)과 같은 때 한 번
    private const float L = 128f, R = 1792f;   // 무대 좌우 여백(1920 - 128)
    private const float ColW = 520f, ColGap = 56f, ColTop = 404f;
    private const float TimelineY = 318f;
    // 10-11 대비 · 단계(리서치 C §5 「고칠 값」): 정보 글 알파 바닥 0.55(바탕 #0C0C0C 위 5.2:1 — 4.5:1에 여유를 둔다,
    // 「아직 안 간 챕터」 0.32는 비활성 표시라 예외) · 작은 라벨 13 → 15 Medium 자간 25 → 15 · 행 글자 17 → 20(줄 간격 40)
    private const float InfoA = 0.55f;
    private const float LabelSpacing = 15f;
    private const float RowPitch = 40f;

    // ── Static ──
    /// <summary>지금 떠 있는 결과창(실측 도구용). 없으면 null.</summary>
    public static RunResultView Active { get; private set; }
    /// <summary>마지막으로 띄운 사본(실측 도구용).</summary>
    public static RunResultSnapshot LastShown { get; private set; }

    // ── Private ──
    private RunResultSnapshot _snap;
    private CanvasGroup _root, _header, _timeline, _footer;
    private readonly CanvasGroup[] _cols = new CanvasGroup[3];
    private readonly float[] _colBaseY = new float[3];
    private Image _timelineFill;
    private float _doneFrac;
    private readonly List<(float at, CanvasGroup node)> _nodes = new();
    private readonly List<(TMP_Text label, long value, string format)> _counters = new();
    private readonly List<long> _counterShown = new();   // 등장 중 글자는 값이 바뀔 때만 새로 쓴다(매 프레임 문자열 할당 방지)
    private UIThinFrame _continueFrame;
    private float _stroke = 1f;
    private float _shownAt;
    private bool _continue;
    private bool _continueLit;
    private bool _countSounded;

    // ── Lifecycle ──
    private void OnDestroy()
    {
        if (Active == this) Active = null;
    }

    // ── Public Methods ──

    /// <summary>결과창을 띄우고 [계속]을 누를 때까지 기다린 뒤 닫는다(파괴까지).</summary>
    public static async UniTask ShowAsync(RunResultSnapshot snap, CancellationToken ct)
    {
        if (snap == null) return;
        var go = new GameObject("@RunResult");
        var view = go.AddComponent<RunResultView>();
        Active = view;
        LastShown = snap;
        var revealCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            view.Build(snap);
            view._shownAt = Time.unscaledTime;
            Managers.Sound?.PlayUiEvent(SoundEvent.ResultReveal);   // 등장할 때 한 번(10-12 Z1)
            view.RevealAsync(revealCts.Token).Forget();
            await view.WaitContinueAsync(ct);
            Managers.Sound?.PlayUiEvent(SoundEvent.UiConfirm);      // [계속] — 클릭 · 키 어느 길이든
            revealCts.Cancel();
            view.ApplyReveal(RevealEnd);   // 남은 등장은 끝 상태로
            await UIMotion.Tween(FadeOutSec, k => view._root.alpha = 1f - k, ct);
        }
        finally
        {
            revealCts.Cancel();
            revealCts.Dispose();
            if (go != null) Destroy(go);
        }
    }

    /// <summary>[계속] — 1초가 지난 뒤에만 받는다(사망 직후 연타로 넘어가 버리는 것 방지).</summary>
    public void Continue()
    {
        if (Time.unscaledTime - _shownAt >= InputDelay) _continue = true;
    }

    /// <summary>선 굵기(무대 단위) — 무대 축소(StageFit)까지 곱한 배율에서 최소 1 물리 픽셀(4:3 · 16:10에서 1px 밑으로 안 내려가게).</summary>
    public static float LineStroke(float screenW, float screenH)
        => UITheme.Thin.PixelStroke(UITheme.Thin.CanvasScale(screenW, screenH) * StageFit(screenW, screenH));

    /// <summary>1920×1080 무대를 화면에 맞추는 배율 — 4:3처럼 캔버스가 1920보다 좁으면 줄인다(넓으면 1).</summary>
    public static float StageFit(float screenW, float screenH)
    {
        float k = UITheme.Thin.CanvasScale(screenW, screenH);
        return Mathf.Min(1f, screenW / k / 1920f, screenH / k / 1080f);
    }

    // ── Private Methods — 짓기 ──

    private void Build(RunResultSnapshot s)
    {
        _snap = s;
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = UISortingOrder.RunBoundary;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        _root = gameObject.AddComponent<CanvasGroup>();
        _stroke = LineStroke(Screen.width, Screen.height);

        var bg = ShopUIStyle.MakeImage(transform, "Backdrop", UITheme.Thin.Backdrop, raycast: true);   // 아래 화면 클릭 막기
        var brt = bg.rectTransform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;

        var stage = ShopUIStyle.MakeRect(transform, "Stage").GetComponent<RectTransform>();
        stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
        stage.sizeDelta = new Vector2(1920f, 1080f);
        stage.localScale = Vector3.one * StageFit(Screen.width, Screen.height);

        BuildHeader(stage);
        BuildTimeline(stage);
        BuildCurrency(Column(stage, 0, "재화"));
        BuildBuild(Column(stage, 1, "빌드"));
        BuildCombat(Column(stage, 2, "전투"));
        BuildFooter(stage);
        ApplyReveal(0f);
    }

    private void BuildHeader(RectTransform stage)
    {
        _header = Group(stage, "Header");
        var p = (RectTransform)_header.transform;
        var eyebrow = Text(p, "Eyebrow", L, 98f, 400f, 24f, UITheme.Thin.Small, UIWeight.Medium, UITheme.Thin.InkA(0.5f));
        eyebrow.characterSpacing = 30f;
        eyebrow.text = "런 결과";
        Text(p, "Title", L, 116f, 900f, 104f, UITheme.Thin.Display, UIWeight.Light, UITheme.Thin.InkA(0.96f)).text = RunResultPresenter.Title(_snap);
        var sub = Text(p, "Subtitle", L, 228f, 1100f, 32f, UITheme.Thin.Lead, UIWeight.Regular, UITheme.Thin.InkA(0.68f));
        sub.text = RunResultPresenter.Subtitle(_snap);
        float subW = Mathf.Min(1100f, sub.GetPreferredValues(sub.text).x);
        Chip(p, "Era", L + subW + 22f, 226f, RunResultPresenter.EraLabel(_snap), UITheme.Thin.LineGold, UITheme.Thin.Gold);

        var tl = Text(p, "TimeLabel", R - 400f, 106f, 400f, 24f, UITheme.Thin.Small, UIWeight.Medium, UITheme.Thin.InkA(0.5f), TextAlignmentOptions.TopRight);
        tl.characterSpacing = 30f;
        tl.text = "걸린 시간";
        Text(p, "Time", R - 600f, 124f, 600f, 104f, UITheme.Thin.Display, UIWeight.Light, UITheme.Thin.InkA(0.96f), TextAlignmentOptions.TopRight)
            .text = RunResultPresenter.FormatTime(_snap.PlaySeconds);
    }

    private void BuildTimeline(RectTransform stage)
    {
        _timeline = Group(stage, "Timeline");
        var p = (RectTransform)_timeline.transform;
        float width = R - L;
        Line(p, "Track", L, TimelineY, width, _stroke, UITheme.Thin.LineIdle);
        _timelineFill = Line(p, "Fill", L, TimelineY - 0.5f, width, 2f, UITheme.Thin.Gold);
        _timelineFill.sprite     = UIThinShapes.Solid;
        _timelineFill.type       = Image.Type.Filled;
        _timelineFill.fillMethod = Image.FillMethod.Horizontal;
        _timelineFill.fillOrigin = (int)Image.OriginHorizontal.Left;

        var segs = _snap.Segments;
        var widths = RunResultPresenter.SegmentWidths(segs, width);
        float x = L;
        for (int i = 0; i < segs.Count; i++)
        {
            var g = segs[i];
            var node = Group(p, $"Node{i}");
            var nrt = (RectTransform)node.transform;
            var dot = ShopUIStyle.MakeImage(nrt, "Dot", g.Done ? UITheme.Thin.Gold : UITheme.Thin.LineGold);
            dot.sprite = UIProceduralSprites.Ring(0.22f, 64);
            TL(dot.rectTransform, x - 5f, TimelineY - 5f, 10f, 10f);
            bool lastDone = g.Done && (i + 1 >= segs.Count || !segs[i + 1].Done);
            if (RunResultPresenter.SegmentShowsLabel(widths[i]))
                Text(nrt, "Label", x + 12f, TimelineY - 34f, Mathf.Max(90f, widths[i] - 16f), 24f, UITheme.Thin.Small, UIWeight.Medium,
                     UITheme.Thin.InkA(g.Done ? 0.85f : 0.32f)).text = g.Label;
            if (g.Done && RunResultPresenter.SegmentShowsTime(widths[i], lastDone))
            {
                Text(nrt, "Time", x + 12f, TimelineY + 12f, 160f, 24f, UITheme.Thin.Small, UIWeight.Regular, UITheme.Thin.InkA(0.55f))
                    .text = RunResultPresenter.FormatTime(g.Seconds);
                // 챕터 전투 시계 · 등급(10-24 U5 · 기록만) — 시간 아래 한 줄. 보상은 아직 없다(기준을 사람 시간으로 맞춘 뒤)
                if (g.CombatSeconds > 0)
                    Text(nrt, "Combat", x + 12f, TimelineY + 36f, 200f, 24f, UITheme.Thin.Small, UIWeight.Regular, UITheme.Thin.InkA(0.55f))
                        .text = string.IsNullOrEmpty(g.Grade)
                            ? $"전투 {RunResultPresenter.FormatTime(g.CombatSeconds)}"
                            : $"전투 {RunResultPresenter.FormatTime(g.CombatSeconds)} · {g.Grade}";
            }
            _nodes.Add(((x - L) / width, node));
            x += widths[i];
        }

        float doneW = RunResultPresenter.DoneWidth(segs, width);
        _doneFrac = doneW / width;
        var end = Group(p, "End");
        var ert = (RectTransform)end.transform;
        Color endColor = _snap.Cleared ? UITheme.Thin.Gold : UITheme.Thin.Danger;
        var mark = ShopUIStyle.MakeImage(ert, "Mark", endColor);
        mark.sprite = UIThinShapes.Fill(ThinShape.Diamond, 14f, 14f);
        TL(mark.rectTransform, L + doneW - 7f, TimelineY - 7f, 14f, 14f);
        Text(ert, "EndLabel", L + doneW - 260f, TimelineY + 12f, 250f, 24f, UITheme.Thin.Small, UIWeight.Regular, endColor, TextAlignmentOptions.TopRight)
            .text = _snap.Cleared ? "완주" : "여기서 쓰러짐";
        _nodes.Add((_doneFrac, end));
    }

    private RectTransform Column(RectTransform stage, int i, string title)
    {
        var g = Group(stage, $"Col{i}");
        _cols[i] = g;
        var p = (RectTransform)g.transform;
        _colBaseY[i] = p.anchoredPosition.y;
        float x = ColX(i);
        var head = Text(p, "Head", x, ColTop, ColW, 26f, UITheme.Thin.Small, UIWeight.Medium, UITheme.Thin.InkA(0.55f));
        head.characterSpacing = 25f;
        head.text = title;
        Line(p, "HeadLine", x, ColTop + 34f, ColW, _stroke, UITheme.Thin.LineIdle);
        if (i > 0) Line(p, "Divider", x - ColGap * 0.5f, ColTop, _stroke, 496f, UITheme.Thin.WithA(UITheme.Thin.Gold, 0.16f));
        return p;
    }

    private void BuildCurrency(RectTransform p)
    {
        float x = ColX(0), y = ColTop + 60f;
        var keep = Text(p, "Keep", x, y, 300f, 22f, UITheme.Thin.Small, UIWeight.Medium, UITheme.Thin.WithA(UITheme.Thin.Gold, 0.75f));
        keep.characterSpacing = LabelSpacing;
        keep.text = "남는 것";
        Text(p, "EssenceName", x, y + 28f, 260f, 30f, UITheme.Thin.Lead, UIWeight.Regular, UITheme.Thin.InkA(0.85f)).text = "심연의 정수";
        Counter(Text(p, "Essence", x + ColW - 300f, y + 4f, 300f, 64f, UITheme.Thin.H1, UIWeight.Light, UITheme.Thin.Gold, TextAlignmentOptions.TopRight),
                _snap.Essence, "+{0:N0}");
        var rows = RunResultPresenter.EssenceRows(_snap);
        for (int k = 0; k < rows.Count; k++)
        {
            float yy = y + 96f + k * RowPitch;
            Text(p, $"ERow{k}", x, yy, 360f, 28f, UITheme.Thin.Lead, UIWeight.Regular, UITheme.Thin.InkA(0.6f)).text = rows[k].label;
            Text(p, $"EVal{k}", x + ColW - 160f, yy, 160f, 28f, UITheme.Thin.Lead, UIWeight.Regular, UITheme.Thin.InkA(0.75f), TextAlignmentOptions.TopRight)
                .text = $"+{rows[k].value:N0}";
        }

        float y2 = y + 214f;
        Line(p, "Split", x, y2, ColW, _stroke, UITheme.Thin.LineIdle);
        var gone = Text(p, "Gone", x, y2 + 20f, 400f, 22f, UITheme.Thin.Small, UIWeight.Medium, UITheme.Thin.InkA(InfoA));
        gone.characterSpacing = LabelSpacing;
        gone.text = "런과 함께 사라진 것";
        var items = new (string label, int value, Color color)[]
        {
            ("골드", _snap.Gold, new Color(0.89f, 0.74f, 0.36f, 1f)),
            ("강화 재료", _snap.EnhanceMaterial, new Color(0.69f, 0.59f, 0.84f, 1f)),
            ("원석", _snap.RuneOre, new Color(0.46f, 0.72f, 0.81f, 1f)),
        };
        for (int k = 0; k < items.Length; k++)
        {
            float yy = y2 + 58f + k * 40f;
            var ring = ShopUIStyle.MakeImage(p, $"Ring{k}", items[k].color);
            ring.sprite = UIProceduralSprites.Ring(0.18f, 64);
            TL(ring.rectTransform, x, yy + 4f, 12f, 12f);
            Text(p, $"CName{k}", x + 26f, yy - 4f, 260f, 30f, UITheme.Thin.Lead, UIWeight.Regular, UITheme.Thin.InkA(0.82f)).text = items[k].label;
            Counter(Text(p, $"CVal{k}", x + ColW - 200f, yy - 8f, 200f, 34f, UITheme.Thin.Title, UIWeight.Light, UITheme.Thin.InkA(0.92f),
                         TextAlignmentOptions.TopRight), items[k].value, "{0:N0}");
        }
        Text(p, "Note", x, y2 + 194f, ColW, 22f, UITheme.Thin.Small, UIWeight.Regular, UITheme.Thin.InkA(InfoA))
            .text = "얻은 양 기준 · 정수만 다음 런으로 이어진다";
    }

    private void BuildBuild(RectTransform p)
    {
        float x = ColX(1), y = ColTop + 60f;

        // 무기
        IconFrame(p, "WeaponIcon", x, y, _snap.Weapon?.Icon, active: _snap.Weapon != null);
        Text(p, "WeaponName", x + 76f, y + 2f, ColW - 76f, 30f, UITheme.Thin.Lead, UIWeight.Medium, UITheme.Thin.InkA(0.92f))
            .text = RunResultPresenter.WeaponTitle(_snap.Weapon);
        Text(p, "WeaponSub", x + 76f, y + 32f, ColW - 76f, 24f, UITheme.Thin.Small, UIWeight.Regular, UITheme.Thin.InkA(0.6f))
            .text = RunResultPresenter.WeaponSub(_snap.Weapon, _snap.SubWeapon);
        // 보조 무기 빌드 한 줄(10-22 「무기 빌드도 간단히」) — 유물 줄(y + 84) 위 남는 자리
        Text(p, "SubWeapon", x + 76f, y + 56f, ColW - 76f, 22f, UITheme.Thin.Small, UIWeight.Regular, UITheme.Thin.InkA(0.5f))
            .text = RunResultPresenter.SubWeaponLine(_snap.SubWeapon);

        // 유물 · 되살아난 기억
        float ry = y + 84f;
        bool hasRelic = !string.IsNullOrEmpty(_snap.RelicName);
        IconFrame(p, "RelicIcon", x, ry, _snap.RelicIcon, active: hasRelic);
        Text(p, "RelicName", x + 76f, ry + 2f, ColW - 76f, 30f, UITheme.Thin.Lead, UIWeight.Medium, UITheme.Thin.InkA(0.92f))
            .text = hasRelic ? _snap.RelicName : "유물 없음";
        var mem = Text(p, "Memory", x + 76f, ry + 32f, 220f, 24f, UITheme.Thin.Small, UIWeight.Regular, UITheme.Thin.InkA(0.6f));
        mem.text = RunResultPresenter.MemoryLine(_snap);
        float gx = x + 76f + Mathf.Min(220f, mem.GetPreferredValues(mem.text).x) + 12f;
        gx = GradeDots(p, gx, ry + 40f, _snap.MemFaint, new Color(0.59f, 0.59f, 0.63f, 1f));
        gx = GradeDots(p, gx, ry + 40f, _snap.MemClear, new Color(0.47f, 0.69f, 0.93f, 1f));
        GradeDots(p, gx, ry + 40f, _snap.MemRadiant, UITheme.Thin.Gold);

        // 룬판
        float by = ry + 92f;
        var rl = Text(p, "BoardLabel", x, by, 200f, 22f, UITheme.Thin.Small, UIWeight.Medium, UITheme.Thin.InkA(InfoA));
        rl.characterSpacing = LabelSpacing;
        rl.text = "룬판";
        float boardW = BuildBoard(p, x, by + 28f, 190f, 140f);
        float bx = x + boardW + 24f;
        Text(p, "BoardCount", bx, by + 28f, ColW - boardW - 24f, 28f, UITheme.Thin.Lead, UIWeight.Regular, UITheme.Thin.InkA(0.8f))
            .text = _snap.RuneCount > 0 ? $"룬 {_snap.RuneCount}개  ·  {_snap.BoardFilled} / {_snap.BoardTotal}칸" : "룬 없음";
        float cx = bx, cy = by + 64f;
        foreach (var (el, tier) in _snap.Synergies)
        {
            var c = ElementDef.IdColor(el, UITheme.Thin.Ink);
            float cw = Chip(p, $"Syn_{el}", cx, cy, RunResultPresenter.ElementTier(el, tier), UITheme.Thin.WithA(c, 0.55f), c);
            cx += cw + 8f;
            if (cx > ColX(1) + ColW - 90f) { cx = bx; cy += 38f; }
        }

        // 서약
        float vy = by + 178f;
        var vl = Text(p, "CovLabel", x, vy, 200f, 22f, UITheme.Thin.Small, UIWeight.Medium, UITheme.Thin.InkA(InfoA));
        vl.characterSpacing = LabelSpacing;
        vl.text = "서약";
        var cov = Text(p, "Covenant", x, vy + 26f, ColW, 30f, UITheme.Thin.Lead, UIWeight.Medium, UITheme.Thin.InkA(0.92f));
        var sub = Text(p, "CovSub", x, vy + 60f, ColW, 24f, UITheme.Thin.Small, UIWeight.Regular, UITheme.Thin.InkA(0.6f));
        if (_snap.Covenants.Count == 0) { cov.text = "서약 없음"; sub.text = string.Empty; }
        else
        {
            var first = _snap.Covenants[0];
            cov.text = RunResultPresenter.CovenantRich(first);
            string more = _snap.Covenants.Count > 1 ? $"  ·  외 {_snap.Covenants.Count - 1}개" : string.Empty;
            sub.text = $"{first.Cause}  ·  발동 {_snap.CovenantFires:N0}회{more}";
        }
    }

    private void BuildCombat(RectTransform p)
    {
        float x = ColX(2), y = ColTop + 60f;
        var big = new (string label, int value)[] { ("처치", _snap.Kills), ("방 정리", _snap.RoomClears), ("무피해 챕터", _snap.FlawlessChapters) };
        for (int k = 0; k < big.Length; k++)
        {
            float xx = x + k * 178f;
            Counter(Text(p, $"Big{k}", xx, y - 6f, 170f, 60f, UITheme.Thin.H1, UIWeight.Light, UITheme.Thin.InkA(0.95f)), big[k].value, "{0:N0}");
            Text(p, $"BigL{k}", xx, y + 58f, 170f, 22f, UITheme.Thin.Small, UIWeight.Regular, UITheme.Thin.InkA(0.55f)).text = big[k].label;
        }
        Text(p, "CombatLine", x, y + 92f, ColW, 24f, UITheme.Thin.Small, UIWeight.Regular, UITheme.Thin.InkA(0.6f)).text = RunResultPresenter.CombatLine(_snap);

        float dy = y + 150f;
        var dl = Text(p, "DmgLabel", x, dy, 200f, 22f, UITheme.Thin.Small, UIWeight.Medium, UITheme.Thin.InkA(InfoA));
        dl.characterSpacing = LabelSpacing;
        dl.text = "피해 비중";
        long total = 0;
        foreach (var v in _snap.Damage) total += v;
        if (total <= 0)
        {
            Text(p, "DmgNone", x, dy + 30f, ColW, 24f, UITheme.Thin.Small, UIWeight.Regular, UITheme.Thin.InkA(InfoA)).text = "기록 없음";
            return;
        }
        float barY = dy + 34f, bx = x;
        for (int i = 0; i < RunStats.SourceCount; i++)
        {
            float w = ColW * _snap.Damage[i] / total;
            if (w >= 1f) Line(p, $"Seg{i}", bx, barY, Mathf.Max(1f, w - 2f), 7f, SourceColor((DamageSource)i));
            bx += w;
        }
        for (int i = 0; i < RunStats.SourceCount; i++)
        {
            int col = i % 2, row = i / 2;
            float lx = x + col * 270f, ly = barY + 30f + row * RowPitch;
            Line(p, $"Sw{i}", lx, ly + 9f, 9f, 9f, SourceColor((DamageSource)i));   // 20px 글자 가운데에 맞춘다
            Text(p, $"SL{i}", lx + 20f, ly - 2f, 170f, 28f, UITheme.Thin.Lead, UIWeight.Regular, UITheme.Thin.InkA(0.78f))
                .text = RunResultPresenter.SourceLabel((DamageSource)i);
            Text(p, $"SV{i}", lx + 170f, ly - 2f, 70f, 28f, UITheme.Thin.Lead, UIWeight.Regular, UITheme.Thin.InkA(0.9f), TextAlignmentOptions.TopRight)
                .text = $"{Mathf.RoundToInt(100f * _snap.Damage[i] / total)}%";
        }
    }

    private void BuildFooter(RectTransform stage)
    {
        _footer = Group(stage, "Footer");
        var p = (RectTransform)_footer.transform;
        Line(p, "FootLine", L, 934f, R - L, _stroke, UITheme.Thin.LineIdle);
        Text(p, "Hint", L, 958f, 900f, 24f, UITheme.Thin.Small, UIWeight.Regular, UITheme.Thin.InkA(InfoA))
            .text = "오늘의 기록은 기억의 제단 할인 조건에도 반영된다";

        const float bw = 220f, bh = 54f;
        var btnImg = ShopUIStyle.MakeImage(p, "Continue", UITheme.Thin.WithA(UITheme.Thin.Gold, 0.12f), raycast: true);
        TL(btnImg.rectTransform, R - bw, 944f, bw, bh);
        var btn = btnImg.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(Continue);
        _continueFrame = UIThinFrame.Create(btnImg.rectTransform, ThinShape.Square, new Vector2(bw, bh));
        var label = Text(btnImg.rectTransform, "Label", 0f, 0f, bw, bh, UITheme.Thin.Lead, UIWeight.Medium, UITheme.Thin.Gold, TextAlignmentOptions.Center);
        label.characterSpacing = 20f;
        label.text = "계속";
        Text(p, "Keys", R - bw - 324f, 958f, 300f, 24f, UITheme.Thin.Small, UIWeight.Regular, UITheme.Thin.InkA(InfoA), TextAlignmentOptions.TopRight)
            .text = "Space · 클릭";
    }

    // ── Private Methods — 등장 ──

    private async UniTaskVoid RevealAsync(CancellationToken ct)
    {
        float t0 = Time.unscaledTime;
        try
        {
            while (true)
            {
                float t = Time.unscaledTime - t0;
                ApplyReveal(t);
                if (!_countSounded && t >= CountSoundAt && _counters.Count > 0)
                {
                    _countSounded = true;
                    Managers.Sound?.PlayUiEvent(SoundEvent.ResultCount);   // 카운트업마다 한 번 — 숫자마다 내지 않는다(10-12 Z2)
                }
                if (t >= RevealEnd) return;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>등장 상태를 시각 t(초)로 정한다 — 끝 상태 = ApplyReveal(RevealEnd).</summary>
    private void ApplyReveal(float t)
    {
        _header.alpha = UIMotion.EaseOut(t / 0.3f);
        _timeline.alpha = 1f;
        float fill = _doneFrac * UIMotion.EaseOut((t - 0.2f) / 0.8f);
        _timelineFill.fillAmount = fill;
        foreach (var (at, node) in _nodes) node.alpha = Mathf.Clamp01(1f + (fill - at) * 25f);

        float count = UIMotion.EaseOut((t - 0.5f) / 0.6f);
        for (int i = 0; i < _counters.Count; i++)
        {
            var (label, value, format) = _counters[i];
            long v = (long)Mathf.Round(value * count);
            if (v == _counterShown[i]) continue;
            _counterShown[i] = v;
            label.SetText(string.Format(format, v));
        }

        for (int i = 0; i < _cols.Length; i++)
        {
            float k = UIMotion.EaseOut((t - 0.4f - i * 0.08f) / 0.3f);
            _cols[i].alpha = k;
            var rt = (RectTransform)_cols[i].transform;
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, _colBaseY[i] - 6f * (1f - k));
        }
        _footer.alpha = UIMotion.EaseOut((t - 0.9f) / 0.3f);
        if (!_continueLit && t >= InputDelay)
        {
            _continueLit = true;
            _continueFrame.SetState(ThinFrameState.Ready, animate: true);
        }
    }

    private async UniTask WaitContinueAsync(CancellationToken ct)
    {
        while (!_continue)
        {
            if (Time.unscaledTime - _shownAt >= InputDelay &&
                (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
                 Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.JoystickButton0)))
                _continue = true;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }

    // ── Private Methods — 부품 ──

    private static float ColX(int i) => L + i * (ColW + ColGap);

    private static CanvasGroup Group(RectTransform parent, string name)
    {
        var go = ShopUIStyle.MakeRect(parent, name, typeof(CanvasGroup));
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        var g = go.GetComponent<CanvasGroup>();
        g.blocksRaycasts = true;
        return g;
    }

    private static void TL(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    /// <summary>글자 상자 — 높이는 줄높이보다 크게(작으면 Ellipsis가 줄을 통째로 지운다 — 09-08 함정).</summary>
    private static TMP_Text Text(RectTransform p, string name, float x, float y, float w, float h, float size, UIWeight weight, Color c,
                                 TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
    {
        var t = ShopUIStyle.MakeText(p, name, size, FontStyles.Normal, align, c);
        t.enableAutoSizing = false;   // 단계 밖 크기로 줄지 않게 — 넘치면 말줄임(…)
        UITheme.StyleThinText(t, weight, size, c);
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        // 칸 높이 하한 = 줄 높이(Noto ≈ 1.45em) 이상. 1.4배면 큰 Light 숫자(정수 · 큰 수 · 제목 · 시간)가 첫 줄부터 넘쳐
        // 말줄임 모드에서 한 글자도 안 그려졌다(10-08 「Light 글자 누락」의 실제 원인 — 10-11 실측). 위 정렬이라 늘려도 자리는 그대로다.
        TL(t.rectTransform, x, y, w, Mathf.Max(h, size * 1.6f));
        return t;
    }

    private Image Line(RectTransform p, string name, float x, float y, float w, float h, Color c)
    {
        var img = ShopUIStyle.MakeImage(p, name, c);
        TL(img.rectTransform, x, y, Mathf.Max(w, _stroke), Mathf.Max(h, _stroke));
        return img;
    }

    private void Counter(TMP_Text label, long value, string format)
    {
        _counters.Add((label, value, format));
        _counterShown.Add(long.MinValue);
    }

    /// <summary>얇은 테 알약(1px) + 글자. 폭을 돌려준다.</summary>
    private float Chip(RectTransform p, string name, float x, float y, string label, Color line, Color ink)
    {
        if (string.IsNullOrEmpty(label)) return 0f;
        var img = ShopUIStyle.MakeImage(p, name, line);
        img.sprite = UIProceduralSprites.RoundedOutline(radius: 15f, stroke: 1f);
        img.type = Image.Type.Sliced;
        var t = Text(img.rectTransform, "Label", 0f, 0f, 10f, 30f, UITheme.Thin.Small, UIWeight.Medium, ink, TextAlignmentOptions.Center);
        t.text = label;
        float w = t.GetPreferredValues(label).x + 28f;
        TL(img.rectTransform, x, y, w, 30f);
        TL(t.rectTransform, 0f, 0f, w, 30f);
        return w;
    }

    private void IconFrame(RectTransform p, string name, float x, float y, Sprite icon, bool active)
    {
        var host = ShopUIStyle.MakeImage(p, name, UITheme.Thin.GlassFill);
        TL(host.rectTransform, x, y, 56f, 56f);
        if (icon != null)
        {
            var ic = ShopUIStyle.MakeImage(host.rectTransform, "Icon", Color.white);
            ic.sprite = icon;
            ic.preserveAspect = true;
            TL(ic.rectTransform, 6f, 6f, 44f, 44f);
        }
        var f = UIThinFrame.Create(host.rectTransform, ThinShape.Square, new Vector2(56f, 56f));
        f.SetState(active ? ThinFrameState.Active : ThinFrameState.Idle, animate: false);
    }

    private static float GradeDots(RectTransform p, float x, float y, int n, Color c)
    {
        for (int i = 0; i < n; i++)
        {
            var d = ShopUIStyle.MakeImage(p, "Grade", c);
            d.sprite = UIProceduralSprites.Circle(32);
            TL(d.rectTransform, x, y, 7f, 7f);
            x += 11f;
        }
        return n > 0 ? x + 6f : x;
    }

    /// <summary>룬판 미니 — 놓인 칸 = 속성 색 85%, 빈 칸 = 1px 잉크 테. 차지한 폭을 돌려준다.</summary>
    private float BuildBoard(RectTransform p, float x, float y, float maxW, float maxH)
    {
        if (_snap.Board.Count == 0) return 0f;
        int cols = 0, rows = 0;
        foreach (var c in _snap.Board) { cols = Mathf.Max(cols, c.X + 1); rows = Mathf.Max(rows, c.Y + 1); }
        float step = Mathf.Min(maxW / cols, maxH / rows);
        float cell = Mathf.Max(2f, step - 3f);
        foreach (var c in _snap.Board)
        {
            float cx = x + c.X * step, cy = y + c.Y * step;
            if (c.Element != null)
            {
                Line(p, "Cell", cx, cy, cell, cell, UITheme.Thin.WithA(ElementDef.IdColor(c.Element, UITheme.Thin.Ink), 0.85f));
            }
            else
            {
                var e = ShopUIStyle.MakeImage(p, "Empty", UITheme.Thin.LineIdle);
                e.sprite = UIThinShapes.Outline(ThinShape.Square, cell, cell);
                TL(e.rectTransform, cx, cy, cell, cell);
            }
        }
        return cols * step;
    }

    private static Color SourceColor(DamageSource s) => s switch
    {
        DamageSource.Skill    => new Color(0.49f, 0.66f, 0.87f, 1f),
        DamageSource.RelicQ   => new Color(0.91f, 0.73f, 0.34f, 1f),
        DamageSource.Covenant => new Color(0.82f, 0.41f, 0.44f, 1f),
        DamageSource.Rune     => new Color(0.59f, 0.77f, 0.50f, 1f),
        DamageSource.Dot      => new Color(0.59f, 0.51f, 0.78f, 1f),
        _                     => new Color(0.84f, 0.77f, 0.59f, 1f),
    };
}
