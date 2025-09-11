using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BatAnimationEventReceiver : MonoBehaviour
{
    private MonsterController controller;

    public event System.Action OnAttackStartEvent;
    public event System.Action OnAttackEndEvent;

    private void Awake()
    {
        if (controller == null)
            controller = GetComponent<MonsterController>() ?? GetComponentInParent<MonsterController>();

        if (controller == null)
            Debug.LogWarning("[BatAnimationEventReceiver] MonsterController를 찾을 수 없습니다.");
    }

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

        // 이펙트 생성 코드 (주석 처리된 부분)

        OnAttackStartEvent?.Invoke();
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

        OnAttackEndEvent?.Invoke();
    }
}
