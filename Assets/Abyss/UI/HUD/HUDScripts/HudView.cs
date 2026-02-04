using TMPro;
using UnityEngine;

public sealed class HudView : MonoBehaviour
{
    [SerializeField] private TMP_Text attackPowerText;

    public void Render(UIHudData data)
    {
        if (attackPowerText != null)
            attackPowerText.text = data.AttackPower.ToString();
    }

    public void Clear()
    {
        if (attackPowerText != null)
            attackPowerText.text = "-";
    }
}
