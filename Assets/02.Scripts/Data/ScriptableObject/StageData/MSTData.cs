using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "MSTData", menuName = "Stage/MSTData")]
public class MSTData : ScriptableObject
{
    // 스테이지 간의 연결을 저장하는 딕셔너리 (그래프 형태)
    private Dictionary<string, List<string>> mstGraph;

    // 초기화 메서드: 외부에서 그래프 데이터를 설정할 때 사용
    public void SetGraphData(Dictionary<string, List<string>> graphData)
    {
        mstGraph = new Dictionary<string, List<string>>(graphData);
    }

    // 현재 스테이지에서 이동 가능한 다음 스테이지들을 가져옴
    public List<string> GetNextStages(string currentStageName)
    {
        if (mstGraph != null && mstGraph.ContainsKey(currentStageName))
        {
            return mstGraph[currentStageName];
        }

        Debug.LogError($"Stage '{currentStageName}' not found in MSTData.");
        return new List<string>();
    }

    // MST 기반의 스테이지 순서 목록을 반환
    public List<string> GetStageSequence(string startStageName)
    {
        if (mstGraph == null || !mstGraph.ContainsKey(startStageName))
        {
            Debug.LogError($"Start stage '{startStageName}' not found in MSTData.");
            return new List<string>();
        }

        List<string> sequence = new List<string>();
        HashSet<string> visited = new HashSet<string>();
        Queue<string> queue = new Queue<string>();
        
        queue.Enqueue(startStageName);
        visited.Add(startStageName);

        while (queue.Count > 0)
        {
            string currentStage = queue.Dequeue();
            sequence.Add(currentStage);

            if (mstGraph.ContainsKey(currentStage))
            {
                foreach (var nextStage in mstGraph[currentStage])
                {
                    if (!visited.Contains(nextStage))
                    {
                        queue.Enqueue(nextStage);
                        visited.Add(nextStage);
                    }
                }
            }
        }

        return sequence;
    }

    // 그래프 데이터를 디버깅용으로 출력
    public void PrintGraphData()
    {
        Debug.Log("MST Graph Data:");
        foreach (var key in mstGraph.Keys)
        {
            string connections = string.Join(", ", mstGraph[key]);
            Debug.Log($"{key}: [{connections}]");
        }
    }
}
