using UnityEngine;

[CreateAssetMenu(menuName = "Monster/Abilities/NormalAttack")]
public class NormalAttackAbilitySO : AttackAbilitySO
{
    [SerializeField] private float damage = 15f;
    [SerializeField] private float attackRange = 1.8f;
    [SerializeField] private AnimationClip attackAnimation;
    [SerializeField] private Define.AttackStyle style = Define.AttackStyle.Melee;
    [SerializeField] private Define.AttackPurpose purpose = Define.AttackPurpose.Normal;

    public override Define.MonsterAbilityType MonsterAbilityType => Define.MonsterAbilityType.Attack;

    public override IMonsterAbility CreateAbilityInstance()
    {
        return new NormalAttackAbility(damage, attackRange, attackAnimation, style, purpose);
    }
}
