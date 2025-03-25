using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Linq;
using UnityEngine.AddressableAssets; //[NEW]
using UnityEngine.ResourceManagement.AsyncOperations; //[NEW]

//Managers.Stage.MoveToNextStage(1); 스테이지 넘기기
//Managers.StageTransitionManager.LoadChapter("Chapter1"); 챕터 로드 하기
//Managers.Stage.CleanupChapter(); 현재 챕터 지우기

public class StageManager
{
     private string currentChapterName; // 현재 챕터 이름
     
    private Dictionary<string, GameObject> stageDictionary;   // 스테이지 이름과 오브젝트 매핑
    private Dictionary<string, List<string>> mstGraph;        // MST 결과 그래프
    private Stage currentStage;
    private MSTData mstData; // MSTData 스크립터블 오브젝트를 저장
    private Dictionary<string, GameObject> stagePrefabs; // 캐시된 스테이지 프리팹
    private bool isStageMoving = false;
    // private List<string> usedStages = new List<string>(); //[NEW] 사용된 스테이지 목록
    private List<Stage> eventStages = new List<Stage>(); // 이벤트 스테이지 목록
    private Dictionary<string, int> stageUsageDictionary = new Dictionary<string, int>(); // 스테이지 사용 여부 딕셔너리
    private int stageSteps = 0; // 스테이지 이동 횟수
    public int eventStageThreshold = 5; // 이벤트 스테이지로 진입하기 위한 진행 횟수
    //TODO: 챕터가 넘어갈 때마다 스테이지 이동 횟수 상한 증가 필요 (챕터 1은 5번 진행, 챕터 2는 6번 진행 등)

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

    public StageManager(List<Stage> stages, List<ConnectionRestriction> restrictions, string bossStageName, int chapterNumber)
    {
        stageDictionary = new Dictionary<string, GameObject>();
        mstGraph = new Dictionary<string, List<string>>();
        stagePrefabs = new Dictionary<string, GameObject>(); // 초기화

        //int numberOfStages = 5 + (chapterNumber - 1); // 챕터가 증가할수록 스테이지 갯수 증가
        //List<Stage> selectedStages = stages.OrderBy(x => Guid.NewGuid()).Take(numberOfStages).ToList(); // 랜덤으로 스테이지 선택

        // 이벤트 스테이지를 포함하여 시퀀스 구성
        eventStages = stages.Where(s => s.stageType == StageType.Event).ToList();
        stages.AddRange(eventStages);

        // 스테이지 사용 여부 딕셔너리 초기화
        foreach (var stage in stages)
        {
            if (stage.stageType == StageType.Event)
            {
                stageUsageDictionary[stage.stageName] = 3; // 이벤트 스테이지는 3으로 초기화
            }
            else if (stage.stageType == StageType.InGame)
            {
                stageUsageDictionary[stage.stageName] = 0; // 일반 스테이지는 0으로 초기화
            }
        }

        // 초기화된 딕셔너리의 모든 키와 밸류 값을 디버그 창에 표시
        Debug.Log("<color=gray>Stage Usage Dictionary Initialized:</color>");
        foreach (var kvp in stageUsageDictionary)
        {
            Debug.Log($"<color=gray>Stage: {kvp.Key}, Usage: {kvp.Value}</color>");
        }

        InitializeStages(stages);
        GenerateFilteredMST(stages, restrictions);
        SaveMSTData();
        SetInitialStage();

        Managers.GameEvent.portal   += MoveToNextStage; // 이벤트 추가
    }

    private void InitializeStages(List<Stage> stages)
    {
        // 챕터 이름에 해당하는 부모 오브젝트 생성
        string chapterParentName = $"Chapter_{currentChapterName}_Parent";
        GameObject chapterParent = GameObject.Find(chapterParentName);

        if (chapterParent == null)
        {
            chapterParent = new GameObject(chapterParentName);
            Debug.Log($"Created chapter parent object: {chapterParentName}");
        }

        foreach (var stage in stages)
        {
            if (!stageDictionary.ContainsKey(stage.stageName))
            {
                // 프리팹 캐시 사용
                if (!stagePrefabs.ContainsKey(stage.resourcePath))
                {
                    // GameObject stagePrefab = Managers.Resource.Load<GameObject>($"Prefabs/{stage.resourcePath}");
                    // if (stagePrefab == null)
                    // {
                    //     Debug.LogError($"Prefab not found: {stage.resourcePath}");
                    //     continue;
                    // }
                    // stagePrefabs[stage.resourcePath] = stagePrefab; // 캐싱
                    Addressables.LoadAssetAsync<GameObject>(stage.resourcePath).Completed += (handle) =>
                    {
                        if (handle.Status == AsyncOperationStatus.Succeeded)
                        {
                            stagePrefabs[stage.resourcePath] = handle.Result;
                        }
                        else
                        {
                            Debug.LogError($"Prefab not found: {stage.resourcePath}");
                        }
                    };
                }

                // 스테이지 오브젝트 생성 및 부모 설정
                //GameObject stageObject = GameObject.Instantiate(stagePrefabs[stage.resourcePath], chapterParent.transform);
                AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(stage.resourcePath);
                handle.WaitForCompletion();
                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    GameObject stageObject = handle.Result;
                    stageObject.transform.SetParent(chapterParent.transform);
                    stageObject.name = stage.stageName;
                    stageObject.SetActive(false);
                    stageDictionary[stage.stageName] = stageObject;
                }
                else
                {
                    Debug.LogError($"Failed to instantiate stage: {stage.resourcePath}");
                }
                // stageObject.name = stage.stageName;
                // stageObject.SetActive(false);
                // stageDictionary[stage.stageName] = stageObject;
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

                int minWeight = Mathf.Min(stages[i].weight, stages[j].weight); 
                int maxWeight = Mathf.Max(stages[i].weight, stages[j].weight);

                int weight = UnityEngine.Random.Range(minWeight, maxWeight + 1);

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
#if UNITY_EDITOR
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
#endif
    }

    public void SetInitialStage()
    {
        GameObject firstStageObject = stageDictionary.Values.FirstOrDefault();
        if (firstStageObject != null)
        {
            currentStage = new Stage { stageName = firstStageObject.name };
            ActivateStage(currentStage);
            stageUsageDictionary[currentStage.stageName] = 1; // 첫 스테이지 사용됨
            Debug.Log($"<color=gray>Stage: {currentStage.stageName}, Usage: {stageUsageDictionary[currentStage.stageName]}</color>"); // 로그 출력
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

        else // 모든 상황의 예외가 발생했을 때 예외 처리 디버그
        {
            Debug.LogError($"Stage {stage.stageName} not found in stage dictionary.");
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

        //TODO: 보스 스테이지를 따로 관리할지 한 시퀀스에서 관리할 지 고려해야함.
        // if (stageSteps >= 6) // 스테이지 이동 횟수가 6회 이상인 경우, 보스 스테이지로 이동
        // {
        //     MoveToBossStage();
        //     return;
        // }

        // stageSequences에서 순차적으로 이동
        var stageSequence = mstData.stageSequences;

        if (stageSequence == null || stageSequence.Count == 0)
        {
            Debug.LogError("No stage sequences found in MSTData.");
            return;
        }

        // 모든 스테이지가 사용되었는지 확인
        //List<string> stageNames = stageSequence.Select(s => s.startStageName).ToList();
        bool allUsedStagesIncluded = stageUsageDictionary.Values.All(value => value == 1 || value == 4);
    
        if (allUsedStagesIncluded)
        {
            Debug.Log("<color=gray> 모든 스테이지가 사용되었습니다. </color>");
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
        int nextIndex = currentIndex + steps;
        // nextIndex가 stageSequences의 범위보다 커지면 그 값만큼 빼줌
        if (nextIndex >= stageSequence.Count)
        {
            nextIndex -= stageSequence.Count;
        }

        //TODO : 사용된 스테이지 제외 방법 다시 고려해야함
        // 사용된 스테이지는 제외
        HashSet<int> usedValues = new HashSet<int> { 1, 3, 4 }; // 사용된 스테이지 값 집합
        int loopCount = 0; // 무한 루프 방지를 위한 카운터
        while (nextIndex < stageSequence.Count && usedValues.Contains(stageUsageDictionary[stageSequence[nextIndex].startStageName]))
        {
            nextIndex++;
            if (nextIndex >= stageSequence.Count)
            {
                nextIndex -= stageSequence.Count;
            }
            break;
            // loopCount++;
            // if (loopCount > stageSequence.Count)
            // {
            //     Debug.LogWarning("다음 스테이지 진행 중 무한 루프 감지.");
            //     loopCount = 0;
            //     foreach (var key in stageUsageDictionary.Keys.ToList())
            //     {
            //         if (stageUsageDictionary[key] == 1)
            //         {
            //             stageUsageDictionary[key] = 0; // 일반 스테이지 초기화
            //         }
            //         else if (stageUsageDictionary[key] == 4)
            //         {
            //             stageUsageDictionary[key] = 3; // 이벤트 스테이지 초기화
            //         }
            //     }
            //     break;
            // }
        }

        string nextStageName = stageSequence[nextIndex].startStageName;

        if (stageDictionary.TryGetValue(nextStageName, out GameObject nextStageObject))
        {
            Debug.Log($"Moving to next stage: {nextStageName}");
            ActivateStage(new Stage { stageName = nextStageName });

            // 스테이지 사용 여부 업데이트
            if (stageUsageDictionary[nextStageName] == 0)
            {
                stageUsageDictionary[nextStageName] = 1; // 일반 스테이지 사용됨
                Debug.Log($"<color=gray>Stage: {nextStageName}, Usage: {stageUsageDictionary[nextStageName]}</color>"); // 로그 출력
            }
            else if (stageUsageDictionary[nextStageName] == 3)
            {
                stageUsageDictionary[nextStageName] = 4; // 이벤트 스테이지 사용됨
                Debug.Log($"<color=gray>Stage: {nextStageName}, Usage: {stageUsageDictionary[nextStageName]}</color>"); // 로그 출력
            }

            // 스테이지 진행 횟수 증가
            stageSteps++;
            Debug.Log($"<color=orange> 현재 스테이지 진행 횟수 : {stageSteps} </color>");
            if (stageSteps >= eventStageThreshold)
            {
                // 이벤트 스테이지로 진입
                MoveToEventStage();
            }
        }
        else
        {
            Debug.LogError($"Stage {nextStageName} not found in stage dictionary.");
        }

        isStageMoving = false;
    }

    private void MoveToEventStage()
    {
        if (eventStages.Count == 0)
        {
            Debug.LogWarning("No event stages available.");
            return;
        }

        // 랜덤으로 이벤트 스테이지 선택
        // Stage eventStage = eventStages[UnityEngine.Random.Range(0, eventStages.Count)];
        // if (stageDictionary.TryGetValue(eventStage.stageName, out GameObject eventStageObject))
        // {
        //     Debug.Log($"Moving to event stage: {eventStage.stageName}");
        //     ActivateStage(eventStage);

        //     // 이벤트 스테이지 사용 여부 업데이트
        //     stageUsageDictionary[eventStage.stageName] = 4; // 이벤트 스테이지 사용됨
        // }

        // 시퀀스에서 가장 가까운 이벤트 스테이지 찾기
        int currentIndex = mstData.stageSequences.FindIndex(s => s.startStageName == currentStage.stageName);
        int closestEventIndex = -1;
        int minDistance = int.MaxValue;

        for (int i = 0; i < mstData.stageSequences.Count; i++)
        {
            if (eventStages.Any(e => e.stageName == mstData.stageSequences[i].startStageName))
            {
                int distance = Math.Abs(i - currentIndex);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestEventIndex = i;
                }
            }
        }
    
        if (closestEventIndex != -1)
        {
            string eventStageName = mstData.stageSequences[closestEventIndex].startStageName;
            if (stageDictionary.TryGetValue(eventStageName, out GameObject eventStageObject))
            {
                Debug.Log($"Moving to event stage: {eventStageName}");
                ActivateStage(new Stage { stageName = eventStageName });

                // 이벤트 스테이지 사용 여부 업데이트
                stageUsageDictionary[eventStageName] = 4; // 이벤트 스테이지 사용됨
                Debug.Log($"<color=gray>Stage: {eventStageName}, Usage: {stageUsageDictionary[eventStageName]}</color>"); // 로그 출력
            }
            else
            {
                Debug.LogError($"Event stage {eventStageName} not found in stage dictionary.");
            }
        }
        else
        {
            Debug.LogWarning("No event stages found in the sequence.");
        }
        eventStageThreshold += 3; // 이벤트 스테이지로 진입하기 위한 횟수 증가
    }
    
    // public void MoveToNextStage(int steps)
    // {
    //     if (isStageMoving) return; // 이미 한 번 호출된 경우, 다시 호출하지 않도록 막기
    //     if (currentStage == null)
    //     {
    //         Debug.LogError("Current stage is not set.");
    //         return;
    //     }

    //     // Resources에서 MSTData 로드
    //     if (mstData == null)
    //     {
    //         mstData = Resources.Load<MSTData>("Data/MSTData");
    //         if (mstData == null)
    //         {
    //             Debug.LogError("MSTData not found!");
    //             return;
    //         }
    //     }

    //     // stageSequences에서 순차적으로 이동
    //     var stageSequence = mstData.stageSequences;

    //     if (stageSequence == null || stageSequence.Count == 0)
    //     {
    //         Debug.LogError("No stage sequences found in MSTData.");
    //         return;
    //     }

    //     // stageSequences의 순서대로 이동
    //     int currentIndex = stageSequence.FindIndex(s => s.startStageName == currentStage.stageName);

    //     if (currentIndex == -1)
    //     {
    //         Debug.LogError("Current stage not found in stage sequences.");
    //         return;
    //     }

    //     // nextIndex는 현재 인덱스를 기준으로 다음 스테이지로 이동
    //     int nextIndex = Mathf.Clamp(currentIndex + steps, 0, stageSequence.Count - 1);
    //     string nextStageName = stageSequence[nextIndex].startStageName;

    //     if (stageDictionary.TryGetValue(nextStageName, out GameObject nextStageObject))
    //     {
    //         Debug.Log($"Moving to next stage: {nextStageName}");
    //         ActivateStage(new Stage { stageName = nextStageName });
    //     }
    //     else
    //     {
    //         Debug.LogError($"Stage {nextStageName} not found in stage dictionary.");
    //     }

    //     isStageMoving = false;
    // }

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

    public void CleanupChapter()
    {
        Debug.Log("Cleaning up the current chapter...");

        // 모든 스테이지 오브젝트 비활성화 및 삭제
        foreach (var stageName in stageDictionary.Keys.ToList())
        {
            if (stageDictionary.TryGetValue(stageName, out GameObject stageObject))
            {
                // 스테이지 오브젝트 비활성화
                stageObject.SetActive(false);

                // 메모리에서 제거
                GameObject.Destroy(stageObject);
                stageDictionary.Remove(stageName);
            }
        }

        // 캐시된 프리팹 데이터 정리
        stagePrefabs.Clear();

        // 최소 신장 트리 데이터 초기화
        if (mstGraph != null)
        {
            mstGraph.Clear();
            mstData = null;
        }

        // 현재 스테이지 정보 초기화
        currentStage = null;
        foreach (var key in stageUsageDictionary.Keys.ToList())
        {
            if (stageUsageDictionary[key] == 1)
            {
                stageUsageDictionary[key] = 0; // 일반 스테이지 초기화
            }
            else if (stageUsageDictionary[key] == 4)
            {
                stageUsageDictionary[key] = 3; // 이벤트 스테이지 초기화
            }
        }

        Debug.Log("Chapter cleanup complete. Ready for the next chapter.");
    }

}
