using System.Collections;
using UnityEngine;

public class DefaultDodgeAbility : IDodgeAbility<CharacterController>
{
    private bool isDodging = false;

    public void Dodge(CharacterController controller)
    {
        if (isDodging || !controller.CharacterData.canDodge)
            return;

        controller.StartCoroutine(DodgeCoroutine(controller));
    }

    private IEnumerator DodgeCoroutine(CharacterController controller)
    {
        isDodging = true;
        controller.CharacterData.canDodge = false;

        float dashDuration = controller.CharacterData.dashDuration;
        float dashSpeed = controller.CharacterData.dashSpeed;

        Vector3 direction = controller.MoveDirection != Vector3.zero
            ? controller.MoveDirection
            : controller.transform.forward;

        float startTime = Time.time;
        controller.Anim.CrossFade("Dodge", 0.1f);

        while (Time.time < startTime + dashDuration)
        {
            controller.Rigid.velocity = direction * dashSpeed;
            yield return null;
        }

        controller.Rigid.velocity = Vector3.zero;

        controller.GoToIdleState(); // 타입 상관없이 알아서 자기 상태로 감


        yield return new WaitForSeconds(controller.CharacterData.dodgeCooldown);
        controller.CharacterData.canDodge = true;
        isDodging = false;
    }
}
