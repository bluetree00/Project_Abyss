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