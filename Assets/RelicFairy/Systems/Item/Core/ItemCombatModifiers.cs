using UnityEngine;

/// <summary>
/// 아이템 "공격 판정 변형" 누적 스냅샷(형태/사거리/다단/투사체/타이밍 보강).
///
/// ItemDynamicStats가 스탯 채널을 다루는 것과 동일하게, 이건 무기 판정 코드(ColliderInstance 등)가
/// 매 공격 스폰/적중 시 읽는 "현재 활성 변형"이다. ItemEffectManager.OnTick이 활성 효과의
/// ContributeCombatMods를 합산해 <see cref="ItemCombatMods.Current"/>에 push한다.
///
/// 소비처:
///  • ColliderInstance.Activate  — meleeRangeMult로 근접 콜라이더 스케일.
///  • ColliderInstance.ApplyDamage(근접) — meleeExtraHits/Ratio 추가타, meleeCircle 원형 오버랩,
///    justGuardBonus(적 windup 중 공격 시), attackInterrupt(적 공격 캔슬).
///  • 스킬 콜라이더 적중 — skillExtraProjectiles 추가 오버랩.
/// </summary>
public struct ItemCombatModifiers
{
    public float meleeRangeMult;       // 근접 사거리 배율 가산(0.2 = +20%)
    public int   meleeExtraHits;       // 근접 1스윙당 추가 타격 수
    public float meleeExtraHitRatio;   // 추가 타격 1회 피해 비율(0.8 = 80%)
    public bool  meleeCircle;          // 부채꼴→원형(스윙마다 원형 오버랩 추가)
    public float meleeCircleRadius;    // 원형 오버랩 반경
    public int   skillExtraProjectiles;// 스킬 추가 투사체(오버랩 추가타) 수
    public float justGuardBonus;       // 적 windup 중 공격 시 추가 피해 비율(0.5 = +50%)
    public bool  attackInterrupt;      // windup 중인 적 적중 시 적 공격 캔슬

    public bool Approximately(in ItemCombatModifiers o) =>
        Mathf.Approximately(meleeRangeMult,     o.meleeRangeMult)     &&
        meleeExtraHits == o.meleeExtraHits                            &&
        Mathf.Approximately(meleeExtraHitRatio, o.meleeExtraHitRatio) &&
        meleeCircle == o.meleeCircle                                  &&
        Mathf.Approximately(meleeCircleRadius,  o.meleeCircleRadius)  &&
        skillExtraProjectiles == o.skillExtraProjectiles             &&
        Mathf.Approximately(justGuardBonus,     o.justGuardBonus)     &&
        attackInterrupt == o.attackInterrupt;
}

/// <summary>현재 활성 아이템 공격 변형 스냅샷의 전역 접근점(무기 판정 코드가 읽음).</summary>
public static class ItemCombatMods
{
    public static ItemCombatModifiers Current;

    public static void Clear() => Current = default;
}
