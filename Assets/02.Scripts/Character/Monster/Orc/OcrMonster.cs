using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.CharacterStates;
using Game.CharacterStates.OrcStates;

public class OcrMonster : MonsterController
{
    protected override Define.MonsterType MonsterTypeIdentifier => Define.MonsterType.Orc;

    private Dictionary<string, float> hitCooldowns = new Dictionary<string, float>();
    protected new StateMachine<OcrMonster> stateMachine = new StateMachine<OcrMonster>();
    public new StateMachine<OcrMonster> StateMachine => stateMachine;

    private float _lastAttackTime = Mathf.NegativeInfinity;

    public bool CanAttack() => Time.time >= _lastAttackTime + MonsterData.attackCooldown;
    public void MarkAttackTime() => _lastAttackTime = Time.time;

    public Transform PlayerTransform { get; private set; }
    public NavMeshAgent Agent { get; private set; }

    // ✅ 비동기 초기화
    protected override async Task InitAsync()
    {
        Agent = GetComponent<NavMeshAgent>();
        PlayerTransform = GameObject.FindGameObjectWithTag("Player")?.transform;

        await base.InitAsync();

        // ✅ 명시적으로 완료 반환
        return;
    }

    protected override void OnMonsterReady()
    {
        stateMachine.Setup(this, new OrcIdleState());
    }

    protected override void Update()
    {
        base.Update();
        if (!isInitialized) return;
        stateMachine.Update();
    }

    private void OnTriggerEnter(Collider other)
    {
        EffectComponent effectComponent = other.GetComponent<EffectComponent>();
        if (effectComponent == null) return;

        EffectData effectData = effectComponent.effectData;
        if (effectData == null)
        {
            Debug.LogError("EffectData is null in EffectComponent!");
            return;
        }

        if (effectData.isPlayerEffect && CanHit(effectData))
        {
            ApplyDamage(effectData.SetEffectDamage());
            StartHitCooldown(effectData.effectName, effectData.hitInterval);
        }
    }

    private bool CanHit(EffectData effectData)
    {
        if (hitCooldowns.ContainsKey(effectData.effectName))
            return Time.time >= hitCooldowns[effectData.effectName];
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
            Vector3 pos = new Vector3(transform.position.x, 1f, transform.position.z);
            //[ ]
            Managers.ObjectPooler.SpawnFromPool("DieEffect_01", pos, Quaternion.identity);
            Destroy(gameObject);
            return;
        }

        Debug.Log($"Monster took {damage} damage. Current Health: {hp}");
        Vector3 hitPos = new Vector3(transform.position.x, 1f, transform.position.z);
        //[ ]
        Managers.ObjectPooler.SpawnFromPool("HitEffect_02", hitPos, Quaternion.identity);
    }
}
