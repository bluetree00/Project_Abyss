using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TopPanelViewer : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI textNickname;
    [SerializeField] private TextMeshProUGUI textLevel;
    [SerializeField] private Slider          sliderExperience;
    [SerializeField] private TextMeshProUGUI textHeart;
    [SerializeField] private TextMeshProUGUI textGold;
    [SerializeField] private TextMeshProUGUI textJewel;

    // ── Lifecycle ──────────────────────────────────────────────────────────
    private void OnEnable()
    {
        if (BackendGameData.Instance != null)
            BackendGameData.Instance.OnDataLoaded += Refresh;
    }

    private void OnDisable()
    {
        if (BackendGameData.Instance != null)
            BackendGameData.Instance.OnDataLoaded -= Refresh;
    }

    // ── Public Methods ─────────────────────────────────────────────────────

    public void UpdateNickname()
    {
        if (textNickname == null) return;
        textNickname.text = string.IsNullOrEmpty(UserInfo.Data.nickname)
            ? UserInfo.Data.gamerId
            : UserInfo.Data.nickname;
    }

    // ── Private Methods ────────────────────────────────────────────────────

    private void Refresh()
    {
        var d = BackendGameData.Instance?.Data;
        if (d == null) return;

        if (textLevel        != null) textLevel.text        = $"{d.level}";
        if (sliderExperience != null) sliderExperience.value = d.experience / 100f;
        if (textHeart        != null) textHeart.text        = $"{d.heart}/30";
        if (textGold         != null) textGold.text         = $"{d.gold}";
        if (textJewel        != null) textJewel.text        = $"{d.jewel}";
    }
}
