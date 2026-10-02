using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 「플레이어 최대 체력의 X%」 피해(보스 간판 · 최후의 대마법 실패 등)의 원 피해 계산.
/// 플레이어 방어 감소(100/(100+방어))만 미리 되돌려 설계 수치대로 들어가게 한다 — 받피감 · 보호막 · 회피 무적은 그대로 적용된다.
/// 예전엔 방어가 한 번 더 깎아 35~60%가 26~45%가 됐다(10-01 보스 감사 S6).
/// </summary>
public static class BossMaxHpDamage
{
    // ── Constants ──────────────────────────────────────────────
    private const float DefenseK = 100f;   // PlayerController.TakeDamage의 방어 체감 상수와 같게 둔다

    // ── Public Methods ─────────────────────────────────────────
    /// <summary>최대 체력 × <paramref name="ratio"/>가 방어 뒤에 그대로 들어가는 원 피해(최소 1).</summary>
    public static int Raw(PlayerController player, float ratio)
    {
        var stats = player != null ? player.RuntimeStats : null;
        if (stats == null) return 1;
        return Mathf.Max(1, Mathf.CeilToInt(stats.MaxHp * ratio * DefenseFactor(stats.Defense)));
    }

    /// <summary>방어 뒤 피해가 남은 체력 − 1을 넘지 않게 원 피해를 자른다(즉사 없음 — 숲 심장 · 화룡 검은 태양).</summary>
    public static int NonLethal(PlayerController player, int raw)
    {
        var stats = player != null ? player.RuntimeStats : null;
        if (stats == null) return raw;
        int cap = Mathf.FloorToInt((stats.Hp - 1) * DefenseFactor(stats.Defense));
        return Mathf.Min(raw, cap);
    }

    // ── Private Methods ────────────────────────────────────────
    private static float DefenseFactor(int defense) => (DefenseK + Mathf.Max(0, defense)) / DefenseK;
}
}
