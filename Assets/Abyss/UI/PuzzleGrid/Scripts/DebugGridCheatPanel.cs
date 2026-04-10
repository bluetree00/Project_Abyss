#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 디버그용 그리드 시너지 치트 패널.
/// 각 그리드별 "완성" 버튼 → 시너지 즉시 발동.
/// 화면 우하단에 토글 버튼으로 열기/닫기.
/// </summary>
public class DebugGridCheatPanel : MonoBehaviour
{
    // ── Constants ──
    private const float BTN_WIDTH = 140f;
    private const float BTN_HEIGHT = 28f;
    private const float SPACING = 4f;
    private const float PADDING = 8f;

    // ── Private ──
    private GameObject _panel;
    private readonly List<Button> _buttons = new();
    private bool _isOpen;

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

        // 토글 버튼 (우하단)
        var toggleGO = new GameObject("GridCheatToggle", typeof(RectTransform), typeof(Image), typeof(Button));
        toggleGO.transform.SetParent(canvas.transform, false);

        var toggleRT = toggleGO.GetComponent<RectTransform>();
        toggleRT.anchorMin = new Vector2(1, 0);
        toggleRT.anchorMax = new Vector2(1, 0);
        toggleRT.pivot = new Vector2(1, 0);
        toggleRT.anchoredPosition = new Vector2(-10, 10);
        toggleRT.sizeDelta = new Vector2(100, 30);

        var toggleBg = toggleGO.GetComponent<Image>();
        toggleBg.color = new Color(0.2f, 0.2f, 0.6f, 0.8f);

        var toggleTextGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        toggleTextGO.transform.SetParent(toggleGO.transform, false);
        var toggleTextRT = toggleTextGO.GetComponent<RectTransform>();
        toggleTextRT.anchorMin = Vector2.zero;
        toggleTextRT.anchorMax = Vector2.one;
        toggleTextRT.offsetMin = Vector2.zero;
        toggleTextRT.offsetMax = Vector2.zero;

        var toggleText = toggleTextGO.GetComponent<TextMeshProUGUI>();
        toggleText.text = "Grid Cheat";
        toggleText.fontSize = 12;
        toggleText.alignment = TextAlignmentOptions.Center;
        toggleText.color = Color.white;

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

        var blockData = Managers.BlockData;
        if (blockData == null || !blockData.IsInitialized) return;

        var sortedIds = blockData.GetGridIdsSortedByOrder();

        // 패널 배경
        float panelH = PADDING * 2 + sortedIds.Count * (BTN_HEIGHT + SPACING);
        _panel = new GameObject("GridCheatPanel", typeof(RectTransform), typeof(Image));
        _panel.transform.SetParent(canvas.transform, false);

        var panelRT = _panel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(1, 0);
        panelRT.anchorMax = new Vector2(1, 0);
        panelRT.pivot = new Vector2(1, 0);
        panelRT.anchoredPosition = new Vector2(-10, 45);
        panelRT.sizeDelta = new Vector2(BTN_WIDTH + PADDING * 2, panelH);

        var panelBg = _panel.GetComponent<Image>();
        panelBg.color = new Color(0, 0, 0, 0.75f);

        // 그리드별 버튼 생성
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
        btnBg.color = new Color(0.3f, 0.5f, 0.3f, 0.9f);

        var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(btnGO.transform, false);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        var text = textGO.GetComponent<TextMeshProUGUI>();
        text.text = $"✓ {label}";
        text.fontSize = 11;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;

        string capturedId = gridId;
        btnGO.GetComponent<Button>().onClick.AddListener(() => CheatCompleteGrid(capturedId, btnBg));
        _buttons.Add(btnGO.GetComponent<Button>());
    }

    private void CheatCompleteGrid(string gridId, Image btnBg)
    {
        var bridge = BlockSynergyBridge.Instance;
        if (bridge == null)
        {
            Debug.LogWarning("[GridCheat] BlockSynergyBridge not found");
            return;
        }

        // BoardManager에서 해당 그리드의 SO를 찾아 NotifyGridFilled 호출
        var boardManager = Object.FindFirstObjectByType<BoardManager>(FindObjectsInactive.Include);
        if (boardManager == null) return;

        var squares = boardManager.GetGridSquares(gridId);
        if (squares != null)
        {
            // 실제 세션이 있으면 모든 칸 점유 처리
            foreach (var sq in squares)
                if (sq != null && sq.isPlaceable)
                    sq.SetOccupied(true);
        }

        // 시너지 효과 직접 발동
        bridge.CheatTriggerSynergy(gridId);

        // 버튼 색상 변경 (완료 표시)
        btnBg.color = new Color(0.2f, 0.7f, 0.2f, 0.9f);

        Debug.Log($"[GridCheat] 그리드 완성 치트: {gridId}");
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
