using System.Collections;
using UnityEngine;
using Game.CharacterStates;
using Game.CharacterStates.CharacterControllerStates;
using Game.CharacterStates.StateMachine;
using Game.Interfaces;

namespace Game.CharacterStates.CommonStates
{
   public class GenericDodgeState<T> : AnimationState<T> where T : CharacterController
{
    private readonly Vector3 dodgeDirection;
    private readonly float dodgeSpeed;
    private readonly float duration;
    private readonly float cooldown;

    public GenericDodgeState(Vector3 direction, float speed, float duration, float cooldown)
    {
        dodgeDirection = direction.normalized;
        dodgeSpeed = speed;
        this.duration = duration;
        this.cooldown = cooldown;

        SetupAnimation("Dodge", 0.6f); // 애니메이션 이름은 필요 시 매개변수화 가능
    }

    public override void Enter(T owner)
    {
        base.Enter(owner);
        owner.StartCoroutine(DodgeRoutine(owner));
    }

    private IEnumerator DodgeRoutine(T owner)
    {
        float startTime = Time.time;

        // 회피 동작
        while (Time.time < startTime + duration)
        {
            owner.Rigid.velocity = dodgeDirection * dodgeSpeed;

            Quaternion targetRot = Quaternion.LookRotation(dodgeDirection);
            owner.transform.rotation = Quaternion.Slerp(owner.transform.rotation, targetRot, Time.deltaTime * 10f);

            yield return null;
        }

        // 정지
        owner.Rigid.velocity = Vector3.zero;

        // 쿨타임 대기 후 다시 회피 가능 설정
        yield return new WaitForSeconds(cooldown);
        owner.CharacterData.canDodge = true;
    }

    protected override void OnAnimationEnd(T owner)
    {
        
    }
}

}
