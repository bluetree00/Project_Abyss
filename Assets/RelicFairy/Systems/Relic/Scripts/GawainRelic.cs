using System;
using UnityEngine;

/// <summary>
/// 유물 — 태양의 서약 (가웨인). 버스트 딜러.
///   패시브: 태양의 검 (기본공격 적중 시 화상 DoT) — GawainSolarSwordPassive
///   Q스킬:  태양의 검흔 (불 장판) — SolarStrikeSkillRuntime
/// 메커닉: SolarTimer 강화/약화 사이클. 강화 구간 방어 +30%.
/// 기존 Gawain.cs 로직을 합성 객체로 이주.
/// </summary>
public class GawainRelic : IRelicBehavior
{
    private SolarTimer _solarTimer;
    private Action<SolarPhase> _onPhaseChanged;
    private PlayerController _owner;

    /// <summary>HUD에서 태양 타이머 접근용.</summary>
    public SolarTimer SolarTimer => _solarTimer;

    public void OnAttach(PlayerController owner)
    {
        _owner = owner;
        _solarTimer = owner.gameObject.AddComponent<SolarTimer>();
        _solarTimer.Initialize(owner.RuntimeStats);

        owner.RegisterRelicPassive(new GawainSolarSwordPassive());

        _onPhaseChanged = _ => RefreshDefenseBonus();
        _solarTimer.OnPhaseChanged += _onPhaseChanged;
    }

    public void OnDetach(PlayerController owner)
    {
        if (_solarTimer != null) _solarTimer.OnPhaseChanged -= _onPhaseChanged;
    }

    public ISkillRuntime CreateSkillRuntime(PlayerController owner, SkillType slot)
        => slot == SkillType.Q ? new SolarStrikeSkillRuntime(_solarTimer) : null;

    public float GetSkillCooldown(SkillType slot) => slot == SkillType.Q ? 20f : 0f;

    public bool CanUseSkill(SkillType slot) => true; // 레거시 — 게이팅 없음



    public int ModifyIncomingDamage(PlayerController owner, int dmg, GameObject attacker) => dmg;

    /// <summary>태양 페이즈 변경 시 방어 배율 갱신. 강화 구간만 보정.</summary>
    private void RefreshDefenseBonus()
    {
        float defenseMult = (_solarTimer != null && _solarTimer.IsEmpowered)
            ? SolarTimer.EmpoweredDefenseMult
            : 1f;
        _owner.RuntimeStats.SetCharacterDefenseBonus(defenseMult, damageReduction: 0f);
    }
}
