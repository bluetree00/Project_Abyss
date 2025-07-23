using System.Collections.Generic;
using UnityEngine;
using MapGeneratorManager;
using System.Linq;
using System;
using System.Threading.Tasks;
using System.Collections;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations; // MapGeneratorManager에서 정의된 Graph, Node, Edge 클래스 사용


public class StageManager
{
    public Graph stageGraph; // MapGeneratorManager에서 생성된 그래프
    private Node? currentNode; // 현재 활성화된 노드
    private Dictionary<int, GameObject> nodeToStageMap; // 노드 ID와 스테이지 오브젝트 매핑
    public event Action<Graph> OnGraphGenerated; // 그래프 생성 완료 이벤트

    //TODO: Json으로 그래프 정보와 스테이지 정보를 저장하고 불러오는 기능 추가

    public async Task InitializeStageManager()
    {
        Debug.Log("StageManager InitializeAsync 시작");
        // GraphData는 싱글톤 또는 Managers에서 참조
        GraphData graphData = Managers._graphData;
        if (graphData == null || graphData.graph == null)
        {
            Debug.LogError("GraphData or graph is null!");
            return;
        }
        stageGraph = graphData.graph;
        nodeToStageMap = new Dictionary<int, GameObject>();


        // InitializeStages();
        // SetInitialStage();      
        await StageprepareAsync();  
    }
     
    public async Task StageprepareAsync()
    {
        Debug.Log("StageManager StageprepareAsync 시작");
        foreach (var node in stageGraph.Nodes)
        {
            if (!nodeToStageMap.ContainsKey(node.Id))
            {
                GameObject stageObject = await Managers.AddressableManager.InstantiateAsyncTask(GetStageAddress(node));
                if (stageObject != null)
                {
                    stageObject.SetActive(false);
                    nodeToStageMap[node.Id] = stageObject;
                    Debug.Log($"Preparing stage for node {node.Id} ({node.GetLabel()})");
                }
                else
                {
                    Debug.LogError($"Stage prefab not found for node {node.Id} ({node.GetLabel()})");
                }
            }
        }
        Node? startNode = stageGraph.Nodes.Find(node => node.Type == NodeType.Start);
        Debug.LogWarning(startNode.Value.ToString());
        Debug.LogWarning(GetStageAddress(startNode.Value));
        if (startNode.HasValue)
        {
            currentNode = startNode;
            ActivateStage(currentNode.Value);
        }
        else
        {
            Debug.LogError("No start node found in the graph.");
        }
        

    // if (startNode.HasValue)
        // {
        //     //어드레서블을 사용해서 오브젝트를 로드
        //     GameObject stageObject = await Managers.AddressableManager.InstantiateAsyncTask(GetStageAddress(startNode.Value));
        //     if (stageObject == null)
        //     {
        //         Debug.LogError($"Stage prefab not found for node {startNode.Value.Id} ({startNode.Value.GetLabel()})");
        //         return;
        //     }
        //     //stageObject = GameObject.Instantiate(stageObject);
        //     stageObject.SetActive(false);
        //     nodeToStageMap[startNode.Value.Id] = stageObject;
        //     Debug.Log($"Preparing stage for start node {startNode.Value.Id} ({startNode.Value.GetLabel()})");
        //     currentNode = startNode;
        //     await ActivateStage(currentNode.Value);
        // }
        // else
        // {
        //     Debug.LogError("No start node found in the graph.");
        // }

        OnGraphGenerated?.Invoke(stageGraph);
    }

    // Addressables 키 규칙에 맞게 주소 생성
    private string GetStageAddress(Node node)
    {
        string address = $"Stage_{node.Id + 1:D2}";
        Debug.Log($"Generated Addressables key: {address}");
        return address;
    }

        // // ActivateStage를 비동기로 변경
        // public async Task ActivateStageAsync(Node node)
        // {
        //     // 현재 활성화된 스테이지 비활성화
        //     if (currentNode.HasValue && nodeToStageMap.TryGetValue(currentNode.Value.Id, out var prevStage))
        //         prevStage.SetActive(false);

        //     // 해당 노드의 스테이지 오브젝트가 없으면 Addressables에서 생성
        //     if (!nodeToStageMap.TryGetValue(node.Id, out var stageObject) || stageObject == null)
        //     {
        //         stageObject = await Managers.AddressableManager.InstantiateAsyncTask(GetStageAddress(node));
        //         Debug.Log($"Loading stage prefab for node {node.Id} ({node.GetLabel()})");
        //         if (stageObject != null)
        //         {
        //             stageObject.SetActive(false);
        //             nodeToStageMap[node.Id] = stageObject;
        //         }
        //         else
        //         {
        //             Debug.LogError($"Stage prefab load failed for node {node.Id}");
        //             return;
        //         }
        //     }

        //     stageObject.SetActive(true);
        //     currentNode = node;
        //     Debug.Log($"Activated stage: {node.Id} ({node.GetLabel()})");

        //     // 노드 강조 등 추가 로직
        //     var visualizer = GameObject.FindObjectOfType<UIGraphVisualizer>();
        //     if (visualizer != null)
        //         visualizer.HighlightCurrentNode(node.Id);
        // }

    private void InitializeStages()
    {
        //TODO: 스테이지 오브젝트를 어드레서블에서 로드해서 생성해야함.
        foreach (var node in stageGraph.Nodes)
        {
            // 각 노드에 해당하는 스테이지 오브젝트 생성
            GameObject stageObject = new GameObject($"Stage_{node.Id}");
            stageObject.SetActive(false); // 초기에는 비활성화
            nodeToStageMap[node.Id] = stageObject;
            Debug.Log($"Initializing stage for node {node.Id} ({node.GetLabel()})");
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

        // 해당 노드의 스테이지 오브젝트가 없으면 Addressables에서 비동기로 생성
        // if (!nodeToStageMap.ContainsKey(node.Id) || nodeToStageMap[node.Id] == null)
        // {
        //     GameObject stageObject = await Managers.AddressableManager.InstantiateAsyncTask(GetStageAddress(node));
        //     if (stageObject != null)
        //     {
        //         stageObject.SetActive(false);
        //         nodeToStageMap[node.Id] = stageObject;
        //     }
        //     else
        //     {
        //         Debug.LogError($"Stage prefab not found for node {node.Id} ({node.GetLabel()})");
        //         return;
        //     }
        // }

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
        // if (nodeToStageMap.ContainsKey(node.Id))
        // {
        //     nodeToStageMap[node.Id].SetActive(true);
        //     currentNode = node;

        //     // 현재 노드 강조
        //     var visualizer = GameObject.FindObjectOfType<UIGraphVisualizer>();
        //     if (visualizer != null)
        //         visualizer.HighlightCurrentNode(node.Id);
        // }
        // else
        // {
        //     Debug.LogError($"Stage for node {node.Id} not found.");
        // }
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

        Debug.Log($"Finding connected nodes for current node: {currentNode.Value.Id}");
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
