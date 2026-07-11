//============================================================
// HudView.cs
// - Section 플래그에 따라 루트 표시 스왑
// - 각 패널은 Presenter가 패널별 View로 직접 데이터 전달
//============================================================
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class HudView : MonoBehaviour
{
    // 런 재화(강화재료·원석) 임시 아이콘 색 — 전용 아이콘 에셋 나오면 교체.
    private static readonly Color EnhanceMatIcon = new(0.90f, 0.55f, 0.20f); // 강화재료(주황)
    private static readonly Color RuneOreIcon    = new(0.35f, 0.70f, 0.95f); // 원석(청록)

    [Header("Sections")]
    [SerializeField] private GameObject topBarRoot;
    [SerializeField] private CombatPanelView combatPanel;
    [SerializeField] private GameObject gridPanel;
    [SerializeField] private BossPanelView bossPanelView;
    [SerializeField] private GameObject systemNoticesRoot;
    [SerializeField] private GameObject minimapPanel;
    [SerializeField] private CovenantPanelView covenantPanel;

    [Header("TopBar Info")]
    [SerializeField] private TMP_Text nicknameText;
    [SerializeField] private TMP_Text goldText;

    // 런 재화 표시(강화재료·원석) — goldText 옆에 절차 생성(임시). 0이면 슬롯 숨김.
    private RectTransform _fuelRow;
    private GameObject    _enhanceMatSlot;
    private GameObject    _runeOreSlot;
    private TMP_Text      _enhanceMatText;
    private TMP_Text      _runeOreText;

    public CombatPanelView   CombatPanel   => combatPanel;
    public BossPanelView     BossPanel     => bossPanelView;
    public CovenantPanelView CovenantPanel => covenantPanel;
    public MinimapView MinimapView { get; private set; }

    private void Awake()
    {
        combatPanel    ??= GetComponentInChildren<CombatPanelView>(true);
        bossPanelView  ??= GetComponentInChildren<BossPanelView>(true);
        covenantPanel  ??= GetComponentInChildren<CovenantPanelView>(true);

        if (bossPanelView == null)
        {
            var bossRoot = FindChildRecursive(transform, "Panel_boss");
            if (bossRoot != null)
                bossPanelView = bossRoot.GetComponent<BossPanelView>() ?? bossRoot.gameObject.AddComponent<BossPanelView>();
        }

        if (minimapPanel == null)
        {
            var mapRoot = FindChildRecursive(transform, "Panel_Minimap");
            if (mapRoot != null)
                minimapPanel = mapRoot.gameObject;
        }

        if (minimapPanel != null)
            MinimapView = minimapPanel.GetComponentInChildren<MinimapView>(true);
    }

    public void SetSections(HUDIds.Section sections)
    {
        bool showCombat = (sections & HUDIds.Section.CombatPanel) != 0;

        SetActiveSafe(topBarRoot,       (sections & HUDIds.Section.TopBar)        != 0);
        SetActiveSafe(combatPanel,      showCombat);
        SetActiveSafe(gridPanel,        (sections & HUDIds.Section.GridPanel)      != 0);
        SetActiveSafe(bossPanelView,    (sections & HUDIds.Section.BossPanel)      != 0);
        SetActiveSafe(systemNoticesRoot,(sections & HUDIds.Section.SystemNotices)  != 0);
        SetActiveSafe(minimapPanel,     (sections & HUDIds.Section.Minimap)        != 0);
        SetActiveSafe(covenantPanel,    (sections & HUDIds.Section.CovenantPanel)  != 0);

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

    public void SetEnhanceMaterial(int amount)
    {
        EnsureFuelSlots();
        if (_enhanceMatText != null) _enhanceMatText.text = amount.ToString();
        if (_enhanceMatSlot != null) _enhanceMatSlot.SetActive(amount > 0);
    }

    public void SetRuneOre(int amount)
    {
        EnsureFuelSlots();
        if (_runeOreText != null) _runeOreText.text = amount.ToString();
        if (_runeOreSlot != null) _runeOreSlot.SetActive(amount > 0);
    }

    // ── 런 재화 슬롯 절차 생성 (goldText 기준 바로 아래 행) ──
    private void EnsureFuelSlots()
    {
        if (_fuelRow != null || goldText == null || goldText.transform.parent == null) return;

        var rowGo = new GameObject("CurrencyRow_Fuel", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        _fuelRow = (RectTransform)rowGo.transform;
        _fuelRow.SetParent(goldText.transform.parent, false);

        var g = goldText.rectTransform;
        _fuelRow.anchorMin = g.anchorMin;
        _fuelRow.anchorMax = g.anchorMax;
        _fuelRow.pivot     = g.pivot;
        _fuelRow.anchoredPosition = g.anchoredPosition + new Vector2(0f, -32f); // 골드 바로 아래
        _fuelRow.sizeDelta = new Vector2(240f, 26f);

        var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 16f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;  hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        _enhanceMatSlot = MakeFuelSlot(_fuelRow, EnhanceMatIcon, out _enhanceMatText);
        _runeOreSlot    = MakeFuelSlot(_fuelRow, RuneOreIcon,    out _runeOreText);
        _enhanceMatSlot.SetActive(false);
        _runeOreSlot.SetActive(false);
    }

    private GameObject MakeFuelSlot(Transform parent, Color iconColor, out TMP_Text valueText)
    {
        var slot = new GameObject("FuelSlot", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        slot.transform.SetParent(parent, false);
        var shlg = slot.GetComponent<HorizontalLayoutGroup>();
        shlg.spacing = 5f;
        shlg.childAlignment = TextAnchor.MiddleLeft;
        shlg.childControlWidth = true;  shlg.childControlHeight = true;
        shlg.childForceExpandWidth = false; shlg.childForceExpandHeight = false;

        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(slot.transform, false);
        iconGo.GetComponent<Image>().color = iconColor;
        var ile = iconGo.AddComponent<LayoutElement>();
        ile.preferredWidth = 18f; ile.preferredHeight = 18f;

        var txtGo = new GameObject("Value", typeof(RectTransform));
        txtGo.transform.SetParent(slot.transform, false);
        valueText = txtGo.AddComponent<TextMeshProUGUI>();
        if (goldText != null) { valueText.font = goldText.font; valueText.fontSize = goldText.fontSize; }
        valueText.color = new Color(0.93f, 0.91f, 0.85f, 1f);
        valueText.alignment = TextAlignmentOptions.MidlineLeft;
        valueText.text = "0";
        return slot;
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
