using UnityEngine;

public class NormalAttackAbility : IMonsterAbility
{
    private float damage;
    private float range;
    private AnimationClip animationClip;

    private MonsterController owner;

    public Define.MonsterAbilityType Type => Define.MonsterAbilityType.Attack;

    public NormalAttackAbility(float damage, float range, AnimationClip anim)
    {
        this.damage = damage;
        this.range = range;
        this.animationClip = anim;
    }

    public void Init(MonsterController owner)
    {
        this.owner = owner;
    }

    public void Execute()
    {
        if (owner == null || owner.playerTarget == null)
            return;

        float distance = Vector3.Distance(owner.transform.position, owner.playerTarget.position);
        if (distance > range)
            return;

        // 공격 애니메이션 재생
        if (animationClip != null)
        {
            owner.animator.CrossFade(animationClip.name, 0.1f);
        }

        // 데미지 처리 (실제 게임 로직에 맞게 구현)
        Debug.Log($"Normal Attack! Damage: {damage}");
    }
}
