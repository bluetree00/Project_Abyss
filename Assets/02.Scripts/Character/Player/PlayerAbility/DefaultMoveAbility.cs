using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DefaultMoveAbility : IMoveAbility<CharacterController>
{
    public void Move(CharacterController  owner, Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.01f)
        {
            owner.StopHorizontalMovement();
            return;
        }

        Vector3 moveDir = direction.normalized;
        float speed = owner.CharacterData.baseMoveSpeed;

        owner.Rigid.velocity = new Vector3(moveDir.x * speed, owner.Rigid.velocity.y, moveDir.z * speed);

        Quaternion targetRot = Quaternion.LookRotation(moveDir);
        owner.transform.rotation = Quaternion.Slerp(owner.transform.rotation, targetRot, Time.deltaTime * 10f);
    }
}
