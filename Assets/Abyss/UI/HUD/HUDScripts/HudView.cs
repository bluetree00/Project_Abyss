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
    [SerializeField] private BossPanelView    bossPanelView;
    [SerializeField] private GameObject       systemNoticesRoot;

    [Header("TopBar — Info")]
    [SerializeField] private TMP_Text nicknameText;
    [SerializeField] private TMP_Text goldText;

    // ─────────────────────────────────────────────────────────
    // 패널 접근자 (HudPresenter → 패널별 데이터 전달 시 사용)
    // ─────────────────────────────────────────────────────────
    public CombatPanelView CombatPanel => combatPanel;
    public BossPanelView   BossPanel   => bossPanelView;

    // ─────────────────────────────────────────────────────────
    // Section 제어
    // ─────────────────────────────────────────────────────────
    public void SetSections(HUDIds.Section sections)
    {
        bool showCombat = (sections & HUDIds.Section.CombatPanel) != 0;

        SetActiveSafe(topBarRoot,        (sections & HUDIds.Section.TopBar)        != 0);
        SetActiveSafe(combatPanel,       showCombat);
        SetActiveSafe(bossPanelView,     (sections & HUDIds.Section.BossPanel)     != 0);
        SetActiveSafe(systemNoticesRoot, (sections & HUDIds.Section.SystemNotices) != 0);

        if (showCombat)
            EnsureCombatPanelVisible();
    }

    public void EnsureCombatPanelVisible()
    {
        SetActiveSafe(combatPanel, true);

        var panelCombat = FindChildRecursive(transform, "Panel_Combat");
        if (panelCombat != null && !panelCombat.gameObject.activeSelf)
            panelCombat.gameObject.SetActive(true);
    }

    // ─────────────────────────────────────────────────────────
    // TopBar 데이터 갱신 (HudPresenter → 여기)
    // ─────────────────────────────────────────────────────────
    public void SetNickname(string nickname)
    {
        if (nicknameText != null)
            nicknameText.text = nickname;
    }

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

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindChildRecursive(root.GetChild(i), name);
            if (found != null) return found;
        }

        return null;
    }
}
