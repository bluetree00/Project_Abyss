using UnityEngine;

[CreateAssetMenu(menuName = "Monster/Abilities/NormalAttack")]
public class NormalAttackAbilitySO : AttackAbilitySO
{
    [SerializeField] private float damage = 15f;
    [SerializeField] private float attackRange = 1.8f;
    [SerializeField] private AnimationClip attackAnimation;

    public override Define.MonsterAbilityType MonsterAbilityType => throw new System.NotImplementedException();

    public override IMonsterAbility CreateAbilityInstance()
    {
        return new NormalAttackAbility(damage, attackRange, attackAnimation);
    }
}
