using System.Collections.Generic;
using UnityEngine;

public static class StageEffectInitializer
{
    public static List<StageManager.Stage> GetInitialStagesForChapter(
        string chapterDataName, 
        out List<StageManager.ConnectionRestriction> restrictions, 
        out string bossStageName)
    {
        // StageData 로드
        StageData stageData = Resources.Load<StageData>($"Data/{chapterDataName}");

        if (stageData == null)
        {
            Debug.LogError($"StageData with name {chapterDataName} not found.");
            restrictions = new List<StageManager.ConnectionRestriction>();
            bossStageName = string.Empty;
            return new List<StageManager.Stage>();
        }

        // 결과 목록 초기화
        List<StageManager.Stage> stages = new List<StageManager.Stage>();
        restrictions = new List<StageManager.ConnectionRestriction>();
        bossStageName = string.Empty;

        // Chapter별로 데이터 초기화
        foreach (var chapter in stageData.chapters)
        {
            bossStageName = chapter.bossStageName;

            // 각 스테이지의 설정을 처리
            foreach (var stageSetting in chapter.stages)
            {
                // UnityEngine.Random 사용하여 랜덤으로 가중치를 설정
                int weight = stageSetting.randomWeight
                    ? UnityEngine.Random.Range(stageSetting.minWeight, stageSetting.maxWeight + 1)
                    : stageSetting.weight;

                stages.Add(new StageManager.Stage
                {
                    stageName = stageSetting.stageName,
                    resourcePath = stageSetting.resourcePath,
                    stageType = stageSetting.stageType,
                    weight = weight
                });
            }

            // 연결 제한 사항 처리
            foreach (var restriction in chapter.connectionRestrictions)
            {
                restrictions.Add(new StageManager.ConnectionRestriction
                {
                    restrictedStage = restriction.restrictedStage,
                    requiredStage = restriction.requiredStage
                });
            }
        }

        return stages;
    }
}
