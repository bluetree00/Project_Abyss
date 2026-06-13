using UnityEngine;

/// <summary>
/// 유물 — 성배의 수호자 (갈라하드). 방어형.
///   패시브: 빛의 반사 (받는 피해 10% 경감 + 반사) — GalahadLightReflectionPassive
///   Q스킬:  성스러운 방패 (전방 방패 전개) — HolyShieldSkillRuntime
/// 기존 Galahad.cs 로직을 합성 객체로 이주.
/// </summary>
public class GalahadRelic : IRelicBehavior
{
    private const float ShieldFrontalArcCos = 0.5f; // cos(60°) — 120° 부채꼴 절반
    private const float ShieldFrontalReduce = 0.5f; // 전방 피해 50% 감소

    private float _shieldEnd;

    public void OnAttach(PlayerController owner)
    {
        owner.RegisterRelicPassive(new GalahadLightReflectionPassive());
        // 빛의 반사 1단계: 받는 피해 10% 경감 상시 적용
        owner.RuntimeStats.SetCharacterDefenseBonus(defenseMult: 1f, damageReduction: 0.10f);
    }

    public void OnDetach(PlayerController owner) { }

    public ISkillRuntime CreateSkillRuntime(PlayerController owner, SkillType slot)
        => slot == SkillType.Q ? new HolyShieldSkillRuntime() : null;

    public float GetSkillCooldown(SkillType slot) => slot == SkillType.Q ? 18f : 0f;

    public bool CanUseSkill(SkillType slot) => true; // 레거시 — 게이팅 없음

    /// <summary>HolyShieldSkillRuntime가 호출 — 방패 활성 종료 시각 기록.</summary>
    public void SetHolyShieldActive(float duration, Vector3 forward) => _shieldEnd = Time.time + duration;

    public int ModifyIncomingDamage(PlayerController owner, int dmg, GameObject attacker)
    {
        if (IsShieldBlocking(owner, attacker))
            return Mathf.Max(0, Mathf.RoundToInt(dmg * (1f - ShieldFrontalReduce)));
        return dmg;
    }

    private bool IsShieldBlocking(PlayerController owner, GameObject attacker)
    {
        if (Time.time >= _shieldEnd || attacker == null) return false;
        Vector3 toAttacker = attacker.transform.position - owner.transform.position;
        toAttacker.y = 0f;
        if (toAttacker.sqrMagnitude < 0.01f) return false;
        return Vector3.Dot(toAttacker.normalized, owner.transform.forward) >= ShieldFrontalArcCos;
    }
}
