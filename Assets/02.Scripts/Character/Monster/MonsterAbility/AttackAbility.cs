using UnityEngine;

public class AttackAbility : IMonsterAbility
{
    private float attackRange;
    private float cooldownTime;
    private float lastAttackTime = Mathf.NegativeInfinity;

    private MonsterController owner;

    public Define.MonsterAbilityType Type => Define.MonsterAbilityType.Attack;

    public AttackAbility(float attackRange, float cooldownTime)
    {
        this.attackRange = attackRange;
        this.cooldownTime = cooldownTime;
    }

    public void Init(MonsterController owner)
    {
        this.owner = owner;
    }

    public void Execute()
    {
        if (!CanAttack())
            return;

        Transform target = owner.playerTarget;
        if (target == null)
            return;

        float distance = Vector3.Distance(owner.transform.position, target.position);
        if (distance <= attackRange)
        {
            lastAttackTime = Time.time;

            // 공격 시작 시 처리
            owner.SetAttack(true); 
            // 애니메이션은 상태 클래스에서 따로 실행
        }
    }

    public bool CanAttack()
    {
        return Time.time >= lastAttackTime + cooldownTime;
    }
}
