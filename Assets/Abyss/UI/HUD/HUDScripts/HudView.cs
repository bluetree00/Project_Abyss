//============================================================
// HudView.cs
//============================================================
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class HudView : MonoBehaviour
{
    [Header("Sections")]
    [SerializeField] private GameObject topBarRoot;
    [SerializeField] private GameObject combatPanelRoot;
    [SerializeField] private GameObject explorePanelRoot;
    [SerializeField] private GameObject bossPanelRoot;
    [SerializeField] private GameObject systemNoticesRoot;

    [Header("HP")]
    [SerializeField] private Slider hpSlider;
    [SerializeField] private TMP_Text hpText;

    [Header("Gold")]
    [SerializeField] private TMP_Text goldText;

    public void SetSections(HUDIds.Section sections)
    {
        SetActiveSafe(topBarRoot, (sections & HUDIds.Section.TopBar) != 0);
        SetActiveSafe(combatPanelRoot, (sections & HUDIds.Section.CombatPanel) != 0);
        SetActiveSafe(explorePanelRoot, (sections & HUDIds.Section.ExplorePanel) != 0);
        SetActiveSafe(bossPanelRoot, (sections & HUDIds.Section.BossPanel) != 0);
        SetActiveSafe(systemNoticesRoot, (sections & HUDIds.Section.SystemNotices) != 0);
    }

    private static void SetActiveSafe(GameObject go, bool on)
    {
        if (go == null) return;
        if (go.activeSelf == on) return;
        go.SetActive(on);
    }

    public void SetHp(int hp, int maxHp)
    {
        if (hpSlider != null)
        {
            hpSlider.maxValue = Mathf.Max(1, maxHp);
            hpSlider.value = Mathf.Clamp(hp, 0, maxHp);
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
