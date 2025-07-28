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
    private List<int> LoadedvisitedNodes = new List<int>(); // 방문한 노드 목록
    private int LoadedcurrentNode; // 로드된 현재 노드

    private bool isNodemoved = false; // 노드 이동 여부

    //TODO: Json으로 그래프 정보와 스테이지 정보를 저장하고 불러오는 기능 추가

    public async Task InitializeStageManager()
    {
        Debug.Log("StageManager InitializeAsync 시작");

        // LoadGraphState에서 그래프 데이터를 로드
        GraphState loadedGraphState = LoadGraphState();

        if (loadedGraphState != null)
        {
            // 로드된 그래프 데이터를 Managers._graphData에 덮어씌움
            Managers._graphData = new GraphData
            {
                graph = loadedGraphState.graph
            };

            Debug.Log("Loaded graph state applied to Managers._graphData.");
        }

        // Managers._graphData를 사용하여 그래프 초기화
        GraphData graphData = Managers._graphData;

        if (graphData == null || graphData.graph == null || graphData.graph.Nodes == null || graphData.graph.Nodes.Count == 0)
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
        
        Debug.Log("All stages prepared, setting initial stage.");
        Node? startNode = stageGraph.Nodes.Find(node => node.Id == LoadedcurrentNode);
        if (startNode == null)
        {
            Debug.LogWarning($"Start node with ID {LoadedcurrentNode} not found in graph nodes. Using first node as start.");
            startNode = stageGraph.Nodes.Find(node => node.Type == NodeType.Start);
        }
        Debug.LogWarning(startNode.Value.ToString());
        Debug.LogWarning(GetStageAddress(startNode.Value));
        if (startNode.HasValue)
        {
            currentNode = startNode;
            ActivateStage(currentNode.Value);
            Debug.Log($"Activated stage: {startNode.Value.Id} {startNode.Value.GetLabel()})");
        }
        else
        {
            Debug.LogError("No start node found in the graph.");
        }

    }

    // Addressables 키 규칙에 맞게 주소 생성
    private string GetStageAddress(Node node)
    {
        // string address = $"Stage_{node.Id + 1:D2}";
        // Debug.Log($"Generated Addressables key: {address}");
        // return address;

        char labelChar = (char)('A' + node.Id); // 0 -> 'A', 1 -> 'B', ...
        string address = $"Stage_{labelChar}";
        Debug.Log($"Generated Addressables key: {address}");
        return address;
    }

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
    }

    public void MoveToNextStage(int direction)
    {
        if (isNodemoved) return; // 이미 노드가 이동된 상태면 무시
        isNodemoved = true; // 노드 이동 상태 설정

        Debug.Log($"Moving to next stage in direction: {direction}");
        {
            var connectedNodes = GetConnectedNodes();
            if (connectedNodes.Count == 0)
            {
                Debug.LogError("No connected nodes to move to.");
                return;
            }

            // CHECKLIST : 20250729 현재 노드 예외처리 작동 확인
            // 연결된 노드가 하나만 있을 경우 예외 처리
            if (connectedNodes.Count == 1)
            {
                Debug.LogWarning("연결된 노드가 단 하나이므로 자동적으로 연결되어있는 노드로 이동.");
                ActivateStage(connectedNodes[0]); // 유일한 노드로 이동
                return;
            }

            // 방향에 따라 노드 이동
            if (direction == 1) // 오른쪽
            {
                if (connectedNodes.Count > 1)
                {
                    ActivateStage(connectedNodes[1]); // 오른쪽 노드로 이동
                }
                else
                {
                    Debug.LogWarning("No right node available. Moving to the only connected node.");
                    ActivateStage(connectedNodes[0]);
                }
            }
            else if (direction == -1) // 왼쪽
            {
                ActivateStage(connectedNodes[0]); // 왼쪽 노드로 이동
            }
            else
            {
                Debug.LogWarning("Invalid direction or no node available in that direction.");
            }
        }

        Managers.StartCoroutineStatic(WaitForNodeMove()); // 노드 이동 완료 대기
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

    private IEnumerator WaitForNodeMove()
    {
        yield return null; // 노드 이동이 완료될 때까지 대기
        isNodemoved = false; // 노드 이동 상태 해제
    }

    public void SaveGraphState()
    {
        var graphState = new
        {
            currentNode = currentNode.HasValue ? currentNode.Value.Id : -1,
            visitedNodes = stageGraph.Nodes.Where(node => nodeToStageMap[node.Id].activeSelf).Select(node => node.Id).ToList(),
            graph = stageGraph
        };

        DataManager.SaveJsonFile("GraphState.json", graphState);
        Debug.Log("그래프 상태 저장.");
    }

    public GraphState LoadGraphState()
    {
        var graphState = DataManager.LoadJsonFile<GraphState>("GraphState.json");
        if (graphState == null)
        {
            Debug.LogWarning("Graph state 파일을 로드할 수 없습니다.");
            return null;
        }
        Debug.Log("그래프 상태 로드 완료.");

        // 그래프 복원
        if (graphState.graph == null)
        {
            Debug.LogError("Loaded graph is null.");
            return null;
        }
        stageGraph = graphState.graph;
        

        LoadedvisitedNodes = graphState.visitedNodes;
        LoadedcurrentNode = graphState.currentNode;

        Debug.Log("Graph state loaded.");
        return graphState;
    }

    // GraphState 클래스 정의
    [Serializable]
    public class GraphState
    {
        public int currentNode;
        public List<int> visitedNodes;
        public Graph graph;
    }

}


