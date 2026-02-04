using TMPro;
using UnityEngine;

public sealed class HudView : MonoBehaviour
{
    [SerializeField] private TMP_Text attackPowerText;
    [SerializeField] private TMP_Text hpText;

    public void Render(UIHudData data)
    {
        if (attackPowerText != null)
            attackPowerText.text = data.AttackPower.ToString();

        if (hpText != null)
            hpText.text = $"{data.Hp}/{data.MaxHp}";
    }

    public void Clear()
    {
        if (attackPowerText != null)
            attackPowerText.text = "-";

        if (hpText != null)
            hpText.text = "-/-";
    }
}
