using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

public static class StageEffectInitializer
{
    public static async Task<(List<StageManager.Stage>, List<StageManager.ConnectionRestriction>, string)> GetInitialStagesForChapterAsync(string chapterSOName)
    {
        // StageData 로드
        //StageData stageData = Resources.Load<StageData>($"Data/{chapterSOName}");

        string chapterFileName = "Data/" + chapterSOName;

        var handle = Addressables.LoadAssetsAsync<StageData>(chapterFileName, null);
        IList<StageData> stageDataList = await handle.Task;

        // 데이터가 없을 경우 처리
        if (stageDataList == null || stageDataList.Count == 0)
        {
            Debug.LogError($"No StageData found with name {chapterSOName}.");
            return (null, null, null);
        }

        // 첫 번째 StageData 사용
        StageData stageData = stageDataList[0];

        // 결과 목록 초기화
        List<StageManager.Stage> stages = new List<StageManager.Stage>();
        List<StageManager.ConnectionRestriction> restrictions = new List<StageManager.ConnectionRestriction>();
        string bossStageName = stageData.chapters[0].bossStageName;
        string chapterName = stageData.chapters[0].chapterName.ToString();

        try
        {
            // 어드레서블로 프리팹 로드
            var prefabs = await LoadPrefabsAsync(chapterName);

            // StageData와 stages 리스트 업데이트
            UpdateStageDataAndStages(stageData.chapters[0], prefabs, stages);

            // 연결 제한 사항 업데이트
            UpdateConnectionRestrictions(stageData.chapters[0].connectionRestrictions, restrictions);

            Debug.Log($"StageData and stages updated with {prefabs.Count} prefabs from label {chapterSOName}.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to load assets with label: {chapterSOName}. Exception: {ex.Message}");
            return (null, null, null);
        }

        return (stages, restrictions, bossStageName);
    }

    #region 사용 메서드 분리
    /// <summary>
    /// 어드레서블로 프리팹을 비동기 로드하는 메서드 분리
    /// </summary>
    /// <param name="chapterName"></param>
    /// <returns></returns>
    private static async Task<List<GameObject>> LoadPrefabsAsync(string chapterName)
    {
        var handle = Addressables.LoadAssetsAsync<GameObject>(chapterName, null);
        IList<GameObject> prefabList = await handle.Task;

        // Convert IList<GameObject> to List<GameObject>
        return prefabList.ToList();
    }

    /// <summary>
    /// StageData와 stages 리스트를 업데이트하는 메서드
    /// </summary>
    /// <param name="chapterData"></param>
    /// <param name="prefabs"></param>
    /// <param name="stages"></param>
    private static void UpdateStageDataAndStages(
        StageData.ChapterData chapterData,
        List<GameObject> prefabs,
        List<StageManager.Stage> stages)
    {
        // 기존 데이터 초기화 없이 덮어쓰기
        chapterData.stages = new List<StageData.ChapterData.StageSettings>();

        foreach (var prefab in prefabs)
        {
            chapterData.stages.Add(new StageData.ChapterData.StageSettings
            {
                stageName = prefab.name,
                resourcePath = prefab.name,
                stageType = StageManager.StageType.InGame,
                randomWeight = true,
            });

            stages.Add(new StageManager.Stage
            {
                stageName = prefab.name,
                addressableKey = prefab.name,
                stageType = StageManager.StageType.InGame,
                weight = UnityEngine.Random.Range(1, 20), // 랜덤 가중치 설정
            });
        }
    }

    

    /// <summary>
    /// 연결 제한 사항을 업데이트하는 메서드
    /// </summary>
    /// <param name="connectionRestrictions"></param>
    /// <param name="restrictions"></param>
    private static void UpdateConnectionRestrictions(
        List<StageData.ChapterData.StageConnectionRestriction> connectionRestrictions,
        List<StageManager.ConnectionRestriction> restrictions)
    {
        foreach (var restriction in connectionRestrictions)
        {
            restrictions.Add(new StageManager.ConnectionRestriction
            {
                restrictedStage = restriction.restrictedStage,
                requiredStage = restriction.requiredStage
            });
        }
    }

    
    #endregion
}


