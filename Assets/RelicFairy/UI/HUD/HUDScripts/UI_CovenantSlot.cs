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

        if (_icon != null)
        {
            _icon.sprite = covenant.Icon;
            _icon.color  = covenant.Icon != null ? Color.white : new Color(1f, 1f, 1f, 0.25f);
        }

        if (_nameText != null)
            _nameText.text = covenant.DisplayName;

        // 효과 설명 — 현재 단계에 해당하는 문구(목업처럼 따옴표 인용체)
        if (_descText != null)
        {
            string desc = covenant.Stage switch
            {
                CovenantStage.Evolved  => covenant.EvolvedDescription,
                CovenantStage.Enhanced => covenant.EnhancedDescription,
                _                      => covenant.BasicDescription,
            };
            bool has = !string.IsNullOrWhiteSpace(desc);
            _descText.text = has ? $"“{desc}”" : string.Empty;
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
}
