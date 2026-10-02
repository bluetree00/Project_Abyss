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
        // 그 외(고정 서약)는 기존대로 단계별 문구를 인용체로.
        if (_descText != null)
        {
            string cause  = covenant.CauseText;
            string effect = covenant.EffectText;

            string text;
            if (!string.IsNullOrWhiteSpace(cause) && !string.IsNullOrWhiteSpace(effect))
            {
                _descText.fontStyle = FontStyles.Normal;
                text = cause + "\n↓\n" + effect;
            }
            else
            {
                string desc = covenant.Stage switch
                {
                    CovenantStage.Evolved  => covenant.EvolvedDescription,
                    CovenantStage.Enhanced => covenant.EnhancedDescription,
                    _                      => covenant.BasicDescription,
                };
                _descText.fontStyle = FontStyles.Italic;
                text = string.IsNullOrWhiteSpace(desc) ? string.Empty : $"\"{desc}\"";
            }

            bool has = !string.IsNullOrEmpty(text);
            _descText.text = UIKeywordInk.Words(text, UIKeywordInk.OnDark);   // 수치에 금색(09-29)
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

    private RectTransform _pipRow;
    private readonly System.Collections.Generic.List<Image> _pips = new();
    private readonly System.Collections.Generic.List<float> _pipUntil = new();

    /// <summary>서약서 한 장 — 제목 「서약서 · 조건」, 설명 = 조건 + 결과 줄(이음 머리말), 절 표식(결과마다 마름모). 반환 = 설명 줄 수.</summary>
    public int BindSentence(CovenantSentence s)
    {
        gameObject.SetActive(true);
        if (_icon != null) _icon.gameObject.SetActive(false);
        if (_stageBadge != null) _stageBadge.color = ColorBasic;

        string causeName = CovenantPalette.TryGetCause(s.CauseId, out var cd) ? cd.name : s.CauseId;
        if (_nameText != null) _nameText.text = $"서약서 · {causeName}";

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
            var c = lit ? PipLit : PipDim;
            if (_pips[i].color != c) _pips[i].color = c;
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
    }
}
