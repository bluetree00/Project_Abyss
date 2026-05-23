using System;
using UnityEngine;

/// <summary>
/// 유물 4 — 태양의 서약 (가웨인)
/// 스타일: 중전사형 / 타이밍 집중 / 버스트 딜러
///
/// 패시브:
///   정오의 서약 — 강화 구간 공격력 3× (SolarTimer가 직접 적용)
///   명예의 기사 — HP 50% 초과 시 방어력+25%, 피해감소 10%
/// Q스킬:
///   태양의 강타 — 전방 내리찍기. 강화 구간 시 피해+100%, 범위+80%.
/// 고유 메커닉:
///   태양 타이머 — 강화(20초)/약화(15초) 자동 사이클.
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

        RegisterPassive(new GawainNoonVowPassive());
        RegisterPassive(new GawainHonorKnightPassive());

        // delegate 인스턴스 저장 후 구독 (OnDestroy에서 정확히 해제 가능)
        _onPhaseChanged = _ => RefreshDefenseBonus();
        _solarTimer.OnPhaseChanged += _onPhaseChanged;
        RuntimeStats.OnChanged     += RefreshDefenseBonus;
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

    /// <summary>태양 페이즈(+30%)와 명예의 기사(+25%, 피해감소 10%)를 합산 적용.</summary>
    private void RefreshDefenseBonus()
    {
        float defenseMult   = 1f;
        float damageReduce  = 0f;

        if (_solarTimer != null && _solarTimer.IsEmpowered)
            defenseMult *= SolarTimer.EmpoweredDefenseMult;

        var stats = RuntimeStats;
        if (stats.MaxHp > 0 && (float)stats.Hp / stats.MaxHp > 0.5f)
        {
            defenseMult   *= 1.25f;
            damageReduce  += 0.10f;
        }

        stats.SetCharacterDefenseBonus(defenseMult, damageReduce);
    }

    protected override void OnDestroy()
    {
        if (_solarTimer != null) _solarTimer.OnPhaseChanged -= _onPhaseChanged;
        RuntimeStats.OnChanged -= RefreshDefenseBonus;
        base.OnDestroy();
    }
}
