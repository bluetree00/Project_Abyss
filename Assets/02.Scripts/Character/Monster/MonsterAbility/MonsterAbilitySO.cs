using UnityEngine;

public abstract class MonsterAbilitySO : ScriptableObject
{
    public Define.AbilityType type;
    public abstract IMonsterAbility CreateAbilityInstance();
}
