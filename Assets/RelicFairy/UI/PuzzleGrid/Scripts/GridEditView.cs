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

    private static readonly Color COLOR_TAB_ACTIVE    = new(0.3f, 0.6f, 1f,  1f);
    private static readonly Color COLOR_TAB_INACTIVE  = new(0.2f, 0.2f, 0.28f, 0.9f);
    private static readonly Color COLOR_TAB_TXT       = new(0.9f, 0.92f, 1f, 1f);
    private static readonly Color COLOR_SYNERGY_TXT   = new(0.85f, 1f, 0.5f, 1f);
    private static readonly Color COLOR_BACK_BTN      = new(0.14f, 0.32f, 0.58f, 0.92f);
    private static readonly Color COLOR_SYNERGY_PANEL = new(0.04f, 0.1f, 0.06f, 0.88f);

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
    private GameObject _synergyPanel;

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

        RepositionBackButton();
        EnsureSynergyPanel();
    }

    private void OnEnable()
    {
        if (BlockSynergyBridge.Instance != null)
            BlockSynergyBridge.Instance.OnSynergyActivated += OnSynergyActivatedExternal;
    }

    private void OnDisable()
    {
        if (BlockSynergyBridge.Instance != null)
            BlockSynergyBridge.Instance.OnSynergyActivated -= OnSynergyActivatedExternal;
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
            if (string.IsNullOrEmpty(synergy.effectType)) continue;

            string triggerLabel = synergy.trigger switch
            {
                "Always"  => "항상",
                "OnHit"   => "피격 시",
                "OnKill"  => "처치 시",
                "OnLowHp" => "체력 낮을 때",
                "OnUse"   => "사용 시",
                _         => synergy.trigger,
            };
            float pct = synergy.value * 100f;
            sb.AppendLine($"  • {synergy.effectType}  {(pct >= 0 ? "+" : "")}{pct:F0}%  [{triggerLabel}]");
        }

        if (sb.Length > 0)
        {
            synergyText.text  = $"✦ 시너지 효과 활성화\n{sb}";
            synergyText.color = COLOR_SYNERGY_TXT;
            if (_synergyPanel != null) _synergyPanel.SetActive(true);
        }
        else
        {
            synergyText.text = string.Empty;
            if (_synergyPanel != null) _synergyPanel.SetActive(false);
        }
    }

    // ── Setup Helpers ──

    private void RepositionBackButton()
    {
        if (backButton == null) return;

        // 탭 스트립 레이아웃 그룹에서 분리
        var le = backButton.GetComponent<LayoutElement>() ?? backButton.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;

        // GridEditView 직속 자식으로 재부모 (앵커 기준 명확화)
        backButton.transform.SetParent(transform, false);

        var rt = backButton.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin        = new Vector2(0f, 1f);
            rt.anchorMax        = new Vector2(0f, 1f);
            rt.pivot            = new Vector2(0f, 1f);
            rt.sizeDelta        = new Vector2(110f, 36f);
            // 탭 스트립(TAB_HEIGHT) 아래 12px 여백
            rt.anchoredPosition = new Vector2(8f, -(TAB_HEIGHT + 12f));
        }

        // 배경 + 텍스트 스타일
        var img = backButton.GetComponent<Image>() ?? backButton.gameObject.AddComponent<Image>();
        img.color = COLOR_BACK_BTN;
        backButton.targetGraphic = img;

        var txt = backButton.GetComponentInChildren<TMP_Text>();
        if (txt != null)
        {
            txt.text      = "◀ 갤러리";
            txt.fontSize  = 13f;
            txt.color     = new Color(0.9f, 0.95f, 1f, 1f);
            txt.alignment = TextAlignmentOptions.Center;
        }
    }

    private void EnsureSynergyPanel()
    {
        // 시너지 텍스트가 Prefab에서 할당되지 않은 경우 코드로 생성
        if (synergyText != null) return;

        _synergyPanel = new GameObject("SynergyPanel", typeof(RectTransform));
        _synergyPanel.transform.SetParent(transform, false);

        var panelRT = _synergyPanel.GetComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(0f, 0.16f);
        panelRT.anchorMax        = new Vector2(0.62f, 0.32f);
        panelRT.offsetMin        = new Vector2(8f, 4f);
        panelRT.offsetMax        = new Vector2(-8f, -4f);

        var panelBG = _synergyPanel.AddComponent<Image>();
        panelBG.color = COLOR_SYNERGY_PANEL;

        var textGO = new GameObject("SynergyText", typeof(RectTransform));
        textGO.transform.SetParent(_synergyPanel.transform, false);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(6f, 4f);
        textRT.offsetMax = new Vector2(-6f, -4f);

        synergyText = textGO.AddComponent<TextMeshProUGUI>();
        if (tabFont != null) synergyText.font = tabFont;
        synergyText.fontSize          = 12f;
        synergyText.color             = COLOR_SYNERGY_TXT;
        synergyText.alignment         = TextAlignmentOptions.TopLeft;
        synergyText.enableWordWrapping = true;
        synergyText.raycastTarget     = false;

        _synergyPanel.SetActive(false);
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

    // 시너지 적용 완료 후 호출 — 텍스트를 올바른 타이밍에 갱신
    private void OnSynergyActivatedExternal(string _)
    {
        RefreshSynergyText();
    }
}
