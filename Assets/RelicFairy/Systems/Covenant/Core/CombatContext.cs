using UnityEngine;

/// <summary>
/// 전투 이벤트 발생 시 서약에 전달되는 전투 컨텍스트.
/// </summary>
public struct CombatContext
{
    /// <summary>피해를 받거나 처치된 대상</summary>
    public GameObject Target;

    /// <summary>실제 처리된 피해량</summary>
    public float Damage;

    /// <summary>공격에 사용된 무기 타입</summary>
    public WeaponType WeaponType;

    /// <summary>치명타 여부</summary>
    public bool IsCritical;

    /// <summary>스킬 피해 여부 (Q/E)</summary>
    public bool IsSkillDamage;

    /// <summary>스킬 피해일 때 해당 슬롯</summary>
    public SkillType? Skill;
}
