using System.Collections;
using System.Collections.Generic;
using MapGeneratorManager;
using UnityEngine;

[CreateAssetMenu(fileName = "NewGraphData", menuName = "Stage/GraphData")]
public class GraphData : ScriptableObject
{
    [Header("Graph Settings")]
    public Graph graph; // 그래프 데이터
    public bool isProgress; // 진행 여부
    public bool isClear; // 클리어 여부
    public Node currentNode; // 현재 노드
    public List<Node> visitedNodes; // 방문한 노드 목록
    
}
