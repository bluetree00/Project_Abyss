using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewChapterData", menuName = "Stage/ChapterData")]
public class StageData : ScriptableObject
{
    public List<ChapterData> chapters;
    [System.Serializable]
    public class ChapterData
    {
        public ChapterName chapterName;
        public int[] layerSizes;
        public List<StageSettings> stages;
        //public List<StageConnectionRestriction> connectionRestrictions;
        public string bossStageName;

        [System.Serializable]
        public class StageSettings
        {
            public StageName stageName;
            public string resourcePath;
            public int weight;
            public bool randomWeight;

            //CHECKLIST : 랜덤 가중치 설정 시 최소, 최대 가중치 설정 필요
            // public int minWeight = 1;
            // public int maxWeight = 20;
        }

        // [System.Serializable]
        // public class StageConnectionRestriction
        // {
        //     public string restrictedStage;
        //     public string requiredStage;
        // }
    }

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

    public enum StageName
    {
        Stage1,
        Stage2,
        Stage3,
        Stage4,
        Stage5,
        Stage6,
        Stage7,
        Stage8,
        Stage9,
        Stage10,
        Stage11,
        Stage12,
        Stage13,
        Stage14,
        Stage15,
        Stage16,
        Stage17,
        Stage18,
        Stage19,
        Stage20,
        Stage21,
        Stage22,
        Stage23,
        Stage24,
        Stage25,
        Stage26,
        Stage27,
        Stage28,
        Stage29,
        Stage30,
    }
}
