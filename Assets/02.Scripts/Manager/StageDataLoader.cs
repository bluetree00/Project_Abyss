using System.Collections.Generic;
using UnityEngine;

public static class StageDataLoader
{
    public static List<StageData> LoadStageData()
    {
        List<StageData> stageList = new List<StageData>();

        // 예시 스테이지 데이터 추가 (ScriptableObject나 JSON으로 대체 가능)
        stageList.Add(new StageData { stageName = "Stage1", IsShop = false, IsBoss = false, appearanceProbability = 70 });
        stageList.Add(new StageData { stageName = "Stage2", IsShop = true, IsBoss = false, appearanceProbability = 20 });
        stageList.Add(new StageData { stageName = "BossStage", IsShop = false, IsBoss = true, appearanceProbability = 10 });

        return stageList;
    }
}
