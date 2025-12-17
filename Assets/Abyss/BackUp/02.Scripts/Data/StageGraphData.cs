using System.Collections.Generic;
using UnityEngine;
using MapGeneratorManager;
using System.Linq;
using System;

[System.Serializable]
public class StageGraphData
{
    [Header("Chapter Information")]
    public string chapterName;
    public List<int> layerSizes;
    public List<StageSettings> stages;

    [Header("Graph Structure")]
    public List<NodeData> nodes;
    public List<EdgeData> edges;

    [Header("Progress Information")]
    public int currentNodeId;
    public List<int> visitedNodes;
    public List<int> clearedNodes;
    public bool isProgress;
    public bool isClear;

    [Header("Version Control")]
    public int dataVersion = 1;
    public string lastUpdateTime;

    // 👇 생성자에서 명시적으로 초기화
    public StageGraphData()
    {
        chapterName = "Chapter1";
        layerSizes = new List<int>(); // 🔥 여기서만 초기화
        stages = new List<StageSettings>();
        nodes = new List<NodeData>();
        edges = new List<EdgeData>();
        currentNodeId = -1;
        visitedNodes = new List<int>();
        clearedNodes = new List<int>();
        isProgress = false;
        isClear = false;
        dataVersion = 1;
        lastUpdateTime = "";
    }

}

[System.Serializable]
public class NodeData
{
    public int id;
    public int layer;
    public int positionInLayer;
    public StageNodeType nodeType;
    public string stageAddress;
    public bool isVisited;
    public bool isCleared;
}

[System.Serializable]
public class EdgeData
{
    public int fromId;
    public int toId;
}

[System.Serializable]
public class StageSettings
{
    public string stageName;
    public string resourcePath;
}

public enum StageNodeType
{
    Start,
    Normal,
    End,
    Boss,
    Elite,
    Treasure,
    Event
}

// ⭐ JSON 변환을 위한 임시 데이터 구조체들
[System.Serializable]
public class MapDataJSON
{
    public string chapterName;
    public List<int> layerSizes;
    public List<StageSettingsJSON> stages;
    public List<NodeDataJSON> nodes;
    public List<EdgeDataJSON> edges;
    public int dataVersion;
}

[System.Serializable]
public class StageSettingsJSON
{
    public string stageName;
    public string resourcePath;
}

[System.Serializable]
public class NodeDataJSON
{
    public int nodeId;
    public int layer;
    public float positionX;
    public float positionY;
    public string nodeType;
}

[System.Serializable]
public class EdgeDataJSON
{
    public int fromId;
    public int toId;
}

[System.Serializable]
public class ProgressDataJSON
{
    public int currentNodeId;
    public List<int> visitedNodes;
    public List<int> clearedNodes;
    public bool isProgress;
    public bool isClear;
    public int dataVersion;
    public string lastUpdateTime;
}