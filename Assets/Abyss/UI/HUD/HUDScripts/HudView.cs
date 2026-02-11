using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class HudView : MonoBehaviour
{
    [Header("HP")]
    [SerializeField] private Slider hpSlider;
    [SerializeField] private TMP_Text hpText;

    [Header("Gold")]
    [SerializeField] private TMP_Text goldText;

    public void SetHp(int hp, int maxHp)
    {
        if (hpSlider != null)
        {
            hpSlider.maxValue = maxHp;
            hpSlider.value = hp;
        }

        if (hpText != null)
            hpText.text = $"{hp} / {maxHp}";
    }

    public void SetGold(int gold)
    {
        if (goldText != null)
            goldText.text = gold.ToString();
    }
}
