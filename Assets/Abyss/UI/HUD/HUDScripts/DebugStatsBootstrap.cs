using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 게임 시작 시 디버그 스탯 UI를 Canvas_Overlay에 자동 생성.
/// 빌드에서는 비활성화 가능 (Inspector에서 enabled = false).
/// </summary>
public class DebugStatsBootstrap : MonoBehaviour
{
    private void Start()
    {
        CreateDebugStatsUI();
    }

    private void CreateDebugStatsUI()
    {
        // Canvas_Overlay 찾기
        var overlayCanvas = FindOverlayCanvas();
        if (overlayCanvas == null)
        {
            Debug.LogWarning("[DebugStatsBootstrap] Canvas_Overlay not found");
            return;
        }

        // 패널 생성
        var panel = new GameObject("DebugStatsPanel");
        panel.transform.SetParent(overlayCanvas.transform, false);

        var rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(10, -10);
        rect.sizeDelta = new Vector2(350, 320);

        // 배경
        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.6f);
        bg.raycastTarget = false;

        // 텍스트
        var textGO = new GameObject("StatsText");
        textGO.transform.SetParent(panel.transform, false);

        var textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8, 8);
        textRect.offsetMax = new Vector2(-8, -8);

        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 14;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.richText = true;
        tmp.text = "Loading stats...";

        // DebugStatsView 컴포넌트
        var view = panel.AddComponent<DebugStatsView>();

        // statsText 필드 연결 (리플렉션 없이 SerializeField이라 직접 못 넣음 — public 접근자 추가)
        view.SetStatsText(tmp);

        Debug.Log("[DebugStatsBootstrap] 디버그 스탯 UI 생성 완료");
    }

    private Canvas FindOverlayCanvas()
    {
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay
                || canvas.gameObject.name.Contains("Overlay"))
                return canvas;
        }
        return null;
    }
}
