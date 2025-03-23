using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class NormalEnemy : MonsterBaseController
{
    private Dictionary<string, float> hitCooldowns = new Dictionary<string, float>(); // 각 이펙트의 쿨타임을 저장하는 딕셔너리

    protected override void UpdateIdle()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");

        if (player == null)
        {
            return;
        }

        float distance = (player.transform.position - transform.position).magnitude;
        if (distance <= MonsterData._scacRange)
        {
            _lockTarget = player;
            State = Define.MonsterState.Moving;
            return;
        }
    }

    protected override void UpdateMoving()
    {
        if (_lockTarget != null)
        {
            _destPos = _lockTarget.transform.position;
            float distance = (_destPos - transform.position).magnitude;
            if (distance <= MonsterData._attackRange)
            {
                NavMeshAgent nma = gameObject.GetComponent<NavMeshAgent>();
                nma.SetDestination(transform.position);
                State = Define.MonsterState.NormalAttack_01;
                return;
            }
        }

        Vector3 dir = _destPos - transform.position;
        if (dir.magnitude < 0.1f)
        {
            State = Define.MonsterState.Idle;
        }
        else
        {
            NavMeshAgent nma = gameObject.GetComponent<NavMeshAgent>();
            nma.SetDestination(_destPos);
            nma.speed = monsterData.moveSpeed;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 20 * Time.deltaTime);
        }
    }

    protected override void UpdateNormalAttack_01()
    {
        if (_lockTarget != null)
        {
            Vector3 dir = _lockTarget.transform.position - transform.position;
            Quaternion quat = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Lerp(transform.rotation, quat, 20 * Time.deltaTime);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        EffectComponent effectComponent = other.GetComponent<EffectComponent>();
        if (effectComponent != null)
        {
            EffectData effectData = effectComponent.effectData;
            if (effectData == null)
            {
                Debug.LogError("EffectData is null in EffectComponent!");
                return;
            }

            // 플레이어용 이펙트인지 확인 후 데미지 처리
            if (effectData.isPlayerEffect)
            {
                // 쿨타임 체크 및 데미지 처리
                if (CanHit(effectData))
                {
                    ApplyDamage(effectData.SetEffectDamage());
                    StartHitCooldown(effectData.effectName, effectData.hitInterval); // 쿨타임 시작
                }
            }
        }
    
    }

    private bool CanHit(EffectData effectData)
    {
        // 쿨타임 체크
        if (hitCooldowns.ContainsKey(effectData.effectName))
        {
            bool canHit = Time.time >= hitCooldowns[effectData.effectName]; // 현재 시간이 쿨타임이 끝나는 시간보다 크거나 같은지 체크
            return canHit;
        }
        return true; // 처음 공격일 경우
    }

    private void StartHitCooldown(string effectName, float hitInterval)
    {
        hitCooldowns[effectName] = Time.time + hitInterval; // 다음 공격 가능 시간이 현재 시간 + 쿨타임
    }

    private void ApplyDamage(float damage)
    {
        hp -= damage;
        if (hp <= 0)
        {
            // 몬스터의 위치에서 Y축으로 1 단위 위쪽에 이펙트를 생성
            Vector3 spawnPosition2 = new Vector3(transform.position.x, 1f, transform.position.z); // Y축을 1로 고정
            Quaternion spawnRotation2 = Quaternion.identity; // 회전 값은 필요에 따라 설정

            GameObject effectObject2 = Managers.ObjectPooler.SpawnFromPool("DieEffect_01", spawnPosition2, spawnRotation2);
            Debug.Log("Monster died, spawning die effect.");
            Destroy(this.gameObject);
        }
        Debug.Log($"Monster took {damage} damage. Current Health: {hp}");

        // 몬스터의 위치에서 Y축으로 1 단위 위쪽에 이펙트를 생성
        Vector3 spawnPosition = new Vector3(transform.position.x, 1f, transform.position.z); // Y축을 1로 고정
        Quaternion spawnRotation = Quaternion.identity; // 회전 값은 필요에 따라 설정

        GameObject effectObject = Managers.ObjectPooler.SpawnFromPool("HitEffect_02", spawnPosition, spawnRotation);
        Debug.Log("Spawned hit effect.");
    }

    // 몬스터 공격 애니메이션 이벤트
    void OnAttackEvent()
    {
        Debug.Log("Monster OnHitEvent");

        if (_lockTarget != null)
        {
            // 플레이어 체력 감소 필요 (플레이어의 스크립트에서 처리)
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
