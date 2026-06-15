using UnityEngine;

/// <summary>
/// 아이템 동적(조건부/타임드) 스탯 기여 누적기.
///
/// 정적 아이템 스탯(ModifyStats→RefreshItemBonuses, 그리드 배치 시 1회)과 달리,
/// 조건부/타임드 효과는 매 프레임 조건이 바뀐다. ItemEffectManager.OnTick이 활성 효과들의
/// ContributeDynamicStats를 합산해 PlayerRuntimeStats.ApplyItemDynamicStats로 push한다.
///
/// 룬의 SetSynergyDynamic* 레이어와 동일 개념이되, 여러 아이템이 같은 스탯에 기여하므로
/// 단일 소유자가 아니라 가산 합산이다. 모두 +배율/+%포인트(가산).
/// </summary>
public struct ItemDynamicStats
{
    public float attackPercent;   // 공격력 % (dmgMul 가산, 공격만)
    public float defensePercent;  // 방어력 % (defMul 가산)
    public float attackSpeed;     // 공격속도 % (배율 가산)
    public float moveSpeed;       // 이동속도 % (배율 가산)
    public float critChance;      // 치명타 확률 %포인트
    public float critDamage;      // 치명타 피해 배율 가산
    public float skillDamage;     // 스킬 피해 % (SkillDamageBonus 가산)
    public float allDamage;       // 모든 피해 % (공격 dmgMul + 스킬 SkillDamageBonus 양쪽)
    public float maxHpPercent;    // 최대 HP % (PlayerRuntimeStats.Recalculate에서 정적+동적 합산 적용됨)

    public bool Approximately(in ItemDynamicStats o) =>
        Mathf.Approximately(attackPercent, o.attackPercent) &&
        Mathf.Approximately(defensePercent, o.defensePercent) &&
        Mathf.Approximately(attackSpeed,  o.attackSpeed)  &&
        Mathf.Approximately(moveSpeed,    o.moveSpeed)    &&
        Mathf.Approximately(critChance,   o.critChance)   &&
        Mathf.Approximately(critDamage,   o.critDamage)   &&
        Mathf.Approximately(skillDamage,  o.skillDamage)  &&
        Mathf.Approximately(allDamage,    o.allDamage)    &&
        Mathf.Approximately(maxHpPercent, o.maxHpPercent);
}
