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

    private void Awake()
    {
        _stageBadge ??= transform.Find("StageBadge")?.GetComponent<Image>();
        _icon       ??= transform.Find("Icon")?.GetComponent<Image>();
        _nameText   ??= transform.Find("NameText")?.GetComponent<TMP_Text>();
    }

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
