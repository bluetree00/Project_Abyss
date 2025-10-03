using System.Collections.Generic;
using UnityEngine;

public class AttackAbilitySet : IMonsterAbility
{
    private List<IAttackAbility> attackAbilities = new();
    private Dictionary<Define.AttackStyle, List<IAttackAbility>> attackByStyle = new();
    private MonsterController owner;

    // 캐싱: 전체 어빌리티 스냅샷 (읽기 전용 노출용)
    private IAttackAbility[] _cachedAll;
    private bool _cacheDirty = true;

    public Define.MonsterAbilityType Type => Define.MonsterAbilityType.Attack;

    public AttackAbilitySet() { }

    public void Init(MonsterController owner)
    {
        this.owner = owner;
        foreach (var ability in attackAbilities)
        {
            ability.Init(owner);
        }
    }

    public void AddAttackAbility(IMonsterAbility ability)
    {
        if (ability is IAttackAbility attackAbility)
        {
            attackAbilities.Add(attackAbility);
            _cacheDirty = true; // 목록 변경 → 캐시 무효화

            if (!attackByStyle.TryGetValue(attackAbility.Style, out var list))
            {
                list = new List<IAttackAbility>();
                attackByStyle[attackAbility.Style] = list;
            }
            list.Add(attackAbility);

            if (owner != null)
                attackAbility.Init(owner);
        }
        else
        {
               Debug.LogWarning("[AttackAbilitySet] 공격 어빌리티가 아닙니다: " + ability.GetType().Name);
        }
    }

    public void Execute()
    {
        var selectedAbility = SelectAttackAbility(Define.AttackStyle.Melee, Define.AttackPurpose.Normal01);
        selectedAbility?.Execute();
    }

    public IAttackAbility SelectAttackAbility(Define.AttackStyle style, Define.AttackPurpose purpose)
    {
        if (!attackByStyle.TryGetValue(style, out var list))
            return null;

        var filtered = list.FindAll(a => a.Purpose == purpose);
        if (filtered.Count == 0)
            return null;

        return filtered[Random.Range(0, filtered.Count)];
    }
}
