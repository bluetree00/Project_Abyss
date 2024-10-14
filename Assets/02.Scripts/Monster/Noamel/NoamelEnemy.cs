using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AI;

public class NoamelEneny : MonsterBaseController
{

    [SerializeField]
    float _scacRange = 10;

    [SerializeField]
    float _attackRange = 1;

    private CapsuleCollider monsterCollider;
    private Rigidbody monsterigidbody;
     protected override void UpdateIdle()
    {

        GameObject player = GameObject.FindGameObjectWithTag("Player");

        if(player == null)
        {
            return;
        }

        float distance = (player.transform.position - transform.position).magnitude;
        if(distance <= _scacRange)
        {
            _lockTarget = player;
            State = Define.MonsterState.Moving;
            return;
        }
    }

    protected override void UpdateMoving()
    {
        //플레이어가 사정거리 안에 들어오면 공격
        if(_lockTarget !=null)
        {
            _destPos = _lockTarget.transform.position;
            float distance = (_destPos - transform.position).magnitude;
            if(distance <= _attackRange)
            {
                NavMeshAgent nma = gameObject.GetComponent<NavMeshAgent>();
                nma.SetDestination(transform.position);
                State = Define.MonsterState.NormalAttack_01;
                return;
            }
        }

        Vector3 dir = _destPos - transform.position;
        if(dir.magnitude <0.1f)
        {
            State = Define.MonsterState.Idle;
        }
        else
        {
            NavMeshAgent nma = gameObject.GetComponent<NavMeshAgent>();
            nma.SetDestination(_destPos);
            nma.speed = moveSpeed;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 20 * Time.deltaTime);
        }

    }

    protected override void UpdateNormalAttack_01()
    {
        if(_lockTarget != null)
        {
            Vector3 dir = _lockTarget.transform.position - transform.position;
            Quaternion quat = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Lerp(transform.rotation, quat, 20*Time.deltaTime);
        }
        
    }


    //몬스터 공격 애니메이션 이벤트
    void OnAttackEvent()
    {
        Debug.Log("Monster OnHitEvent");

        if(_lockTarget != null)
        {
            //플레이어 체력 감소 필요 플레이어가 죽었다면 후처리 필요
        }
        else
        {
            State = Define.MonsterState.Idle;
        }
        State = Define.MonsterState.Idle;
    }


    void OnEndHitEvent()
    {
        State = Define.MonsterState.Idle;
    }


}
