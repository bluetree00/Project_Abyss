using System.Collections.Generic;
using UnityEngine;

public class StageManager
{
    private int _currentStageIndex = 0; // 현재 스테이지 인덱스
    private List<StageData> _stages; // 모든 스테이지 데이터 목록

    public StageManager(List<StageData> stages)
    {
        _stages = stages;
        _currentStageIndex = 0; // 초기 스테이지 설정
    }

    public StageData GetNextStage(out StageData alternateStage)
    {
        // 기본적으로 다음 두 개의 무작위 스테이지를 선택
        int nextStageIndex = Random.Range(0, _stages.Count);
        int alternateStageIndex;

        do
        {
            alternateStageIndex = Random.Range(0, _stages.Count);
        }
        while (alternateStageIndex == nextStageIndex || (IsShopStageRepeat(nextStageIndex, alternateStageIndex)));

        alternateStage = _stages[alternateStageIndex];
        return _stages[nextStageIndex];
    }

    private bool IsShopStageRepeat(int nextStageIndex, int alternateStageIndex)
    {
        // 상점 스테이지의 연속 방지 로직
        return _stages[nextStageIndex].IsShop && _stages[alternateStageIndex].IsShop;
    }
}

[System.Serializable]
public class StageData
{
    public string stageName;
    public bool IsShop;
    public bool IsBoss;
    public int appearanceProbability;
}
