using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class stageTrigger : MonoBehaviour
{
    [SerializeField]
    private int stagesteps; // 스테이지 인덱스
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            // 플레이어가 입구에 닿았을 때 다음 스테이지로 이동
            Managers.Stage.MoveToNextStage(stagesteps); // 1단계 이동
        }
    }
}
