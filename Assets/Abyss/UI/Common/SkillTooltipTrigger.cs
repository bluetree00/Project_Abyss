using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 스킬 아이콘에 부착. 마우스 호버 시 툴팁 패널을 표시한다.
/// </summary>
public class SkillTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private GameObject _tooltipPanel;
    private TMP_Text   _tooltipName;
    private TMP_Text   _tooltipDesc;
    private TMP_Text   _tooltipCooldown;

    private string _name;
    private string _desc;
    private float  _cooldown;

    public void SetData(SkillSO skill)
    {
        if (skill == null) { _name = "---"; _desc = ""; _cooldown = 0; return; }
        _name = skill.skillName;
        _desc = skill.description;
        _cooldown = skill.cooldown;
    }

    public void SetData(string name, string desc, float cooldown)
    {
        _name = name ?? "---";
        _desc = desc ?? "";
        _cooldown = cooldown;
    }

    public void SetTooltipPanel(GameObject panel, TMP_Text nameText, TMP_Text descText, TMP_Text cooldownText)
    {
        _tooltipPanel    = panel;
        _tooltipName     = nameText;
        _tooltipDesc     = descText;
        _tooltipCooldown = cooldownText;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_tooltipPanel == null) return;
        _tooltipPanel.SetActive(true);

        if (_tooltipName != null) _tooltipName.text = _name ?? "---";
        if (_tooltipDesc != null) _tooltipDesc.text = _desc ?? "";
        if (_tooltipCooldown != null)
            _tooltipCooldown.text = _cooldown > 0 ? $"쿨다운: {_cooldown:F1}초" : "";

        // 마우스 위치 근처에 표시
        var rt = _tooltipPanel.GetComponent<RectTransform>();
        if (rt != null)
            rt.position = eventData.position + new Vector2(10, 40);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_tooltipPanel != null)
            _tooltipPanel.SetActive(false);
    }
}
