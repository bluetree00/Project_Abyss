using System.Collections.Generic;
using UnityEngine;

public static class StageEffectInitializer
{
    // 챕터 데이터를 받아오는 함수
    public static List<StageManager.Stage> GetInitialStagesForChapter(string chapterDataName, out List<StageManager.ConnectionRestriction> restrictions, out string bossStageName)
    {
        StageData stageData = Resources.Load<StageData>($"Data/{chapterDataName}");

        if (stageData == null)
        {
            Debug.LogError($"StageData with name {chapterDataName} not found.");
            restrictions = new List<StageManager.ConnectionRestriction>();
            bossStageName = string.Empty;
            return new List<StageManager.Stage>();
        }

        List<StageManager.Stage> stages = new List<StageManager.Stage>();
        restrictions = new List<StageManager.ConnectionRestriction>();
        bossStageName = string.Empty;

        // 챕터마다 스테이지 로드
        foreach (var chapter in stageData.chapters)
        {
            bossStageName = chapter.bossStageName; // 보스 스테이지 설정

            // 스테이지 데이터 처리
            foreach (var stageSetting in chapter.stages)
            {
                int weight = stageSetting.randomWeight
                    ? Random.Range(stageSetting.minWeight, stageSetting.maxWeight + 1)
                    : stageSetting.weight;

                stages.Add(new StageManager.Stage
                {
                    stageName = stageSetting.stageName,
                    resourcePath = stageSetting.resourcePath,
                    stageType = stageSetting.stageType,
                    weight = weight
                });
            }

            // 연결 제약 추가
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
