using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using MapGeneratorManager;
using Newtonsoft.Json;
using System.Linq;
using BackEnd;
using LitJson;
using System.Text;

public class StageGraphDataManager
{
    private const string DataFileName = "StageGraphData.json";
    private string FilePath => Path.Combine(Application.persistentDataPath, DataFileName);

    // 서버 차트 ID (실제 환경에서는 적절한 값으로 변경)
    private const string MapChartId = "stage_map_chart_id";
    private const string ProgressChartId = "user_progress_chart_id";

    private StageGraphData _currentData;
    public StageGraphData CurrentData => _currentData;
    public bool IsInitialized { get; private set; } = false;
    private Graph _cachedGraph;

    public async UniTask InitializeAsync()
    {
        if (File.Exists(FilePath))
        {
            Debug.Log("로컬 StageGraphData 로드");
            LoadFromJson();

            Debug.Log("서버에서 변경된 맵/진행상황 데이터만 갱신");
            //await LoadFromServerAsync(); // 버전 비교 후 부분 갱신
            await LoadFromLocalCSVAsync();
        }
        else
        {
            Debug.Log("로컬 데이터 없음. 서버에서 전체 다운로드");
            //await LoadFromServerAsync();
        }

        // 모든 로드 시도 후에도 데이터가 없으면 새로 생성
        if (_currentData == null)
        {
            Debug.LogWarning("로컬 및 서버 데이터 모두 없음. 새로운 그래프를 생성합니다.");
            await CreateNewGraphAsync();
            SaveToJson();
        }

        _cachedGraph = ConvertToMapGeneratorGraph();
        IsInitialized = true;
    }
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
    
    // TODO: 서버 연동용 메서드들
    private async UniTask CheckServerVersionAndUpdate()
    {
        // 임시로 로컬 CSV에서 데이터 로드
        await LoadFromLocalCSVAsync();
    }

    // 현재 파일의 LoadFromLocalCSVAsync 메서드를 다음과 같이 수정
    private async UniTask LoadFromLocalCSVAsync()
    {
        string csvFilePath = @"C:\Users\skyth\OneDrive\Desktop\project-memo\StageMapData.csv";

        if (!File.Exists(csvFilePath))
        {
            Debug.LogWarning($"맵 CSV 파일을 찾을 수 없습니다: {csvFilePath}");
            return;
        }

        try
        {
            Debug.Log("로컬 맵 CSV 파일에서 데이터 로딩 중...");
            string[] lines = File.ReadAllLines(csvFilePath);

            var tempData = new StageGraphData
            {
                nodes = new List<NodeData>(),
                edges = new List<EdgeData>(),
                stages = new List<StageSettings>(),
                visitedNodes = new List<int>(),
                clearedNodes = new List<int>(),
                isProgress = false,
                isClear = false
            };

            // 기존 진행 상황 백업
            var existingProgress = BackupProgressData();

            // CSV 헤더와 메타데이터 파싱
            bool headerFound = false;
            int csvVersion = 1;

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                // 주석 라인 처리 (메타데이터) 부분 수정
                if (trimmedLine.StartsWith("#"))
                {
                    if (trimmedLine.Contains("chapterName:"))
                    {
                        // ⭐ 수정: 쉼표 제거 후 파싱
                        string value = trimmedLine.Split(':')[1].Trim().TrimEnd(',');
                        tempData.chapterName = value;
                    }
                    else if (trimmedLine.Contains("map_version:"))
                    {
                        // ⭐ 수정: 쉼표 제거 후 파싱
                        string value = trimmedLine.Split(':')[1].Trim().TrimEnd(',');
                        int.TryParse(value, out csvVersion);
                        tempData.dataVersion = csvVersion;
                        Debug.Log($"CSV에서 파싱된 map_version: {csvVersion}"); // 디버그 로그 추가
                    }
                    else if (trimmedLine.Contains("layerSizes:"))
                    {
                        // ⭐ 수정: 쉼표 제거 후 파싱
                        string layerSizeStr = trimmedLine.Split(':')[1].Trim().TrimEnd(',');
                        var layerSizes = layerSizeStr.Split(',')
                            .Select(s => int.TryParse(s.Trim(), out int size) ? size : 0)
                            .Where(s => s > 0)
                            .ToList();
                        tempData.layerSizes = layerSizes;
                    }
                    continue;
                }

                // 헤더 라인 건너뛰기
                if (trimmedLine.StartsWith("node_id,"))
                {
                    headerFound = true;
                    continue;
                }

                // 빈 라인 건너뛰기
                if (string.IsNullOrEmpty(trimmedLine) || !headerFound)
                    continue;

                // 데이터 라인 파싱
                await ParseCSVDataLine(trimmedLine, tempData, csvVersion);
            }
            Debug.Log($"CSV 데이터 버전: {csvVersion}");

            // 맵 데이터 버전 비교 및 업데이트
            bool mapUpdated = false;
            // 로컬 데이터와 버전 비교
            if (_currentData == null || csvVersion > _currentData.dataVersion)
            {
                // CSV 데이터로 교체
                _currentData = tempData;
                _currentData.lastUpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                // 진행 상황 복원
                RestoreProgressData(existingProgress);

                // 시작 노드 설정
                SetStartNode();

                // 캐시 갱신
                _cachedGraph = ConvertToMapGeneratorGraph();

                SaveToJson();
                Debug.Log($"맵 CSV에서 {_currentData.nodes.Count}개 노드, {_currentData.edges.Count}개 간선 로드 완료");
            }
            else
            {
                Debug.Log("로컬 맵 데이터가 이미 최신 버전입니다.");
            }

            // 진행상황 CSV도 로드
            await LoadProgressFromLocalCSVAsync();

            // 맵이 업데이트된 경우에만 캐시 갱신 및 저장
            if (mapUpdated)
            {
                _cachedGraph = ConvertToMapGeneratorGraph();
                SaveToJson();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"맵 CSV 파일 로드 실패: {e.Message}");
        }
    }

    // ⭐ CSV 데이터 라인을 파싱하는 헬퍼 메서드
    private async UniTask ParseCSVDataLine(string line, StageGraphData tempData, int csvVersion)
    {
        await UniTask.Yield(); // 비동기 처리를 위한 프레임 양보

        // ⭐ 따옴표를 고려한 CSV 분할
        string[] values = ParseCSVLine(line);

        if (values.Length < 7)
        {
            Debug.LogWarning($"값이 부족함 (필요: 7, 실제: {values.Length}): {line}");
            return;
        }

        // CSV 데이터 파싱
        if (!int.TryParse(values[0].Trim(), out int nodeId)) return;
        if (!int.TryParse(values[1].Trim(), out int layer)) return;
        if (!int.TryParse(values[2].Trim(), out int positionInLayer)) return;

        string nodeTypeStr = values[3].Trim();
        string stageName = values[4].Trim();
        string resourcePath = values[5].Trim();

        // ⭐ 연결 정보 파싱 - 이미 올바르게 분할됨
        string connectionsStr = values[6].Trim();

        // 따옴표 제거
        if (connectionsStr.StartsWith("\"") && connectionsStr.EndsWith("\""))
        {
            connectionsStr = connectionsStr.Substring(1, connectionsStr.Length - 2);
        }

        // NodeType 변환
        StageNodeType nodeType = nodeTypeStr switch
        {
            "Start" => StageNodeType.Start,
            "End" => StageNodeType.End,
            _ => StageNodeType.Normal
        };

        // 노드 데이터 생성
        var nodeData = new NodeData
        {
            id = nodeId,
            layer = layer,
            positionInLayer = positionInLayer,
            nodeType = nodeType,
            stageAddress = $"Stage_{(char)('A' + nodeId)}",
            isVisited = false,
            isCleared = false
        };
        tempData.nodes.Add(nodeData);

        // 스테이지 설정 추가 (중복 방지)
        if (!tempData.stages.Any(s => s.stageName == stageName))
        {
            tempData.stages.Add(new StageSettings
            {
                stageName = stageName,
                resourcePath = resourcePath
            });
        }

        // ⭐ 간선 데이터 생성 개선 - 디버그 강화
        if (!string.IsNullOrEmpty(connectionsStr))
        {
            var connections = connectionsStr.Split(',');
            for (int i = 0; i < connections.Length; i++)
            {
                string cleanConnection = connections[i].Trim();
                

                if (!string.IsNullOrEmpty(cleanConnection) && int.TryParse(cleanConnection, out int toId))
                {
                    tempData.edges.Add(new EdgeData
                    {
                        fromId = nodeId,
                        toId = toId
                    });
                }
                else
                {
                    Debug.LogWarning($"노드 {nodeId}: 연결 {i} 파싱 실패 = '{cleanConnection}'"); // 실패 로그
                }
            }
        }
    }
    // ⭐ 새로 추가할 메서드: 따옴표를 고려한 CSV 파싱
    private string[] ParseCSVLine(string line)
    {
        var result = new List<string>();
        var currentField = new StringBuilder();
        bool inQuotes = false;
    
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
    
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    // 이중 따옴표 처리 ("")
                    currentField.Append('"');
                    i++; // 다음 따옴표 건너뛰기
                }
                else
                {
                    // 따옴표 시작/끝
                    inQuotes = !inQuotes;
                    currentField.Append(c);
                }
            }
            else if (c == ',' && !inQuotes)
            {
                // 따옴표 밖의 쉼표 - 필드 구분자
                result.Add(currentField.ToString());
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }
    
        // 마지막 필드 추가
        result.Add(currentField.ToString());
    
        return result.ToArray();
    }

    // ⭐ 기존 진행 상황 백업
    private (List<int> visited, List<int> cleared, int currentNodeId, bool isProgress, bool isClear) BackupProgressData()
    {
        if (_currentData == null)
            return (new List<int>(), new List<int>(), 0, false, false);

        return (
            new List<int>(_currentData.visitedNodes ?? new List<int>()),
            new List<int>(_currentData.clearedNodes ?? new List<int>()),
            _currentData.currentNodeId,
            _currentData.isProgress,
            _currentData.isClear
        );
    }

    // ⭐ 진행 상황 복원
    private void RestoreProgressData((List<int> visited, List<int> cleared, int currentNodeId, bool isProgress, bool isClear) backup)
    {
        if (_currentData == null) return;

        _currentData.visitedNodes = backup.visited;
        _currentData.clearedNodes = backup.cleared;
        _currentData.currentNodeId = backup.currentNodeId;
        _currentData.isProgress = backup.isProgress;
        _currentData.isClear = backup.isClear;

        // 노드별 방문/클리어 상태 복원
        foreach (var nodeData in _currentData.nodes)
        {
            nodeData.isVisited = _currentData.visitedNodes.Contains(nodeData.id);
            nodeData.isCleared = _currentData.clearedNodes.Contains(nodeData.id);
        }
    }

    // ⭐ 시작 노드 설정
    private void SetStartNode()
    {
        if (_currentData == null || _currentData.nodes == null) return;

        var startNode = _currentData.nodes.FirstOrDefault(n => n.nodeType == StageNodeType.Start);
        if (startNode != null && _currentData.currentNodeId == 0)
        {
            _currentData.currentNodeId = startNode.id;
        }
    }
    private async UniTask LoadFromServerAsync()
    {
        // 1. 맵 구조 데이터 로드
        await LoadMapDataFromServerAsync();

        // 2. 사용자 진행 상황 로드
        await LoadProgressDataFromServerAsync();

        SaveToJson();
    }

    private async UniTask LoadMapDataFromServerAsync()
    {
        var bro = Backend.Chart.GetChartContents(MapChartId);

        if (!bro.IsSuccess())
        {
            Debug.LogError($"맵 데이터 서버 요청 실패: {bro.GetStatusCode()}");
            return;
        }

        var rows = bro.FlattenRows();
        int updateCount = 0;
        bool needsMapUpdate = false;

        // 첫 번째 행에서 맵 버전 확인
        if (rows.Count > 0 && rows[0] is JsonData firstRow)
        {
            int.TryParse(firstRow["map_version"].ToString(), out int serverMapVersion);
            
            if (_currentData == null || serverMapVersion > _currentData.dataVersion)
            {
                Debug.Log($"서버 맵 버전 {serverMapVersion}이 로컬 버전 {_currentData?.dataVersion ?? 0}보다 최신입니다. 맵을 업데이트합니다.");
                needsMapUpdate = true;
                
                // 새로운 맵 구조로 초기화
                _currentData = new StageGraphData
                {
                    chapterName = "Chapter1",
                    layerSizes = new List<int> { 1, 2, 3, 4, 3, 2, 1 }, // 서버에서 파싱
                    stages = new List<StageSettings>(),
                    nodes = new List<NodeData>(),
                    edges = new List<EdgeData>(),
                    visitedNodes = new List<int>(),
                    clearedNodes = new List<int>(),
                    dataVersion = serverMapVersion,
                    lastUpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    isProgress = false,
                    isClear = false,
                    currentNodeId = 0
                };
            }
        }

        if (!needsMapUpdate) return;

        // 맵 구조 데이터 파싱
        foreach (var rowObj in rows)
        {
            if (rowObj is not JsonData row) continue;

            int.TryParse(row["node_id"].ToString(), out int nodeId);
            int.TryParse(row["layer"].ToString(), out int layer);
            int.TryParse(row["position_in_layer"].ToString(), out int positionInLayer);
            string nodeTypeStr = row["node_type"].ToString();
            string stageName = row["stage_name"].ToString();
            string resourcePath = row["resource_path"].ToString();
            string connectionsStr = row["connections_to"].ToString();

            // NodeType 변환
            StageNodeType nodeType = nodeTypeStr switch
            {
                "Start" => StageNodeType.Start,
                "End" => StageNodeType.End,
                _ => StageNodeType.Normal
            };

            // 노드 데이터 추가
            var nodeData = new NodeData
            {
                id = nodeId,
                layer = layer,
                positionInLayer = positionInLayer,
                nodeType = nodeType,
                stageAddress = $"Stage_{(char)('A' + nodeId)}",
                isVisited = false,
                isCleared = false
            };
            _currentData.nodes.Add(nodeData);

            // 스테이지 설정 추가 (중복 방지)
            if (!_currentData.stages.Any(s => s.stageName == stageName))
            {
                _currentData.stages.Add(new StageSettings
                {
                    stageName = stageName,
                    resourcePath = resourcePath
                });
            }

            // 간선 데이터 추가
            if (!string.IsNullOrEmpty(connectionsStr))
            {
                var connections = connectionsStr.Split(',');
                foreach (var connection in connections)
                {
                    if (int.TryParse(connection.Trim(), out int toId))
                    {
                        _currentData.edges.Add(new EdgeData
                        {
                            fromId = nodeId,
                            toId = toId
                        });
                    }
                }
            }

            updateCount++;
        }

        Debug.Log($"서버에서 받아온 맵 데이터 {updateCount}개 노드가 갱신됨");
    }

    private async UniTask LoadProgressDataFromServerAsync()
    {
        // 사용자별 진행 상황 데이터 로드
        // 실제로는 Backend.GameData.Get() 등을 사용하여 사용자별 데이터 조회
        
        // 모의 구현 - 실제로는 서버 API 호출
        await UniTask.Delay(500);
        
        // 예시: 서버에서 받은 진행 상황이 로컬보다 최신인 경우만 업데이트
        // 여기서는 간단하게 구현
        Debug.Log("사용자 진행 상황 서버 동기화 완료");
    }

    // ⭐ 수정: 버전 체크 추가된 진행상황 로드 메서드
    private async UniTask LoadProgressFromLocalCSVAsync()
    {
        string csvFilePath = @"C:\Users\skyth\OneDrive\Desktop\project-memo\UserStageProgress.csv";

        if (!File.Exists(csvFilePath))
        {
            Debug.LogWarning($"진행상황 CSV 파일을 찾을 수 없습니다: {csvFilePath}");
            return;
        }

        try
        {
            Debug.Log("로컬 진행상황 CSV 파일에서 데이터 로딩 중...");
            string[] lines = File.ReadAllLines(csvFilePath);

            if (_currentData == null)
            {
                Debug.LogError("맵 데이터가 먼저 로드되어야 합니다.");
                return;
            }

            int progressVersion = 1;
            int currentNodeId = 0;
            bool isProgress = false;
            bool isClear = false;
            string lastUpdateTime = "";

            // 메타데이터 파싱
            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("#"))
                {
                    if (trimmedLine.Contains("current_node_id:"))
                    {
                        // ⭐ 수정: 쉼표 제거 후 파싱
                        string value = trimmedLine.Split(':')[1].Trim().TrimEnd(',');
                        int.TryParse(value, out currentNodeId);
                    }
                    else if (trimmedLine.Contains("is_progress:"))
                    {
                        // ⭐ 수정: 쉼표 제거 후 파싱
                        string value = trimmedLine.Split(':')[1].Trim().TrimEnd(',');
                        bool.TryParse(value, out isProgress);
                    }
                    else if (trimmedLine.Contains("is_clear:"))
                    {
                        // ⭐ 수정: 쉼표 제거 후 파싱
                        string value = trimmedLine.Split(':')[1].Trim().TrimEnd(',');
                        bool.TryParse(value, out isClear);
                    }
                    else if (trimmedLine.Contains("data_version:"))
                    {
                        // ⭐ 수정: 쉼표 제거 후 파싱
                        string value = trimmedLine.Split(':')[1].Trim().TrimEnd(',');
                        int.TryParse(value, out progressVersion);
                        Debug.Log($"CSV에서 파싱된 progressVersion: {progressVersion}"); // 디버그 로그 추가
                    }
                    else if (trimmedLine.Contains("last_update_time:"))
                    {
                        // ⭐ 수정: 쉼표 제거 후 파싱
                        lastUpdateTime = trimmedLine.Split(':')[1].Trim().TrimEnd(',');
                    }
                    continue;
                }
            }

            // ⭐ 버전 체크: CSV 진행상황 버전이 현재 데이터보다 높거나 같을 때만 업데이트
            //CHECKLIST: 추후에 헷갈릴 수 있는 코드 = 서버 버전과 현재 버전 비교
            if (_currentData.dataVersion >= progressVersion)
            {
                Debug.Log($"CSV 진행상황 버전({progressVersion})이 현재 데이터 버전({_currentData.dataVersion})보다 낮습니다. 진행상황 업데이트를 건너뜁니다.");
                return;
            }

            Debug.Log($"CSV 진행상황 버전({progressVersion})으로 현재 진행상황을 업데이트합니다. (기존 버전: {_currentData.dataVersion})");

            // ⭐ 진행상황 초기화 (덮어쓰기 준비)
            _currentData.visitedNodes.Clear();
            _currentData.clearedNodes.Clear();

            // 모든 노드의 진행상황을 초기화
            foreach (var nodeData in _currentData.nodes)
            {
                nodeData.isVisited = false;
                nodeData.isCleared = false;
            }

            // 진행상황 데이터 파싱
            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();

                // 주석이나 헤더 건너뛰기
                if (trimmedLine.StartsWith("#") || trimmedLine.StartsWith("node_id,") || string.IsNullOrEmpty(trimmedLine))
                    continue;

                // 진행상황 데이터 파싱
                await ParseProgressCSVLine(trimmedLine);
            }

            // ⭐ CSV에서 로드된 메타데이터로 덮어씌우기
            _currentData.currentNodeId = currentNodeId;
            _currentData.isProgress = isProgress;
            _currentData.isClear = isClear;

            // 진행상황 버전 업데이트 (맵 버전은 그대로 유지)
            // 단, 진행상황 데이터의 버전이 더 높을 때만
            if (progressVersion > _currentData.dataVersion)
            {
                _currentData.dataVersion = progressVersion;
            }

            // 업데이트 시간 설정
            if (!string.IsNullOrEmpty(lastUpdateTime))
            {
                _currentData.lastUpdateTime = lastUpdateTime;
            }
            else
            {
                _currentData.lastUpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }

            Debug.Log($"진행상황 CSV에서 데이터 로드 완료 - 현재 노드: {currentNodeId}, 방문한 노드: {_currentData.visitedNodes.Count}개, 클리어한 노드: {_currentData.clearedNodes.Count}개");
        }
        catch (Exception e)
        {
            Debug.LogError($"진행상황 CSV 파일 로드 실패: {e.Message}");
        }
    }

    // ⭐ 새로 추가할 메서드
    private async UniTask ParseProgressCSVLine(string line)
    {
        await UniTask.Yield();

        string[] values = line.Split(',');
        if (values.Length < 4) return;

        if (!int.TryParse(values[0].Trim(), out int nodeId)) return;
        bool.TryParse(values[1].Trim(), out bool isVisited);
        bool.TryParse(values[2].Trim(), out bool isCleared);

        // 해당 노드 찾아서 상태 업데이트
        var nodeData = _currentData.nodes.Find(n => n.id == nodeId);
        if (nodeData != null)
        {
            nodeData.isVisited = isVisited;
            nodeData.isCleared = isCleared;
        }

        // 리스트에도 추가
        if (isVisited && !_currentData.visitedNodes.Contains(nodeId))
        {
            _currentData.visitedNodes.Add(nodeId);
        }
        if (isCleared && !_currentData.clearedNodes.Contains(nodeId))
        {
            _currentData.clearedNodes.Add(nodeId);
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