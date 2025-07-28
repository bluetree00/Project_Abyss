using System.Collections.Generic;
using UnityEngine;
using MapGeneratorManager;
using Newtonsoft.Json;

[CreateAssetMenu(fileName = "NewGraphData", menuName = "Stage/GraphData")]
public class GraphData : ScriptableObject
{
    [Header("Graph Settings")]
    public Graph graph;
    public bool isProgress;
    public bool isClear;
    public Node currentNode;
    public List<Node> visitedNodes;

    public GraphDataRuntime ToRuntime()
    {
        if (graph == null)
        {
            Debug.LogWarning("GraphData.graph 가 null입니다. 저장 불가 상태입니다.");
        }
        
        return new GraphDataRuntime
        {
            graph = this.graph,
            currentNode = this.currentNode,
            visitedNodes = this.visitedNodes,
            isProgress = this.isProgress,
            isClear = this.isClear
        };
    }

    public void FromRuntime(GraphDataRuntime runtimeData)
    {
        this.graph = runtimeData.graph;
        this.currentNode = runtimeData.currentNode;
        this.visitedNodes = runtimeData.visitedNodes;
        this.isProgress = runtimeData.isProgress;
        this.isClear = runtimeData.isClear;
    }
}

[System.Serializable]
public class GraphDataRuntime
{
    public Graph graph;
    public bool isProgress;
    public bool isClear;
    public Node currentNode;
    public List<Node> visitedNodes;
}