using System;
using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 영웅 유물 — 랜슬롯(찢긴 서약의 검). 리워크 v2.
///   리소스: 광기 스택(MadnessStack) — 적중 누적, 미공격 감쇠.
///   스택↑ → 공격력 상승 + 받는피해 증가(기사의 긍지로 희석, 상한 완화).
///   <b>MAX 50 → 광란(Frenzy) 6초 진입</b>: 이동속도↑ + 흡혈, 심판의 일격 <b>수동 1회</b> 활성.
///   광란 종료 → 게이지 초기화. (빈틈·49홀드·자동발동 제거)
/// 수치는 RELIC_STAT_DATA(lancelot) 슬롯 구동. 낙인은 MonsterBase.ApplyDamageTakenAmp 재사용.
/// </summary>
public sealed class LancelotMadnessRelic : IRelicBehavior, IBuffViewSource, IRelicResourceProvider
{
    private const string RelicKey = "lancelot";
    private const int V_STACK_ATK = 3, V_TAKEN_CAP = 4, V_GUARD = 5, V_FRENZY_DUR = 6,
                      V_LIFESTEAL = 7, V_FRENZY_MOVE = 8, V_SKILL_BASE = 10,
                      V_SKILL_PER = 11, V_BRAND_DUR = 12, V_BRAND_AMP = 13;
    private const string PassiveTip =
        "찢긴 서약의 검 — 적중으로 광기를 쌓아 공격력이 오르지만 받는 피해도 늘어난다. 광기 최대치에서 '광란'(이속·흡혈)에 들며 심판의 일격을 쓸 수 있다";

    // VFX Addressable 키(에셋 배선 후 실 프리팹 등록). 미등록 시 무해.
    private const string FrenzyVfxKey   = "vfx_lancelot_frenzy";    // 광란 오라(상태 토글)
    private const string JudgmentVfxKey = "vfx_lancelot_judgment";  // 심판의 일격 히트

    private PlayerController _owner;
    private MadnessStack     _madness;
    private RelicStateVfx    _vfx;
    private Action           _onChanged;
    private Action<bool>     _onFrenzy;
    private bool             _usedThisFrenzy;

    public MadnessStack Madness => _madness;
    public IRelicResource RelicResource => _madness;   // HUD 아이덴티티 바 연결
    public bool  IsFrenzy      => _madness != null && _madness.IsFrenzy;
    public float LifestealPct  => V(V_LIFESTEAL, 0.40f);

    public void OnAttach(PlayerController owner)
    {
        _owner   = owner;
        _madness = owner.gameObject.AddComponent<MadnessStack>();
        _madness.Initialize();
        _vfx     = owner.gameObject.AddComponent<RelicStateVfx>();
        _vfx.Register("frenzy", FrenzyVfxKey);

        owner.RegisterRelicPassive(new LancelotMadnessPassive());          // OnAttackHit → 스택 +1
        owner.RegisterRelicPassive(new LancelotFrenzyLifestealPassive());  // OnAttackHit(광란) → 흡혈
        owner.RegisterRelicPassive(new LancelotBetrayalPassive());         // OnKill(광란) → 지속 연장

        _onChanged = RefreshStackBuff;
        _onFrenzy  = OnFrenzyChanged;
        _madness.OnChanged     += _onChanged;
        _madness.OnMaxReached  += HandleMaxReached;
        _madness.OnFrenzyChanged += _onFrenzy;
        RefreshStackBuff();
    }

    public void OnDetach(PlayerController owner)
    {
        if (_madness != null)
        {
            _madness.OnChanged     -= _onChanged;
            _madness.OnMaxReached  -= HandleMaxReached;
            _madness.OnFrenzyChanged -= _onFrenzy;
        }
        var rs = _owner != null ? _owner.RuntimeStats : null;
        rs?.SetCharacterAttackMultiplier(1f, 1f);
        rs?.SetRelicMoveSpeedBonus(0f);
        if (_vfx != null) { UnityEngine.Object.Destroy(_vfx); _vfx = null; }
        GuidelineVisual.ClearBadge("lancelot");
    }

    // 심판의 일격 = 광란 중 수동 1회
    public ISkillRuntime CreateSkillRuntime(PlayerController owner, SkillType slot)
        => slot == SkillType.Q ? new JudgmentStrikeRuntime(this) : null;
    public float GetSkillCooldown(SkillType slot) => 0f; // 광란 게이팅 + 광란당 1회
    public bool  CanUseSkill(SkillType slot)
        => slot == SkillType.Q && _madness != null && _madness.IsFrenzy && !_usedThisFrenzy;

    public int ModifyIncomingDamage(PlayerController owner, int dmg, GameObject attacker)
    {
        if (_madness == null || dmg <= 0) return dmg;
        float ratio     = _madness.Ratio;
        float increase  = ratio * V(V_TAKEN_CAP, 0.20f);        // 스택 받는피해 증가(완화: 상한 20%)
        float reduction = V(V_GUARD, 0.10f) * (1f - ratio);    // 기사의 긍지(저스택 희석)
        float mult = (1f + increase) * (1f - reduction);
        return Mathf.Max(0, Mathf.RoundToInt(dmg * mult));
    }

    /// <summary>광기 패시브(OnAttackHit)가 호출 — 스택 +1.</summary>
    public void AddStack() => _madness?.AddStack(1);

    /// <summary>심판의 일격 런타임이 발동 시 호출 — 광란당 1회 소비 + 스트라이크 실행.</summary>
    public void PerformJudgmentStrike(Transform origin)
    {
        _usedThisFrenzy = true;
        if (_owner == null || origin == null) return;

        int   stacks   = _madness != null ? _madness.Stacks : 0;
        float mult     = V(V_SKILL_BASE, 2.0f) + stacks * V(V_SKILL_PER, 0.08f);
        int   effAtk   = _owner.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee);
        float dmg      = DamageFormula.Calculate(mult, effAtk);
        float brandDur = V(V_BRAND_DUR, 4f);
        float brandAmp = V(V_BRAND_AMP, 0.20f);

        Vector3 pos = origin.position;
        Vector3 fwd = origin.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) return;
        fwd.Normalize();

        GuidelineVisual.Cone(pos, fwd, 6f, 45f);
        GuidelineVisual.Toast(pos + Vector3.up * 2.4f, "심판의 일격", GuidelineVisual.ToastKind.Relic);
        RelicStateVfx.PlayOneShot(JudgmentVfxKey, pos + fwd * 2f + Vector3.up * 0.8f); // 전방 참격 1회

        var owner = _owner.gameObject;
        var buffer = new List<MonsterBase>(16);
        CombatQuery.GetEnemiesInCone(pos, fwd, 6f, 45f, 32, buffer);
        foreach (var mb in buffer)
        {
            if (mb == null || mb.gameObject == owner) continue;
            if (mb is IDamageable d) d.TakeDamage(dmg, owner, 0.4f);
            GuidelineVisual.SynergyDamage(mb.transform.position + Vector3.up * 1.2f, false);
            mb.ApplyDamageTakenAmp(brandAmp, brandDur); // 심판 낙인
        }
    }

    private void HandleMaxReached() => _madness?.EnterFrenzy(V(V_FRENZY_DUR, 6f));

    private void OnFrenzyChanged(bool on)
    {
        if (on) _usedThisFrenzy = false;   // 광란 진입 시 심판 1회 재충전
        _vfx?.SetActive("frenzy", on);     // 광란 오라 활성/비활성
        RefreshStackBuff();
    }

    private static float V(int slot, float fallback)
        => Managers.RelicStatData != null ? Managers.RelicStatData.Get(RelicKey, slot, fallback) : fallback;

    /// <summary>스택 비율 공격력 배율 + 광란 이동속도 갱신.</summary>
    private void RefreshStackBuff()
    {
        var rs = _owner != null ? _owner.RuntimeStats : null;
        if (rs == null || _madness == null) return;
        float atkMul = 1f + _madness.Ratio * V(V_STACK_ATK, 0.40f);
        rs.SetCharacterAttackMultiplier(atkMul, atkMul);
        rs.SetRelicMoveSpeedBonus(_madness.IsFrenzy ? V(V_FRENZY_MOVE, 0.30f) : 0f); // 광란 이속↑ (빈틈 감소 삭제)
    }

    // ── 버프창 수집(IBuffViewSource) ────────────────────────
    public void Contribute(List<BuffViewItem> into)
    {
        if (_owner == null || _madness == null) return;
        into.Add(new BuffViewItem("dmg", PassiveTip, 1, -1f, "", BuffSource.Relic, isDebuff: false));
    }
}
