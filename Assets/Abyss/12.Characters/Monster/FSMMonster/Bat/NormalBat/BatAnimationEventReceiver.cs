using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BatAnimationEventReceiver : MonsterAnimationEventReceiver
{
    public override void OnAttackStart()
    {
        if (controller == null)
            return;

        controller.SetAttack(true);
        Debug.Log("OnAttackStart: 공격 시작");

        if (TryGetAttackEffect(out var effectName, out var offset, out var rotationEuler))
        {
            Vector3 spawnPos = controller.transform.TransformPoint(offset);
            Quaternion spawnRot = Quaternion.Euler(rotationEuler);
            SpawnAttackEffect(effectName, spawnPos, spawnRot);
        }

        RaiseAttackStartEvent();
    }

    public override void OnAttackEnd()
    {
        if (controller == null)
        {
            Debug.LogWarning("OnAttackEnd: controller가 null입니다.");
            return;
        }

        controller.SetAttack(false);
        Debug.Log("OnAttackEnd: 공격 종료");

        RaiseAttackEndEvent();
    }

    // 배트 전용 커스터마이징이 필요하면 아래를 오버라이드
    // protected override void SpawnAttackEffect(string effectName, Vector3 pos, Quaternion rot) { /* Bat 커스텀 */ }
}
