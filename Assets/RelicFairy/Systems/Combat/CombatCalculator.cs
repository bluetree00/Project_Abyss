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
        if (baseDamage <= 0f) return baseDamage;

        // 서약 치명타 오버라이드(갤러해드 — 치명타 포기↔최소피해 보장). 응답 시 일반 굴림 대체.
        var cov = GameRunBootstrapper.Instance?.Run?.CovenantHandler;
        if (cov != null && cov.TryGetCritOverride(weapon, out bool forceCrit, out float floorRatio))
        {
            float critMulti = (weapon != null && weapon.critDamage > 0f) ? weapon.critDamage : 1.25f;
            if (forceCrit) { isCrit = true; return baseDamage * critMulti; }
            // 치명타 억제 + 최소피해 하한(최대피해×floorRatio). base보다 낮으면 base 유지.
            return Mathf.Max(baseDamage, floorRatio * baseDamage * critMulti);
        }

        // 유물 크릿 보너스(베이스/일시 버프) — 무기 크릿 위에 가산. 플레이어 공격 경로에서만 호출됨.
        var rs = GameRunBootstrapper.Instance?.Run?.Player?.RuntimeStats;
        float chanceBonus = rs?.CritChanceBonus ?? 0f;
        float damageBonus = rs?.CritDamageBonus ?? 0f;

        float weaponChance = weapon != null ? weapon.critChance : 0f;
        float chance = Mathf.Clamp(weaponChance + chanceBonus, 0f, 100f);
        if (chance <= 0f) return baseDamage;

        if (Random.value * 100f < chance)
        {
            isCrit = true;
            float baseMulti = (weapon != null && weapon.critDamage > 0f) ? weapon.critDamage : 1.25f;
            return baseDamage * (baseMulti + damageBonus);
        }
        return baseDamage;
    }
}
