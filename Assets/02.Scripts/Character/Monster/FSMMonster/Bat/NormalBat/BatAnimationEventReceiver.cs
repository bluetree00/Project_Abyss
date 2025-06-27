using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BatAnimationEventReceiver : MonoBehaviour
{
    private MonsterController controller;

    private void Awake()
    {
        if (controller == null)
            controller = GetComponent<MonsterController>() ?? GetComponentInParent<MonsterController>();

        if (controller == null)
            Debug.LogWarning("[BatAnimationEventReceiver] MonsterController를 찾을 수 없습니다.");
    }

    // 애니메이션 이벤트 연결
    public void OnAttackStart()
    {
        if (controller == null || controller.EffectProfile == null)
            return;

        controller.SetAttack(true);
        Debug.Log("OnAttackStart: 공격 시작");

        string effectName = controller.EffectProfile.attackEffect;
        Vector3 offset = controller.EffectProfile.attackEffectOffset;
        Vector3 rotationEuler = controller.EffectProfile.attackEffectRotation;

        Vector3 spawnPos = controller.transform.TransformPoint(offset);
        Quaternion spawnRot = Quaternion.Euler(rotationEuler);

        // var effectObj = Managers.ObjectPooler.SpawnFromPool(effectName, spawnPos, spawnRot);
        // if (effectObj == null)
        //     Debug.LogWarning($"{effectName} 이펙트 생성 실패");
    }

    public void OnAttackEnd()
    {
        if (controller == null)
        {
            Debug.LogWarning("OnAttackEnd: controller가 null입니다.");
            return;
        }

        controller.SetAttack(false);
        Debug.Log("OnAttackEnd: 공격 종료");
    }
}
