using System.Collections.Generic;
using UnityEngine;
using MapGeneratorManager;
using System.Linq;
using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;


public class StageManager
{
    private StageGraphDataManager _dataManager => Managers.StageGraphData;
    
    public Graph stageGraph; // MapGeneratorManager에서 생성된 그래프
    private Node? currentNode; // 현재 활성화된 노드
    public Dictionary<int, GameObject> nodeToStageMap; // 노드 ID와 스테이지 오브젝트 매핑

    private float stageTransitionCooldown = 0.5f; // 스테이지 이동 쿨다운 (초)
    private bool isNodemoved = false; // 노드 이동 여부
    
    public StageManager()
    {
        nodeToStageMap = new Dictionary<int, GameObject>();
        isNodemoved = false;
        currentNode = null;
    }

    public async Task InitializeStageManager()
    {
        Debug.Log("StageManager 초기화 시작");

        // ⭐ StageGraphDataManager에서 그래프 생성/로드
        await _dataManager.InitializeAsync();

        // ⭐ 생성된 그래프 가져오기 (기존 Managers._graphData.graph 대체)
        stageGraph = _dataManager.GetMapGeneratorGraph();

        // 현재 노드 설정
        SetCurrentNode();

        nodeToStageMap = new Dictionary<int, GameObject>();

        // UI 초기화 - StageGraphData 사용
        Managers.UI.InitializeNodeIcons(stageGraph.Nodes);

        await StagePrepareAsync();
    }
    
    private void SetCurrentNode()
    {
        var currentNodeId = _dataManager.CurrentData.currentNodeId;
        if (currentNodeId >= 0)
        {
            currentNode = stageGraph.Nodes.Find(n => n.Id == currentNodeId);
        }
        else
        {
            // 시작 노드 찾기
            currentNode = stageGraph.Nodes.Find(node => node.Type == NodeType.Start);
            if (currentNode.HasValue)
            {
                // StageGraphDataManager에 현재 노드 업데이트
                _dataManager.UpdateProgress(currentNode.Value.Id);
            }
        }
    }

    public async Task StagePrepareAsync()
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
                    //Debug.Log($"Preparing stage for node {node.Id} ({node.GetLabel()})");
                }
                else
                {
                    Debug.LogError($"Stage prefab not found for node {node.Id} ({node.GetLabel()})");
                }
            }
        }

        Debug.Log("All stages prepared, setting initial stage.");
        Node? startNode = currentNode ?? stageGraph.Nodes.Find(node => node.Type == NodeType.Start);
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
        char labelChar = (char)('A' + node.Id); // 0 -> 'A', 1 -> 'B', ...
        string address = $"Stage_{labelChar}";
        return address;
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
            
            // ⭐ StageGraphDataManager에 진행 상황 업데이트 (기존 Managers._graphData.currentNode 대체)
            _dataManager.UpdateProgress(node.Id);
            
            Managers.UI.UpdateNodeIcon(node.Id, node);
            Debug.Log($"Activated stage: {node.Id} ({node.GetLabel()})");
        }
        else
        {
            Debug.LogError($"Stage for node {node.Id} not found.");
        }
    }

    public async void MoveToNextStage(int direction)
    {
        if (isNodemoved) return; // 이미 노드가 이동된 상태면 무시
        isNodemoved = true; // 노드 이동 상태 설정

        Debug.Log($"Moving to next stage in direction: {direction}");
        
        var connectedNodes = GetConnectedNodes();
        if (connectedNodes.Count == 0)
        {
            Debug.LogError("No connected nodes to move to.");
            isNodemoved = false; // ⭐ 상태 해제 추가
            return;
        }

        // CHECKLIST : 20250729 현재 노드 예외처리 작동 확인
        // 연결된 노드가 하나만 있을 경우 예외 처리
        if (connectedNodes.Count == 1)
        {
            Debug.LogWarning("연결된 노드가 단 하나이므로 자동적으로 연결되어있는 노드로 이동.");
            ActivateStage(connectedNodes[0]); // 유일한 노드로 이동
            // 쿨다운 적용
            await UniTask.Delay(TimeSpan.FromSeconds(stageTransitionCooldown));
            isNodemoved = false; // ⭐ 상태 해제 추가
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

        // 쿨다운 적용
        await UniTask.Delay(TimeSpan.FromSeconds(stageTransitionCooldown));

        isNodemoved = false; // ⭐ 상태 해제 추가
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

    // 스테이지 매니저 정리 메서드 추가
    public void Cleanup()
    {
        Debug.Log("StageManager 정리 시작");

        // 모든 스테이지 오브젝트 제거 - null 체크 강화
        if (nodeToStageMap != null)
        {
            foreach (var kvp in nodeToStageMap)
            {
                if (kvp.Value != null)
                {
                    UnityEngine.Object.Destroy(kvp.Value);
                }
            }
            nodeToStageMap.Clear();
            nodeToStageMap = null;
        }

        // 변수들 초기화 - null 체크 추가
        if (stageGraph != null)
        {
            stageGraph.Nodes?.Clear();
            stageGraph.Edges?.Clear();
            stageGraph = null;
        }

        currentNode = null;
        isNodemoved = false;

        Debug.Log("StageManager 정리 완료");
    }
}



