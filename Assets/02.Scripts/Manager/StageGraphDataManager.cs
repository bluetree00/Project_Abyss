using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using MapGeneratorManager;
using Newtonsoft.Json;
using System.Linq;
using System.Threading.Tasks;

public class StageGraphDataManager
{
    private const string DataFileName = "StageGraphData.json";
    private string FilePath => Path.Combine(Application.persistentDataPath, DataFileName);

    private StageGraphData _currentData;
    public StageGraphData CurrentData => _currentData;
    public bool IsInitialized { get; private set; } = false;

    // 캐시된 그래프 (JSON 데이터에서 변환된 MapGeneratorManager.Graph)
    private Graph _cachedGraph;

    public async UniTask InitializeAsync()
    {
        bool success = false;

        if (File.Exists(FilePath))
        {
            Debug.Log("로컬 StageGraphData 로드");
            success = LoadFromJson();

            // TODO: 서버 연결 시 버전 비교 로직 추가
            // await CheckServerVersionAndUpdate();
        }
        if (!success)
        {
            Debug.Log("로컬 StageGraphData가 없거나 비어있음. 새로 생성");
            await CreateNewGraphAsync();
            SaveToJson();
        }

        // JSON 데이터를 MapGeneratorManager.Graph로 변환
        _cachedGraph = ConvertToMapGeneratorGraph();

        IsInitialized = true;
    }

    // private async UniTask LoadFromJsonAsync()
    // {
    //     string json = File.ReadAllText(FilePath);
    //     _currentData = JsonConvert.DeserializeObject<StageGraphData>(json);
    // }

    private bool LoadFromJson()
    {
        try
        {
            string json = File.ReadAllText(FilePath);
            _currentData = JsonConvert.DeserializeObject<StageGraphData>(json);

            if (_currentData == null || _currentData.nodes == null || _currentData.nodes.Count == 0)
            {
                Debug.LogWarning("StageGraphData가 비어 있음");
                return false;
            }

            Debug.Log($"StageGraphData 로드 완료 - 노드: {_currentData.nodes.Count}개, 간선: {_currentData.edges.Count}개");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"JSON 로드 실패: {e.Message}");
            return false;
        }
    }

    public void SaveToJson()
    {
        Debug.Log("layerSizes = " + string.Join(",", _currentData.layerSizes));

        try
        {
            _currentData.lastUpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string json = JsonConvert.SerializeObject(_currentData, Formatting.Indented);
            File.WriteAllText(FilePath, json);
            Debug.Log($"StageGraphData 저장 완료: {FilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"JSON 저장 실패: {e.Message}");
        }
    }

    // ⭐ 핵심: 여기서 MapGeneratorManager를 사용해서 그래프 생성
    private async UniTask CreateNewGraphAsync()
    {
        var defaultChapter = CreateDefaultChapterData();

        // MapGeneratorManager로 그래프 생성 ⭐
        var generatedGraph = MapGeneratorManager.MapGeneratorManager.Generate(defaultChapter);


        // StageGraphData 초기화 - 항상 새로운 기본값 사용
        _currentData = null;
        _currentData = new StageGraphData
        {
            chapterName = defaultChapter.chapterName.ToString(),
            layerSizes = new List<int>(defaultChapter.layerSizes),
            stages = ConvertStageSettings(defaultChapter.stages),
            dataVersion = 1,
            lastUpdateTime = DateTime.Now.ToString(),
            nodes = new List<NodeData>(),
            edges = new List<EdgeData>(),
            visitedNodes = new List<int>(),
            clearedNodes = new List<int>(),
            isProgress = false,
            isClear = false
        };

        // 생성된 그래프를 StageGraphData에 저장
        UpdateFromMapGeneratorGraph(generatedGraph);

        // 시작 노드 설정
        var startNodes = generatedGraph.Nodes.Where(n => n.Type == NodeType.Start).ToList();
        if (startNodes.Count > 0)
        {
            _currentData.currentNodeId = startNodes[0].Id;
        }
    }

    // ⭐ MapGeneratorManager.Graph를 StageGraphData로 변환
    private void UpdateFromMapGeneratorGraph(Graph graph)
    {
        _currentData.nodes.Clear();
        _currentData.edges.Clear();

        // Node 데이터 변환
        foreach (var node in graph.Nodes)
        {
            _currentData.nodes.Add(new NodeData
            {
                id = node.Id,
                layer = node.Layer,
                positionInLayer = node.Position,
                nodeType = ConvertFromMapGeneratorNodeType(node.Type),
                stageAddress = GenerateStageAddress(node),
                isVisited = _currentData.visitedNodes.Contains(node.Id),
                isCleared = _currentData.clearedNodes.Contains(node.Id)
            });
        }

        // Edge 데이터 변환
        foreach (var edge in graph.Edges)
        {
            _currentData.edges.Add(new EdgeData
            {
                fromId = edge.FromId,
                toId = edge.ToId
            });
        }
    }

    // ⭐ StageGraphData를 MapGeneratorManager.Graph로 변환
    private Graph ConvertToMapGeneratorGraph()
    {
        var graph = new Graph();

        // null 체크 강화
        if (_currentData == null)
        {
            Debug.LogError("_currentData가 null입니다.");
            return graph;
        }

        if (_currentData.nodes == null)
        {
            Debug.LogError("_currentData.nodes가 null입니다.");
            return graph;
        }

        // NodeData를 MapGeneratorManager.Node로 변환
        foreach (var nodeData in _currentData.nodes)
        {
            var nodeType = ConvertToMapGeneratorNodeType(nodeData.nodeType);
            var node = new Node(nodeData.id, nodeData.layer, nodeData.positionInLayer, nodeType);
            graph.Nodes.Add(node);
        }

        // EdgeData를 MapGeneratorManager.Edge로 변환 - null 체크 추가
        if (_currentData.edges != null)
        {
            foreach (var edgeData in _currentData.edges)
            {
                var edge = new Edge(edgeData.fromId, edgeData.toId);
                graph.Edges.Add(edge);
            }
        }

        return graph;
    }

    // ⭐ StageManager에서 사용할 그래프 반환
    public Graph GetMapGeneratorGraph()
    {
        if (!IsInitialized)
        {
            Debug.LogError("StageGraphDataManager가 초기화되지 않았습니다. InitializeAsync()를 먼저 호출해주세요.");
            return new Graph();
        }

        if (_cachedGraph == null)
        {
            _cachedGraph = ConvertToMapGeneratorGraph();
        }

        return _cachedGraph;
    }

    // ⭐ 누락된 UpdateProgress 메서드 추가
    public void UpdateProgress(int nodeId, bool isCompleted = false)
    {
        _currentData.currentNodeId = nodeId;

        if (!_currentData.visitedNodes.Contains(nodeId))
        {
            _currentData.visitedNodes.Add(nodeId);
        }

        var nodeData = _currentData.nodes.Find(n => n.id == nodeId);
        if (nodeData != null)
        {
            nodeData.isVisited = true;
            if (isCompleted)
            {
                nodeData.isCleared = true;
                if (!_currentData.clearedNodes.Contains(nodeId))
                {
                    _currentData.clearedNodes.Add(nodeId);
                }
            }
        }

        _currentData.isProgress = true;
        SaveToJson();

        Debug.Log($"Progress updated for node {nodeId}, visited: {nodeData?.isVisited}, cleared: {nodeData?.isCleared}");
    }


    // TODO: 서버 연동용 메서드들
    private async UniTask CheckServerVersionAndUpdate()
    {
        // 서버에서 버전 정보 확인
        // 서버 버전이 더 높으면 데이터 다운로드
    }

    #region 헬퍼 메서드
    // 나머지 헬퍼 메서드들...
    private StageData.ChapterData CreateDefaultChapterData()
    {
        return new StageData.ChapterData
        {
            chapterName = StageData.ChapterName.Chapter1,
            layerSizes = new int[] { 1, 2, 3, 4, 3, 2, 1 },
            stages = CreateDefaultStages(),
            bossStageName = StageData.StageName.Stage_10.ToString()
        };
    }

    private List<StageData.ChapterData.StageSettings> CreateDefaultStages()
    {
        var stages = new List<StageData.ChapterData.StageSettings>();
        for (int i = 0; i < 10; i++)
        {
            stages.Add(new StageData.ChapterData.StageSettings
            {
                stageName = (StageData.StageName)i,
            });
        }
        return stages;
    }

    private StageNodeType ConvertFromMapGeneratorNodeType(NodeType nodeType)
    {
        return nodeType switch
        {
            NodeType.Start => StageNodeType.Start,
            NodeType.End => StageNodeType.End,
            _ => StageNodeType.Normal
        };
    }

    private NodeType ConvertToMapGeneratorNodeType(StageNodeType stageNodeType)
    {
        return stageNodeType switch
        {
            StageNodeType.Start => NodeType.Start,
            StageNodeType.End => NodeType.End,
            _ => NodeType.Normal
        };
    }

    private string GenerateStageAddress(Node node)
    {
        char labelChar = (char)('A' + node.Id);
        return $"Stage_{labelChar}";
    }
    
    // ⭐ 추가로 필요한 ConvertStageSettings 메서드
    private List<StageSettings> ConvertStageSettings(List<StageData.ChapterData.StageSettings> originalStages)
    {
        var stageSettings = new List<StageSettings>();
        foreach (var stage in originalStages)
        {
            stageSettings.Add(new StageSettings
            {
                stageName = stage.stageName.ToString(),
                resourcePath = stage.resourcePath,
            });
        }
        return stageSettings;
    }
    
    #endregion
}