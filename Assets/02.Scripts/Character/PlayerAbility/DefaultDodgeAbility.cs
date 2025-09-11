using System.Collections;
using UnityEngine;

[CreateAssetMenu(fileName = "NewPlayerDodgeAbility", menuName = "Abilities/Player/DodgeAbility")]
public class DefaultDodgeAbility : IDodgeAbility<PlayerCharacter>
{
    private bool isDodging = false;

    public void Dodge(PlayerCharacter controller)
    {
        if (isDodging || !controller.canDodge)
            return;

        controller.StartCoroutine(DodgeCoroutine(controller));
    }

    private IEnumerator DodgeCoroutine(PlayerCharacter controller)
    {
        isDodging = true;
        controller.canDodge = false;

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
        controller.canDodge = true;
        isDodging = false;
    }
}
