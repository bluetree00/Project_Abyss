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
            _descText.text = UIKeywordInk.Values(UIKoreanWrap.Words(text));   // 수치만 Noto 500 · 조용한 잉크(10-15 — 금 아님)
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
    // 10-15: 절 표식 = 결과 줄의 머리(옛 「→」 자리). 예전엔 제목 위 한 줄에 따로 모여 어느 절이 켜졌는지 줄과 짝을 지을 수 없었다 —
    // 이제 연쇄가 일어나면 표식이 목록을 따라 위에서 아래로 켜진다.
    private const float PipSize   = 8f;
    private const float PipLitSec = 0.7f;
    private const float ResultIndentEm = 1.15f;   // 결과 줄 들여쓰기(표식 자리) — 줄바꿈돼도 글머리가 맞는다
    private const string CauseAlpha = "<alpha=#B3>";   // 원인 줄 = 결과보다 한 단 옅게(0.70)
    private static readonly Color PipDim = UITheme.Thin.InkA(0.45f);                  // 글머리 — 잉크(옛 갈색 0.55는 어두운 판에 묻혔다)
    private static readonly Color PipLit = new(1.00f, 0.82f, 0.40f, 1.00f);

    private static readonly Color PipCool = UITheme.Thin.InkA(0.16f);   // 절이 쿨다운 중 — 지금은 이어지지 않는다
    private bool    _pipLayoutDirty;   // 줄 자리가 바뀌었다(묶음 · 칸 크기 변경) — Update에서 한 번 다시 놓는다
    private Vector2 _pipRectSize;
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

    /// <summary>
    /// 서약서 한 장 — 제목 = 조건 이름(+ 진척), 설명 = 조건 한 줄(옅게) + 결과마다 한 줄(머리 = 절 표식 · 줄바꿈은 들여쓴 채로).
    /// 「서약서」 낱말은 칸 위 「서약서 n절」이 맡는다(10-15 — 예전엔 제목 「서약서 · 연격」과 두 번). 반환 = 설명 높이(px, 줄바꿈까지 잰 값).
    /// </summary>
    public float BindSentence(CovenantSentence s, float descWidth)
    {
        gameObject.SetActive(true);
        if (_icon != null) _icon.gameObject.SetActive(false);
        if (_stageBadge != null) _stageBadge.color = ColorBasic;

        string causeName = CovenantPalette.TryGetCause(s.CauseId, out var cd) ? cd.name : s.CauseId;
        if (_nameText != null) _nameText.text = causeName;
        BuildProgressLabels(s, causeName);

        float h = 0f;
        if (_descText != null)
        {
            var sb = new System.Text.StringBuilder(CauseAlpha).Append(UIKoreanWrap.Words(cd.desc)).Append("<alpha=#FF>");
            for (int i = 0; i < s.ResultCount; i++)
            {
                string prev = i > 0 ? s.ResultId(i - 1) : null;
                sb.Append("\n<indent=").Append(ResultIndentEm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append("em>")
                  .Append(UIKeywordInk.Values(UIKoreanWrap.Words(CovenantGrammar.Line(s.ResultId(i), s.ResultLink(i), prev)))).Append("</indent>");   // 결과 줄만 수치 강조(10-15) — 원인 줄은 옅은 채(색 태그가 <alpha>를 되살린다)
            }
            _descText.fontStyle = FontStyles.Normal;
            _descText.text = sb.ToString();
            _descText.gameObject.SetActive(true);
            h = _descText.GetPreferredValues(_descText.text, descWidth, 0f).y;
        }
        EnsurePips(s.ResultCount);
        return h;
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
        if (_pipRow != null && _pipRow.gameObject.activeSelf && _descText != null
            && (_pipLayoutDirty || _descText.rectTransform.rect.size != _pipRectSize)) LayoutPips();
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

    /// <summary>결과 줄 글머리 마름모 — 결과 수만큼. 설명 글자와 같은 좌표계(설명 위에 겹친 판)에 두고 자리는 <see cref="LayoutPips"/>가 줄에서 잰다.</summary>
    private void EnsurePips(int count)
    {
        if (_pipRow == null && _descText != null)
        {
            var go = new GameObject("Pips", typeof(RectTransform));
            _pipRow = (RectTransform)go.transform;
            _pipRow.SetParent(_descText.transform, false);
            _pipRow.anchorMin = Vector2.zero;
            _pipRow.anchorMax = Vector2.one;
            _pipRow.offsetMin = _pipRow.offsetMax = Vector2.zero;
            _pipRow.pivot = _descText.rectTransform.pivot;   // 글자 좌표(피벗 기준)를 그대로 쓴다
        }
        if (_pipRow == null) return;
        while (_pips.Count < count)
        {
            var go = new GameObject($"Pip{_pips.Count}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_pipRow, false);
            rt.anchorMin = rt.anchorMax = _pipRow.pivot;
            rt.sizeDelta = new Vector2(PipSize, PipSize);
            rt.localEulerAngles = new Vector3(0f, 0f, 45f);
            var img = go.GetComponent<Image>();
            img.color = PipDim;
            img.raycastTarget = false;
            _pips.Add(img);
            _pipUntil.Add(0f);
        }
        for (int i = 0; i < _pips.Count; i++) _pips[i].gameObject.SetActive(i < count);
        _pipRow.gameObject.SetActive(count > 0);
        _pipLayoutDirty = true;
    }

    /// <summary>
    /// 글머리를 결과 줄 첫 줄에 놓는다 — 줄마다 「바로 앞 글자가 줄바꿈(\n)」이면 새 문단(0 = 조건 줄, k = 결과 k-1).
    /// 묶을 때 · 칸 크기가 바뀔 때만(Update가 부른다) — 매 프레임 메시를 다시 만들지 않는다.
    /// </summary>
    private void LayoutPips()
    {
        _pipLayoutDirty = false;
        var rt = _descText.rectTransform;
        _pipRectSize = rt.rect.size;
        if (_pipRectSize.x < 1f) return;   // 아직 레이아웃 전 — 크기가 생기면 다시 온다
        _descText.ForceMeshUpdate();
        var ti = _descText.textInfo;
        float x = rt.rect.xMin + _descText.fontSize * ResultIndentEm * 0.5f;
        int para = 0;
        for (int l = 0; l < ti.lineCount; l++)
        {
            int first = ti.lineInfo[l].firstCharacterIndex;
            if (first > 0 && ti.characterInfo[first - 1].character != '\n') continue;   // 줄바꿈으로 이어진 줄
            int pip = para - 1;
            para++;
            if (pip < 0 || pip >= _pips.Count || !_pips[pip].gameObject.activeSelf) continue;
            float y = (ti.lineInfo[l].ascender + ti.lineInfo[l].descender) * 0.5f;
            ((RectTransform)_pips[pip].transform).anchoredPosition = new Vector2(x, y);
        }
    }

    /// <summary>조립 서약으로 다시 쓰일 때(옛 세이브) 표식을 감춘다.</summary>
    public void HidePips()
    {
        if (_pipRow != null) _pipRow.gameObject.SetActive(false);
        _bound = null;   // 진척 글자도 멈춘다
    }
}
