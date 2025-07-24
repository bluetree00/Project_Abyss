using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using MapGeneratorManager;

public class UI_GraphPopup : UI_Popup
{
    enum TMPTexts
    {
        NodeText, // 노드 버튼의 텍스트
    }

    enum Buttons
    {
        nodeButton, // 노드 버튼
    }

    [SerializeField] private RectTransform graphContainer;
    [SerializeField] private GameObject nodeButtonPrefab;

    private Dictionary<int, GameObject> nodeButtons = new Dictionary<int, GameObject>();

    public override void Init()
    {
        base.Init();
        Bind<Button>(typeof(Button));
        Bind<TextMeshProUGUI>(typeof(TMPTexts));
    }

    public void InitializeGraph(Graph graph, int currentNodeId)
    {
        // 기존 버튼 제거
        foreach (Transform child in graphContainer)
        {
            Destroy(child.gameObject);
        }
        nodeButtons.Clear();

        // 노드 버튼 생성 및 배치
        foreach (var node in graph.Nodes)
        {
            GameObject nodeButtonGO = Instantiate(nodeButtonPrefab, graphContainer);
            nodeButtons[node.Id] = nodeButtonGO;

            // 버튼 텍스트 설정 (GetTMPText로 바인딩)
            GetTMPText((int)TMPTexts.NodeText).text = node.GetLabel();

            // 버튼 클릭 이벤트 바인딩
            BindEvent(nodeButtonGO, (PointerEventData data) => OnNodeButtonClicked(node.Id));

            // 버튼 위치 설정 (노드 그래프의 위치에 따라 배치)
            RectTransform buttonRect = nodeButtonGO.GetComponent<RectTransform>();
            buttonRect.anchoredPosition = new Vector2(node.Position * 100, -node.Layer * 100);

            // 현재 노드 강조
            if (node.Id == currentNodeId)
            {
                Button button = nodeButtonGO.GetComponent<Button>();
                if (button != null)
                    button.interactable = false;

                Image buttonImage = nodeButtonGO.GetComponent<Image>();
                if (buttonImage != null)
                    buttonImage.color = Color.yellow;
            }
        }

        // 연결된 노드만 활성화
        HighlightConnectedNodes(currentNodeId);
    }

    private void HighlightConnectedNodes(int currentNodeId)
    {
        var connectedNodes = Managers.Stage.GetConnectedNodes();
        foreach (var kvp in nodeButtons)
        {
            Button button = kvp.Value.GetComponent<Button>();
            Image buttonImage = kvp.Value.GetComponent<Image>();

            if (connectedNodes.Exists(node => node.Id == kvp.Key))
            {
                if (button != null)
                    button.interactable = true;

                if (buttonImage != null)
                    buttonImage.color = Color.white; // 활성화된 노드 색상
            }
            else
            {
                if (button != null)
                    button.interactable = false;

                if (buttonImage != null)
                    buttonImage.color = Color.gray; // 비활성화된 노드 색상
            }
        }
    }

    private void OnNodeButtonClicked(int nodeId)
    {
        Managers.Stage.MoveToNextStage(nodeId);
        ClosePopupUI();
    }

    private void OnCloseButtonClicked(PointerEventData data)
    {
        ClosePopupUI();
    }
        
}
