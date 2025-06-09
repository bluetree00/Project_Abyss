using UnityEngine;

public abstract class MonsterAbilitySO : ScriptableObject
{
    [SerializeField, HideInInspector]
    private Define.MonsterAbilityType type;

    public Define.MonsterAbilityType Type => type;  // public 읽기 전용 프로퍼티

    public void SetType(Define.MonsterAbilityType newType)
    {
        if (type == Define.MonsterAbilityType.None)
        {
            type = newType;
        }
        else
        {
            Debug.LogWarning("Ability type is already set and cannot be changed.");
        }
    }

    public abstract IMonsterAbility CreateAbilityInstance();
}
