using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DefaultJumpAbility : JumpAbilitySO
{
    public override void Jump(CharacterController controller)
    {
        if (!controller.isGrounded) return;

        controller.isJumping = true;
        controller.Rigid.velocity = new Vector3(controller.Rigid.velocity.x, 0, controller.Rigid.velocity.z);
        controller.Rigid.AddForce(Vector3.up * controller.CharacterData.jumpForce, ForceMode.Impulse);
    }
}
