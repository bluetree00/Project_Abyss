using UnityEngine;

/// <summary>
/// 장비 스킬(E · R) 실제 재사용 시간(초) — 재련소 마스터리 「E 재사용 5.0 → 4.85초」 전후가 읽는다(10-12 A9).
/// 실제 시전과 같은 셈: 스킬 SO 재사용 × 악몽 규칙 배율 × (1 − 전체 재사용 감소). R 칸은 무기 데이터의 skillQ다.
/// </summary>
public static class SkillCooldownPreview
{
    /// <param name="masteryLevel">이 마스터리 단계일 때의 초 — 지금 단계는 WeaponEnhanceService.MasteryLevel(w, Table).</param>
    /// <returns>0이면 그 칸에 스킬이 없거나 강화 표가 아직 없다.</returns>
    public static float Seconds(PlayerController player, WeaponData weapon, SkillType slot, int masteryLevel)
    {
        var skill = slot == SkillType.E ? weapon?.skillE : slot == SkillType.R ? weapon?.skillQ : null;
        var table = WeaponEnhanceService.Table;
        if (player == null || player.RuntimeStats == null || skill == null || table == null) return 0f;
        float cdr = player.RuntimeStats.SkillCooldownReductionWithMastery(table.MasterySkillCdr(Mathf.Max(0, masteryLevel)));
        return skill.cooldown * NightmareRules.WeaponSkillCooldownMultiplier * (1f - cdr);
    }
}
