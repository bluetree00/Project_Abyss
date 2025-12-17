using Unity.VisualScripting;
using UnityEngine;

public class SkeletonKnightAnimationEventReceiver : MonsterAnimationEventReceiver
{

    public override void OnAttackStart()
    {
        if (controller == null)
            return;

        controller.SetAttack(true);

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
            return;
        controller.SetAttack(false); // 콤보/목적 증가는 AttackState에서 처리
        RaiseAttackEndEvent();
    }
}
