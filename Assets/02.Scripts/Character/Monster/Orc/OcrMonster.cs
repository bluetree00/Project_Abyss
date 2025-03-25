using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using Game.CharacterStates;
using Game.CharacterStates.VagabondStates;

public class OcrMonster : MonsterBaseController
{
    // 예시: OcrMonster는 Define.MonsterType.Orc로 식별
    protected override Define.MonsterType MonsterTypeIdentifier
    {
        get { return Define.MonsterType.Orc; }
    }

    private Dictionary<string, float> hitCooldowns = new Dictionary<string, float>(); // 각 이펙트의 쿨타임을 저장하는 딕셔너리

    protected new StateMachine<Vagabond> stateMachine = new StateMachine<Vagabond>();

    public new StateMachine<Vagabond> StateMachine => stateMachine;

    protected override void UpdateIdle()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return;

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
                NavMeshAgent nma = GetComponent<NavMeshAgent>();
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
            NavMeshAgent nma = GetComponent<NavMeshAgent>();
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
                if (CanHit(effectData))
                {
                    ApplyDamage(effectData.SetEffectDamage());
                    StartHitCooldown(effectData.effectName, effectData.hitInterval);
                }
            }
        }
    }

    private bool CanHit(EffectData effectData)
    {
        if (hitCooldowns.ContainsKey(effectData.effectName))
        {
            bool canHit = Time.time >= hitCooldowns[effectData.effectName];
            return canHit;
        }
        return true;
    }

    private void StartHitCooldown(string effectName, float hitInterval)
    {
        hitCooldowns[effectName] = Time.time + hitInterval;
    }

    private void ApplyDamage(float damage)
    {
        hp -= damage;
        if (hp <= 0)
        {
            Vector3 spawnPosition2 = new Vector3(transform.position.x, 1f, transform.position.z);
            Quaternion spawnRotation2 = Quaternion.identity;
            GameObject effectObject2 = Managers.ObjectPooler.SpawnFromPool("DieEffect_01", spawnPosition2, spawnRotation2);
            Debug.Log("Monster died, spawning die effect.");
            Destroy(gameObject);
        }
        Debug.Log($"Monster took {damage} damage. Current Health: {hp}");

        Vector3 spawnPosition = new Vector3(transform.position.x, 1f, transform.position.z);
        Quaternion spawnRotation = Quaternion.identity;
        GameObject effectObject = Managers.ObjectPooler.SpawnFromPool("HitEffect_02", spawnPosition, spawnRotation);
        Debug.Log("Spawned hit effect.");
    }

    // 몬스터 공격 애니메이션 이벤트
    void OnAttackEvent()
    {
        Debug.Log("Monster OnHitEvent");
        if (_lockTarget != null)
        {
            // 플레이어 체력 감소 처리 (플레이어 스크립트에서 처리)
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
