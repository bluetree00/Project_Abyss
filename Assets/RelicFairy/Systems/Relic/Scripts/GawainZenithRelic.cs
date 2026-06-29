using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 영웅 유물 — 가웨인(정오의 맹세). 차세대 프레임워크 v1.
///   리소스: 정오 게이지(ZenithGauge) — 충전/정오/쿨다운 사이클.
///   정오 구간: 공속·치명타·모든피해 강화 + 스킬 발동 가능(게이팅).
///   각인(게이지 80%+): 공격력 선행 강화.
/// 패시브2(잔열)·태양 강림 스킬·각인 첫타 +50%·HUD는 후속 슬라이스.
/// (레거시 GawainRelic은 보존 — 본 구현이 RelicRegistry에서 Gawain을 대체)
///
/// 수치는 RELIC_STAT_DATA(gawain) 슬롯 구동.
/// </summary>
public sealed class GawainZenithRelic : IRelicBehavior, IBuffViewSource
{
    private const string RelicKey = "gawain";
    private const int V_NOON_ATKSPD = 3, V_NOON_CRITCH = 4, V_NOON_CRITDMG = 5, V_NOON_ALLDMG = 6,
                      V_MARK_ATK = 8;

    private PlayerController _owner;
    private ZenithGauge      _gauge;
    private Action           _onChanged;
    private bool             _skillUsedThisNoon;
    private bool             _wasNoon;
    private bool             _wasMarkReady;        // [가이드라인 비주얼] 각인 진입 엣지 검출
    private bool             _markPendingFirstHit; // 각인: 정오 첫 공격 +50%

    public ZenithGauge Gauge => _gauge;

    /// <summary>태양 강림이 호출 — 정오 구간당 1회 소비.</summary>
    public void MarkSkillUsed() => _skillUsedThisNoon = true;

    /// <summary>각인 첫타(+50%) 보유분 소비. 정오 첫 공격(일반/스킬)이 1회만 가져간다.</summary>
    public bool ConsumeMarkFirstHit()
    {
        if (!_markPendingFirstHit) return false;
        _markPendingFirstHit = false;
        return true;
    }

    public void OnAttach(PlayerController owner)
    {
        _owner = owner;
        _gauge = owner.gameObject.AddComponent<ZenithGauge>();
        _gauge.Initialize();

        // 패시브: 각인 첫타(+50%) · 황혼 처치 충전 가속(잔열)
        owner.RegisterRelicPassive(new GawainSolarMarkPassive());
        owner.RegisterRelicPassive(new GawainAfterglowPassive());

        _onChanged = RefreshBuffs;
        _gauge.OnChanged += _onChanged;
        RefreshBuffs();
    }

    public void OnDetach(PlayerController owner)
    {
        if (_gauge != null) _gauge.OnChanged -= _onChanged;
        ClearBuffs();
        GuidelineVisual.ClearBadge("gawain"); // [가이드라인 비주얼]
    }

    public ISkillRuntime CreateSkillRuntime(PlayerController owner, SkillType slot)
        => slot == SkillType.Q ? new SolarDescentSkillRuntime(this) : null;
    public float GetSkillCooldown(SkillType slot) => 0f; // 정오 게이팅 + 구간당 1회가 발동 제어
    public bool  CanUseSkill(SkillType slot)
        => slot == SkillType.Q && _gauge != null && _gauge.IsSkillReady && !_skillUsedThisNoon;
    public int   ModifyIncomingDamage(PlayerController owner, int dmg, GameObject attacker) => dmg;

    private static float V(int slot, float fallback)
        => Managers.RelicStatData != null ? Managers.RelicStatData.Get(RelicKey, slot, fallback) : fallback;

    /// <summary>게이지 구간/각인에 따라 정오·각인 스탯 버프를 재적용. 구간 전환 시에만 호출됨.</summary>
    private void RefreshBuffs()
    {
        var rs = _owner != null ? _owner.RuntimeStats : null;
        if (rs == null || _gauge == null) return;

        // 정오 진입 시: 스킬 1회 리셋 + 각인 첫타(+50%) 적립(충전 100%→정오라 각인 항상 발동)
        if (_gauge.IsNoon && !_wasNoon)
        {
            _skillUsedThisNoon = false; _markPendingFirstHit = true;
            if (_owner != null) GuidelineVisual.Toast(_owner.transform.position + Vector3.up * 2.4f, "정오 진입", GuidelineVisual.ToastKind.Relic); // [가이드라인 비주얼]
        }
        _wasNoon = _gauge.IsNoon;

        // [가이드라인 비주얼] 각인 진입 엣지 토스트(정오 아님 + 각인 준비 상승엣지)
        bool markEntering = _gauge.IsMarkReady && !_gauge.IsNoon;
        if (markEntering && !_wasMarkReady && _owner != null)
            GuidelineVisual.Toast(_owner.transform.position + Vector3.up * 2.4f, "각인", GuidelineVisual.ToastKind.Relic);
        _wasMarkReady = markEntering;

        if (_gauge.IsNoon)
        {
            rs.SetBonusAttackSpeed(V(V_NOON_ATKSPD, 0.30f));
            rs.SetRelicCritBuff(V(V_NOON_CRITCH, 10f), V(V_NOON_CRITDMG, 0.20f));
            float allMul = 1f + V(V_NOON_ALLDMG, 0.20f);
            rs.SetCharacterAttackMultiplier(allMul, allMul);
            if (_owner != null) GuidelineVisual.SetBadge(_owner.transform, "gawain", "정오", GuidelineVisual.BadgeTint.Relic); // [가이드라인 비주얼]
        }
        else if (_gauge.IsMarkReady)
        {
            // 각인 선행 강화(충전 80%+): 공격력만. 첫타 +50%·이동속도는 후속.
            rs.SetBonusAttackSpeed(0f);
            rs.SetRelicCritBuff(0f, 0f);
            float atkMul = 1f + V(V_MARK_ATK, 0.10f);
            rs.SetCharacterAttackMultiplier(atkMul, atkMul);
            if (_owner != null) GuidelineVisual.SetBadge(_owner.transform, "gawain", "각인", GuidelineVisual.BadgeTint.Light); // [가이드라인 비주얼]
        }
        else
        {
            ClearBuffs();
            GuidelineVisual.ClearBadge("gawain"); // [가이드라인 비주얼]
        }
    }

    private void ClearBuffs()
    {
        var rs = _owner != null ? _owner.RuntimeStats : null;
        if (rs == null) return;
        rs.SetBonusAttackSpeed(0f);
        rs.SetRelicCritBuff(0f, 0f);
        rs.SetCharacterAttackMultiplier(1f, 1f);
    }

    // ── 버프창 수집(IBuffViewSource) ────────────────────────
    /// <summary>현재 지속 상태(정오/각인)를 버프창 항목으로 기여. 머리 위 배지와 동일 판정(읽기 전용).</summary>
    public void Contribute(List<BuffViewItem> into)
    {
        if (_owner == null || _gauge == null) return;
        // 게이지: 정오=남은 정오시간(1→0), 각인=충전 진행도. ZenithGauge.Fill이 구간별로 제공.
        if (_gauge.IsNoon)
            into.Add(new BuffViewItem("atk", "정오", 1, _gauge.Fill, "", BuffSource.Relic, isDebuff: false));
        else if (_gauge.IsMarkReady)
            into.Add(new BuffViewItem("atk", "각인", 1, _gauge.Fill, "", BuffSource.Relic, isDebuff: false));
    }
}
