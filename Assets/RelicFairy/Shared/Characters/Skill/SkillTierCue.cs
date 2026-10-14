using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// 장비 스킬 단계(1 → 2 → 3)가 오른 뒤 그 스킬을 처음 쓸 때 한 번 — 얇은 글 「스킬 이름 N단계」 + 짧은 소리(10-12 S6).
/// 아이콘만 조용히 바뀌어 단계가 오른 줄 몰랐다. 무기 데이터(런마다 새로 만든다)마다 E · R 칸별로 이미 알린 단계를 기억한다.
/// 진화로 2단계에서 시작한 무기도 첫 사용에 한 번 알린다. 로마 숫자는 3D 글 폰트 밖일 수 있어 「N단계」로 쓴다.
/// </summary>
public static class SkillTierCue
{
    private static readonly Color TextColor = new(0.86f, 0.93f, 1f);
    private static readonly ConditionalWeakTable<WeaponData, int[]> s_shown = new();   // [E, R] 알린 단계(0 = 아직 · 1단계는 알리지 않는다)

    public static void OnUse(PlayerController player, WeaponData weapon, SkillType slot, SkillSO skill)
    {
        if (player == null || weapon == null || skill == null || slot == SkillType.Q) return;
        int tier = weapon.SkillTier;
        var shown = s_shown.GetValue(weapon, _ => new int[2]);
        int i = slot == SkillType.E ? 0 : 1;
        if (tier <= Mathf.Max(1, shown[i])) return;
        shown[i] = tier;
        RelicFloatText.Show(player.transform.position, $"{skill.skillName} {tier}단계", TextColor);
        Managers.Sound?.PlayEvent(SoundEvent.SkillTierUp);
    }
}
