using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class StageManager
{
    private Dictionary<string, GameObject> stageDictionary;   // 스테이지 이름과 오브젝트 매핑
    private Dictionary<string, List<string>> mstGraph;        // MST 결과 그래프
    private Stage currentStage;
    private string bossStageName;
    private MSTData mstData; // MSTData 스크립터블 오브젝트를 저장

    public enum StageType
    {
        MainMenu,
        InGame,
        Event,
        BossBattle
    }

    [Serializable]
    public class Stage
    {
        public string stageName;
        public string resourcePath;
        public StageType stageType;
        public int weight;
    }

    public class ConnectionRestriction
    {
        public string restrictedStage;
        public string requiredStage;
    }

    public StageManager(List<Stage> stages, List<ConnectionRestriction> restrictions, string bossStageName)
    {
        this.bossStageName = bossStageName;
        stageDictionary = new Dictionary<string, GameObject>();
        mstGraph = new Dictionary<string, List<string>>();

        InitializeStages(stages);
        GenerateFilteredMST(stages, restrictions);
        SaveMSTData();
        SetInitialStage();
    }

    private void InitializeStages(List<Stage> stages)
    {
        foreach (var stage in stages)
        {
            if (!stageDictionary.ContainsKey(stage.stageName))
            {
                GameObject stagePrefab = Managers.Resource.Load<GameObject>($"Prefabs/{stage.resourcePath}");
                GameObject stageObject = GameObject.Instantiate(stagePrefab);
                stageObject.name = stage.stageName;
                stageObject.SetActive(false);
                stageDictionary[stage.stageName] = stageObject;
            }
        }
    }

    private void GenerateFilteredMST(List<Stage> stages, List<ConnectionRestriction> restrictions)
    {
        List<Edge> edges = new List<Edge>();

        for (int i = 0; i < stages.Count; i++)
        {
            for (int j = i + 1; j < stages.Count; j++)
            {
                if (IsConnectionRestricted(stages[i].stageName, stages[j].stageName, restrictions))
                    continue;

                int weight = (stages[i].weight + stages[j].weight) / 2;
                edges.Add(new Edge(stages[i], stages[j], weight));
            }
        }

        edges.Sort((a, b) => a.weight.CompareTo(b.weight));
        UnionFind unionFind = new UnionFind(stages.Count);

        foreach (var edge in edges)
        {
            int indexA = stages.IndexOf(edge.start);
            int indexB = stages.IndexOf(edge.end);

            if (unionFind.Union(indexA, indexB))
            {
                if (!mstGraph.ContainsKey(edge.start.stageName))
                    mstGraph[edge.start.stageName] = new List<string>();
                if (!mstGraph.ContainsKey(edge.end.stageName))
                    mstGraph[edge.end.stageName] = new List<string>();

                mstGraph[edge.start.stageName].Add(edge.end.stageName);
                mstGraph[edge.end.stageName].Add(edge.start.stageName);
            }
        }
    }

    private bool IsConnectionRestricted(string stageA, string stageB, List<ConnectionRestriction> restrictions)
    {
        foreach (var restriction in restrictions)
        {
            if ((stageA == restriction.restrictedStage && !stageDictionary.ContainsKey(restriction.requiredStage)) ||
                (stageB == restriction.restrictedStage && !stageDictionary.ContainsKey(restriction.requiredStage)))
            {
                return true;
            }
        }
        return false;
    }

    private void SaveMSTData()
    {
        mstData = ScriptableObject.CreateInstance<MSTData>();
        mstData.SetGraphData(mstGraph);
        AssetDatabase.CreateAsset(mstData, "Assets/Resources/Data/MSTData.asset");
        AssetDatabase.SaveAssets();
    }

    private void SetInitialStage()
    {
        GameObject firstStageObject = stageDictionary.Values.FirstOrDefault();
        if (firstStageObject != null)
        {
            currentStage = new Stage { stageName = firstStageObject.name };
            ActivateStage(currentStage);
        }
        else
        {
            Debug.LogError("No stages found to initialize.");
        }
    }

    private void ActivateStage(Stage stage)
    {
        if (currentStage != null && stageDictionary.ContainsKey(currentStage.stageName))
        {
            GameObject previousStageObject = stageDictionary[currentStage.stageName];
            previousStageObject.SetActive(false);
        }

        if (stageDictionary.ContainsKey(stage.stageName))
        {
            GameObject stageObject = stageDictionary[stage.stageName];
            stageObject.SetActive(true);
            currentStage = stage;
        }
    }

    public void MoveToNextStage(int steps)
    {
        if (currentStage == null)
        {
            Debug.LogError("Current stage is not set.");
            return;
        }

        var allStages = mstData.GetStageSequence(currentStage.stageName);
        int nextIndex = Mathf.Clamp(steps - 1, 0, allStages.Count - 1);
        string nextStageName = allStages[nextIndex];

        if (stageDictionary.TryGetValue(nextStageName, out GameObject nextStageObject))
        {
            Debug.Log($"Moving to next stage: {nextStageName}");
            ActivateStage(new Stage { stageName = nextStageName });
        }
        else
        {
            Debug.LogError($"Stage {nextStageName} not found in stage dictionary.");
        }
    }

    // Union-Find 클래스
    private class UnionFind
    {
        private int[] parent;
        private int[] rank;

        public UnionFind(int size)
        {
            parent = new int[size];
            rank = new int[size];
            for (int i = 0; i < size; i++) parent[i] = i;
        }

        public int Find(int x)
        {
            if (parent[x] != x)
                parent[x] = Find(parent[x]);
            return parent[x];
        }

        public bool Union(int x, int y)
        {
            int rootX = Find(x), rootY = Find(y);
            if (rootX == rootY) return false;

            if (rank[rootX] > rank[rootY])
                parent[rootY] = rootX;
            else if (rank[rootX] < rank[rootY])
                parent[rootY] = rootY;
            else
            {
                parent[rootY] = rootX;
                rank[rootX]++;
            }
            return true;
        }
    }

    // Edge 클래스
    private class Edge
    {
        public Stage start, end;
        public int weight;
        public Edge(Stage start, Stage end, int weight)
        {
            this.start = start;
            this.end = end;
            this.weight = weight;
        }
    }
}
