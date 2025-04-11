using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "EventStageData_", menuName = "Stage/EventStageData")]
public class EventStageData : ScriptableObject
{
    public List<EventStage> eventStages;
    public enum EventStageType
    {
        Money,
        Item,
        Monster,
        GangHwa,
    }

    [System.Serializable]
    public class EventStage
    {
        public EventStageType eventstageType; // 이벤트 스테이지 이름
        public List<EventStageSettings> eventStages; // 이벤트 스테이지 설정

        [System.Serializable]
        public class EventStageSettings
        {
            public string addressableKey; // 프리팹 경로
            public StageManager.StageType stageType; // 스테이지 타입
            public int triggerCondition; // 이벤트 스테이지를 활성화하는 조건
        }
        
    }
}