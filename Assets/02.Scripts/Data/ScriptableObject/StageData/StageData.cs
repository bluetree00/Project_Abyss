using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewChapterData", menuName = "Stage/ChapterData")]
public class StageData : ScriptableObject
{
    // 각 챕터에 포함된 스테이지 데이터를 관리합니다.
    public List<ChapterData> chapters;

    [System.Serializable]
    public class ChapterData
    {
        [Tooltip("The name of the chapter")]
        public string chapterName; // 챕터 이름

        [Tooltip("List of stages in this chapter")]
        public List<StageSettings> stages; // 해당 챕터에 포함된 스테이지 리스트

        [Tooltip("Connection restrictions between stages")]
        public List<StageConnectionRestriction> connectionRestrictions; // 스테이지 간 연결 제약

        [Tooltip("Boss stage in this chapter")]
        public string bossStageName; // 보스 스테이지 이름

        [System.Serializable]
        public class StageSettings
        {
            [Tooltip("Stage name")]
            public string stageName; // 스테이지 이름

            [Tooltip("Stage resource path")]
            public string resourcePath; // 리소스 경로

            [Tooltip("Stage type (MainMenu, InGame, Event, BossBattle)")]
            public StageManager.StageType stageType; // 스테이지 타입

            [Tooltip("Weight for the stage")]
            public int weight; // 가중치

            [Tooltip("Whether the stage weight is randomized")]
            public bool randomWeight; // 가중치 랜덤화 여부

            [Tooltip("Min weight (if randomWeight is true)")]
            public int minWeight = 1; // 랜덤 가중치 최소값

            [Tooltip("Max weight (if randomWeight is true)")]
            public int maxWeight = 10; // 랜덤 가중치 최대값
        }

        [System.Serializable]
        public class StageConnectionRestriction
        {
            [Tooltip("The restricted stage name")]
            public string restrictedStage; // 제한된 스테이지 이름

            [Tooltip("The required stage that must be visited before the restricted stage")]
            public string requiredStage; // 제한된 스테이지가 연결되기 전에 방문해야 할 스테이지 이름
        }
    }
}
