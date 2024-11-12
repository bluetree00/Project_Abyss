using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewChapterData", menuName = "Stage/ChapterData")]
public class StageData : ScriptableObject
{
    public List<ChapterData> chapters;

    [System.Serializable]
    public class ChapterData
    {
        public string chapterName;
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
            public int minWeight = 1;
            public int maxWeight = 10;
        }

        [System.Serializable]
        public class StageConnectionRestriction
        {
            public string restrictedStage;
            public string requiredStage;
        }
    }
}
