using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "MSTData", menuName = "Stage/MSTData")]
public class MSTData : ScriptableObject
{
    
    [SerializeField]  // SerializeField 속성 추가하여 인스펙터에 표시
    public List<StageSequence> stageSequences;

    [System.Serializable]
    public class StageSequence
    {
        public string startStageName;  // 시작 스테이지 이름

        public StageSequence(string startStageName)
        {
            this.startStageName = startStageName;
        }
    }

    // Graph 데이터를 받아서 stageSequences를 설정하는 메서드
    public void SetGraphData(Dictionary<string, List<string>> mstGraph)
    {
        stageSequences = new List<StageSequence>();

        foreach (var kvp in mstGraph)
        {
            stageSequences.Add(new StageSequence(kvp.Key));
        }
    }
}
