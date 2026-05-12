using UnityEngine;

/// <summary>
/// 원소 효과를 받을 수 있는 대상이 노출하는 표면.
/// ElementBehavior가 효과 적용 시 호출하는 콜백 인터페이스.
/// 구현체는 자신의 시스템(NavMesh, 회복 등)에 맞게 처리.
/// </summary>
public interface IElementTarget
{
    Transform Transform { get; }
    GameObject GameObject { get; }
    float MaxHp { get; }

    /// <summary>DoT 등 원소 효과로 인한 데미지.</summary>
    void TakeElementalDoT(float damage, ElementType source);

    /// <summary>받는 데미지 배율 (미사용 가능, 레거시).</summary>
    void SetIncomingDamageMultiplier(float multi);

    /// <summary>이동 속도 배율 (0 = 정지). Water slow / Earth petrify / Lightning paralyze.</summary>
    void SetMovementMultiplier(float multi);

    /// <summary>공격 속도 배율 (0 = 공격 불가). Water slow / Lightning paralyze.</summary>
    void SetAttackSpeedMultiplier(float multi);

    /// <summary>방어력 배율 (1.0 = 기본, 0.9 = -10%). Earth petrify.</summary>
    void SetDefenseMultiplier(float multi);
}
