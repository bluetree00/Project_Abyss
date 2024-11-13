using System.Collections.Generic;
using UnityEngine;

public class PortalScript : MonoBehaviour
{
    public string portalName;        // 포탈의 이름
    public StageManager stageManager; // StageManager 인스턴스
    public Portal portalData;        // 연결된 포탈 데이터

    // 포탈이 활성화될 때 호출될 메서드
    private void OnTriggerEnter(Collider other)
    {
        // 플레이어와 충돌한 경우에만 포탈을 활성화
        if (other.CompareTag("Player"))
        {
            Debug.Log($"Player entered portal: {portalName}");
            ActivatePortal();  // 포탈의 ActivatePortal 메서드 호출
        }
    }

    // 포탈을 활성화하여 스테이지를 이동하는 메서드
    public void ActivatePortal()
    {
        if (stageManager == null)
        {
            Debug.LogError("StageManager is not assigned to the portal.");
            return;
        }

        if (portalData != null)
        {
            portalData.ActivatePortal(stageManager);  // 포탈의 ActivatePortal 메서드 호출
        }
        else
        {
            Debug.LogError("No portal data assigned.");
        }
    }

    // 포탈에 연결된 스테이지 목록을 설정하는 메서드
    public void SetConnectedStages(List<StageManager.Stage> stages)
    {
        if (portalData == null)
        {
            portalData = new Portal();
        }

        portalData.ConnectStages(stages);  // 연결된 스테이지 데이터를 설정
    }
}
