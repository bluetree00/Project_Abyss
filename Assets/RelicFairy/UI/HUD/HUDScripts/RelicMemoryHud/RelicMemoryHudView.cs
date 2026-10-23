using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 유물 HUD 확장 View(유물 성장 v2 §6-1). 유물 게이지 루트 아래에 코드로 짓는다 — CombatPanelView의 게이지와 같은 방식.
/// <list type="bullet">
/// <item><b>가웨인 하루 띠</b> — 태양 게이지 오른쪽의 가로 띠 셋(여명 · 정오 · 황혼, 길이 = 구간 시간). 띠마다 위에 조각 점 ●●○○(2 · 4개면 공명),
///       공명이 깊을수록 띠가 밝다(4 = 금빛). 바늘이 지금 시간을 따라간다. 구간에 들어서면 그 띠가 한 번 빛난다. 「새벽이 길다」 붙듦 중엔 안내 한 줄.
///       (설계는 태양을 두른 고리였으나 태양이 화면 맨 아래 · 체력바 뒤라 고리가 가려져 띠로 폈다 — 10-02 실측)
///       10-18 사용자 「색별로 있는 UI바는 뭘 의미하는지도 모르겠어」 — 띠 아래에 이름 · 조각 수(「여명 1/4」), 처음 한 번 띠 위에 규칙 한 줄.</item>
/// <item><b>랜슬롯 광기 눈금</b> — 막대 10 · 20 · 30 · 40에 눈금과 칸 표식(빈 칸 회색 · 조각 진홍 · 공명이 켜지면 조각 칸 금빛).
///       10-22 E1 D7 — 공명이 「10부터 이어진 계단」에서 「광기 칸 조각 수」로 바뀌어 이어짐 금선을 걷고 맨 오른쪽 문턱 값 옆에 「조각 2/4」를 둔다.
///       광기가 눈금을 넘으면 표식이 빛난다. 막대 오른쪽에 원한 불씨 + 「원한 3/5」(가득 = 금 테 · 금 숫자). 표식 위에 문턱 값(「광기 10」 · 20 · 30 · 40 — 10-18).
///       첫 조각 때 처음 한 번 표식 위에 규칙 한 줄(가웨인 하루 띠 안내와 짝 — 10-22 E1 A6).
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
    private static readonly Color LabelDim   = new(0.62f, 0.60f, 0.57f, 1f);   // 잉크를 밝기로 흐린 것(알파는 하드 가드가 1로 되돌린다)
    // 「여명 1/4」는 1/4를 여명 진행도로 읽을 수 있었다 → 둘째 줄에 「조각 1/4」(10-18 사용자 「진행」 · 한 줄이면 정오 띠(44px) 이름끼리 겹친다)
    private static readonly string[] ArcFormat = { "여명\n조각 {0}/4", "정오\n조각 {0}/4", "황혼\n조각 {0}/4" };
    private static readonly Color[] ArcColor =
    {
        new(1f, 0.87f, 0.55f),   // 여명 — 옅은 금
        new(1f, 0.48f, 0.16f),   // 정오 — 주홍
        new(0.74f, 0.38f, 0.64f) // 황혼 — 노을 보라
    };

    // ── 치수 ──
    private const float StripX       = 186f;   // 태양 게이지(폭 340) 오른쪽 끝 + 16 — 컨테이너 가운데 기준(게이지를 못 찾을 때)
    private const float StripPad     = 12f;    // 게이지 오른쪽 끝 → 띠
    // 태양 게이지 높이(50) + 8(10-18) — 띠 이름이 두 줄(「여명」 / 「조각 1/4」)이 되자 둘째 줄이 체력바 오른쪽 끝(금선 · 마름모)과
    // 공 · 방 숫자에 닿았다. 띠 묶음을 올리면 게이지 줄과도 어긋나 「게이지가 띠로 이어진 한 막대」(사진 13)로 덜 읽힌다.
    private const float StripY       = 58f;
    // 150 → 200(10-18) — 띠 아래 「정오 1/4」(약 48px)가 이웃 이름과 겹치지 않게(옛 정오 띠 32px).
    // 더 넓히면 16:10(스팀 덱)에서 엇갈린 빛 별이 물약 C 칸에 닿는다.
    private const float StripW       = 200f;
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
    private const float ArcLabelW    = 72f;
    private const float ArcLabelH    = 18f;
    private const float ArcLabelGap  = 10f;    // 띠 아랫변 → 이름 가운데
    private const float DialLabelH   = 34f;    // 하루 띠 이름 두 줄(「여명」 / 「조각 1/4」)
    private const float DialLabelGap = 18f;    // 띠 아랫변 → 두 줄 이름 가운데(윗줄이 예전 한 줄 자리)
    private const float HoldLabelY   = 28f;    // 붙듦 안내 — 조각 점 위(띠 아래는 이름 줄)
    private const float HintY        = 50f;    // 처음 안내 — 붙듦 안내 위(16:10에서도 E 칸 원 위로 지난다)
    private const float HintFadeIn   = 0.25f;
    private const float HintFadeOut  = 0.5f;
    private const float RungLabelUp  = 17f;    // 계단 표식 가운데 → 문턱 값 가운데(마름모 꼭짓점 위)
    // 10-22 E1 — 광기 칸 조각 수(맨 오른쪽 문턱 값 「40」 옆, 막대 오른쪽 끝 +14 ~ +94 · 문턱 값 줄 높이 — 16:10에서도 물약 C 칸(+410부터)과 멀다)
    private const float RungCountX   = 14f;
    private const float RungCountW   = 80f;
    private const float LadderHintUp = 22f;    // 문턱 값 가운데 → 처음 안내 가운데(문턱 값 줄 위)
    private const string RungCountFormat = "조각 {0}/4";

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
    private readonly TextMeshProUGUI[] _rungLabel = new TextMeshProUGUI[4];   // 문턱 값(10-18)
    private TextMeshProUGUI _rungCount;   // 광기 칸 조각 수(10-22 E1)
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
    private bool            _holdShown;     // 붙듦 안내를 쓰고 있다 — 바뀔 때만 쓴다
    private readonly TextMeshProUGUI[] _arcLabel = new TextMeshProUGUI[3];   // 띠 이름 · 조각 수(10-18)
    // 처음 안내 한 줄(10-18) — 흐림은 CanvasGroup으로(하드 가드가 글자 알파를 1로 되돌린다)
    private TextMeshProUGUI _hint;
    private CanvasGroup     _hintGroup;
    private float           _hintSec;
    private float           _hintLeft;

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

            // 문턱 값 — 표식 위 가운데. 값은 SetRungValue가 쓴다(10-18 「뜻 모를 표식」).
            var lbl = NewLabel("RungValue" + k, _overlay, UITheme.Thin.Caption, TextAlignmentOptions.Center);
            lbl.color = LabelDim;
            var lrt = lbl.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(x, 1f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(ArcLabelW, ArcLabelH);
            lrt.anchoredPosition = new Vector2(rt.anchoredPosition.x, RungIconY + RungLabelUp);
            _rungLabel[k - 1] = lbl;
        }

        // 광기 칸 조각 수 — 맨 오른쪽 문턱 값 옆(10-22 E1 D7 · 수는 SetRungs가 쓴다)
        _rungCount = NewLabel("RungCount", _overlay, UITheme.Thin.Caption, TextAlignmentOptions.MidlineLeft);
        var crt = _rungCount.rectTransform;
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0f, 0.5f);
        crt.sizeDelta = new Vector2(RungCountW, ArcLabelH);
        crt.anchoredPosition = new Vector2(RungCountX, RungIconY + RungLabelUp);
        _rungCount.color = LabelDim;

        // 처음 안내 한 줄 — 문턱 값 줄 위, 막대 왼쪽 끝에서(ShowHint가 채운다 · 가웨인 하루 띠 안내와 짝 — 10-22 E1 A6)
        BuildHint(new Vector2(0f, 1f), RungIconY + RungLabelUp + LadderHintUp);

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
        _grudgeLabel.rectTransform.sizeDelta = new Vector2(80f, 20f);   // 「원한 3/5」(10-18 — 숫자만으론 뜻을 몰랐다)
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

    /// <summary>
    /// 칸 표식 — occupied[i] = 그 칸에 조각(메아리 포함)이 있다 · count = 광기 칸 조각 수(10-22 E1 D7).
    /// 공명이 켜지면(2개 이상) 조각 칸이 금빛 · 「조각 n/4」는 공명 전 흐림 · 2 · 3 잉크 · 4 금(가웨인 띠 이름과 같은 단계).
    /// </summary>
    public void SetRungs(bool[] occupied, int count)
    {
        if (_dial || _overlay == null) return;
        int tier = RelicResonance.LancelotTier(count);
        for (int i = 0; i < 4; i++)
        {
            if (_rungIcon[i] == null) continue;
            _rungBase[i] = occupied[i] ? (tier >= 2 ? RungLinked : RungOwned) : RungEmpty;
            _rungIcon[i].color = _rungBase[i];
        }
        if (_rungCount == null) return;
        _rungCount.SetText(RungCountFormat, Mathf.Min(count, 4));
        _rungCount.color = tier >= 4 ? UITheme.Thin.Gold : tier >= 2 ? UITheme.Thin.Ink : LabelDim;
    }

    /// <summary>원한 불씨 — 상한만큼 칸, 찬 만큼 불. 옆에 「원한 3/5」, 가득(<paramref name="full"/>)이면 줄에 금 테 · 숫자 금.</summary>
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
            if (cap > 0) _grudgeLabel.SetText("원한 {0}/{1}", Mathf.Min(value, cap), cap);
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

    /// <summary>계단 i(0~3)의 문턱 값(광기 스택) — 첫 칸만 「광기 10」, 나머지는 숫자(카드 자리 이름 「광기 20」과 같은 값).</summary>
    public void SetRungValue(int i, int value)
    {
        if (_dial || i < 0 || i >= 4 || _rungLabel[i] == null) return;
        if (i == 0) _rungLabel[i].SetText("광기 {0}", value);
        else _rungLabel[i].SetText("{0}", value);
    }

    /// <summary>광기가 계단 i(0~3)를 넘었다 — 표식이 한 번 빛난다.</summary>
    public void FlashRung(int i)
    {
        if (i >= 0 && i < 4) _rungFlash[i] = 1f;
    }

    /// <summary>
    /// 매 프레임 — 표식 빛 · 멈춤 · 끝의 문턱 떨림 · 별 · 처음 안내.
    /// <paramref name="holdLeft"/> = 피 냄새 남은 초(0 이하 = 없음) · <paramref name="fill01"/> = 막대 채움(멈춤 선 자리).
    /// </summary>
    public void TickLadder(bool holding, float holdLeft, float fill01)
    {
        float dt = Time.unscaledDeltaTime;
        TickHold(holding, holdLeft, fill01);
        TickHint();
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
        // 자리는 게이지 실제 폭에서 잰다(10-18) — 명조형 게이지가 340 → 440으로 넓어진 뒤에도 186에 고정돼 있어 띠 앞 34px가
        // 게이지 끝(채움 · 끝 빛 · 끝 눈금)에 겹쳤다. 사진 13: 게이지 채움이 여명 띠로 이어져 한 막대처럼 읽히고, 끝 눈금이 두 번째 바늘로 보였다.
        float stripX = sunRoot.Find("SunGauge") is RectTransform sunGauge ? sunGauge.sizeDelta.x * 0.5f + StripPad : StripX;
        _overlay.anchorMin = _overlay.anchorMax = new Vector2(0.5f, 0f);
        _overlay.pivot = new Vector2(0f, 0.5f);
        _overlay.anchoredPosition = new Vector2(stripX, StripY);
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

            // 이름 · 조각 수 — 띠 아래 가운데(「여명 1/4」, 수는 SetArcs가 쓴다)
            var arcName = NewLabel("ArcName" + i, _overlay, UITheme.Thin.Caption, TextAlignmentOptions.Center);
            var nrt = arcName.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0f, 0.5f);
            nrt.pivot = new Vector2(0.5f, 0.5f);
            nrt.anchoredPosition = new Vector2(x + w * 0.5f, -(StripH * 0.5f + DialLabelGap));
            nrt.sizeDelta = new Vector2(ArcLabelW, DialLabelH);
            arcName.color = LabelDim;
            arcName.SetText(ArcFormat[i], 0);
            _arcLabel[i] = arcName;
            x += w + StripGap;
        }

        var needle = NewImage("Needle", _overlay, null, Color.white);
        _needle = needle.rectTransform;
        _needle.anchorMin = _needle.anchorMax = new Vector2(0f, 0.5f);
        _needle.sizeDelta = new Vector2(2f, StripH + 8f);

        // 「새벽이 길다」 붙듦 안내 — 조각 점 위(띠 아래는 이름 줄). 끄지 않고 비운다:
        // 예전엔 SetActive(false)로 숨겨 HUD 하드 가드(LateUpdate · 0.2초마다)가 다시 켠 채 그 프레임이 그려지고 다음 Update에 꺼져,
        // 「…낙일로 정오」가 띠 아래에서 1초에 5번 깜빡였다(10-18 ⑨ 사용자 「HUD 오른쪽 아래에 정오 텍스트가 계속 깜빡이는 버그」).
        _holdLabel = NewLabel("Hold", _overlay, UITheme.Thin.Small, TextAlignmentOptions.MidlineLeft);
        var hrt = _holdLabel.rectTransform;
        hrt.anchorMin = hrt.anchorMax = new Vector2(0f, 0.5f);
        hrt.pivot = new Vector2(0f, 0.5f);
        hrt.anchoredPosition = new Vector2(0f, HoldLabelY);
        hrt.sizeDelta = new Vector2(260f, 22f);
        _holdLabel.color = HoldColor;

        // 처음 안내 한 줄 — ShowHint가 채운다. 붙듦 안내 위, 띠 왼쪽 끝에서(오른쪽으로 길어도 Q · E 칸보다 위라 겹치지 않는다).
        BuildHint(new Vector2(0f, 0.5f), HintY);

        // 엇갈린 빛 별 — 하루 띠 오른쪽 끝
        _star = NewStar(_overlay);
        _star.rectTransform.anchoredPosition = new Vector2(StripW + StarGap + StarSize * 0.5f, 0f);
        Show();
    }

    /// <summary>
    /// 호마다 조각 수(가지형 「궤적」은 두 호에 다 센다). 2 · 4개면 공명 — 호가 밝아진다(4 = 금빛).
    /// 띠 아래 이름도 「여명 1/4」로 — 공명 전 흐림 · 공명 2 잉크 · 공명 4 금(점과 같은 단계).
    /// </summary>
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
            if (_arcLabel[i] == null) continue;
            _arcLabel[i].SetText(ArcFormat[i], Mathf.Min(counts[i], 4));
            _arcLabel[i].color = tier >= 4 ? UITheme.Thin.Gold : tier >= 2 ? UITheme.Thin.Ink : LabelDim;
        }
    }

    /// <summary>
    /// 처음 안내 한 줄 — <paramref name="seconds"/>초 띄우고 걷는다(들어올 때 0.25초 · 나갈 때 0.5초 흐림).
    /// 게임 시간으로 센다 — 조각을 고른 창(시간 멈춤)이 닫힌 뒤부터 보인다. 가웨인 하루 띠 · 랜슬롯 광기 눈금 둘 다(10-22 E1 A6).
    /// </summary>
    public void ShowHint(string text, float seconds)
    {
        if (_hint == null) return;
        _hint.text = text;
        _hintSec = _hintLeft = Mathf.Max(HintFadeIn + HintFadeOut, seconds);
        if (_hintGroup != null) _hintGroup.alpha = 0f;
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
        if (_holdLabel != null && _holdShown != holdingDawn)
        {
            _holdShown = holdingDawn;
            _holdLabel.text = holdingDawn ? "새벽을 붙들었다 · 낙일로 정오" : string.Empty;
        }
        TickHint();
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
        for (int i = 0; i < 4; i++) { _rungIcon[i] = null; _rungFlash[i] = 0f; _rungLabel[i] = null; }
        for (int i = 0; i < 3; i++) { _arc[i] = null; _arcFlash[i] = 0f; _arcLabel[i] = null; }
        _rungCount = null; _emberRow = null; _needle = null; _holdLabel = null; _holdShown = false;
        _hint = null; _hintGroup = null; _hintSec = _hintLeft = 0f;
        _grudgeLabel = null; _grudgeRim = null; _holdTick = null; _holdSec = null; _holdKey = -1;
        _star = null; _starOn = false; _starPop = 0f;
        _host = host;
        _dial = dial;
        _overlay = NewRect(dial ? "RelicMemoryDial" : "RelicMemoryLadder", host);
        Anchor(_overlay, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return true;
    }

    /// <summary>처음 안내 한 줄 자리 — <paramref name="anchor"/>(overlay 기준) 왼쪽 끝에서 오른쪽으로(420px · 흐림은 CanvasGroup — 하드 가드가 글자 알파를 1로 되돌린다).</summary>
    private void BuildHint(Vector2 anchor, float y)
    {
        _hint = NewLabel("Hint", _overlay, UITheme.Thin.Caption, TextAlignmentOptions.MidlineLeft);
        var irt = _hint.rectTransform;
        irt.anchorMin = irt.anchorMax = anchor;
        irt.pivot = new Vector2(0f, 0.5f);
        irt.anchoredPosition = new Vector2(0f, y);
        irt.sizeDelta = new Vector2(420f, ArcLabelH);
        _hintGroup = _hint.gameObject.AddComponent<CanvasGroup>();
        _hintGroup.blocksRaycasts = _hintGroup.interactable = false;
        _hintGroup.alpha = 0f;
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

    /// <summary>처음 안내 — 들어오고 머물고 흐려진 뒤 글을 비운다(끄지 않는다 — 하드 가드).</summary>
    private void TickHint()
    {
        if (_hintLeft <= 0f || _hint == null) return;
        _hintLeft = Mathf.Max(0f, _hintLeft - Time.deltaTime);
        float a = Mathf.Min(1f, (_hintSec - _hintLeft) / HintFadeIn) * Mathf.Min(1f, _hintLeft / HintFadeOut);
        if (_hintGroup != null) _hintGroup.alpha = a;
        if (_hintLeft <= 0f) _hint.text = string.Empty;
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
