#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 디버그용 그리드 시너지 치트 패널.
/// 각 그리드별 토글 버튼 → ON: 시너지 활성, OFF: 비활성.
/// 토글 시 전체 시너지 Clear → 활성화된 그리드만 재적용.
/// </summary>
public class DebugGridCheatPanel : MonoBehaviour
{
    // ── Constants ──
    private const float BTN_WIDTH = 140f;
    private const float BTN_HEIGHT = 28f;
    private const float SPACING = 4f;
    private const float PADDING = 8f;

    private static readonly Color COLOR_OFF = new(0.4f, 0.4f, 0.4f, 0.9f);
    private static readonly Color COLOR_ON  = new(0.2f, 0.7f, 0.2f, 0.9f);

    // ── Private ──
    private GameObject _panel;
    private bool _isOpen;
    private readonly HashSet<string> _activeGrids = new();
    private readonly Dictionary<string, Image> _btnImages = new();
    private readonly Dictionary<string, TMP_Text> _btnTexts = new();

    // ── Lifecycle ──

    private void Start()
    {
        CreateToggleButton();
    }

    // ── Private Methods ──

    private void CreateToggleButton()
    {
        var canvas = FindOverlayCanvas();
        if (canvas == null) return;

        var toggleGO = new GameObject("GridCheatToggle", typeof(RectTransform), typeof(Image), typeof(Button));
        toggleGO.transform.SetParent(canvas.transform, false);

        var toggleRT = toggleGO.GetComponent<RectTransform>();
        toggleRT.anchorMin = new Vector2(1, 0);
        toggleRT.anchorMax = new Vector2(1, 0);
        toggleRT.pivot = new Vector2(1, 0);
        toggleRT.anchoredPosition = new Vector2(-10, 10);
        toggleRT.sizeDelta = new Vector2(100, 30);

        toggleGO.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.6f, 0.8f);

        var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(toggleGO.transform, false);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        var text = textGO.GetComponent<TextMeshProUGUI>();
        text.text = "Grid Cheat";
        text.fontSize = 12;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;

        toggleGO.GetComponent<Button>().onClick.AddListener(TogglePanel);
    }

    private void TogglePanel()
    {
        if (_panel == null)
            BuildPanel();

        _isOpen = !_isOpen;
        _panel.SetActive(_isOpen);
    }

    private void BuildPanel()
    {
        var canvas = FindOverlayCanvas();
        if (canvas == null) return;

        var blockData = Managers.RuneData;
        if (blockData == null || !blockData.IsInitialized) return;

        var sortedIds = blockData.GetGridIdsSortedByOrder();

        float panelH = PADDING * 2 + sortedIds.Count * (BTN_HEIGHT + SPACING);
        _panel = new GameObject("GridCheatPanel", typeof(RectTransform), typeof(Image));
        _panel.transform.SetParent(canvas.transform, false);

        var panelRT = _panel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(1, 0);
        panelRT.anchorMax = new Vector2(1, 0);
        panelRT.pivot = new Vector2(1, 0);
        panelRT.anchoredPosition = new Vector2(-10, 45);
        panelRT.sizeDelta = new Vector2(BTN_WIDTH + PADDING * 2, panelH);

        _panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.75f);

        for (int i = 0; i < sortedIds.Count; i++)
        {
            var gridId = sortedIds[i];
            var meta = blockData.GetGridMeta(gridId);
            string label = meta != null ? meta.grid_name : gridId;
            CreateGridButton(_panel.transform, gridId, label, i);
        }

        _panel.SetActive(false);
    }

    private void CreateGridButton(Transform parent, string gridId, string label, int index)
    {
        var btnGO = new GameObject($"Btn_{gridId}", typeof(RectTransform), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(parent, false);

        var btnRT = btnGO.GetComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0.5f, 1);
        btnRT.anchorMax = new Vector2(0.5f, 1);
        btnRT.pivot = new Vector2(0.5f, 1);
        btnRT.sizeDelta = new Vector2(BTN_WIDTH, BTN_HEIGHT);
        btnRT.anchoredPosition = new Vector2(0, -(PADDING + index * (BTN_HEIGHT + SPACING)));

        var btnBg = btnGO.GetComponent<Image>();
        btnBg.color = COLOR_OFF;

        var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(btnGO.transform, false);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        var btnText = textGO.GetComponent<TextMeshProUGUI>();
        btnText.text = $"[ ] {label}";
        btnText.fontSize = 11;
        btnText.alignment = TextAlignmentOptions.Center;
        btnText.color = Color.white;

        _btnImages[gridId] = btnBg;
        _btnTexts[gridId] = btnText;

        string capturedId = gridId;
        string capturedLabel = label;
        btnGO.GetComponent<Button>().onClick.AddListener(() => ToggleGrid(capturedId, capturedLabel));
    }

    private void ToggleGrid(string gridId, string label)
    {
        if (_activeGrids.Contains(gridId))
            _activeGrids.Remove(gridId);
        else
            _activeGrids.Add(gridId);

        // UI 갱신
        bool isOn = _activeGrids.Contains(gridId);
        if (_btnImages.TryGetValue(gridId, out var img))
            img.color = isOn ? COLOR_ON : COLOR_OFF;
        if (_btnTexts.TryGetValue(gridId, out var txt))
            txt.text = isOn ? $"[✓] {label}" : $"[ ] {label}";

        // 전체 시너지 Clear → 활성화된 것만 재적용
        ReapplyAllSynergies();
    }

    private void ReapplyAllSynergies()
    {
        var bridge = MerlinRuneBridge.Instance;
        if (bridge == null) return;

        var run = GameRunBootstrapper.Instance != null ? GameRunBootstrapper.Instance.Run : null;
        var player = run?.Player;
        if (player == null) return;

        // 전체 초기화
        player.RuntimeStats.ClearSynergyEffects();

        // 활성화된 그리드만 재적용
        foreach (var gridId in _activeGrids)
            bridge.CheatTriggerSynergy(gridId);

        Debug.Log($"[GridCheat] 시너지 재적용: {_activeGrids.Count}개 활성");
    }

    private static Canvas FindOverlayCanvas()
    {
        foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay
                || canvas.gameObject.name.Contains("Overlay"))
                return canvas;
        }
        return null;
    }
}
#endif
