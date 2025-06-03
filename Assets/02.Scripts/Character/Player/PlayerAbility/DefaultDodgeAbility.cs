using System.Collections;
using UnityEngine;

public class DefaultDodgeAbility : IDodgeAbility<CharacterBase>
{
    private bool isDodging = false;

    public void Dodge(CharacterBase controller)
    {
        if (isDodging || !controller.CharacterData.canDodge)
            return;

        controller.StartCoroutine(DodgeCoroutine(controller));
    }

    private IEnumerator DodgeCoroutine(CharacterBase controller)
    {
        isDodging = true;
        controller.CharacterData.canDodge = false;

        float dashDuration = controller.CharacterData.dashDuration;
        float dashSpeed = controller.CharacterData.dashSpeed;

        Vector3 direction = controller.MoveDirection != Vector3.zero
            ? controller.MoveDirection
            : controller.transform.forward;

        float startTime = Time.time;
        
        controller.GotoDodgeState();

        while (Time.time < startTime + dashDuration)
        {
            controller.Rigid.velocity = direction * dashSpeed;
            yield return null;
        }

        controller.Rigid.velocity = Vector3.zero;


        yield return new WaitForSeconds(controller.CharacterData.dodgeCooldown);
        controller.CharacterData.canDodge = true;
        isDodging = false;
    }
}
