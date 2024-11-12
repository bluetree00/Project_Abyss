using System.Collections.Generic;
using UnityEngine;

public static class StageEffectInitializer
{
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

        foreach (var chapter in stageData.chapters)
        {
            bossStageName = chapter.bossStageName;

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
