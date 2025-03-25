using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using Game.CharacterStates;
using Game.CharacterStates.OrcStates;

public class OcrMonster : MonsterController
{
    // 예시: OcrMonster는 Define.MonsterType.Orc로 식별
    protected override Define.MonsterType MonsterTypeIdentifier
    {
        get { return Define.MonsterType.Orc; }
    }

    private Dictionary<string, float> hitCooldowns = new Dictionary<string, float>(); // 각 이펙트의 쿨타임을 저장하는 딕셔너리

    protected new StateMachine<OcrMonster> stateMachine = new StateMachine<OcrMonster>();
    public new StateMachine<OcrMonster> StateMachine => stateMachine;

    protected override void Init()
    {
        base.Init();
        stateMachine.Setup(this, new OrcIdleState());
    }


    private void UpdateNormalAttack_01()
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

}
