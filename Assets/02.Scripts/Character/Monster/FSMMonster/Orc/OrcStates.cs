using Game.CharacterStates.MonsterControllerStates;
using UnityEngine;
using UnityEngine.AI;

namespace Game.CharacterStates.OrcStates
{
     public static class AnimationHelper // 애니메이션 도우미 클래스 예시 : if (AnimationHelper.IsAnimationFinished(owner.Anim, "Attack_01")) 후에 체크크
    {                                   // 이 값의 endtime 또한 변수로 가져와서 매개변수에 자동으로 들어가도록 추후 변경
        public static bool IsAnimationFinished(Animator anim, string animationName, float endTime = 0.95f, int layer = 0)
        {
            if (anim == null) return false;

            AnimatorStateInfo animState = anim.GetCurrentAnimatorStateInfo(layer);
            return animState.IsName(animationName) && animState.normalizedTime >= endTime;
        }
    }

  public class OrcIdleState : State<OcrMonster>
    {
        public override void Enter(OcrMonster owner)
        {
            Debug.Log("IdleState Enter 호출됨");
            owner.Anim.CrossFade("Idle", 0.1f);
        }

        public override void Execute(OcrMonster owner)
        {
            if (owner.PlayerTransform == null)
                return;

            float distance = Vector3.Distance(owner.PlayerTransform.position, owner.transform.position);
            if (distance <= owner.MonsterData._scacRange)
            {
                owner._lockTarget = owner.PlayerTransform.gameObject;
                owner.StateMachine.ChangeState(new MoveState());
            }
        }
        public override void Exit(OcrMonster owner) {}
    }


    public class MoveState :  State<OcrMonster>
    {
        public override void Enter(OcrMonster owner)
        {
            owner.Anim.CrossFade("Moving", 0.1f);
        }

    public override void Execute(OcrMonster owner)
    {
        if (owner._lockTarget != null)
        {
            owner._destPos = owner._lockTarget.transform.position;
            float distance = Vector3.Distance(owner._destPos, owner.transform.position);

            if (distance <= owner.MonsterData._attackRange && owner.CanAttack())
            {
                owner.Agent.SetDestination(owner.transform.position); // 제자리 멈춤
                owner.StateMachine.ChangeState(new NormalAttackState());
                return;
            }
        }

        Vector3 dir = owner._destPos - owner.transform.position;
        if (dir.magnitude < 0.1f)
        {
            owner.StateMachine.ChangeState(new OrcIdleState());
        }
        else
        {
            owner.Agent.SetDestination(owner._destPos);
            owner.Agent.speed = owner.MonsterData.moveSpeed;
            owner.transform.rotation = Quaternion.Slerp(owner.transform.rotation, Quaternion.LookRotation(dir), 20 * Time.deltaTime);
        }
    }



        public override void Exit(OcrMonster owner)
        {
          
        }
    }

    public class NormalAttackState : State<OcrMonster>
{
    public override void Enter(OcrMonster owner)
    {
         owner.Anim.CrossFade("NormalAttack_01", 0.1f);
        owner.MarkAttackTime(); // ✅ 공격 시작 시 시간 저장
    }

    public override void Execute(OcrMonster owner)
    {
         if (owner._lockTarget != null)
        {
            Vector3 dir = owner._lockTarget.transform.position - owner.transform.position;
            Quaternion quat = Quaternion.LookRotation(dir);
            owner.transform.rotation = Quaternion.Lerp(owner.transform.rotation, quat, 20 * Time.deltaTime);
        }

        if (AnimationHelper.IsAnimationFinished(owner.Anim, "NormalAttack_01"))
        {
            owner.StateMachine.ChangeState(new OrcIdleState());
        }
    }

    public override void Exit(OcrMonster owner)
    {
        // 필요 시 나갈 때 로직 추가
    }
}




}
