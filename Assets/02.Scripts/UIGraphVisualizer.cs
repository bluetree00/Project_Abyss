using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using MapGeneratorManager;
using System.Collections;

public class UIGraphVisualizer : MonoBehaviour
{
    [SerializeField] private int seed = 42;
    [Header("Visualization Settings (UI)")]
    [SerializeField] private GameObject uiNodePrefab;
    [SerializeField] private GameObject uiEdgePrefab;
    [SerializeField] private RectTransform graphContainer;
    [SerializeField] private float layerSpacingUI = 100.0f;
    [SerializeField] private float nodeSpacingUI = 50.0f;
    [SerializeField] private Color visitedColor = Color.yellow; // 방문한 노드 강조 색상
    [SerializeField] private Color defaultColor = Color.white;  // 기본 노드 색상

    private Dictionary<int, RectTransform> nodeUIRectMap = new Dictionary<int, RectTransform>();
    private Graph generatedGraph;
    private StageData stageData;
    private HashSet<int> visitedNodeIds = new HashSet<int>();

    void Start()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            Debug.LogWarning("Screen Space - Overlay Canvas mode is not fully supported for Line Renderers. Consider using Screen Space - Camera or World Space, or a UI Line Renderer asset.");
        }

        StartCoroutine(WaitForStageManagerInitialization());
    }

    private IEnumerator WaitForStageManagerInitialization()
    {
        while (Managers.Stage == null)
        yield return null;

        Managers.Stage.OnGraphGenerated += HandleGraphGenerated;
        stageData = Managers.Instance.GetStageData();

        // 이미 그래프가 생성되어 있으면 직접 호출
        if (Managers.Stage.stageGraph != null)
            HandleGraphGenerated(Managers.Stage.stageGraph);
    }

    private void HandleGraphGenerated(Graph graph)
    {
        if (graph == null)
        {
            Debug.LogError("Received null graph from StageManager.");
            return;
        }

        generatedGraph = graph;
        // 방문한 노드 정보 초기화 (예시: 시작 노드만 방문했다고 가정)
        visitedNodeIds.Clear();
        Node? startNode = generatedGraph.Nodes.Find(n => n.Type == NodeType.Start);
        if (startNode.HasValue)
            visitedNodeIds.Add(startNode.Value.Id);

        GenerateAndVisualizeGraph();
    }

    public void MarkNodeAsVisited(int nodeId)
    {
        visitedNodeIds.Add(nodeId);
        GenerateAndVisualizeGraph(); // 강조 갱신
    }

    public void GenerateAndVisualizeGraph()
    {
        ClearExistingVisualization();

        if (generatedGraph == null || generatedGraph.Nodes == null || generatedGraph.Edges == null)
        {
            Debug.LogError("Graph generation failed or returned null data.");
            return;
        }

        VisualizeUINodes();
        VisualizeEdges();
    }

    public void HighlightCurrentNode(int nodeId)
    {
        visitedNodeIds.Clear();
        visitedNodeIds.Add(nodeId);
        GenerateAndVisualizeGraph();
    }

    void VisualizeUINodes()
    {
        nodeUIRectMap.Clear();

        var nodesByLayer = generatedGraph.Nodes.GroupBy(node => node.Layer).OrderBy(g => g.Key);

        foreach (var layerGroup in nodesByLayer)
        {
            int layerIndex = layerGroup.Key;
            var nodesInLayer = layerGroup.OrderBy(node => node.Position).ToList();
            float startX = -(nodesInLayer.Count - 1) * nodeSpacingUI / 2.0f;

            foreach (var nodeData in nodesInLayer)
            {
                Vector2 anchoredPosition = new Vector2(
                    startX + nodeData.Position * nodeSpacingUI,
                    layerIndex * layerSpacingUI
                );

                GameObject uiNodeGO = Instantiate(uiNodePrefab, graphContainer);
                RectTransform nodeRect = uiNodeGO.GetComponent<RectTransform>();
                nodeRect.anchoredPosition = anchoredPosition;
                nodeRect.anchorMin = nodeRect.anchorMax = nodeRect.pivot = new Vector2(0.5f, 0.5f);

                nodeUIRectMap[nodeData.Id] = nodeRect;
                uiNodeGO.name = $"UINode_{nodeData.GetLabel()}";


                // 방문한 노드 강조
                Image img = uiNodeGO.GetComponent<Image>();
                if (img != null)
                {
                    img.color = visitedNodeIds.Contains(nodeData.Id) ? visitedColor : defaultColor;
                }
            }
        }
    }

    void VisualizeEdges()
    {
        foreach (var edgeData in generatedGraph.Edges)
        {
            if (nodeUIRectMap.TryGetValue(edgeData.FromId, out RectTransform fromNodeRect) &&
                nodeUIRectMap.TryGetValue(edgeData.ToId, out RectTransform toNodeRect))
            {
                GameObject edgeGO = Instantiate(uiEdgePrefab, this.transform);
                LineRenderer lineRenderer = edgeGO.GetComponent<LineRenderer>();

                if (lineRenderer != null)
                {
                    lineRenderer.positionCount = 2;
                    lineRenderer.SetPosition(0, fromNodeRect.position);
                    lineRenderer.SetPosition(1, toNodeRect.position);
                }
                else
                {
                    Destroy(edgeGO);
                }
                edgeGO.name = $"UIEdge_{fromNodeRect.name}_to_{toNodeRect.name}";
            }
        }
    }

    void ClearExistingVisualization()
    {
        if (graphContainer != null)
        {
            while (graphContainer.childCount > 0)
            {
                if (Application.isPlaying)
                    Destroy(graphContainer.GetChild(0).gameObject);
                else
                    DestroyImmediate(graphContainer.GetChild(0).gameObject);
            }
        }

        List<GameObject> childrenToDestroy = new List<GameObject>();
        foreach (Transform child in transform)
        {
            if (child.GetComponent<LineRenderer>() != null)
                childrenToDestroy.Add(child.gameObject);
        }
        foreach (GameObject child in childrenToDestroy)
        {
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
        nodeUIRectMap.Clear();
    }
}