using System.Collections.Generic;
using UnityEngine;
using MapGeneratorManager;
using System.Linq; // MapGeneratorManager에서 정의된 Graph, Node, Edge 클래스 사용


public class StageManager
{
    public Graph stageGraph; // MapGeneratorManager에서 생성된 그래프
    private Node? currentNode; // 현재 활성화된 노드
    private Dictionary<int, GameObject> nodeToStageMap; // 노드 ID와 스테이지 오브젝트 매핑

    public StageManager(StageData stageData)
    {
        if (stageData == null)
        {
            Debug.LogError("StageData is null!");
            return;
        }
        stageGraph = MapGeneratorManager.MapGeneratorManager.Generate(stageData.chapters[0]);
        nodeToStageMap = new Dictionary<int, GameObject>();
        InitializeStages();
        SetInitialStage();
    }

    private void InitializeStages()
    {
        foreach (var node in stageGraph.Nodes)
        {
            // 각 노드에 해당하는 스테이지 오브젝트 생성
            GameObject stageObject = new GameObject($"Stage_{node.Id}");
            stageObject.SetActive(false); // 초기에는 비활성화
            nodeToStageMap[node.Id] = stageObject;
        }
    }

    private void SetInitialStage()
    {
        // 그래프의 첫 번째 노드를 활성화
        currentNode = stageGraph.Nodes.Find(node => node.Type == NodeType.Start);
        if (currentNode.HasValue)
        {
            ActivateStage(currentNode.Value);
        }
        else
        {
            Debug.LogError("No start node found in the graph.");
        }
    }

    private void ActivateStage(Node node)
    {
        // 현재 활성화된 스테이지 비활성화
        if (currentNode.HasValue && nodeToStageMap.ContainsKey(currentNode.Value.Id))
        {
            nodeToStageMap[currentNode.Value.Id].SetActive(false);
        }

        // 새로운 노드의 스테이지 활성화
        if (nodeToStageMap.ContainsKey(node.Id))
        {
            nodeToStageMap[node.Id].SetActive(true);
            currentNode = node;
            Debug.Log($"Activated stage: {node.Id} ({node.GetLabel()})");
        }
        else
        {
            Debug.LogError($"Stage for node {node.Id} not found.");
        }
    }

    public void MoveToNextStage()
    {
        var connectedNodes = GetConnectedNodes();
        if (connectedNodes.Count == 0)
        {
            Debug.LogError("No connected nodes to move to.");
            return;
        }

        // 방향에 따라 첫 번째 또는 두 번째 노드로 이동
        if (Input.GetKeyDown(KeyCode.LeftArrow)) // 왼쪽 화살표
        {
            ActivateStage(connectedNodes[0]);
        }
        else if (Input.GetKeyDown(KeyCode.RightArrow)) // 오른쪽 화살표
        {
            ActivateStage(connectedNodes[1]);
        }
        else
        {
            Debug.LogWarning("Invalid direction or no node available in that direction.");
        }
    }

    public List<Node> GetConnectedNodes()
    {
        if (!currentNode.HasValue)
        {
            Debug.LogError("Current node is not set.");
            return new List<Node>();
        }

        var connectedEdges = stageGraph.Edges.Where(edge => edge.FromId == currentNode.Value.Id);
        var connectedNodes = new List<Node>();
        foreach (var edge in connectedEdges)
        {
            Node? connectedNode = stageGraph.Nodes.Find(node => node.Id == edge.ToId);
            if (connectedNode.HasValue)
            {
                connectedNodes.Add(connectedNode.Value);
            }
        }
        return connectedNodes;
    }
}
