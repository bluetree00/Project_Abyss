using System;
using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 영웅 유물 — 랜슬롯(찢긴 서약의 검). 차세대 프레임워크 v1.
///   리소스: 광기 스택(MadnessStack) — 적중 누적, 미공격 감쇠, MAX 자동 발동.
///   스택↑ → 공격력 상승 + 받는피해 증가(기사의 긍지로 희석). MAX 50 → 심판의 일격 자동(전방 직선 + 심판 낙인).
///   심판 후 배신의 대가 빈틈(받피+/이속-, 빈틈 중 처치 시 즉시 해제, 종료 후 스택 재시작). 스킬 수동 불가.
/// 수치는 RELIC_STAT_DATA(lancelot) 슬롯 구동. 낙인은 MonsterBase.ApplyDamageTakenAmp 재사용.
/// </summary>
public sealed class LancelotMadnessRelic : IRelicBehavior, IBuffViewSource
{
    private const string RelicKey = "lancelot";
    private const int V_STACK_ATK = 3, V_TAKEN_CAP = 4, V_GUARD = 5, V_FALTER_DUR = 6,
                      V_FALTER_TAKEN = 7, V_FALTER_MOVE = 8, V_RESTART = 9, V_SKILL_BASE = 10,
                      V_SKILL_PER = 11, V_BRAND_DUR = 12, V_BRAND_AMP = 13;
    private const float StrikeRange = 6f;     // 전방 직선 사거리(근사)
    private const float StrikeCos   = 0.7f;   // 전방 ±45도
    // 기본 패시브 설명(버프창 첫 셀의 호버 툴팁). 광기 스택/심판/빈틈 사이클 요약.
    private const string PassiveTip =
        "찢긴 서약의 검 — 적중으로 광기를 쌓아 공격력이 오르지만 받는 피해도 늘어난다 (미공격 시 감쇠). 광기 최대치에서 심판의 일격, 이후 '빈틈'";

    private PlayerController _owner;
    private MadnessStack     _madness;
    private Action           _onChanged, _onMax;

    public MadnessStack Madness => _madness;

    public void OnAttach(PlayerController owner)
    {
        _owner   = owner;
        _madness = owner.gameObject.AddComponent<MadnessStack>();
        _madness.Initialize();

        owner.RegisterRelicPassive(new LancelotMadnessPassive());  // OnAttackHit → 스택 +1
        owner.RegisterRelicPassive(new LancelotBetrayalPassive()); // OnKill → 빈틈 즉시 해제

        _onChanged = RefreshStackBuff;
        _onMax     = JudgmentStrike;
        _madness.OnChanged    += _onChanged;
        _madness.OnMaxReached += _onMax;
        RefreshStackBuff();
    }

    public void OnDetach(PlayerController owner)
    {
        if (_madness != null) { _madness.OnChanged -= _onChanged; _madness.OnMaxReached -= _onMax; }
        var rs = _owner != null ? _owner.RuntimeStats : null;
        rs?.SetCharacterAttackMultiplier(1f, 1f);
        rs?.SetRelicMoveSpeedBonus(0f);
        GuidelineVisual.ClearBadge("lancelot");   // [가이드라인 비주얼]
    }

    public ISkillRuntime CreateSkillRuntime(PlayerController owner, SkillType slot) => null; // 자동 발동(스킬런타임 없음)
    public float GetSkillCooldown(SkillType slot) => 0f;
    public bool  CanUseSkill(SkillType slot) => false; // 수동 발동 불가
    public int ModifyIncomingDamage(PlayerController owner, int dmg, GameObject attacker)
    {
        if (_madness == null || dmg <= 0) return dmg;
        float ratio     = _madness.Ratio;
        float increase  = ratio * V(V_TAKEN_CAP, 0.30f);                        // 스택 받는피해 증가
        float reduction = V(V_GUARD, 0.10f) * (1f - ratio);                    // 기사의 긍지(스택 희석)
        float falter    = _madness.IsFaltering ? V(V_FALTER_TAKEN, 0.50f) : 0f; // 배신의 대가 빈틈
        float mult = (1f + increase) * (1f - reduction) * (1f + falter);
        return Mathf.Max(0, Mathf.RoundToInt(dmg * mult));
    }

    /// <summary>광기 패시브(OnAttackHit)가 호출 — 스택 +1.</summary>
    public void AddStack() => _madness?.AddStack(1);

    private static float V(int slot, float fallback)
        => Managers.RelicStatData != null ? Managers.RelicStatData.Get(RelicKey, slot, fallback) : fallback;

    /// <summary>스택 비율에 따라 공격력 배율 갱신(0→+상한). 스택 변화 시마다.</summary>
    private void RefreshStackBuff()
    {
        var rs = _owner != null ? _owner.RuntimeStats : null;
        if (rs == null || _madness == null) return;
        float atkMul = 1f + _madness.Ratio * V(V_STACK_ATK, 0.40f);
        rs.SetCharacterAttackMultiplier(atkMul, atkMul);
        // 배신의 대가 빈틈 — 이동속도 감소
        rs.SetRelicMoveSpeedBonus(_madness.IsFaltering ? -V(V_FALTER_MOVE, 0.20f) : 0f);

        // [가이드라인 비주얼] 광기 스택/빈틈 배지(통지만)
        if (_owner != null)
        {
            if (_madness.IsFaltering)    GuidelineVisual.SetBadge(_owner.transform, "lancelot", "빈틈", GuidelineVisual.BadgeTint.Dark);
            else if (_madness.Stacks > 0) GuidelineVisual.SetBadge(_owner.transform, "lancelot", "광기 " + _madness.Stacks, GuidelineVisual.BadgeTint.Dark);
            else                          GuidelineVisual.ClearBadge("lancelot");
        }
    }

    /// <summary>MAX 스택 도달 시 자동 발동 — 전방 직선 관통 ATK×(base+stack×per) + 심판 낙인.</summary>
    private void JudgmentStrike()
    {
        if (_owner == null || _madness == null) return;

        int   stacks = _madness.Stacks;
        float mult   = V(V_SKILL_BASE, 2.0f) + stacks * V(V_SKILL_PER, 0.08f);
        int   effAtk = _owner.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee);
        float dmg    = DamageFormula.Calculate(mult, effAtk);
        float brandDur = V(V_BRAND_DUR, 4f);
        float brandAmp = V(V_BRAND_AMP, 0.20f);

        var pt = _owner.transform;
        Vector3 origin = pt.position;
        Vector3 fwd = pt.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) { _madness.StartFalter(V(V_FALTER_DUR, 4f), Mathf.RoundToInt(V(V_RESTART, 10f))); return; }
        fwd.Normalize();

        // [가이드라인 비주얼] 심판 일격 발동 — 전방 직선(±45°) 윤곽 + 발동 토스트
        GuidelineVisual.Cone(origin, fwd, StrikeRange, 45f);
        GuidelineVisual.Toast(origin + Vector3.up * 2.4f, "심판의 일격", GuidelineVisual.ToastKind.Relic);

        var owner = _owner.gameObject;
        var cols  = Physics.OverlapSphere(origin, StrikeRange);
        foreach (var col in cols)
        {
            if (col == null || col.gameObject == owner) continue;
            Vector3 to = col.transform.position - origin; to.y = 0f;
            if (to.sqrMagnitude < 0.001f) continue;
            if (Vector3.Dot(fwd, to.normalized) < StrikeCos) continue; // 전방 직선 밖

            var d = col.GetComponent<IDamageable>() ?? col.GetComponentInParent<IDamageable>();
            if (d == null || d is not Component dc) continue;

            d.TakeDamage(dmg, owner, 0.4f);
            GuidelineVisual.SynergyDamage(dc.transform.position + Vector3.up * 1.2f, false);   // [가이드라인 비주얼] 적중 피해 플래시
            // 심판 낙인 — 적 받는피해 증폭(이후 피해 +brandAmp, brandDur초). 마커는 ApplyDamageTakenAmp(기본 "brand")가 표시.
            dc.GetComponentInParent<MonsterBase>()?.ApplyDamageTakenAmp(brandAmp, brandDur);
        }

        // 발동 후 스택 재시작(배신의 대가 재시작값은 후속 — 현재 0/슬롯값)
        _madness.StartFalter(V(V_FALTER_DUR, 4f), Mathf.RoundToInt(V(V_RESTART, 10f)));
    }

    // ── 버프창 수집(IBuffViewSource) ────────────────────────
    /// <summary>현재 지속 상태(빈틈/광기 N)를 버프창 항목으로 기여. 머리 위 배지와 동일 판정(읽기 전용).</summary>
    public void Contribute(List<BuffViewItem> into)
    {
        if (_owner == null || _madness == null) return;

        // 기본 패시브(항상 첫 셀, 게이지 없는 상시 표시) — 상세는 호버 툴팁(Label)으로.
        into.Add(new BuffViewItem("dmg", PassiveTip, 1, -1f, "", BuffSource.Relic, isDebuff: false));

        // 광기: 카운트=스택 배지(×N), 게이지=MAX(심판)까지 진행도(Fill=Ratio). 빈틈: 남은시간 미노출 → 게이지 없음.
        if (_madness.IsFaltering)
            into.Add(new BuffViewItem("dark", "빈틈", 1, -1f, "", BuffSource.Relic, isDebuff: true));
        else if (_madness.Stacks > 0)
            into.Add(new BuffViewItem("dark", "광기", _madness.Stacks, _madness.Fill, "", BuffSource.Relic, isDebuff: false));
    }
}
