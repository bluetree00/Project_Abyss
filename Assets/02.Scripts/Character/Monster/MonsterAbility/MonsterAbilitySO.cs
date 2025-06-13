using UnityEngine;

public abstract class MonsterAbilitySO : ScriptableObject
{

    public abstract Define.MonsterAbilityType MonsterAbilityType { get; }

    // 능력 인스턴스 생성 추상 메서드
    public abstract IMonsterAbility CreateAbilityInstance();
}
