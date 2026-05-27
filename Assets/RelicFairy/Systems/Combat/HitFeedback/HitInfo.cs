using UnityEngine;

/// <summary>
/// 히트 이벤트 페이로드.
/// ColliderInstance(이펙트 판정) → HitFeedbackService.RaiseHit 경로로 전달되어
/// 공격자/피격자 양측 연출 구독자에게 전파된다.
/// </summary>
public readonly struct HitInfo
{
    public readonly GameObject       Attacker;
    public readonly GameObject       Target;
    public readonly Vector3          HitPoint;         // 이펙트-타겟 접촉점 (ClosestPoint)
    public readonly Vector3          HitNormal;        // 피격자 → 공격자 방향 (반사/파티클 용)
    public readonly Vector3          AttackDirection;  // 공격자 → 피격자 방향
    public readonly float            Damage;
    public readonly bool             IsCritical;
    public readonly WeaponActionType ActionType;

    public HitInfo(
        GameObject attacker,
        GameObject target,
        Vector3 hitPoint,
        Vector3 attackDirection,
        float damage,
        bool isCritical,
        WeaponActionType actionType)
    {
        Attacker        = attacker;
        Target          = target;
        HitPoint        = hitPoint;
        AttackDirection = attackDirection.sqrMagnitude > 0.0001f ? attackDirection.normalized : Vector3.forward;
        HitNormal       = -AttackDirection;
        Damage          = damage;
        IsCritical      = isCritical;
        ActionType      = actionType;
    }
}
