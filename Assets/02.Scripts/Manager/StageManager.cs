using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Linq;

public class StageManager
{
    private Dictionary<string, GameObject> stageDictionary;   // 스테이지 이름과 오브젝트 매핑
    private Dictionary<string, List<string>> mstGraph;        // MST 결과 그래프
    private Stage currentStage;
    private string bossStageName;
    private MSTData mstData; // MSTData 스크립터블 오브젝트를 저장
    private Dictionary<string, GameObject> stagePrefabs; // 캐시된 스테이지 프리팹
    private bool isStageMoving = false;

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
        stagePrefabs = new Dictionary<string, GameObject>(); // 초기화

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
                // 프리팹 캐시 사용
                if (!stagePrefabs.ContainsKey(stage.resourcePath))
                {
                    GameObject stagePrefab = Managers.Resource.Load<GameObject>($"Prefabs/{stage.resourcePath}");
                    if (stagePrefab == null)
                    {
                        Debug.LogError($"Prefab not found: {stage.resourcePath}");
                        continue;
                    }
                    stagePrefabs[stage.resourcePath] = stagePrefab; // 캐싱
                }

                GameObject stageObject = GameObject.Instantiate(stagePrefabs[stage.resourcePath]);
                stageObject.name = stage.stageName;
                stageObject.SetActive(false);
                stageDictionary[stage.stageName] = stageObject;
            }
        }
    }

    private void GenerateFilteredMST(List<Stage> stages, List<ConnectionRestriction> restrictions)
{
    if (stages == null || stages.Count == 0)
    {
        Debug.LogError("No stages provided for MST generation.");
        return;
    }

    List<Edge> edges = new List<Edge>();

    // 각 스테이지 간의 연결 관계를 고려한 에지 리스트 생성
    for (int i = 0; i < stages.Count; i++)
    {
        for (int j = i + 1; j < stages.Count; j++)
        {
            // 연결 제한을 고려한 필터링
            if (IsConnectionRestricted(stages[i].stageName, stages[j].stageName, restrictions))
                continue;

            // 가중치를 랜덤하게 설정 (예: 1 ~ 10 범위의 랜덤 값)
            int weight = UnityEngine.Random.Range(1, 11);  // 1에서 10까지 랜덤 값으로 가중치 설정

            edges.Add(new Edge(stages[i], stages[j], weight));
        }
    }

    // 에지들을 가중치 기준으로 오름차순 정렬
    edges.Sort((a, b) => a.weight.CompareTo(b.weight));
    
    UnionFind unionFind = new UnionFind(stages.Count);

    // 최소 신장 트리(MST) 생성
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
        // 기존에 동일한 MSTData 자산이 존재하는지 확인
        string assetPath = "Assets/Resources/Data/MSTData.asset";
        MSTData existingData = AssetDatabase.LoadAssetAtPath<MSTData>(assetPath);

        // 만약 기존 자산이 있다면 삭제
        if (existingData != null)
        {
            AssetDatabase.DeleteAsset(assetPath);
            Debug.Log("Existing MSTData asset deleted.");
        }

        // 새로운 MSTData 생성
        mstData = ScriptableObject.CreateInstance<MSTData>();
        mstData.SetGraphData(mstGraph);

        // 새로 생성된 MSTData를 지정된 경로에 저장
        AssetDatabase.CreateAsset(mstData, assetPath);
        AssetDatabase.SaveAssets();
        Debug.Log("New MSTData asset created.");
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
        if (isStageMoving) return; // 이미 한 번 호출된 경우, 다시 호출하지 않도록 막기
        if (currentStage == null)
        {
            Debug.LogError("Current stage is not set.");
            return;
        }

        // Resources에서 MSTData 로드
        if (mstData == null)
        {
            mstData = Resources.Load<MSTData>("Data/MSTData");
            if (mstData == null)
            {
                Debug.LogError("MSTData not found!");
                return;
            }
        }

        // stageSequences에서 순차적으로 이동
        var stageSequence = mstData.stageSequences;

        if (stageSequence == null || stageSequence.Count == 0)
        {
            Debug.LogError("No stage sequences found in MSTData.");
            return;
        }

        // stageSequences의 순서대로 이동
        int currentIndex = stageSequence.FindIndex(s => s.startStageName == currentStage.stageName);

        if (currentIndex == -1)
        {
            Debug.LogError("Current stage not found in stage sequences.");
            return;
        }

        // nextIndex는 현재 인덱스를 기준으로 다음 스테이지로 이동
        int nextIndex = Mathf.Clamp(currentIndex + steps, 0, stageSequence.Count - 1);
        string nextStageName = stageSequence[nextIndex].startStageName;

        if (stageDictionary.TryGetValue(nextStageName, out GameObject nextStageObject))
        {
            Debug.Log($"Moving to next stage: {nextStageName}");
            ActivateStage(new Stage { stageName = nextStageName });
        }
        else
        {
            Debug.LogError($"Stage {nextStageName} not found in stage dictionary.");
        }

        isStageMoving = false;
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
