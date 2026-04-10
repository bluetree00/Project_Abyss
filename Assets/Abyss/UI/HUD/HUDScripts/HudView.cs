//============================================================
// HudView.cs
// - Section 플래그에 따라 루트 표시 스왑
// - 각 패널은 Presenter가 패널별 View로 직접 데이터 전달
//============================================================
using UnityEngine;
using TMPro;

public sealed class HudView : MonoBehaviour
{
    [Header("Sections")]
    [SerializeField] private GameObject topBarRoot;
    [SerializeField] private CombatPanelView combatPanel;
    [SerializeField] private GameObject gridPanel;
    [SerializeField] private BossPanelView bossPanelView;
    [SerializeField] private GameObject systemNoticesRoot;

    [Header("TopBar Info")]
    [SerializeField] private TMP_Text nicknameText;
    [SerializeField] private TMP_Text goldText;

    public CombatPanelView CombatPanel => combatPanel;
    public BossPanelView BossPanel => bossPanelView;

    private void Awake()
    {
        combatPanel ??= GetComponentInChildren<CombatPanelView>(true);
        bossPanelView ??= GetComponentInChildren<BossPanelView>(true);

        if (bossPanelView == null)
        {
            var bossRoot = FindChildRecursive(transform, "Panel_boss");
            if (bossRoot != null)
                bossPanelView = bossRoot.GetComponent<BossPanelView>() ?? bossRoot.gameObject.AddComponent<BossPanelView>();
        }

        if (gridPanel == null)
        {
            var gridRoot = FindChildRecursive(transform, "Panel_Grid");
            if (gridRoot != null)
                gridPanel = gridRoot.gameObject;
        }
    }

    public void SetSections(HUDIds.Section sections)
    {
        bool showCombat = (sections & HUDIds.Section.CombatPanel) != 0;

        SetActiveSafe(topBarRoot, (sections & HUDIds.Section.TopBar) != 0);
        SetActiveSafe(combatPanel, showCombat);
        bool showGrid = (sections & HUDIds.Section.GridPanel) != 0;
        if (showGrid && gridPanel == null)
            Debug.LogWarning("[HudView] gridPanel is null! Panel_Grid를 찾을 수 없음");
        SetActiveSafe(gridPanel, showGrid);
        SetActiveSafe(bossPanelView, (sections & HUDIds.Section.BossPanel) != 0);
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
