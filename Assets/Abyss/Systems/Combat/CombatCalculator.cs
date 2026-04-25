using UnityEngine;

/// <summary>
/// 전투 계산 공통 유틸 — 크리티컬 굴림, 최종 데미지 계산.
/// 무상태 헬퍼.
/// </summary>
public static class CombatCalculator
{
    /// <summary>
    /// 무기 기준 크리티컬 굴림 + 최종 데미지 계산.
    /// 데미지에 critDamage 배율 곱하고 isCrit 결과 반환.
    /// </summary>
    public static float RollCrit(WeaponData weapon, float baseDamage, out bool isCrit)
    {
        isCrit = false;
        if (weapon == null || baseDamage <= 0f) return baseDamage;

        float chance = Mathf.Clamp(weapon.critChance, 0f, 100f);
        if (chance <= 0f) return baseDamage;

        if (Random.value * 100f < chance)
        {
            isCrit = true;
            float multi = weapon.critDamage > 0f ? weapon.critDamage : 1.25f;
            return baseDamage * multi;
        }
        return baseDamage;
    }
}
