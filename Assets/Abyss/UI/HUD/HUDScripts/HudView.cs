//============================================================
// HudView.cs
// - Section 플래그에 따라 패널 루트 온/오프
// - 각 패널의 세부 바인딩은 패널별 XxxPanelView에 위임
//============================================================
using UnityEngine;
using TMPro;

public sealed class HudView : MonoBehaviour
{
    [Header("Sections")]
    [SerializeField] private GameObject       topBarRoot;
    [SerializeField] private CombatPanelView  combatPanel;
    [SerializeField] private GameObject       bossPanelRoot;
    [SerializeField] private GameObject       systemNoticesRoot;

    [Header("TopBar — Gold")]
    [SerializeField] private TMP_Text goldText;

    // ─────────────────────────────────────────────────────────
    // 패널 접근자 (HudPresenter → 패널별 데이터 전달 시 사용)
    // ─────────────────────────────────────────────────────────
    public CombatPanelView CombatPanel => combatPanel;

    // ─────────────────────────────────────────────────────────
    // Section 제어
    // ─────────────────────────────────────────────────────────
    public void SetSections(HUDIds.Section sections)
    {
        SetActiveSafe(topBarRoot,        (sections & HUDIds.Section.TopBar)        != 0);
        SetActiveSafe(combatPanel,       (sections & HUDIds.Section.CombatPanel)   != 0);
        SetActiveSafe(bossPanelRoot,     (sections & HUDIds.Section.BossPanel)     != 0);
        SetActiveSafe(systemNoticesRoot, (sections & HUDIds.Section.SystemNotices) != 0);
    }

    // ─────────────────────────────────────────────────────────
    // TopBar 데이터 갱신 (HudPresenter → 여기)
    // ─────────────────────────────────────────────────────────
    public void SetGold(int gold)
    {
        if (goldText != null)
            goldText.text = gold.ToString();
    }

    // ─────────────────────────────────────────────────────────
    // Internal helpers
    // ─────────────────────────────────────────────────────────
    private static void SetActiveSafe(MonoBehaviour comp, bool on)
    {
        if (comp == null) return;
        SetActiveSafe(comp.gameObject, on);
    }

    private static void SetActiveSafe(GameObject go, bool on)
    {
        if (go == null) return;
        if (go.activeSelf == on) return;
        go.SetActive(on);
    }
}
