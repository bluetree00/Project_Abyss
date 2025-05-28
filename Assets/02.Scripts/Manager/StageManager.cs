using System.Collections.Generic;
using UnityEngine;
using MapGeneratorManager;
using System.Linq;
using System;
using System.Threading.Tasks; // MapGeneratorManager에서 정의된 Graph, Node, Edge 클래스 사용


public class StageManager
{
    public Graph stageGraph; // MapGeneratorManager에서 생성된 그래프
    private Node? currentNode; // 현재 활성화된 노드
    private Dictionary<int, GameObject> nodeToStageMap; // 노드 ID와 스테이지 오브젝트 매핑
    public event Action<Graph> OnGraphGenerated; // 그래프 생성 완료 이벤트

    //TODO: Json으로 그래프 정보와 스테이지 정보를 저장하고 불러오는 기능 추가

    // public StageManager(StageData stageData)
    // {
    //     if (stageData == null)
    //     {
    //         Debug.LogError("StageData is null!");
    //         return;
    //     }
    //     stageGraph = MapGeneratorManager.MapGeneratorManager.Generate(stageData.chapters[0]);
    //     nodeToStageMap = new Dictionary<int, GameObject>();
    //     InitializeStages();
    //     SetInitialStage();
    //     if (stageGraph != null)
    //     {
    //         Debug.Log("Stage graph generated successfully.");
    //         OnGraphGenerated?.Invoke(stageGraph); // 그래프 생성 완료 이벤트 호출
    //     }
    //     else
    //     {
    //         Debug.LogError("Failed to generate stage graph.");
    //     }
    // }

    public void Initialize()
    {
        // GraphData는 싱글톤 또는 Managers에서 참조
        GraphData graphData = Managers._graphData;
        if (graphData == null || graphData.graph == null)
        {
            Debug.LogError("GraphData or graph is null!");
            return;
        }
        stageGraph = graphData.graph;
        nodeToStageMap = new Dictionary<int, GameObject>();
        InitializeStages();
        OnGraphGenerated?.Invoke(stageGraph);
    }

    private async void InitializeStages()
    {
        //TODO: 스테이지 오브젝트를 어드레서블에서 로드해서 생성해야함.
        // foreach (var node in stageGraph.Nodes)
        // {
        //     // 각 노드에 해당하는 스테이지 오브젝트 생성
        //     GameObject stageObject = new GameObject($"Stage_{node.Id}");
        //     stageObject.SetActive(false); // 초기에는 비활성화
        //     nodeToStageMap[node.Id] = stageObject;
        // }

        // 1. StageData에서 현재 챕터와 스테이지 설정 리스트 가져오기
        StageData stageData = Managers.Instance.GetStageData();
        var chapter = stageData.chapters[0]; // 예시: 첫 번째 챕터 사용
        var stageSettingsList = chapter.stages;

        if (stageSettingsList == null || stageSettingsList.Count == 0)
        {
            Debug.LogError("stageSettingsList가 null이거나 비어 있습니다!");
            return;
        }

        // 2. 노드 수와 스테이지 설정 수 비교
        int nodeCount = stageGraph.Nodes.Count;
        int settingsCount = stageSettingsList.Count;

        nodeToStageMap = new Dictionary<int, GameObject>();

        for (int i = 0; i < nodeCount; i++)
        {
            // 스테이지 설정이 부족하면 순환해서 사용
            var stageSettings = stageSettingsList[i % settingsCount];
            string address = stageSettings.stageName.ToString();

            // Addressables에서 오브젝트 비동기 로드
            GameObject stageObject = await AddressablesManager.Instance.InstantiateAsyncTask(address);
            if (stageObject == null)
            {
                Debug.LogError($"해당 주소를 가진 오브젝트 로드 실패 : {address}");
                continue;
            }
            stageObject.SetActive(false);
            var node = stageGraph.Nodes[i];
            nodeToStageMap[node.Id] = stageObject;
        }

        SetInitialStage();

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

        // 현재 노드 강조 //
        if (nodeToStageMap.ContainsKey(node.Id))
        {
            nodeToStageMap[node.Id].SetActive(true);
            currentNode = node;
            Debug.Log($"Activated stage: {node.Id} ({node.GetLabel()})");

            // 현재 노드 강조
            var visualizer = GameObject.FindObjectOfType<UIGraphVisualizer>();
            if (visualizer != null)
                visualizer.HighlightCurrentNode(node.Id);
        }
        else
        {
            Debug.LogError($"Stage for node {node.Id} not found.");
        }
    }

    public void MoveToNextStage(int direction)
    {
        Debug.Log($"Moving to next stage in direction: {direction}");
        {
            var connectedNodes = GetConnectedNodes();
            if (connectedNodes.Count == 0)
            {
                Debug.LogError("No connected nodes to move to.");
                return;
            }

            // 방향에 따라 노드 이동
            if (direction == 1) // 오른쪽
            {
                ActivateStage(connectedNodes[1]);
            }
            else if (direction == -1) // 왼쪽
            {
                ActivateStage(connectedNodes[0]);
            }
            else
            {
                Debug.LogWarning("Invalid direction or no node available in that direction.");
            }
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
