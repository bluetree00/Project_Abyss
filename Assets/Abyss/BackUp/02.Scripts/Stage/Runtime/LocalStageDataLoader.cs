using System;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using StageSystem;

namespace StageSystem
{
    /// <summary>
    /// 로컬 persistentDataPath 기반 구현.
    /// </summary>
    public class LocalStageDataLoader : IStageDataLoader
    {
        private readonly string _mapFile = "StageMapStruct.json";
        private readonly string _progressFile = "StageProgress.json";

        public async UniTask<StageGraphData> LoadMapStructAsync()
        {
            string path = Path.Combine(Application.persistentDataPath, _mapFile);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[LocalStageDataLoader] Map file missing. Creating default.");
                return CreateDefault();
            }
            try
            {
                var json = File.ReadAllText(path);
                var mapData = JsonConvert.DeserializeObject<MapDataJSON>(json);
                var data = new StageGraphData
                {
                    chapterName = mapData.chapterName,
                    layerSizes = mapData.layerSizes,
                    stages = mapData.stages?.ConvertAll(s => new StageSettings { stageName = s.stageName, resourcePath = s.resourcePath }) ?? new System.Collections.Generic.List<StageSettings>(),
                    nodes = mapData.nodes?.ConvertAll(n => new NodeData
                    {
                        id = n.nodeId,
                        layer = n.layer,
                        positionInLayer = (int)n.positionX,
                        nodeType = (StageNodeType)Enum.Parse(typeof(StageNodeType), n.nodeType),
                        stageAddress = $"Stage_{n.nodeId}",
                        isVisited = false,
                        isCleared = false
                    }) ?? new System.Collections.Generic.List<NodeData>(),
                    edges = mapData.edges?.ConvertAll(e => new EdgeData { fromId = e.fromId, toId = e.toId }) ?? new System.Collections.Generic.List<EdgeData>(),
                    dataVersion = mapData.dataVersion,
                };
                await UniTask.Yield();
                return data;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalStageDataLoader] Failed to load map: {ex.Message}");
                return CreateDefault();
            }
        }

        public async UniTask<ProgressDataJSON> LoadProgressAsync()
        {
            string path = Path.Combine(Application.persistentDataPath, _progressFile);
            if (!File.Exists(path))
            {
                await UniTask.Yield();
                return null; // 새 게임으로 간주
            }
            try
            {
                var json = File.ReadAllText(path);
                var progress = JsonConvert.DeserializeObject<ProgressDataJSON>(json);
                await UniTask.Yield();
                return progress;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalStageDataLoader] Failed to load progress: {ex.Message}");
                return null;
            }
        }

        private StageGraphData CreateDefault()
        {
            return new StageGraphData
            {
                chapterName = "Chapter1",
                layerSizes = new System.Collections.Generic.List<int> { 1,2,3,4,3,2,1 },
                stages = new System.Collections.Generic.List<StageSettings>(),
                nodes = new System.Collections.Generic.List<NodeData>(),
                edges = new System.Collections.Generic.List<EdgeData>(),
                currentNodeId = 0,
                visitedNodes = new System.Collections.Generic.List<int>(),
                clearedNodes = new System.Collections.Generic.List<int>(),
                isProgress = false,
                isClear = false,
                dataVersion = 1,
                lastUpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }
    }
}
