using System.Collections.Generic;
using UnityEngine;
using MapGeneratorManager;
using Newtonsoft.Json;
using System.Linq;

[CreateAssetMenu(fileName = "NewGraphData", menuName = "Stage/GraphData")]
public class GraphData : ScriptableObject
{
    [Header("Graph Settings")]
    
    public Graph graph;
    public bool isProgress;
    public bool isClear;
    public Node currentNode;
    public List<Node> visitedNodes = new List<Node>();

    public GraphDataRuntime ToRuntime()
    {
        return new GraphDataRuntime
        {
            graph = this.graph,
            currentNode = this.currentNode.Id,
            visitedNodes = (this.visitedNodes ?? new List<Node>()).Select(n => n.Id).ToList(),
            isProgress = this.isProgress,
            isClear = this.isClear
        };
    }
    
    public void FromRuntime(GraphDataRuntime runtimeData)
    {
        this.graph = runtimeData.graph;

        // edges 필드로 강제 복원
        if ((this.graph.Edges == null || this.graph.Edges.Count == 0) &&
            runtimeData.edges != null && runtimeData.edges.Count > 0)
        {
            this.graph.Edges = new HashSet<Edge>(runtimeData.edges);
            Debug.LogWarning($"[GraphData] Edges 복원 결과: Edges {this.graph.Edges.Count}개");
        }
        else
        {
            Debug.LogError("[GraphData] edges가 null이거나 비어 있음. 복원 불가!");
        }
            
        

        this.currentNode = this.graph.Nodes.Find(n => n.Id == runtimeData.currentNode);
        this.visitedNodes = this.graph.Nodes.Where(n => runtimeData.visitedNodes.Contains(n.Id)).ToList();
        this.isProgress = runtimeData.isProgress;
        this.isClear = runtimeData.isClear;
    }
}

[System.Serializable]
public class GraphDataRuntime
{
    public Graph graph;
    public List<Edge> edges;
    public bool isProgress;
    public bool isClear;
    public int currentNode;
    public List<int> visitedNodes;
}