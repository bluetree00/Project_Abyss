using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class StageManager
{
    private List<Chapter> chapters; // 챕터 목록
    private Dictionary<string, GameObject> stageDictionary;
    private Dictionary<string, Portal> portals;  // 포탈을 저장하는 딕셔너리
    private string bossStageName;  // 보스 스테이지 이름 추가
    private Stage currentStage;  // 현재 진행 중인 스테이지를 추적

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

    public class Portal
    {
        public string portalName;
        public string currentStage;
        public List<string> connectedStages;
    }

    // 챕터 관리 클래스
    [Serializable]
    public class Chapter
    {
        public string chapterName;
        public List<Stage> stages;
        public List<ConnectionRestriction> connectionRestrictions;
        public string bossStageName;
    }

    public StageManager(List<Chapter> chapters, Dictionary<string, Portal> portals)
    {
        this.chapters = chapters;
        this.portals = portals;
        stageDictionary = new Dictionary<string, GameObject>();

        foreach (var chapter in chapters)
        {
            foreach (var stage in chapter.stages)
            {
                InitializeStage(stage);
            }
        }

        // 첫 번째 스테이지 설정
        SetInitialStage();

        // 챕터별로 스테이지를 연결하는 로직을 작성
        GenerateFilteredMST();
    }

    private void InitializeStage(Stage stage)
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

    private void GenerateFilteredMST()
    {
        List<Edge> edges = new List<Edge>();

        // 각 챕터의 스테이지끼리 연결할 수 있도록 엣지 생성
        foreach (var chapter in chapters)
        {
            for (int i = 0; i < chapter.stages.Count; i++)
            {
                for (int j = i + 1; j < chapter.stages.Count; j++)
                {
                    if (IsConnectionRestricted(chapter.stages[i].stageName, chapter.stages[j].stageName))
                        continue;

                    int weight = (chapter.stages[i].weight + chapter.stages[j].weight) / 2;
                    edges.Add(new Edge(chapter.stages[i], chapter.stages[j], weight));
                }
            }
        }

        edges.Sort((a, b) => a.weight.CompareTo(b.weight));
        UnionFind unionFind = new UnionFind(chapters.Count);

        foreach (var edge in edges)
        {
            int indexA = chapters.SelectMany(c => c.stages).ToList().IndexOf(edge.start);
            int indexB = chapters.SelectMany(c => c.stages).ToList().IndexOf(edge.end);

            if (unionFind.Union(indexA, indexB))
            {
                Debug.Log($"Connecting {edge.start.stageName} <-> {edge.end.stageName} with weight {edge.weight}");
            }
        }
    }

    private bool IsConnectionRestricted(string stageA, string stageB)
    {
        foreach (var chapter in chapters)
        {
            foreach (var restriction in chapter.connectionRestrictions)
            {
                if ((stageA == restriction.restrictedStage && !stageDictionary.ContainsKey(restriction.requiredStage)) ||
                    (stageB == restriction.restrictedStage && !stageDictionary.ContainsKey(restriction.requiredStage)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // 첫 번째 스테이지를 설정하는 메서드
    private void SetInitialStage()
    {
        // 첫 번째 스테이지를 첫 번째 챕터의 첫 번째 스테이지로 설정
        Stage firstStage = chapters.SelectMany(c => c.stages).FirstOrDefault();
        
        if (firstStage != null)
        {
            currentStage = firstStage;
            ActivateStage(firstStage);
            Debug.Log($"Initial stage: {firstStage.stageName} activated.");
        }
        else
        {
            Debug.LogError("No stages found to initialize.");
        }
    }

    // 스테이지를 활성화하는 메서드
    private void ActivateStage(Stage stage)
    {
        if (currentStage != null && stageDictionary.ContainsKey(currentStage.stageName))
        {
            // 이전 스테이지 비활성화
            GameObject previousStageObject = stageDictionary[currentStage.stageName];
            previousStageObject.SetActive(false);
        }

        if (stageDictionary.ContainsKey(stage.stageName))
        {
            GameObject stageObject = stageDictionary[stage.stageName];
            stageObject.SetActive(true);
        }
    }

    // 포탈을 통해 이동할 다음 스테이지를 선택하는 함수
    public void MoveToNextStage(string portalName)
    {
        if (!portals.ContainsKey(portalName)) 
        {
            Debug.LogError($"Portal {portalName} not found.");
            return;
        }

        Portal portal = portals[portalName];
        List<Stage> connectedStages = new List<Stage>();

        // 포탈에 연결된 스테이지 목록 가져오기
        foreach (var stageName in portal.connectedStages)
        {
            Stage stage = chapters.SelectMany(c => c.stages).FirstOrDefault(s => s.stageName == stageName);
            if (stage != null)
                connectedStages.Add(stage);
        }

        // 연결된 스테이지 중 가중치가 가장 낮은 스테이지로 이동
        Stage nextStage = connectedStages.OrderBy(s => s.weight).FirstOrDefault();
        if (nextStage != null)
        {
            Debug.Log($"Moving to next stage: {nextStage.stageName}");
            currentStage = nextStage;  // 현재 스테이지 업데이트
            ActivateStage(nextStage);  // 새로운 스테이지 활성화
        }
        else
        {
            Debug.LogError("No valid connected stages found.");
        }
    }

    private class UnionFind
    {
        private int[] parent;
        private int[] rank;

        public UnionFind(int size)
        {
            parent = new int[size];
            rank = new int[size];
            for (int i = 0; i < size; i++)
                parent[i] = i;
        }

        public int Find(int x)
        {
            if (parent[x] != x)
                parent[x] = Find(parent[x]); // 경로 압축
            return parent[x];
        }

        public bool Union(int x, int y)
        {
            int rootX = Find(x), rootY = Find(y);
            if (rootX == rootY) return false;

            // 랭크 최적화
            if (rank[rootX] > rank[rootY])
                parent[rootY] = rootX;
            else if (rank[rootX] < rank[rootY])
                parent[rootX] = rootY;
            else
            {
                parent[rootY] = rootX;
                rank[rootX]++;
            }
            return true;
        }
    }

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
