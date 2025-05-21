using UnityEngine;
using UnityEngine.UI; // UI 요소 사용
using System.Collections.Generic;
using System.Linq;
using MapGeneratorManager;
using System.Collections; // 그래프 생성 코드가 있는 네임스페이스 사용

public class UIGraphVisualizer : MonoBehaviour
{
    [Header("Graph Generation Parameters")]
    [SerializeField] private int[] layerSizes = { 1, 2, 3, 4, 3, 2, 1 }; // 인스펙터에서 설정할 레이어 크기
    [SerializeField] private int seed = 42; // 그래프 생성 시드

    [Header("Visualization Settings (UI)")]
    [SerializeField] private GameObject uiNodePrefab; // 노드 시각화에 사용할 UI 프리팹 (Image, Button 등 Rect Transform 가짐)
    [SerializeField] private GameObject uiEdgePrefab; // 간선 시각화에 사용할 프리 (Line Renderer 포함)
    [SerializeField] private RectTransform graphContainer; // 그래프 요소들이 배치될 UI 부모 (Canvas 아래의 Panel 등)
    [SerializeField] private float layerSpacingUI = 100.0f; // 레이어 간 거리 (UI Canvas 단위)
    [SerializeField] private float nodeSpacingUI = 50.0f; // 같은 레이어 내 노드 간 거리 (UI Canvas 단위)

    // 노드 ID와 생성된 UI 게임 오브젝트의 RectTransform을 연결하기 위한 딕셔너리
    private Dictionary<int, RectTransform> nodeUIRectMap = new Dictionary<int, RectTransform>();

    private Graph generatedGraph;

    void Start()
    {
        // World Space 또는 Screen Space - Camera 모드의 캔버스에서만 Line Renderer가 제대로 동작합니다.
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            Debug.LogWarning("Screen Space - Overlay Canvas mode is not fully supported for Line Renderers. Consider using Screen Space - Camera or World Space, or a UI Line Renderer asset.");
            // Screen Space Overlay에서는 Line Renderer 대신 다른 UI 선 그리기 방법을 사용해야 합니다.
        }

        StartCoroutine(WaitForStageManagerInitialization());

        // GenerateAndVisualizeGraph();
    }

    private IEnumerator WaitForStageManagerInitialization()
    {
       // StageManager가 초기화될 때까지 대기
       while (Managers.Stage == null || Managers.Stage.stageGraph == null)
       {
           yield return null;
       }

       // 그래프 시각화
       GenerateAndVisualizeGraph();
    }

    public void GenerateAndVisualizeGraph()
    {
        ClearExistingVisualization(); 

        // 1. 그래프 데이터 생성
        generatedGraph = Managers.Stage.stageGraph;

        if (generatedGraph == null || generatedGraph.Nodes == null || generatedGraph.Edges == null)
        {
            Debug.LogError("Graph generation failed or returned null data.");
            return;
        }

        Debug.Log($"Generated graph with {generatedGraph.Nodes.Count} nodes and {generatedGraph.Edges.Count} edges.");

        // 2. UI 노드 시각화 및 배치 (Rect Transform 사용)
        VisualizeUINodes();

        // 3. 간선 시각화 (Line Renderer 사용 - Canvas 모드 고려 필요)
        VisualizeEdges();
    }

    void VisualizeUINodes()
    {
        nodeUIRectMap.Clear();

        var nodesByLayer = generatedGraph.Nodes.GroupBy(node => node.Layer).OrderBy(g => g.Key);

        foreach (var layerGroup in nodesByLayer)
        {
            int layerIndex = layerGroup.Key;
            var nodesInLayer = layerGroup.OrderBy(node => node.Position).ToList();
            int numberOfNodesInLayer = nodesInLayer.Count;

            // 같은 레이어 노드들의 시작 X 위치 계산 (중앙 정렬)
            float startX = -(numberOfNodesInLayer - 1) * nodeSpacingUI / 2.0f;

            foreach (var nodeData in nodesInLayer)
            {
                // UI 노드 위치 계산 (캔버스 UI 단위)
                // Y는 레이어 인덱스, X는 레이어 내 위치
                // RectTransform.anchoredPosition 사용 시 부모의 Anchors 기준
                Vector2 anchoredPosition = new Vector2(
                    startX + nodeData.Position * nodeSpacingUI, // X 위치 (레이어 내 정렬)
                    layerIndex * layerSpacingUI               // Y 위치 (레이어 간 거리)
                );

                // UI 노드 프리팹 생성
                GameObject uiNodeGO = Instantiate(uiNodePrefab, graphContainer); // graphContainer의 자식으로 생성
                RectTransform nodeRect = uiNodeGO.GetComponent<RectTransform>();

                // RectTransform 위치 설정
                nodeRect.anchoredPosition = anchoredPosition;

                // 앵커와 피벗을 중앙으로 설정 (선택 사항이지만 배치 계산에 편리)
                nodeRect.anchorMin = new Vector2(0.5f, 0.5f);
                nodeRect.anchorMax = new Vector2(0.5f, 0.5f);
                nodeRect.pivot = new Vector2(0.5f, 0.5f);


                // 생성된 UI 오브젝트의 RectTransform과 노드 ID를 연결
                nodeUIRectMap[nodeData.Id] = nodeRect;

                // UI 노드에 추가 정보 표시 (선택 사항)
                uiNodeGO.name = $"UINode_{nodeData.GetLabel()}";
                // 자식 Text 컴포넌트 등에 노드 라벨 표시 가능
            }
        }
    }

    void VisualizeEdges()
    {
        foreach (var edgeData in generatedGraph.Edges)
        {
            // 간선 데이터의 From/To 노드 ID로부터 해당 UI 노드의 RectTransform 찾기
            if (nodeUIRectMap.TryGetValue(edgeData.FromId, out RectTransform fromNodeRect) &&
                nodeUIRectMap.TryGetValue(edgeData.ToId, out RectTransform toNodeRect))
            {
                // 간선 프리팹 생성
                // Line Renderer는 월드 좌표를 사용하므로, UI 계층이 아닌 별도의 부모 또는 최상위에서 생성하는 것이 편리할 수 있습니다.
                // 아니면 World Space 캔버스 모드에서 UI 부모 아래 생성하고, UI 요소의 WorldPosition을 얻어 사용합니다.
                GameObject edgeGO = Instantiate(uiEdgePrefab, this.transform); // Visualizer 스크립트 오브젝트의 자식으로 생성 (Canvas 아래가 아닐 수 있음)

                LineRenderer lineRenderer = edgeGO.GetComponent<LineRenderer>();

                if (lineRenderer != null)
                {
                    // Line Renderer 설정
                    lineRenderer.positionCount = 2;
                    // UI 요소의 RectTransform의 WorldPosition을 얻어 Line Renderer에 설정
                    lineRenderer.SetPosition(0, fromNodeRect.position); // From UI 노드의 월드 위치
                    lineRenderer.SetPosition(1, toNodeRect.position);   // To UI 노드의 월드 위치

                    // Line Renderer의 색상, 두께 등 추가 설정 가능
                    // lineRenderer.startColor = Color.gray;
                    // lineRenderer.endColor = Color.gray;
                    // lineRenderer.startWidth = 5f; // UI 스케일에 맞게 두께 조절 필요
                    // lineRenderer.endWidth = 5f;
                }
                else
                {
                    Debug.LogWarning($"UI Edge Prefab '{uiEdgePrefab.name}' is missing LineRenderer component.");
                     Destroy(edgeGO); // Line Renderer 없으면 오브젝트 제거
                }

                edgeGO.name = $"UIEdge_{fromNodeRect.name}_to_{toNodeRect.name}";
            }
            else
            {
                Debug.LogWarning($"Could not find UI rect transforms for edge {edgeData.FromId} -> {edgeData.ToId}.");
            }
        }
    }

    void ClearExistingVisualization()
    {
        // Graph Container 아래의 모든 자식 UI 오브젝트 제거
        if (graphContainer != null)
        {
            while (graphContainer.childCount > 0)
            {
                // 에디터 중에는 DestroyImmediate 사용 (Runtime에는 Destroy)
                if (Application.isPlaying)
                {
                    Destroy(graphContainer.GetChild(0).gameObject);
                }
                else
                {
                    DestroyImmediate(graphContainer.GetChild(0).gameObject);
                }
            }
        }

        // 시각화 스크립트 오브젝트의 자식으로 생성된 간선 오브젝트들도 제거 (UI Container 밖에 있을 수 있으므로 별도 처리)
         List<GameObject> childrenToDestroy = new List<GameObject>();
         foreach (Transform child in transform)
         {
             // UI Container 자체가 visualizer의 자식이 아니라면 여기 포함되지 않음
             // 만약 간선이 UI Container 아래 생성되었다면 이 부분은 필요 없음
             // 간선이 UI가 아니라 일반 GameObject이고 visualizer의 자식이라면 여기에 포함
             if(child.GetComponent<LineRenderer>() != null) // Line Renderer 가진 자식 찾기
             {
                  childrenToDestroy.Add(child.gameObject);
             }
         }

         foreach(GameObject child in childrenToDestroy)
         {
             if (Application.isPlaying) Destroy(child);
             else DestroyImmediate(child);
         }


        nodeUIRectMap.Clear();
    }

    // 인스펙터에서 값 변경 시 에디터에서 바로 확인하고 싶다면 주석 해제
    // void OnValidate()
    // {
    //     if (!Application.isPlaying)
    //     {
    //         // 에디터 모드에서 변경 시 ClearExistingVisualization에서 DestroyImmediate 사용 필요
    //         GenerateAndVisualizeGraph();
    //     }
    // }
}