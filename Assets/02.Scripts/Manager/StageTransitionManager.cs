using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

public class StageTransitionManager
{
    private string currentChapterName; // 현재 챕터 이름
    private int currentStageIndex;     // 현재 스테이지 인덱스
    private List<StageData.ChapterName> chapterSequence; // 챕터 순서 리스트

    public StageTransitionManager()
    {
        // 초기화 스테이지가 추가되면 리스트 챕터를 추가
        //chapterSequence = new List<string> { "Chapter1", "Chapter2", "Chapter3" };
        //currentChapterName = chapterSequence[0]; // 첫 번째 챕터부터 시작

        chapterSequence = new List<StageData.ChapterName> { StageData.ChapterName.Chapter1, StageData.ChapterName.Chapter2, StageData.ChapterName.Chapter3 };
        currentChapterName = chapterSequence[0].ToString(); // 첫 번째 챕터부터 시작
        currentStageIndex = 0;
    }

    // 챕터를 로드하는 메서드
    public async void LoadChapter(string chapterName)
    {
        // 현재 챕터 정리 코드 추가 필요
        //Managers.Stage.CleanupChapter();

        //currentChapterName = chapterName;

        // 현재 챕터의 스테이지 데이터 로드
        List<StageManager.Stage> stages;
        List<StageManager.ConnectionRestriction> restrictions;
        string bossStageName;

        // stages = StageEffectInitializer.GetInitialStagesForChapter(
        //     chapterName, out restrictions, out bossStageName);

        (stages, restrictions, bossStageName) = await StageEffectInitializer.GetInitialStagesForChapterAsync(chapterName);

        if (stages == null || stages.Count == 0)
        {
            Debug.LogError($"Failed to load stages for chapter {chapterName}");
            return;
        }

        // StageManager 초기화
        Managers.Instance._stageManager = new StageManager(stages, restrictions, bossStageName, 1);

        // 첫 번째 스테이지부터 시작
        // Managers.Stage.MoveToNextStage(0); // 첫 번째 스테이지 시작
        // currentStageIndex = 0;
        Managers.Stage.SetInitialStage(); // 첫 번째 스테이지 시작
        currentStageIndex = 0; // 첫 번째 스테이지 인덱스 설정
        Debug.Log($"Loaded chapter: {chapterName}");
    }

    // TODO : 보스 스테이지 클리어 후 다음 챕터 로드 로직 수정 필요
    // 보스 스테이지 클리어 후 다음 챕터 로드
    // public void OnBossStageCleared()
    // {
    //     int currentChapterIndex = chapterSequence.IndexOf(currentChapterName);
    //     if (currentChapterIndex + 1 < chapterSequence.Count)
    //     {
    //         string nextChapterName = chapterSequence[currentChapterIndex + 1];
    //         LoadChapter(nextChapterName);
    //     }
    //     else
    //     {
    //         Debug.Log("All chapters completed!");
    //         // 게임 종료 혹은 다른 로직
    //     }
    // }
}
