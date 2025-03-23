using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class SpecterMonster : MonsterBaseController
{
    // 예시: SpecterMonster는 Define.MonsterType.Specter로 식별
    protected override Define.MonsterType MonsterTypeIdentifier => Define.MonsterType.Specter;

    private Dictionary<string, float> hitCooldowns = new Dictionary<string, float>(); // 각 이펙트의 쿨타임 저장
    private float _nextAttackTime = 0.0f; // 다음 공격 가능 시간

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
            // 공격 범위 내에 있고, 쿨타임이 끝났을 때만 공격 상태로 전환
            if (distance <= MonsterData._attackRange && Time.time >= _nextAttackTime)
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
            // 플레이어 방향으로 회전
            Vector3 dir = _lockTarget.transform.position - transform.position;
            Quaternion targetRotation = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, 20 * Time.deltaTime);

            // 쿨타임 조건은 이동 시에 체크하므로, 여기서는 공격 애니메이션 실행 등 다른 처리를 수행할 수 있음
            // 예: 애니메이션 이벤트(OnAttackEvent)로 실제 공격 실행 처리
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

            // 플레이어용 이펙트라면 데미지 처리
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
            return Time.time >= hitCooldowns[effectData.effectName];
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
            // 실제 공격 실행: 예를 들어, 플레이어 체력 감소 처리 등
            Vector3 dir = _lockTarget.transform.position - transform.position;
            Quaternion targetRotation = Quaternion.LookRotation(dir);
            Vector3 spawnPosition = transform.position + transform.forward; // 몬스터 전방에서 발사
            Quaternion spawnRotation = targetRotation;
            GameObject effectObject = Managers.ObjectPooler.SpawnFromPool("BloodShot", spawnPosition, spawnRotation);

            // Rigidbody가 있다면, 발사체에 속도 부여 (발사체 이동 처리)
            Rigidbody rb = effectObject.GetComponent<Rigidbody>();
            if (rb != null)
            {
                float projectileSpeed = 5f; // 발사체 속도 (필요에 따라 조정)
                rb.velocity = dir.normalized * projectileSpeed;
            }
        }
    }

    void OnEndHitEvent()
    {
        // 공격 애니메이션 종료 시 호출되어 상태를 Idle로 전환
        State = Define.MonsterState.Idle;
    }  
}
