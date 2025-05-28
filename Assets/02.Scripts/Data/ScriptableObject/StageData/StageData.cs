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
        public string bossStageName;

        [System.Serializable]
        public class StageSettings
        {
            public StageName stageName;
            public string resourcePath => stageName.ToString();
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
        Stage_01,
        Stage_02,
        Stage_03,
        Stage_04,
        Stage_05,
        Stage_06,
        Stage_07,
        Stage_08,
        Stage_09,
        Stage_10,
        Stage_11,
        Stage_12,
        Stage_13,
        Stage_14,
        Stage_15,
        Stage_16,
        Stage_17,
        Stage_18,
        Stage_19,
        Stage_20,
        Stage_21,
        Stage_22,
        Stage_23,
        Stage_24,
        Stage_25,
        Stage_26,
        Stage_27,
        Stage_28,
        Stage_29,
        Stage_30,
    }
}
