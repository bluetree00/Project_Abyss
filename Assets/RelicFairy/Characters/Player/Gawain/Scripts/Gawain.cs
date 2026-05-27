using System;
using UnityEngine;

/// <summary>
/// 유물 4 — 태양의 서약 (가웨인)
/// 스타일: 중전사형 / 타이밍 집중 / 버스트 딜러
///
/// 패시브 1개 + Q스킬 1개 (기획 원칙).
///   패시브: 태양의 검 — 기본 공격 적중 시 화상(DoT) 부여
///   Q스킬:  태양의 검흔 — 검 휘두름 → 궤적에 불 장판 생성
/// 메커닉(태양 타이머)은 강화/약화 사이클을 자동으로 굴리며, 강화 구간 시 공격 배율을 적용한다.
/// </summary>
public class Gawain : PlayerController
{
    private SolarTimer _solarTimer;
    private Action<SolarPhase> _onPhaseChanged;

    // ── 초기화 ───────────────────────────────────────────────────────────────

    protected override void InitLayerFSMs()
    {
        RegisterDefaultFSMs();
    }

    protected override bool IsInAttackOrSkillState() =>
        base.IsInAttackOrSkillState()       ||
        actSM.CurrentId == ActState.Charge  ||
        actSM.CurrentId == ActState.HeavyAttack;

    protected override void InitPassives()
    {
        _solarTimer = gameObject.AddComponent<SolarTimer>();
        _solarTimer.Initialize(RuntimeStats);

        RegisterPassive(new GawainSolarSwordPassive());

        // delegate 인스턴스 저장 후 구독 (OnDestroy에서 정확히 해제 가능)
        _onPhaseChanged = _ => RefreshDefenseBonus();
        _solarTimer.OnPhaseChanged += _onPhaseChanged;
    }

    protected override void RouteInputsToLayers()
    {
        DefaultRouteInputsToLayers();
    }

    // ── 캐릭터 고유 스킬 ─────────────────────────────────────────────────────

    public override ISkillRuntime CreateCharacterSkillRuntime(SkillType slot)
    {
        if (slot == SkillType.Q) return new SolarStrikeSkillRuntime(_solarTimer);
        return null;
    }

    public override float GetCharacterSkillCooldown(SkillType slot)
    {
        if (slot == SkillType.Q) return 20f;
        return 0f;
    }

    // ── 공개 API ─────────────────────────────────────────────────────────────

    /// <summary>HUD에서 태양 타이머 상태에 접근할 때 사용.</summary>
    public SolarTimer SolarTimer => _solarTimer;

    // ── 내부 ─────────────────────────────────────────────────────────────────

    /// <summary>태양 페이즈 변경 시 방어 배율 갱신. 강화 구간일 때만 보정 적용.</summary>
    private void RefreshDefenseBonus()
    {
        float defenseMult = (_solarTimer != null && _solarTimer.IsEmpowered)
            ? SolarTimer.EmpoweredDefenseMult
            : 1f;

        RuntimeStats.SetCharacterDefenseBonus(defenseMult, damageReduction: 0f);
    }

    protected override void OnDestroy()
    {
        if (_solarTimer != null) _solarTimer.OnPhaseChanged -= _onPhaseChanged;
        base.OnDestroy();
    }
}
