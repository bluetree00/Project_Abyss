using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 편집 뷰 — 특정 보드를 확대해 Shape를 드래그 배치한다.
///
/// ■ 상단: [◀갤러리] + 보드 탭 스트립 [B1][B2]...
/// ■ 중앙: BoardManager가 해당 그리드 세션을 활성화.
/// ■ 하단(그리드 아래): 시너지 설명 텍스트.
/// </summary>
public sealed class GridEditView : MonoBehaviour
{
    // ── Constants ──
    private const float TAB_WIDTH   = 60f;
    private const float TAB_HEIGHT  = 36f;
    private const float TAB_SPACING = 6f;

    private static readonly Color COLOR_TAB_ACTIVE   = new(0.3f, 0.6f, 1f,  1f);
    private static readonly Color COLOR_TAB_INACTIVE = new(0.2f, 0.2f, 0.28f, 0.9f);
    private static readonly Color COLOR_TAB_TXT      = new(0.9f, 0.92f, 1f, 1f);
    private static readonly Color COLOR_SYNERGY_TXT  = new(0.85f, 1f, 0.5f, 1f);

    // ── SerializeField ──
    [Header("버튼")]
    [SerializeField] private Button backButton;

    [Header("탭 스트립")]
    [SerializeField] private RectTransform tabStrip;
    [SerializeField] private TMP_FontAsset tabFont;

    [Header("시너지 텍스트")]
    [SerializeField] private TMP_Text synergyText;

    // ── Private ──
    private string _activeGridId;
    private readonly List<TabEntry> _tabs = new();

    private sealed class TabEntry
    {
        public string    gridId;
        public Button    button;
        public Image     bg;
        public TMP_Text  label;
    }

    // ── Lifecycle ──
    private void Awake()
    {
        if (backButton != null)
            backButton.onClick.AddListener(OnBackClicked);
    }

    private void OnDestroy()
    {
        if (backButton != null)
            backButton.onClick.RemoveListener(OnBackClicked);
    }

    // ── Public API ──

    /// <summary>편집 뷰 활성화 및 해당 보드 진입.</summary>
    public void Activate(string gridId)
    {
        _activeGridId = gridId;
        RebuildTabs();
        EnterGrid(gridId);
        RefreshSynergyText(gridId);
    }

    /// <summary>편집 뷰 비활성화 (그리드 세션은 BoardManager가 유지).</summary>
    public void Deactivate()
    {
        _activeGridId = null;
    }

    // ── Tab Management ──

    private void RebuildTabs()
    {
        ClearTabs();

        var bridge = BlockSynergyBridge.Instance;
        if (bridge == null || tabStrip == null) return;

        var grids = bridge.GetRegisteredGrids();
        if (grids == null) return;

        int index = 0;
        foreach (var pair in grids)
        {
            CreateTab(pair.Key, $"B{index + 1}", index);
            index++;
        }

        RefreshTabHighlight();
    }

    private void CreateTab(string gridId, string label, int index)
    {
        if (tabStrip == null) return;

        var tabGO = new GameObject($"Tab_{gridId}", typeof(RectTransform));
        tabGO.transform.SetParent(tabStrip, false);

        var rt = tabGO.GetComponent<RectTransform>();
        rt.sizeDelta        = new Vector2(TAB_WIDTH, TAB_HEIGHT);
        rt.anchorMin        = new Vector2(0f, 0.5f);
        rt.anchorMax        = new Vector2(0f, 0.5f);
        rt.pivot            = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(index * (TAB_WIDTH + TAB_SPACING), 0f);

        var bg  = tabGO.AddComponent<Image>();
        bg.color = COLOR_TAB_INACTIVE;

        var btn = tabGO.AddComponent<Button>();
        btn.targetGraphic = bg;

        var txtGO = new GameObject("Label", typeof(RectTransform));
        txtGO.transform.SetParent(tabGO.transform, false);
        var txtRT = txtGO.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
        txtRT.sizeDelta = Vector2.zero;
        var txt = txtGO.AddComponent<TextMeshProUGUI>();
        if (tabFont != null) txt.font = tabFont;
        txt.text      = label;
        txt.fontSize  = 13f;
        txt.color     = COLOR_TAB_TXT;
        txt.alignment = TextAlignmentOptions.Center;
        txt.raycastTarget = false;

        var entry     = new TabEntry { gridId = gridId, button = btn, bg = bg, label = txt };
        var capturedId = gridId;
        btn.onClick.AddListener(() => OnTabClicked(capturedId));

        _tabs.Add(entry);
    }

    private void RefreshTabHighlight()
    {
        foreach (var tab in _tabs)
        {
            bool active  = tab.gridId == _activeGridId;
            tab.bg.color = active ? COLOR_TAB_ACTIVE : COLOR_TAB_INACTIVE;
        }
    }

    private void ClearTabs()
    {
        foreach (var tab in _tabs)
            if (tab.button != null) Destroy(tab.button.gameObject);
        _tabs.Clear();
    }

    // ── Grid Entry ──

    private void EnterGrid(string gridId)
    {
        if (string.IsNullOrEmpty(gridId)) return;

        var bridge = BlockSynergyBridge.Instance;
        if (bridge == null) return;

        var grids = bridge.GetRegisteredGrids();
        if (grids == null || !grids.TryGetValue(gridId, out var data)) return;

        BoardManager.Instance?.EnterGrid(data);
    }

    // ── Synergy Text ──

    /// <summary>현재 활성 그리드의 시너지 텍스트를 갱신한다. Shape 배치 후 UI_GridPanel에서 호출.</summary>
    public void RefreshSynergyText()
    {
        if (_activeGridId != null)
            RefreshSynergyText(_activeGridId);
    }

    private void RefreshSynergyText(string gridId)
    {
        if (synergyText == null) return;

        var run = GameRunBootstrapper.Instance?.Run;
        if (run == null) { synergyText.text = string.Empty; return; }

        var sb = new System.Text.StringBuilder();
        foreach (var synergy in run.AppliedSynergies)
        {
            if (synergy.gridId != gridId) continue;
            if (!string.IsNullOrEmpty(synergy.effectType))
                sb.AppendLine($"{synergy.effectType}: {(synergy.value >= 0 ? "+" : "")}{synergy.value * 100f:F0}% [{synergy.trigger}]");
        }

        synergyText.text  = sb.Length > 0 ? $"시너지: {sb}" : string.Empty;
        synergyText.color = COLOR_SYNERGY_TXT;
    }

    // ── Event Handlers ──

    private void OnBackClicked()
    {
        UI_GridPanel.Instance?.EnterGalleryMode();
    }

    private void OnTabClicked(string gridId)
    {
        _activeGridId = gridId;
        RefreshTabHighlight();
        EnterGrid(gridId);
        RefreshSynergyText(gridId);
    }
}
