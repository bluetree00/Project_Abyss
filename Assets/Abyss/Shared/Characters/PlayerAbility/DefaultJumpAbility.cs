using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DefaultJumpAbility : JumpAbilitySO
{
    public override void Jump(PlayerController controller)
    {
        if (!controller.IsGrounded()) return;

        controller.SetJumping(true);
        controller.Rigid.linearVelocity = new Vector3(controller.Rigid.linearVelocity.x, 0, controller.Rigid.linearVelocity.z);
        controller.Rigid.AddForce(Vector3.up * controller.CharacterData.jumpForce, ForceMode.Impulse);
    }
}
