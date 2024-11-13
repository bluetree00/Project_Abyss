using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Portal
{
    public string portalName;  // 포탈의 이름
    public List<StageManager.Stage> connectedStages;  // 포탈에 연결된 스테이지들

    // 포탈을 활성화하여 연결된 스테이지 중 우선순위가 가장 낮은 스테이지로 이동하는 메서드
    public void ActivatePortal(StageManager stageManager)
    {
        // 연결된 스테이지들을 가중치 순으로 정렬하여 우선순위가 가장 낮은(가장 적은 가중치) 스테이지로 이동
        StageManager.Stage nextStage = connectedStages.OrderBy(stage => stage.weight).FirstOrDefault();
        
        if (nextStage != null)
        {
            // 해당 스테이지로 이동하는 로직
            Debug.Log($"Activating portal to next stage: {nextStage.stageName}");
            stageManager.MoveToNextStage(nextStage);
        }
        else
        {
            Debug.LogError("No connected stages available for this portal.");
        }
    }

    // 연결된 스테이지들을 설정하는 메서드
    public void ConnectStages(List<StageManager.Stage> stages)
    {
        connectedStages = stages;
    }
}
