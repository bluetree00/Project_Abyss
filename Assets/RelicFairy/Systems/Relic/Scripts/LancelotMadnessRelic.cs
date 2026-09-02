using System;
using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 영웅 유물 — 랜슬롯(찢긴 서약의 검). 리워크 v2.
///   리소스: 광기 스택(MadnessStack) — 적중 누적, 미공격 감쇠.
///   스택↑ → 공격력 상승 + 받는피해 증가(기사의 긍지로 희석, 상한 완화).
///   <b>MAX 50 → 광란(Frenzy) 6초 진입</b>: 이동속도↑, 심판의 일격 <b>수동 1회</b> 활성.
///   광란 종료 → 게이지 초기화. (빈틈·49홀드·자동발동 제거)
/// 수치는 RELIC_STAT_DATA(lancelot) 슬롯 구동. 낙인은 MonsterBase.ApplyDamageTakenAmp 재사용.
/// </summary>
public sealed class LancelotMadnessRelic : IRelicBehavior, IBuffViewSource, IRelicResourceProvider
{
    private const string RelicKey = "lancelot";
    // 슬롯 7(흡혈)은 광란 회복 제거로 더 이상 읽지 않는다 — 번호는 서버 차트와 맞춰 비워둔다.
    private const int V_STACK_ATK = 3, V_TAKEN_CAP = 4, V_GUARD = 5, V_FRENZY_DUR = 6,
                      V_FRENZY_MOVE = 8, V_SKILL_BASE = 10,
                      V_SKILL_PER = 11, V_BRAND_DUR = 12, V_BRAND_AMP = 13;
    private const string PassiveTip =
        "찢긴 서약의 검 — 적중으로 광기를 쌓아 공격력이 오르지만 받는 피해도 늘어난다. 광기 최대치에서 '광란'(이속)에 들며 심판의 일격을 쓸 수 있다";

    // VFX Addressable 키(에셋 배선 후 실 프리팹 등록). 미등록 시 무해.
    // 광란 오라는 2겹이다 — 붉은 분노(Rage) 위에 검보라 저주(Cursed)를 얹어
    // "분노에 잠식당한다"는 서사를 색 대비로 읽히게 한다.
    private const string FrenzyVfxKey   = "vfx_lancelot_frenzy";    // 광란 오라 ①  분노
    private const string CursedVfxKey   = "vfx_lancelot_cursed";    // 광란 오라 ②  저주 침식
    private const string JudgmentVfxKey = "vfx_lancelot_judgment";  // 심판의 일격 히트

    // RelicStateVfx는 상태 키 하나당 프리팹 하나를 잡으므로, 레이어마다 키를 따로 둔다.
    private const string FrenzyState = "frenzy";
    private const string CursedState = "frenzy_curse";

    // 심판 참격 VFX 배율 — 원본 프리팹이 실제 판정(콘 6m/±45°)보다 훨씬 커서 축소.
    // 이펙트가 판정보다 크면 "닿았는데 안 맞는다"는 체감이 생긴다.
    private const float JudgmentVfxScale     = 0.42f;   // 연타 1타분(작고 빠르게)
    private const float JudgmentFinisherVfx  = 1.5f;    // 마무리 강타 배수 → 0.63

    // 연타는 훑고, 마무리 한 방이 총 피해의 절반 가까이를 가져간다("몰아치다 마지막에 크게").
    private const float JudgmentFinisherShare = 0.45f;

    // 판정 범위 — VFX/가이드라인과 같은 값을 쓰도록 상수화(따로 놀지 않게).
    private const float JudgmentRange     = 6f;
    private const float JudgmentHalfAngle = 45f;

    private PlayerController _owner;
    private MadnessStack     _madness;
    private RelicStateVfx    _vfx;
    private Action           _onChanged;
    private Action<bool>     _onFrenzy;
    private bool             _usedThisFrenzy;

    public MadnessStack Madness => _madness;
    public IRelicResource RelicResource => _madness;   // HUD 아이덴티티 바 연결
    public bool  IsFrenzy      => _madness != null && _madness.IsFrenzy;

    public void OnAttach(PlayerController owner)
    {
        _owner   = owner;
        _madness = owner.gameObject.AddComponent<MadnessStack>();
        _madness.Initialize();
        _vfx     = owner.gameObject.AddComponent<RelicStateVfx>();
        _vfx.Register(FrenzyState, FrenzyVfxKey);
        _vfx.Register(CursedState, CursedVfxKey);

        owner.RegisterRelicPassive(new LancelotMadnessPassive());          // OnAttackHit → 스택 +1
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

    /// <summary>광기 패시브(OnAttackHit)가 호출 — 대상 등급에 따른 스택 획득(보스일수록 크다).</summary>
    public void AddStack(int amount = 1) => _madness?.AddStack(amount);

    /// <summary>
    /// 심판의 일격 — 연속 참격 1타분. 런타임(JudgmentStrikeRuntime)이 hitCount번 나눠 호출한다.
    ///
    /// 시나리오: <b>빠른 연타로 몰아친 뒤 마지막에 강력한 일격</b>.
    /// 총 피해는 단발이던 시절과 동일하고, 마무리 타가 그중 <see cref="JudgmentFinisherShare"/>를
    /// 가져가며 나머지를 앞선 연타가 균등 분할한다(밸런스 중립).
    /// 광기 스택은 광란 중 동결이라 다타로도 안 불어난다.
    ///
    /// 매 타마다 콘을 <b>다시 질의</b>하므로 도중에 들어온 적도 맞는다.
    /// 낙인은 1타에서 걸리므로 뒤 타들, 특히 마무리 강타가 증폭된 피해로 꽂힌다(연타의 보상).
    /// </summary>
    public void PerformJudgmentStrike(Transform origin, int hitIndex, int hitCount)
    {
        _usedThisFrenzy = true;
        if (_owner == null || origin == null) return;

        hitCount = Mathf.Max(1, hitCount);
        bool isFirst = hitIndex <= 0;
        bool isLast  = hitIndex >= hitCount - 1;

        // 피해 배분 — 마무리가 큰 몫, 앞선 연타가 나머지를 균등 분할.
        float share = hitCount == 1 ? 1f
                    : isLast        ? JudgmentFinisherShare
                                    : (1f - JudgmentFinisherShare) / (hitCount - 1);

        int   stacks   = _madness != null ? _madness.Stacks : 0;
        float mult     = V(V_SKILL_BASE, 2.0f) + stacks * V(V_SKILL_PER, 0.08f);
        int   effAtk   = _owner.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee);

        // ⚠️ DamageFormula.Calculate(a, b)는 <b>a + b</b>다 — '어빌리티 기본 피해 + 공격 스탯'용 가산 헬퍼.
        //    여기에 배율(2.0~5.2)을 넘기면 "공격력 + 5"가 되어 평타보다도 약해진다(실제로 그랬다).
        //    이 스킬은 설계상 ATK × 배율이므로 직접 곱한다.
        float dmg      = effAtk * mult * share;
        float brandDur = V(V_BRAND_DUR, 4f);
        float brandAmp = V(V_BRAND_AMP, 0.20f);

        Vector3 pos = origin.position;
        Vector3 fwd = origin.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) return;
        fwd.Normalize();

        if (isFirst)
        {
            GuidelineVisual.Cone(pos, fwd, JudgmentRange, JudgmentHalfAngle);
            GuidelineVisual.Toast(pos + Vector3.up * 2.4f, "심판의 일격", GuidelineVisual.ToastKind.Relic);
        }

        // 참격 VFX — 연타는 좌우로 각을 엇갈리고 높이를 바꿔 몰아치는 인상을 주고,
        // 마무리는 정면·크게 내리꽂아 한 방임을 읽히게 한다.
        float yaw    = isLast ? 0f : (hitIndex % 2 == 0 ? -14f : 14f);
        float height = isLast ? 0.9f : 0.55f + 0.12f * (hitIndex % 3);
        float scale  = JudgmentVfxScale * (isLast ? JudgmentFinisherVfx : 1f);
        Vector3 vfxDir = Quaternion.AngleAxis(yaw, Vector3.up) * fwd;
        RelicStateVfx.PlayOneShot(JudgmentVfxKey, pos + vfxDir * 2f + Vector3.up * height, scale, vfxDir);

        var owner  = _owner.gameObject;
        var buffer = new List<GameObject>(16);
        // 몬스터로 좁히지 않고 IDamageable 전체를 잡는다 — 그래야 훈련용 허수아비에도 들어간다.
        int found  = CombatQuery.GetDamageablesInCone(pos, fwd, JudgmentRange, JudgmentHalfAngle, owner, 32, buffer);

        RFLog.D($"[랜슬롯Q] {hitIndex + 1}/{hitCount}타{(isLast ? " (마무리)" : "")} | 피해 {dmg:F0} | 적중 {found}");

        foreach (var target in buffer)
        {
            if (target == null) continue;

            // 주 피해 파이프라인 — 직접 TakeDamage를 부르면 크리티컬·아이템·서약·패시브·타격감이 전부 스킵된다.
            // 넉백은 연타 중 대상이 콘 밖으로 밀려나면 뒤 타가 헛치므로, 마무리에만 세게 준다.
            CombatDamage.Deal(new CombatDamage.Request
            {
                Target              = target,
                BaseDamage          = dmg,
                Owner               = owner,
                ActionType          = WeaponActionType.QSkill,
                KnockbackMultiplier = isLast ? 0.8f : 0.05f,

                // 연타는 방어력을 무시한다.
                // 몬스터 피해식이 감산이다 — actual = max(1, 피해 − 방어). 10타로 줄여 타당 피해가
                // 2배(ATK100·40스택 기준 29)로 올랐어도, 보스 방어 20을 빼면 9만 남아 69%가 증발한다.
                // '총 피해를 n등분한 뒤 각각에 방어를 빼는' 구조 자체가 다단히트에 불리해서,
                // 타수를 줄이는 것만으로는 못 없앤다. 마무리는 한 방이라 정상 계산한다.
                DefenseIgnore       = isLast ? 0f : 1f,

                HitPoint            = target.transform.position + Vector3.up * 1.2f,
                SourcePosition      = pos,
                ComboStep           = hitIndex,
            });

            // 심판 낙인(매 타 갱신 → 지속시간은 마무리 기준). 몬스터 전용 상태라 더미는 건너뛴다.
            if (target.TryGetComponent<MonsterBase>(out var mb))
                mb.ApplyDamageTakenAmp(brandAmp, brandDur);
        }

        if (isLast) HitFeelService.CameraShake(0.14f, 0.16f);   // 마무리 임팩트
    }

    private void HandleMaxReached() => _madness?.EnterFrenzy(V(V_FRENZY_DUR, 6f));

    private void OnFrenzyChanged(bool on)
    {
        if (on) _usedThisFrenzy = false;      // 광란 진입 시 심판 1회 재충전

        // 광란 오라 2겹 — 분노 + 저주 침식. 광란 내내 함께 유지된다.
        _vfx?.SetActive(FrenzyState, on);
        _vfx?.SetActive(CursedState, on);

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
