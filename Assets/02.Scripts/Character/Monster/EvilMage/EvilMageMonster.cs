using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EvilMageMonster : MonsterController
{
    // 예시: SpecterMonster는 Define.MonsterType.Specter로 식별
    protected override Define.MonsterType MonsterTypeIdentifier => Define.MonsterType.Specter;

    private Dictionary<string, float> hitCooldowns = new Dictionary<string, float>(); // 각 이펙트의 쿨타임 저장
    private float _nextAttackTime = 0.0f; // 다음 공격 가능 시간

    protected  void UpdateIdle()
    {
        // 씬 내의 MonsterBaseController 컴포넌트를 가진 모든 오브젝트를 찾습니다.
        MonsterController[] candidates = GameObject.FindObjectsOfType<MonsterController>();
        MonsterController target = null;
        float minDistance = float.MaxValue;

        foreach (MonsterController candidate in candidates)
        {
            // 자기 자신은 제외
            if (candidate == this)
                continue;

            float distance = (candidate.transform.position - transform.position).magnitude;
            if (distance < minDistance)
            {
                minDistance = distance;
                target = candidate;
            }
        }

        // 만약 MonsterBaseController를 가진 대상이 있고, 사정거리 내라면 선택
        if (target != null && minDistance <= MonsterData._scacRange)
        {
            _lockTarget = target.gameObject;
         
            return;
        }
        
        // 위 조건에 해당하는 대상이 없으면 "Player" 태그를 가진 오브젝트를 찾아 사용
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return;
        
        float playerDistance = (player.transform.position - transform.position).magnitude;
        if (playerDistance <= MonsterData._scacRange)
        {
            _lockTarget = player;
    
            return;
        }
    }

    protected  void UpdateMoving()
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
            
                return;
            }
        }

        Vector3 dir = _destPos - transform.position;
        if (dir.magnitude < 0.1f)
        {
         
        }
        else
        {
            NavMeshAgent nma = GetComponent<NavMeshAgent>();
            nma.SetDestination(_destPos);
            nma.speed = monsterData.moveSpeed;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 20 * Time.deltaTime);
        }
    }

    protected  void UpdateNormalAttack_01()
    {
        if (_lockTarget != null)
        {
            // 플레이어 방향으로 회전
            Vector3 dir = _lockTarget.transform.position - transform.position;
            Quaternion targetRotation = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, 20 * Time.deltaTime);

            // 공격 애니메이션 이벤트(OnAttackEvent)로 실제 공격 실행 처리
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
            //[ ]
            GameObject effectObject2 = Managers.ObjectPooler.SpawnFromPool("DieEffect_01", spawnPosition2, spawnRotation2);
            Debug.Log("Monster died, spawning die effect.");
            Destroy(gameObject);
        }
        Debug.Log($"Monster took {damage} damage. Current Health: {hp}");

        Vector3 spawnPosition = new Vector3(transform.position.x, 1f, transform.position.z);
        Quaternion spawnRotation = Quaternion.identity;
        //[ ]
        GameObject effectObject = Managers.ObjectPooler.SpawnFromPool("HitEffect_02", spawnPosition, spawnRotation);
        Debug.Log("Spawned hit effect.");
    }

    // 몬스터 공격 애니메이션 이벤트에서 호출 (실제 공격 실행)
    void OnAttackEvent()
    {
        Debug.Log("Monster OnHitEvent");
        if (_lockTarget != null)
        {
            // 대상 방향 계산
            Vector3 dir = _lockTarget.transform.position - transform.position;
            Quaternion targetRotation = Quaternion.LookRotation(dir);
            Vector3 spawnPosition = transform.position + transform.forward * 2f + Vector3.up * 0.5f;
            Quaternion spawnRotation = targetRotation;

            // 발사체(이펙트) 생성
            //[ ]
            GameObject effectObject = Managers.ObjectPooler.SpawnFromPool("Laser", spawnPosition, spawnRotation);

            // 코루틴을 통해 일정 시간(trackingDuration) 동안 발사체가 대상 방향으로 회전(추적)하도록 함.
            // 반환 처리는 EffectBehaviour에서 처리하므로 여기서는 회전 업데이트만 수행.
            float trackingDuration = 1f;    // 원하는 추적 시간 (예: 1초)
            float rotationSpeed = 5f;       // 회전 속도 (필요에 따라 조정)
            StartCoroutine(TrackTargetCoroutine(effectObject, _lockTarget.transform, trackingDuration, rotationSpeed));
        }
    }

    private IEnumerator TrackTargetCoroutine(GameObject projectile, Transform target, float duration, float rotationSpeed)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (projectile == null || target == null)
                break;
            
            // 타겟 방향 계산
            Vector3 direction = target.position - projectile.transform.position;
            if (direction != Vector3.zero)
            {
                Quaternion desiredRotation = Quaternion.LookRotation(direction);
                projectile.transform.rotation = Quaternion.Slerp(projectile.transform.rotation, desiredRotation, Time.deltaTime * rotationSpeed);
            }
            
            elapsed += Time.deltaTime;
            yield return null;
        }
        // 이 코루틴은 풀 반환을 직접 처리하지 않습니다.
    }

    void OnEndHitEvent()
    {
        // 공격 애니메이션 종료 시 호출되어 상태를 Idle로 전환
       
    }  
}
