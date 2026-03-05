//============================================================
// CombatPanelView.cs
// - Panel_Combat 하위 전투 HUD 바인딩 담당
// - CombatStatusRoot 안의 UI 요소에 직접 연결
//============================================================
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class CombatPanelView : MonoBehaviour
{
    [Header("Status — HP")]
    [SerializeField] private Slider   hpSlider;
    [SerializeField] private TMP_Text hpText;

    // TODO: 스킬 슬롯 구현 시 여기에 추가
    // [Header("Skills")]
    // [SerializeField] private SkillSlotUI[] skillSlots;

    // ─────────────────────────────────────────────────────────
    // 데이터 갱신 (HudPresenter → HudView.CombatPanel → 여기)
    // ─────────────────────────────────────────────────────────

    public void SetHp(int hp, int maxHp)
    {
        if (hpSlider != null)
        {
            hpSlider.maxValue = Mathf.Max(1, maxHp);
            hpSlider.value    = Mathf.Clamp(hp, 0, maxHp);
        }
        if (hpText != null)
            hpText.text = $"{hp} / {maxHp}";
    }
}
