using UnityEngine;

/// <summary>
/// 패시브 발동 시 함께 전달되는 컨텍스트 데이터.
/// 트리거마다 관련 필드만 채워서 전달한다.
/// </summary>
public struct PassiveContext
{
    /// <summary>대상 오브젝트 (OnAttackHit, OnKill)</summary>
    public GameObject target;

    /// <summary>데미지량 (OnAttackHit, OnKill, OnTakeDamage)</summary>
    public float damage;

    /// <summary>사용한 스킬 슬롯 (OnSkillUse)</summary>
    public SkillType? skillUsed;

    /// <summary>콤보 단계 (OnComboFinish, OnAttackHit)</summary>
    public int comboStep;
}
