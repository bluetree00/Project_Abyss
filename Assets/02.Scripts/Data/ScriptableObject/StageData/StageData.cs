using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewChapterData", menuName = "Stage/ChapterData")]
public class StageData : ScriptableObject
{
    public List<ChapterData> chapters;
    public enum ChapterName
    {
        Chapter1,
        Chapter2,
        Chapter3,
        Chapter4,
        Chapter5,
        Chapter6,
        Chapter7,
        Chapter8,
        Chapter9,
        Chapter10
    }

    [System.Serializable]
    public class ChapterData
    {
        public ChapterName chapterName;
        public List<StageSettings> stages;
        public List<StageConnectionRestriction> connectionRestrictions;
        public string bossStageName;

        [System.Serializable]
        public class StageSettings
        {
            public string stageName;
            public string resourcePath;
            public StageManager.StageType stageType;
            public int weight;
            public bool randomWeight;

            //CHECKLIST : 랜덤 가중치 설정 시 최소, 최대 가중치 설정 필요
            // public int minWeight = 1;
            // public int maxWeight = 20;
        }

        [System.Serializable]
        public class StageConnectionRestriction
        {
            public string restrictedStage;
            public string requiredStage;
        }
    }
}
