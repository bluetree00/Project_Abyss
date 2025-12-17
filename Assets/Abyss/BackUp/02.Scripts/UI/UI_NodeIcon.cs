using UnityEngine;
using TMPro;
using MapGeneratorManager;

public class NodeIcon : MonoBehaviour
{
    public int nodeId; // 인스펙터에서 입력 가능한 노드 ID
    public TextMeshProUGUI labelText; // 노드 라벨 표시 (예: L0-0)
    private Node nodeData; // 바인딩된 노드 데이터

    public void SetNodeData(Node node)
    {
        nodeId = node.Id;
        nodeData = node;
        if (labelText != null)
        {
            labelText.text = $"{node.GetLabel()} (ID: {node.Id})"; // 예: L0-0 (ID: 0)
        }
        // 노드 타입에 따라 시각적 스타일 변경 (선택적)
        UpdateVisuals(node.Type);
    }

    private void UpdateVisuals(NodeType type)
    {
        // 예: 노드 타입에 따라 색상 변경
        var image = GetComponent<UnityEngine.UI.Image>();
        if (image != null)
        {
            switch (type)
            {
                case NodeType.Start:
                    image.color = Color.green;
                    break;
                case NodeType.End:
                    image.color = Color.red;
                    break;
                default:
                    image.color = Color.white;
                    break;
            }
        }
    }

    // 클릭 이벤트 바인딩 (선택적)
    public void BindClickEvent(System.Action<Node> onClick)
    {
        UI_Base.BindEvent(gameObject, (evt) => onClick(nodeData), Define.UIEvent.Click);
    }
}