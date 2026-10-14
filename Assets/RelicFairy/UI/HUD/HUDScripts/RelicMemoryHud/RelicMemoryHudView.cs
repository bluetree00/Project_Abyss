using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 유물 HUD 확장 View(유물 성장 v2 §6-1). 유물 게이지 루트 아래에 코드로 짓는다 — CombatPanelView의 게이지와 같은 방식.
/// <list type="bullet">
/// <item><b>가웨인 하루 띠</b> — 태양 게이지 오른쪽의 가로 띠 셋(여명 · 정오 · 황혼, 길이 = 구간 시간). 띠마다 위에 조각 점 ●●○○(2 · 4개면 공명),
///       공명이 깊을수록 띠가 밝다(4 = 금빛). 바늘이 지금 시간을 따라간다. 구간에 들어서면 그 띠가 한 번 빛난다. 「새벽이 길다」 붙듦 중엔 안내 한 줄.
///       (설계는 태양을 두른 고리였으나 태양이 화면 맨 아래 · 체력바 뒤라 고리가 가려져 띠로 폈다 — 10-02 실측)</item>
/// <item><b>랜슬롯 광기 눈금</b> — 막대 10 · 20 · 30 · 40에 눈금과 계단 표식(빈 칸 회색 · 조각 진홍 · 이어진 계단 금빛 + 금선).
///       광기가 눈금을 넘으면 표식이 빛난다. 막대 오른쪽에 원한 불씨 + 「3/5」(가득 = 금 테 · 금 숫자).
///       막대가 안 줄어드는 동안(피 냄새 · 끝의 문턱) 채움 끝에 얇은 멈춤 선 + 막대 왼쪽 끝 위(계단 표식 줄)에 「멈춤 N초」. 끝의 문턱 버팀 중엔 막대가 떤다(±1.5 px).</item>
/// <item><b>엇갈린 빛</b>(두 유물) — 띠 · 원한 숫자 오른쪽 작은 네 갈래 별. 대기 = 빈 별(반응 조각 2개 이상일 때만) · 켜짐 = 금 별(켜지는 순간 한 번 커진다 · 은은히 숨쉰다).</item>
/// </list>
/// 이 위젯은 전투 패널 아래라 HUD 하드 가드가 0.2초마다 글자를 켜고 알파를 1로 되돌린다 — 글자는 끄지 않고 비운다 · 흐림은 색으로.
/// </summary>
public sealed class RelicMemoryHudView : MonoBehaviour
{
    // ── 색 ──
    private static readonly Color RungEmpty  = new(0.32f, 0.32f, 0.34f, 0.75f);
    private static readonly Color RungOwned  = new(0.86f, 0.27f, 0.25f, 1f);
    private static readonly Color RungLinked = new(1f, 0.82f, 0.35f, 1f);
    private static readonly Color TickColor  = new(1f, 1f, 1f, 0.45f);
    private static readonly Color EmberOn    = new(1f, 0.45f, 0.18f, 1f);
    private static readonly Color EmberOff   = new(0.25f, 0.08f, 0.06f, 0.75f);
    private static readonly Color PipOff     = new(1f, 1f, 1f, 0.22f);
    private static readonly Color HoldColor  = new(1f, 0.86f, 0.5f, 1f);
    private static readonly Color[] ArcColor =
    {
        new(1f, 0.87f, 0.55f),   // 여명 — 옅은 금
        new(1f, 0.48f, 0.16f),   // 정오 — 주홍
        new(0.74f, 0.38f, 0.64f) // 황혼 — 노을 보라
    };

    // ── 치수 ──
    private const float StripX       = 186f;   // 태양 게이지(폭 340) 오른쪽 끝 + 16 — 컨테이너 가운데 기준
    private const float StripY       = 50f;    // 태양 게이지 높이(컨테이너 하단에서)
    private const float StripW       = 150f;
    private const float StripH       = 6f;
    private const float StripGap     = 3f;
    private const float PipSize      = 6f;
    private const float PipStep      = 9f;
    private const float RungIconSize = 12f;
    private const float RungIconY    = 10f;    // 막대 윗변 위
    private const float EmberSize    = 9f;
    private const float EmberGap     = 11f;
    private const float FlashDecay   = 2.5f;   // 초당
    private const float ShakePx      = 1.5f;
    private const float GrudgeLabelGap = 6f;   // 불씨 끝 → 「3/5」
    private const float GrudgeRimPad   = 3f;   // 가득 금 테가 불씨 줄을 두르는 여백
    private const float StarSize     = 14f;
    private const float StarGap      = 8f;
    private const float StarPopSec   = 0.3f;
    private const float HoldTickW    = 2f;

    private static Sprite s_dot;
    private static Sprite s_starFill, s_starLine;

    private RectTransform _host;
    private RectTransform _overlay;
    private bool _visible;
    private bool _dial;

    // 계단
    private readonly Image[] _rungIcon = new Image[4];
    private readonly float[] _rungFlash = new float[4];
    private readonly Color[] _rungBase = new Color[4];
    private RectTransform _link;
    private RectTransform _emberRow;
    private readonly List<Image> _embers = new(11);
    private Vector2 _barBasePos;
    private bool    _shaking;

    // 해시계
    private readonly Image[]   _arc = new Image[3];
    private readonly Image[][] _pips = { new Image[4], new Image[4], new Image[4] };
    private readonly float[]   _arcStart = new float[3];
    private readonly float[]   _arcSpan  = new float[3];
    private readonly float[]   _arcAlpha = new float[3];
    private readonly float[]   _arcFlash = new float[3];
    private RectTransform   _needle;
    private TextMeshProUGUI _holdLabel;

    // 원한 숫자 · 가득 금 테(10-12 A2)
    private TextMeshProUGUI _grudgeLabel;
    private RectTransform   _grudgeRim;
    // 광기 멈춤(10-12 S11)
    private RectTransform   _holdTick;
    private TextMeshProUGUI _holdSec;
    private int             _holdKey = -1;   // 표시 중인 남은 0.1초 단위(-2 = 문턱 · 0 = 없음) — 바뀔 때만 쓴다
    // 엇갈린 빛 별(10-12 A3)
    private Image _star;
    private bool  _starOn;
    private float _starPop;   // 켜지는 순간 1 → 0

    public bool Visible => _visible;

    // ── Public: 공통 ─────────────────────────────────────────────

    public void Hide()
    {
        StopShake();
        if (_overlay != null) _overlay.gameObject.SetActive(false);
        _visible = false;
    }

    // ── Public: 랜슬롯 계단 ──────────────────────────────────────

    public void BuildLadder(RectTransform bar)
    {
        if (!Prepare(bar, dial: false)) { Show(); return; }
        for (int k = 1; k <= 4; k++)
        {
            float x = k / 4f;
            var tick = NewImage("Tick" + k, _overlay, null, TickColor);
            Anchor(tick.rectTransform, new Vector2(x, 0.2f), new Vector2(x, 0.8f), new Vector2(k == 4 ? -2f : -1f, 0f), new Vector2(k == 4 ? 0f : 1f, 0f));

            var icon = NewImage("Rung" + k, _overlay, null, RungEmpty);
            var rt = icon.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(x, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(RungIconSize, RungIconSize);
            rt.anchoredPosition = new Vector2(k == 4 ? -RungIconSize * 0.5f : 0f, RungIconY);
            rt.localRotation = Quaternion.Euler(0f, 0f, 45f);   // 마름모
            _rungIcon[k - 1] = icon;
            _rungBase[k - 1] = RungEmpty;
        }
        var link = NewImage("RungLink", _overlay, null, RungLinked);
        _link = link.rectTransform;
        _link.SetAsFirstSibling();
        _link.gameObject.SetActive(false);

        _emberRow = NewRect("Grudge", _overlay);
        _emberRow.anchorMin = _emberRow.anchorMax = new Vector2(1f, 0.5f);
        _emberRow.pivot = new Vector2(0f, 0.5f);
        _emberRow.anchoredPosition = new Vector2(10f, 0f);
        _emberRow.sizeDelta = new Vector2(EmberGap * 11f, EmberSize);

        // 가득 금 테 — 불씨 줄을 두르는 1px 테(크기는 SetGrudge가 상한에 맞춘다)
        _grudgeRim = UIThinBox.Create(_emberRow, "GrudgeRim", Vector2.zero, Vector2.one, 0f, UITheme.Thin.LineReady, Color.clear, Color.clear);
        _grudgeRim.anchorMin = _grudgeRim.anchorMax = new Vector2(0f, 0.5f);
        _grudgeRim.pivot = new Vector2(0f, 0.5f);
        _grudgeRim.anchoredPosition = new Vector2(-GrudgeRimPad, 0f);
        _grudgeRim.gameObject.SetActive(false);
        _grudgeLabel = NewLabel("GrudgeCount", _emberRow, UITheme.Thin.Small, TextAlignmentOptions.MidlineLeft);
        _grudgeLabel.rectTransform.anchorMin = _grudgeLabel.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        _grudgeLabel.rectTransform.pivot = new Vector2(0f, 0.5f);
        _grudgeLabel.rectTransform.sizeDelta = new Vector2(48f, 20f);
        _star = NewStar(_emberRow);

        // 멈춤 — 채움 끝의 얇은 선(가로 위치는 TickLadder가 채움을 따라 옮긴다) · 남은 초는 막대 왼쪽 끝 위, 계단 표식 줄 높이
        // (10-13 ②d 사진 — 막대 왼쪽 바깥은 「광기 N%」 자리라 두 글이 겹쳐 읽히지 않았다. 첫 계단 표식(¼ 지점) 앞까지만 쓴다)
        _holdTick = NewImage("HoldTick", _overlay, null, UITheme.Thin.Ink).rectTransform;
        _holdTick.pivot = new Vector2(0.5f, 0.5f);
        Anchor(_holdTick, new Vector2(0f, 0.2f), new Vector2(0f, 0.8f), new Vector2(-HoldTickW * 0.5f, 0f), new Vector2(HoldTickW * 0.5f, 0f));
        _holdTick.gameObject.SetActive(false);
        _holdSec = NewLabel("HoldSeconds", _overlay, UITheme.Thin.Caption, TextAlignmentOptions.MidlineLeft);
        Anchor(_holdSec.rectTransform, new Vector2(0f, 1f), new Vector2(0.25f, 1f),
               new Vector2(0f, RungIconY - 10f), new Vector2(-RungIconSize, RungIconY + 10f));

        _barBasePos = bar.anchoredPosition;
        Show();
    }

    /// <summary>계단 표식 — occupied[i] = 그 계단에 조각(메아리 포함)이 있다 · ladderTier = 이어진 계단(2 미만이면 0).</summary>
    public void SetRungs(bool[] occupied, int ladderTier)
    {
        if (_dial || _overlay == null) return;
        for (int i = 0; i < 4; i++)
        {
            if (_rungIcon[i] == null) continue;
            bool linked = ladderTier >= 2 && i < ladderTier;
            _rungBase[i] = linked ? RungLinked : occupied[i] ? RungOwned : RungEmpty;
            _rungIcon[i].color = _rungBase[i];
        }
        if (_link == null) return;
        _link.gameObject.SetActive(ladderTier >= 2);
        if (ladderTier >= 2)
            Anchor(_link, new Vector2(0.25f, 1f), new Vector2(ladderTier / 4f, 1f), new Vector2(0f, RungIconY - 1f), new Vector2(ladderTier == 4 ? -RungIconSize * 0.5f : 0f, RungIconY + 1f));
    }

    /// <summary>원한 불씨 — 상한만큼 칸, 찬 만큼 불. 옆에 「3/5」, 가득(<paramref name="full"/>)이면 줄에 금 테 · 숫자 금.</summary>
    public void SetGrudge(int value, int cap, bool full)
    {
        if (_dial || _emberRow == null) return;
        cap = Mathf.Clamp(cap, 0, 11);
        while (_embers.Count < cap)
        {
            var e = NewImage("Ember" + _embers.Count, _emberRow, Dot(), EmberOff);
            var rt = e.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(EmberSize, EmberSize);
            rt.anchoredPosition = new Vector2(_embers.Count * EmberGap, 0f);
            _embers.Add(e);
        }
        for (int i = 0; i < _embers.Count; i++)
        {
            _embers[i].gameObject.SetActive(i < cap);
            _embers[i].color = i < value ? EmberOn : EmberOff;
        }

        float rowW = cap > 0 ? (cap - 1) * EmberGap + EmberSize : 0f;
        bool  rim  = full && cap > 0;
        if (_grudgeRim != null)
        {
            _grudgeRim.sizeDelta = new Vector2(rowW + GrudgeRimPad * 2f, EmberSize + GrudgeRimPad * 2f);
            if (_grudgeRim.gameObject.activeSelf != rim) _grudgeRim.gameObject.SetActive(rim);
        }
        float starX = rowW + GrudgeLabelGap;
        if (_grudgeLabel != null)
        {
            _grudgeLabel.rectTransform.anchoredPosition = new Vector2(rowW + GrudgeLabelGap, 0f);
            _grudgeLabel.color = rim ? UITheme.Thin.Gold : UITheme.Thin.Ink;
            if (cap > 0) _grudgeLabel.SetText("{0}/{1}", Mathf.Min(value, cap), cap);
            else _grudgeLabel.text = string.Empty;
            starX += cap > 0 ? _grudgeLabel.preferredWidth : 0f;
        }
        if (_star != null) _star.rectTransform.anchoredPosition = new Vector2(starX + StarGap + StarSize * 0.5f, 0f);
    }

    /// <summary>
    /// 엇갈린 빛 — 켜짐 = 금 별(<paramref name="animate"/>면 켜지는 순간 한 번 커졌다 자리 잡는다) · 대기 = 빈 별(<paramref name="possible"/> = 반응 조각 2개 이상) · 아니면 별 없음.
    /// </summary>
    public void SetCrossLight(bool possible, bool on, bool animate)
    {
        if (_star == null) return;
        bool show = on || possible;
        if (_star.gameObject.activeSelf != show) _star.gameObject.SetActive(show);
        if (on && !_starOn && animate) _starPop = 1f;
        _starOn = on;
        _star.sprite = on ? StarFill() : StarLine();
        _star.color  = on ? UITheme.Thin.Gold : UITheme.Thin.InkA(0.45f);
        if (on) return;
        _starPop = 0f;
        _star.rectTransform.localScale = Vector3.one;
    }

    /// <summary>광기가 계단 i(0~3)를 넘었다 — 표식이 한 번 빛난다.</summary>
    public void FlashRung(int i)
    {
        if (i >= 0 && i < 4) _rungFlash[i] = 1f;
    }

    /// <summary>
    /// 매 프레임 — 표식 빛 · 멈춤 · 끝의 문턱 떨림 · 별.
    /// <paramref name="holdLeft"/> = 피 냄새 남은 초(0 이하 = 없음) · <paramref name="fill01"/> = 막대 채움(멈춤 선 자리).
    /// </summary>
    public void TickLadder(bool holding, float holdLeft, float fill01)
    {
        float dt = Time.unscaledDeltaTime;
        TickHold(holding, holdLeft, fill01);
        TickStar(dt);
        for (int i = 0; i < 4; i++)
        {
            if (_rungFlash[i] <= 0f || _rungIcon[i] == null) continue;
            _rungFlash[i] = Mathf.Max(0f, _rungFlash[i] - dt * FlashDecay);
            float f = _rungFlash[i];
            _rungIcon[i].color = Color.Lerp(_rungBase[i], Color.white, f);
            _rungIcon[i].rectTransform.localScale = Vector3.one * (1f + 0.6f * f);
        }
        if (holding)
        {
            _shaking = true;
            if (_host != null) _host.anchoredPosition = _barBasePos + Random.insideUnitCircle * ShakePx;
        }
        else StopShake();
    }

    // ── Public: 가웨인 해시계 ────────────────────────────────────

    public void BuildDial(RectTransform sunRoot, ZenithGauge gauge)
    {
        if (!Prepare(sunRoot, dial: true)) { Show(); return; }
        // 태양 게이지 오른쪽에 하루 띠를 둔다(체력바 아래 · Q 칸 왼쪽의 빈자리).
        _overlay.anchorMin = _overlay.anchorMax = new Vector2(0.5f, 0f);
        _overlay.pivot = new Vector2(0f, 0.5f);
        _overlay.anchoredPosition = new Vector2(StripX, StripY);
        _overlay.sizeDelta = new Vector2(StripW, 40f);

        float d0 = gauge != null ? gauge.DurationOf(ZenithGauge.ZPhase.Charging) : 20f;
        float d1 = gauge != null ? gauge.DurationOf(ZenithGauge.ZPhase.Noon) : 10f;
        float d2 = gauge != null ? gauge.DurationOf(ZenithGauge.ZPhase.Cooldown) : 15f;
        float total = Mathf.Max(0.01f, d0 + d1 + d2);
        float x = 0f;
        float usable = StripW - StripGap * 2f;
        float[] dur = { d0, d1, d2 };
        for (int i = 0; i < 3; i++)
        {
            float w = usable * dur[i] / total;
            _arcStart[i] = x;
            _arcSpan[i]  = w;
            var seg = NewImage("Seg" + i, _overlay, null, ArcColor[i]);
            var srt = seg.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0f, 0.5f);
            srt.pivot = new Vector2(0f, 0.5f);
            srt.anchoredPosition = new Vector2(x, 0f);
            srt.sizeDelta = new Vector2(w, StripH);
            _arc[i] = seg;

            // 조각 점 ●●○○ — 띠 위 가운데
            float first = x + w * 0.5f - PipStep * 1.5f;
            for (int j = 0; j < 4; j++)
            {
                var pip = NewImage($"Pip{i}_{j}", _overlay, Dot(), PipOff);
                var rt = pip.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(PipSize, PipSize);
                rt.anchoredPosition = new Vector2(first + PipStep * j, StripH * 0.5f + 7f);
                _pips[i][j] = pip;
            }
            x += w + StripGap;
        }

        var needle = NewImage("Needle", _overlay, null, Color.white);
        _needle = needle.rectTransform;
        _needle.anchorMin = _needle.anchorMax = new Vector2(0f, 0.5f);
        _needle.sizeDelta = new Vector2(2f, StripH + 8f);

        var lblRt = NewRect("Hold", _overlay);
        lblRt.anchorMin = lblRt.anchorMax = new Vector2(0f, 0.5f);
        lblRt.pivot = new Vector2(0f, 0.5f);
        lblRt.anchoredPosition = new Vector2(0f, -16f);
        lblRt.sizeDelta = new Vector2(260f, 22f);
        _holdLabel = lblRt.gameObject.AddComponent<TextMeshProUGUI>();
        _holdLabel.text = "새벽을 붙들었다 · 낙일로 정오";
        _holdLabel.fontSize = 16f;
        _holdLabel.alignment = TextAlignmentOptions.MidlineLeft;
        _holdLabel.color = HoldColor;
        _holdLabel.raycastTarget = false;
        TMPOutlineHelper.ApplySoftShadow(_holdLabel);
        _holdLabel.gameObject.SetActive(false);

        // 엇갈린 빛 별 — 하루 띠 오른쪽 끝
        _star = NewStar(_overlay);
        _star.rectTransform.anchoredPosition = new Vector2(StripW + StarGap + StarSize * 0.5f, 0f);
        Show();
    }

    /// <summary>호마다 조각 수(가지형 「궤적」은 두 호에 다 센다). 2 · 4개면 공명 — 호가 밝아진다(4 = 금빛).</summary>
    public void SetArcs(int dawn, int noon, int dusk)
    {
        if (!_dial || _overlay == null) return;
        int[] counts = { dawn, noon, dusk };
        for (int i = 0; i < 3; i++)
        {
            int tier = RelicResonance.GawainTier(counts[i]);
            _arcAlpha[i] = tier >= 4 ? 1f : tier >= 2 ? 0.65f : 0.32f;
            for (int j = 0; j < 4; j++)
                if (_pips[i][j] != null) _pips[i][j].color = j < counts[i] ? (tier >= 4 ? RungLinked : ArcColor[i]) : PipOff;
        }
    }

    /// <summary>시간대 i(0 여명 · 1 정오 · 2 황혼)에 들어섰다 — 그 호가 한 번 빛난다.</summary>
    public void FlashArc(int i)
    {
        if (i >= 0 && i < 3) _arcFlash[i] = 1f;
    }

    /// <summary>매 프레임 — 바늘 · 지금 호 강조 · 빛 · 붙듦 안내.</summary>
    public void TickDial(ZenithGauge.ZPhase phase, float progress, bool holdingDawn)
    {
        int cur = phase == ZenithGauge.ZPhase.Charging ? 0 : phase == ZenithGauge.ZPhase.Noon ? 1 : 2;
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < 3; i++)
        {
            if (_arc[i] == null) continue;
            if (_arcFlash[i] > 0f) _arcFlash[i] = Mathf.Max(0f, _arcFlash[i] - dt * FlashDecay);
            var c = Color.Lerp(ArcColor[i], Color.white, _arcFlash[i] * 0.7f);
            c.a = Mathf.Min(1f, _arcAlpha[i] + (i == cur ? 0.25f : 0f) + _arcFlash[i] * 0.4f);
            _arc[i].color = c;
        }
        if (_needle != null) _needle.anchoredPosition = new Vector2(_arcStart[cur] + _arcSpan[cur] * Mathf.Clamp01(progress), 0f);
        if (_holdLabel != null && _holdLabel.gameObject.activeSelf != holdingDawn) _holdLabel.gameObject.SetActive(holdingDawn);
        TickStar(dt);
    }

    // ── Private ──────────────────────────────────────────────────

    /// <summary>같은 루트 · 같은 모양이면 다시 짓지 않는다(false). 아니면 옛 위젯을 치우고 새 묶음을 만든다(true).</summary>
    private bool Prepare(RectTransform host, bool dial)
    {
        if (host == _host && _overlay != null && _dial == dial) return false;
        StopShake();
        if (_overlay != null) Destroy(_overlay.gameObject);
        _embers.Clear();
        for (int i = 0; i < 4; i++) { _rungIcon[i] = null; _rungFlash[i] = 0f; }
        for (int i = 0; i < 3; i++) { _arc[i] = null; _arcFlash[i] = 0f; }
        _link = null; _emberRow = null; _needle = null; _holdLabel = null;
        _grudgeLabel = null; _grudgeRim = null; _holdTick = null; _holdSec = null; _holdKey = -1;
        _star = null; _starOn = false; _starPop = 0f;
        _host = host;
        _dial = dial;
        _overlay = NewRect(dial ? "RelicMemoryDial" : "RelicMemoryLadder", host);
        Anchor(_overlay, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return true;
    }

    private void Show()
    {
        if (_overlay != null) _overlay.gameObject.SetActive(true);
        _visible = _overlay != null;
    }

    /// <summary>멈춤 — 피 냄새(남은 초) · 끝의 문턱(「멈춤」만, 막대는 떤다)이면 채움 끝에 얇은 선. 글자는 0.1초 단위가 바뀔 때만 쓴다.</summary>
    private void TickHold(bool holding, float holdLeft, float fill01)
    {
        bool hold = holding || holdLeft > 0f;
        if (_holdTick != null)
        {
            if (_holdTick.gameObject.activeSelf != hold) _holdTick.gameObject.SetActive(hold);
            float x = Mathf.Clamp01(fill01);
            if (hold && !Mathf.Approximately(_holdTick.anchorMin.x, x))
            {
                _holdTick.anchorMin = new Vector2(x, 0.2f);
                _holdTick.anchorMax = new Vector2(x, 0.8f);
            }
        }
        if (_holdSec == null) return;
        int key = holdLeft > 0f ? Mathf.CeilToInt(holdLeft * 10f) : holding ? -2 : 0;
        if (key == _holdKey) return;
        _holdKey = key;
        if (key > 0) _holdSec.SetText("멈춤 {0:1}초", key * 0.1f);
        else _holdSec.text = key == -2 ? "멈춤" : string.Empty;
    }

    /// <summary>켜진 별 — 켜지는 순간 1.5배에서 자리 잡고, 그 뒤 은은히 숨쉰다(배율 ±6% · 알파 0.88~1).</summary>
    private void TickStar(float dt)
    {
        if (_star == null || !_starOn) return;
        if (_starPop > 0f) _starPop = Mathf.Max(0f, _starPop - dt / StarPopSec);
        float breathe = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.4f);
        _star.rectTransform.localScale = Vector3.one * (1f + 0.5f * _starPop * _starPop + 0.06f * breathe);
        var c = UITheme.Thin.Gold;
        c.a = 0.88f + 0.12f * breathe;
        _star.color = c;
    }

    private void StopShake()
    {
        if (!_shaking) return;
        _shaking = false;
        if (_host != null) _host.anchoredPosition = _barBasePos;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var img = NewRect(name, parent).gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>톤 v2 글자(Medium · 그림자) — 빈 글자로 시작한다.</summary>
    private static TextMeshProUGUI NewLabel(string name, Transform parent, float size, TextAlignmentOptions align)
    {
        var t = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        UITheme.StyleThinText(t, UIWeight.Medium, size, UITheme.Thin.Ink, shadow: true);
        t.alignment        = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget    = false;
        t.text             = string.Empty;
        return t;
    }

    private static Image NewStar(Transform parent)
    {
        var star = NewImage("CrossLight", parent, StarLine(), UITheme.Thin.InkA(0.45f));
        var rt = star.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(StarSize, StarSize);
        star.gameObject.SetActive(false);
        return star;
    }

    private static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = offMin;
        rt.offsetMax = offMax;
    }

    private static Sprite Dot()  => s_dot  != null ? s_dot  : s_dot  = MakeCircle(32, 0f);
    private static Sprite StarFill() => s_starFill != null ? s_starFill : s_starFill = MakeStar(32, outline: false);
    private static Sprite StarLine() => s_starLine != null ? s_starLine : s_starLine = MakeStar(32, outline: true);

    /// <summary>
    /// 네 갈래 별(엇갈린 빛 — 십자로 엇갈린 빛살) — 아트 없이 굽는다. 모양 = |u|^p + |v|^p ≤ r^p(p &lt; 1이면 끝이 뾰족한 별).
    /// outline이면 속이 빈 별: 안쪽으로 2.7px 깎은 모양을 뺀다(작게 그려도 선이 1px 남짓). 화소마다 4×4 표본으로 가장자리를 고른다.
    /// </summary>
    private static Sprite MakeStar(int size, bool outline)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = outline ? "RelicHudStarLine" : "RelicHudStar",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        const int ss = 4;
        float c = size * 0.5f, w = 2.7f / c;
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int hit = 0;
            for (int sy = 0; sy < ss; sy++)
            for (int sx = 0; sx < ss; sx++)
            {
                float u = (x + (sx + 0.5f) / ss - c) / c, v = (y + (sy + 0.5f) / ss - c) / c;
                if (!InStar(u, v)) continue;
                if (outline && InStar(u + w, v) && InStar(u - w, v) && InStar(u, v + w) && InStar(u, v - w)
                    && InStar(u + w * 0.7f, v + w * 0.7f) && InStar(u - w * 0.7f, v - w * 0.7f)
                    && InStar(u + w * 0.7f, v - w * 0.7f) && InStar(u - w * 0.7f, v + w * 0.7f)) continue;   // 안쪽 — 테만 남긴다
                hit++;
            }
            px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * hit / (ss * ss)));
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private static bool InStar(float u, float v)
    {
        const float p = 0.66f, r = 0.94f;
        return Mathf.Pow(Mathf.Abs(u), p) + Mathf.Pow(Mathf.Abs(v), p) <= Mathf.Pow(r, p);
    }

    /// <summary>흰 원(inner &gt; 0이면 고리) — 아트 없이 HUD 점을 그린다. 한 번 만들어 돌려 쓴다.</summary>
    private static Sprite MakeCircle(int size, float inner)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = inner > 0f ? "RelicHudRing" : "RelicHudDot",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f, aa = 1.5f / size;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - c, dy = y - c;
            float d = Mathf.Sqrt(dx * dx + dy * dy) / c;
            float a = Mathf.Clamp01((1f - d) / aa);
            if (inner > 0f) a *= Mathf.Clamp01((d - inner) / aa);
            px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
