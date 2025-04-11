using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public static class StageEffectInitializer
{
    /*public static List<StageManager.Stage> GetInitialStagesForChapter(
        string chapterDataName, // <- 챕터 이름(챕터 라벨과 동일)
        out List<StageManager.ConnectionRestriction> restrictions, 
        out string bossStageName,
        Action onComplete = null)
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
        List<StageManager.ConnectionRestriction> localRestrictions = new List<StageManager.ConnectionRestriction>();
        bossStageName = string.Empty;

        // // Chapter별로 데이터 초기화
        // foreach (var chapter in stageData.chapters)
        // {
        //     bossStageName = chapter.bossStageName;

        //     // 각 스테이지의 설정을 처리
        //     foreach (var stageSetting in chapter.stages)
        //     {
        //         // UnityEngine.Random 사용하여 랜덤으로 가중치를 설정
        //         int weight = stageSetting.randomWeight
        //             ? UnityEngine.Random.Range(stageSetting.minWeight, stageSetting.maxWeight + 1)
        //             : stageSetting.weight;

        //         stages.Add(new StageManager.Stage
        //         {
        //             stageName = stageSetting.stageName,
        //             resourcePath = stageSetting.resourcePath,
        //             stageType = stageSetting.stageType,
        //             weight = weight
        //         });
        //     }

        //     // 연결 제한 사항 처리
        //     foreach (var restriction in chapter.connectionRestrictions)
        //     {
        //         restrictions.Add(new StageManager.ConnectionRestriction
        //         {
        //             restrictedStage = restriction.restrictedStage,
        //             requiredStage = restriction.requiredStage
        //         });
        //     }
        // }

        // 어드레서블로 프리팹 로드
        Addressables.LoadAssetsAsync<GameObject>(chapterDataName, null).Completed += (handle) =>
        {
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                // 기존 데이터 초기화 (필요한 경우)
                stageData.chapters[0].stages.Clear();

                // 어드레서블 데이터로 StageData 업데이트
                foreach (var prefab in handle.Result)
                {
                    stageData.chapters[0].stages.Add(new StageData.ChapterData.StageSettings
                    {
                        stageName = prefab.name,
                        resourcePath = prefab.name,
                        stageType = StageManager.StageType.InGame,
                    });

                    // stages 리스트 업데이트
                    stages.Add(new StageManager.Stage
                    {
                        stageName = prefab.name,
                        resourcePath = prefab.name,
                        stageType = StageManager.StageType.InGame,
                    });
                }

                // 연결 제한 사항 업데이트
                foreach (var restriction in stageData.chapters[0].connectionRestrictions)
                {
                    localRestrictions.Add(new StageManager.ConnectionRestriction
                    {
                        restrictedStage = restriction.restrictedStage,
                        requiredStage = restriction.requiredStage
                    });
                }

                Debug.Log($"StageData updated with {handle.Result.Count} prefabs from label {chapterDataName}.");
                onComplete?.Invoke();
            }
            else
            {
                Debug.LogError($"Failed to load assets with label: {chapterDataName}");
            }
        };

        restrictions = localRestrictions;

        return stages;
    }*/

    public static async Task<(List<StageManager.Stage>, List<StageManager.ConnectionRestriction>, string)> GetInitialStagesForChapterAsync(string chapterDataName)
    {
        // StageData 로드
        StageData stageData = Resources.Load<StageData>($"Data/{chapterDataName}");

        if (stageData == null)
        {
            Debug.LogError($"StageData with name {chapterDataName} not found.");
            return (null, null, null);
        }

        // 결과 목록 초기화
        List<StageManager.Stage> stages = new List<StageManager.Stage>();
        List<StageManager.ConnectionRestriction> restrictions = new List<StageManager.ConnectionRestriction>();
        string bossStageName = stageData.chapters[0].bossStageName;

        try
        {
            // 어드레서블로 프리팹 로드
            var handle = Addressables.LoadAssetsAsync<GameObject>(chapterDataName, null);
            var prefabs = await handle.Task;

            // 기존 데이터 초기화
            stageData.chapters[0].stages.Clear();

            // 어드레서블 데이터로 StageData 업데이트 및 stages 리스트 생성
            foreach (var prefab in prefabs)
            {
                stageData.chapters[0].stages.Add(new StageData.ChapterData.StageSettings
                {
                    stageName = prefab.name,
                    resourcePath = prefab.name,
                    stageType = StageManager.StageType.InGame,
                });

                stages.Add(new StageManager.Stage
                {
                    stageName = prefab.name,
                    resourcePath = prefab.name,
                    stageType = StageManager.StageType.InGame,
                });
            }

            // 연결 제한 사항 업데이트
            foreach (var restriction in stageData.chapters[0].connectionRestrictions)
            {
                restrictions.Add(new StageManager.ConnectionRestriction
                {
                    restrictedStage = restriction.restrictedStage,
                    requiredStage = restriction.requiredStage
                });
            }

            Debug.Log($"StageData and stages updated with {prefabs.Count} prefabs from label {chapterDataName}.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to load assets with label: {chapterDataName}. Exception: {ex.Message}");
            return (null, null, null);
        }

        return (stages, restrictions, bossStageName);
    }
}
