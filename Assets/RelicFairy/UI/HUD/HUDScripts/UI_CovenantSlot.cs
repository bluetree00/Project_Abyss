using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD 내 서약 1개를 표시하는 슬롯.
/// 아이콘 + 이름 + 단계 색상 바로 구성된다.
/// </summary>
public sealed class UI_CovenantSlot : MonoBehaviour
{
    [SerializeField] private Image    _stageBadge; // 좌측 색상 바
    [SerializeField] private Image    _icon;
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private TMP_Text _descText;   // 효과 설명(스테이지별) — CovenantPanelView가 없으면 생성

    private void Awake() => EnsureRefs();

    /// <summary>
    /// 자식 참조를 해석한다(멱등).
    /// 부모(CovenantPanelView)의 Awake가 이 슬롯의 Awake보다 <b>먼저</b> 돌 수 있어,
    /// 부모가 슬롯을 만지기 전에 직접 호출해 null 참조를 막는다.
    /// </summary>
    public void EnsureRefs()
    {
        _stageBadge ??= transform.Find("StageBadge")?.GetComponent<Image>();
        _icon       ??= transform.Find("Icon")?.GetComponent<Image>();
        _nameText   ??= transform.Find("NameText")?.GetComponent<TMP_Text>();
        _descText   ??= transform.Find("DescText")?.GetComponent<TMP_Text>();
    }

    /// <summary>CovenantPanelView가 런타임 생성한 설명 텍스트를 주입.</summary>
    public void AttachDescText(TMP_Text desc) => _descText = desc;

    /// <summary>
    /// 톤 v2 글자 — 제목 Noto Medium · 설명 Noto Regular, 가짜 굵게 · 기울임 없음(CovenantPanelView가 글꼴이 올라온 뒤 한 번).
    /// 칸 안쪽 어두운 판 위라 그림자는 두지 않는다(획만 두꺼워진다).
    /// </summary>
    public void StyleThin(float titleSize, Color titleInk, float descSize, Color descInk)
    {
        UITheme.StyleThinText(_nameText, UIWeight.Medium, titleSize, titleInk);
        UITheme.StyleThinText(_descText, UIWeight.Regular, descSize, descInk);
    }

    public TMP_Text NameText => _nameText;
    public Image    Icon       => _icon;
    public Image    StageBadge => _stageBadge;

    private static readonly Color ColorBasic    = new Color(0.75f, 0.75f, 0.75f, 1f);
    private static readonly Color ColorEnhanced = new Color(0.40f, 0.80f, 1.00f, 1f);
    private static readonly Color ColorEvolved  = new Color(1.00f, 0.75f, 0.20f, 1f);

    public void Bind(CovenantBase covenant)
    {
        gameObject.SetActive(true);
        HidePips();

        if (_icon != null)
        {
            _icon.sprite = covenant.Icon;
            _icon.color  = covenant.Icon != null ? Color.white : new Color(1f, 1f, 1f, 0.25f);
        }

        if (_nameText != null)
            _nameText.text = UIKoreanWrap.Words(covenant.DisplayName);   // 「마지막 숨결[골드]」가 낱말 중간에서 갈리지 않게

        // 효과 설명.
        // 조립 서약은 원인/결과가 별도 데이터다 → 한 줄로 이어붙이지 않고 <b>줄을 나눠</b> 보여준다(좁은 칸에서 훨씬 읽힌다).
        // 그 외(고정 서약)는 단계별 문구 그대로 — 기울임 · 따옴표 인용은 톤 v2에서 뺐다(10-11).
        if (_descText != null)
        {
            string cause  = covenant.CauseText;
            string effect = covenant.EffectText;

            string text;
            if (!string.IsNullOrWhiteSpace(cause) && !string.IsNullOrWhiteSpace(effect))
                text = cause + "\n↓\n" + effect;
            else
            {
                string desc = covenant.Stage switch
                {
                    CovenantStage.Evolved  => covenant.EvolvedDescription,
                    CovenantStage.Enhanced => covenant.EnhancedDescription,
                    _                      => covenant.BasicDescription,
                };
                text = string.IsNullOrWhiteSpace(desc) ? string.Empty : desc;
            }

            bool has = !string.IsNullOrEmpty(text);
            _descText.fontStyle = FontStyles.Normal;
            // 수치 금색(09-29)은 뺐다 — 톤 v2에서 금은 「준비됨 · 지금」 한 뜻(10-11 금색 뜻 정리)
            _descText.text = UIKoreanWrap.Words(text);
            _descText.gameObject.SetActive(has);
        }

        if (_stageBadge != null)
            _stageBadge.color = covenant.Stage switch
            {
                CovenantStage.Enhanced => ColorEnhanced,
                CovenantStage.Evolved  => ColorEvolved,
                _                      => ColorBasic,
            };
    }

    public void SetEmpty() => gameObject.SetActive(false);

    // ── 서약서(문장) — 「한 장의 서약서」(10-02 설계서 §4) ──────────
    private const float PipSize   = 9f;
    private const float PipGap    = 5f;
    private const float PipLitSec = 0.7f;
    private static readonly Color PipDim = new(0.55f, 0.50f, 0.42f, 0.55f);
    private static readonly Color PipLit = new(1.00f, 0.82f, 0.40f, 1.00f);

    private static readonly Color PipCool = new(0.30f, 0.28f, 0.25f, 0.35f);   // 절이 쿨다운 중 — 지금은 이어지지 않는다
    private const float ProgressTick = 0.1f;   // 진척 글자 갱신 주기(초)

    private RectTransform _pipRow;
    private readonly System.Collections.Generic.List<Image> _pips = new();
    private readonly System.Collections.Generic.List<float> _pipUntil = new();

    // 진척(10-07 U2) — 제목 오른쪽 한 마디. 문자열은 묶을 때 미리 지어 두고 Update에선 고르거나 숫자만 넣는다(할당 0).
    private CovenantSentence _bound;
    private string[] _countLabels;    // cur 0..max — 「연격 ●●○」
    private string   _fmtSeconds, _fmtMeters, _fmtCooldown, _fmtWindow;
    private string   _labelIdle, _labelWindowWait, _labelArmed, _labelSpent, _labelPlain;
    private float    _nextProgressAt;
    private int      _lastKind = -1, _lastCur = -1;
    private float    _lastSec = -99f;
    private bool     _chargeMeters;   // 행군 — 남은 거리(m)

    /// <summary>서약서 한 장 — 제목 「서약서 · 조건」, 설명 = 조건 + 결과 줄(이음 머리말), 절 표식(결과마다 마름모). 반환 = 설명 줄 수.</summary>
    public int BindSentence(CovenantSentence s)
    {
        gameObject.SetActive(true);
        if (_icon != null) _icon.gameObject.SetActive(false);
        if (_stageBadge != null) _stageBadge.color = ColorBasic;

        string causeName = CovenantPalette.TryGetCause(s.CauseId, out var cd) ? cd.name : s.CauseId;
        if (_nameText != null) _nameText.text = $"서약서 · {causeName}";
        BuildProgressLabels(s, $"서약서 · {causeName}");

        int lines = 1;
        if (_descText != null)
        {
            var sb = new System.Text.StringBuilder(cd.desc);
            for (int i = 0; i < s.ResultCount; i++)
            {
                string prev = i > 0 ? s.ResultId(i - 1) : null;
                sb.Append('\n').Append("→ ").Append(CovenantGrammar.Line(s.ResultId(i), s.ResultLink(i), prev));
                lines++;
            }
            _descText.fontStyle = FontStyles.Normal;
            _descText.text = sb.ToString();
            _descText.gameObject.SetActive(true);
        }
        EnsurePips(s.ResultCount);
        return lines;
    }

    /// <summary>절 하나가 일어났다 — 그 표식을 <paramref name="delay"/>초 뒤부터 잠깐 밝힌다(연쇄 = 차례로 켜짐).</summary>
    public void FlashPip(int index, float delay)
    {
        if (index < 0 || index >= _pips.Count) return;
        _pipUntil[index] = Time.unscaledTime + delay + PipLitSec;
        _pipFrom ??= new System.Collections.Generic.List<float>();
        while (_pipFrom.Count < _pips.Count) _pipFrom.Add(0f);
        _pipFrom[index] = Time.unscaledTime + delay;
    }

    private System.Collections.Generic.List<float> _pipFrom;

    private void Update()
    {
        if (_pips.Count == 0) return;
        float now = Time.unscaledTime;
        for (int i = 0; i < _pips.Count; i++)
        {
            float from = _pipFrom != null && i < _pipFrom.Count ? _pipFrom[i] : 0f;
            bool lit = now >= from && now < _pipUntil[i];
            var c = lit ? PipLit : (_bound != null && _bound.ResultCooldownLeft(i) > 0f ? PipCool : PipDim);
            if (_pips[i].color != c) _pips[i].color = c;
        }
        if (_bound != null && now >= _nextProgressAt)
        {
            _nextProgressAt = now + ProgressTick;
            RefreshProgress();
        }
    }

    /// <summary>
    /// 원인 모양에 맞는 글자 틀을 미리 짓는다 — 「연격 ●●○」 · 「심장박동 2.1초」 · 「포위 재정비 1.2초」 · 「전환 1.5초 안에」.
    /// 색 뜻(10-11 톤 v2 금색 정리): 금 = 지금 일어난다(준비됨 · n초 안에) · 잉크 = 차오르는 중(연격 수 · 남은 초 · 거리) · 회색 = 쉬는 중.
    /// 예전엔 차오르는 값도 금이라 「준비됨」과 같은 색이었다.
    /// </summary>
    private void BuildProgressLabels(CovenantSentence s, string prefix)
    {
        _bound = s;
        _lastKind = -1; _lastCur = -1; _lastSec = -99f; _nextProgressAt = 0f;
        _chargeMeters = CovenantPalette.TryGetCause(s.CauseId, out var def) && def.trigger == CauseTriggerKind.OnMoveDistance;
        var p = s.CauseProgress;
        string ready = "<color=#" + ColorUtility.ToHtmlStringRGB(UITheme.Thin.Gold) + ">";
        string fill  = "<color=#" + ColorUtility.ToHtmlStringRGB(UITheme.Thin.Ink) + ">";
        const string Off = "<color=#8A8594>", End = "</color>";
        _labelPlain = prefix;
        if (p.kind == CovenantProgressKind.Count && p.max > 0)
        {
            _countLabels = new string[p.max + 1];
            for (int cur = 0; cur <= p.max; cur++)
                _countLabels[cur] = p.max <= 6
                    ? prefix + "  " + fill + new string('●', cur) + End + Off + new string('○', p.max - cur) + End
                    : prefix + "  " + fill + cur + "/" + p.max + End;
        }
        _fmtSeconds      = prefix + "  " + fill + "{0:1}초" + End;
        _fmtMeters       = prefix + "  " + fill + "{0:0}m" + End;
        _fmtCooldown     = prefix + "  " + Off + "재정비 {0:1}초" + End;
        _fmtWindow       = prefix + "  " + ready + "{0:1}초 안에!" + End;
        _labelIdle       = prefix + "  " + Off + "전투 대기" + End;
        _labelWindowWait = prefix + "  " + Off + "기회 대기" + End;
        _labelArmed      = prefix + "  " + ready + "준비됨" + End;
        _labelSpent      = prefix + "  " + Off + "이 방은 끝" + End;
    }

    /// <summary>진척 글자를 바꾼다 — 값이 그대로면 손대지 않는다(메시 재생성 없음).</summary>
    private void RefreshProgress()
    {
        if (_nameText == null) return;
        var p = _bound.CauseProgress;
        int kind = (int)p.kind;
        float sec = Mathf.Round(p.seconds * 10f) / 10f;
        if (kind == _lastKind && p.cur == _lastCur && Mathf.Approximately(sec, _lastSec)) return;
        _lastKind = kind; _lastCur = p.cur; _lastSec = sec;

        switch (p.kind)
        {
            case CovenantProgressKind.Count:
                if (_countLabels != null && p.cur >= 0 && p.cur < _countLabels.Length) _nameText.text = _countLabels[p.cur];
                break;
            case CovenantProgressKind.Charge:
                if (p.seconds < 0f) _nameText.text = _labelIdle;
                else if (_chargeMeters) _nameText.SetText(_fmtMeters, p.seconds);
                else _nameText.SetText(_fmtSeconds, p.seconds);
                break;
            case CovenantProgressKind.Cooldown: _nameText.SetText(_fmtCooldown, p.seconds); break;
            case CovenantProgressKind.Window:
                if (p.seconds > 0f) _nameText.SetText(_fmtWindow, p.seconds);
                else _nameText.text = _labelWindowWait;
                break;
            case CovenantProgressKind.Armed: _nameText.text = p.cur > 0 ? _labelArmed : _labelSpent; break;
            default: _nameText.text = _labelPlain; break;
        }
    }

    /// <summary>제목 아래 마름모 줄 — 결과 수만큼(레이아웃에서 빠진 덧그림).</summary>
    private void EnsurePips(int count)
    {
        if (_pipRow == null)
        {
            var go = new GameObject("Pips", typeof(RectTransform), typeof(LayoutElement));
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            _pipRow = (RectTransform)go.transform;
            _pipRow.SetParent(transform, false);
            _pipRow.anchorMin = _pipRow.anchorMax = new Vector2(0.5f, 1f);
            _pipRow.pivot = new Vector2(0.5f, 1f);
            _pipRow.anchoredPosition = new Vector2(0f, -4f);
            _pipRow.sizeDelta = new Vector2(200f, PipSize + 4f);
        }
        while (_pips.Count < count)
        {
            var go = new GameObject($"Pip{_pips.Count}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_pipRow, false);
            rt.sizeDelta = new Vector2(PipSize, PipSize);
            rt.localEulerAngles = new Vector3(0f, 0f, 45f);
            var img = go.GetComponent<Image>();
            img.color = PipDim;
            img.raycastTarget = false;
            _pips.Add(img);
            _pipUntil.Add(0f);
        }
        float total = count * PipSize + (count - 1) * PipGap;
        for (int i = 0; i < _pips.Count; i++)
        {
            bool on = i < count;
            _pips[i].gameObject.SetActive(on);
            if (on) ((RectTransform)_pips[i].transform).anchoredPosition = new Vector2(-total * 0.5f + PipSize * 0.5f + i * (PipSize + PipGap), -(PipSize * 0.5f + 2f));
        }
        _pipRow.gameObject.SetActive(count > 0);
    }

    /// <summary>조립 서약으로 다시 쓰일 때(옛 세이브) 표식을 감춘다.</summary>
    public void HidePips()
    {
        if (_pipRow != null) _pipRow.gameObject.SetActive(false);
        _bound = null;   // 진척 글자도 멈춘다
    }
}
