using System;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using StageSystem;

namespace StageSystem
{
    /// <summary>
    /// 진행 업데이트 + 저장 책임. 기존 StageManager.UpdateProgress & SaveToJSONAsync 이동.
    /// </summary>
    public class StageProgressService : IStageProgressService
    {
        private readonly string _progressFile = "StageProgress.json";

        public UniTask InitializeAsync(StageGraphData data)
        {
            // 초기 방문/클리어 상태 노드에 반영 (progress 파일 로드 후 호출 가정)
            if (data.nodes != null)
            {
                foreach (var node in data.nodes)
                {
                    node.isVisited = data.visitedNodes.Contains(node.id);
                    node.isCleared = data.clearedNodes.Contains(node.id);
                }
            }
            return UniTask.CompletedTask;
        }

        public void UpdateProgress(StageGraphData data, int nodeId, bool isCompleted = false)
        {
            data.currentNodeId = nodeId;
            if (!data.visitedNodes.Contains(nodeId))
                data.visitedNodes.Add(nodeId);

            var nodeData = data.nodes.Find(n => n.id == nodeId);
            if (nodeData != null)
            {
                nodeData.isVisited = true;
                if (isCompleted)
                {
                    nodeData.isCleared = true;
                    if (!data.clearedNodes.Contains(nodeId))
                        data.clearedNodes.Add(nodeId);
                }
            }
            data.isProgress = true;
        }

        public async UniTask SaveAsync(StageGraphData data)
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, _progressFile);
                var progressData = new ProgressDataJSON
                {
                    currentNodeId = data.currentNodeId,
                    visitedNodes = data.visitedNodes,
                    clearedNodes = data.clearedNodes,
                    isProgress = data.isProgress,
                    isClear = data.isClear,
                    dataVersion = data.dataVersion,
                    lastUpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };
                string json = JsonConvert.SerializeObject(progressData, Formatting.Indented);
                File.WriteAllText(path, json);
                await UniTask.Yield();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StageProgressService] Save failed: {ex.Message}");
            }
        }
    }
}
