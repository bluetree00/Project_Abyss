using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DefaultMoveAbility : IMoveAbility<PlayerController>
{
    public void Move(PlayerController  owner, Vector3 direction)
    {
        // 넉백 중에는 이동 처리 전체를 스킵 — 물리 impulse가 override되지 않도록
        if (owner.IsKnockback) return;

        if (direction.sqrMagnitude < 0.01f)
        {
            owner.StopHorizontalMovement();
            return;
        }

        Vector3 moveDir = direction.normalized;
        float speed = owner.CharacterData.baseMoveSpeed * (owner.RuntimeStats?.MoveSpeedMultiplier ?? 1f);

        owner.Rigid.linearVelocity = new Vector3(moveDir.x * speed, owner.Rigid.linearVelocity.y, moveDir.z * speed);

        Quaternion targetRot = Quaternion.LookRotation(moveDir);
        owner.transform.rotation = Quaternion.Slerp(owner.transform.rotation, targetRot, Time.deltaTime * 10f);
    }
}
