using UnityEngine;
using RelicFairy.Monster;

// ═══════════════════════════════════════════════════════════════════════════
// 타이밍형 T3/T4 아이템 전투 효과 (설계 §5)
//
// 즉시 구현(쉬움/중): SkillCdReset / RoomEntryWindow / NoHitThenCrit /
//   ExecuteBonus / CombatStartWindow / DoubleHitTiming(타임스탬프).
// ★windup/형태 의존(JustGuard / AttackInterrupt / StationaryRangeBuff):
//   ContributeCombatMods로 변형 스냅샷에 기여 → ColliderInstance/AttackState(P5)가 소비.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>완벽한 순간 / 완전한 순간 — 적 공격 windup 중 공격 시 +value 피해.
/// 윈도 판정·적용은 ColliderInstance(P5)가 justGuardBonus와 적 IsTelegraphingAttack로 수행.</summary>
public sealed class JustGuardEffect : ItemCombatEffectBase
{
    public JustGuardEffect(ItemEffectSlot s) : base(s) { }

    public override void ContributeCombatMods(ItemEffectContext ctx, ref ItemCombatModifiers mods)
        => mods.justGuardBonus += _value > 0f ? _value : 0.5f;
}

/// <summary>섬광의 순간 — windup 중인 적 적중 시 적 공격 캔슬. 캔슬은 ColliderInstance(P5)가 수행.</summary>
public sealed class AttackInterruptEffect : ItemCombatEffectBase
{
    public AttackInterruptEffect(ItemEffectSlot s) : base(s) { }

    public override void ContributeCombatMods(ItemEffectContext ctx, ref ItemCombatModifiers mods)
        => mods.attackInterrupt = true;
}

/// <summary>마지막 기회 — 적중 시 잔여쿨≤value인 스킬을 즉시 리셋(내부쿨 duration초, 기본 10).</summary>
public sealed class SkillCdResetEffect : ItemCombatEffectBase
{
    private float _cooldownEnd;

    public SkillCdResetEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Time.time < _cooldownEnd) return;
        var ct = ctx?.Player?.CooldownTracker;
        if (ct == null) return;

        float thr = _value > 0f ? _value : 1f;
        for (int i = 0; i < 3; i++)
        {
            var sk = (SkillType)i;
            float rem = ct.GetRemaining(sk);
            if (rem > 0f && rem <= thr)
            {
                ct.ResetCooldown(sk);
                _cooldownEnd = Time.time + (_duration > 0f ? _duration : 10f);
                if (ctx.Player != null) ItemGuide.Toast(ctx.Player.transform.position, "쿨 리셋");
                return;
            }
        }
    }
}

/// <summary>교차하는 칼날 / 이중 교차 — 0.1초 내 2적중 시 추가 피해 +value. value3≥1이면 약점표식(받피 증폭, T4).</summary>
public sealed class DoubleHitTimingEffect : ItemCombatEffectBase
{
    private const float Window = 0.1f;
    private float _lastHitTime = -999f;

    public DoubleHitTimingEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        float now = Time.time;
        bool doubled = now - _lastHitTime <= Window;
        _lastHitTime = now;
        if (!doubled || report.Target == null || report.DamageDealt <= 0f) return;

        float bonus = report.DamageDealt * (_value > 0f ? _value : 1f);
        CombatQuery.DealSynergyDamage(report.Target, bonus, Self(ctx), 1f, report.IsCrit);
        ItemGuide.Flash(report.Target.transform.position, report.IsCrit);

        if (_value3 >= 1f)   // T4: 약점표식
        {
            var mb = report.Target.GetComponentInParent<MonsterBase>();
            if (mb != null) mb.ApplyDamageTakenAmp(0.2f, 4f, "vulnerable");
        }
    }
}

/// <summary>전환의 틈 — 방 진입 후 value2초 윈도 동안 모든 피해 +value.</summary>
public sealed class RoomEntryWindowEffect : ItemCombatEffectBase
{
    private float _windowUntil = -999f;

    public RoomEntryWindowEffect(ItemEffectSlot s) : base(s) { }

    public override void OnRoomEnter(ItemEffectContext ctx)
    {
        _windowUntil = Time.time + (_value2 > 0f ? _value2 : 3f);
        if (ctx.Player != null) ItemGuide.Badge(ctx.Player.transform, "item_roomwindow", "전환");
    }

    public override void ContributeDynamicStats(ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        if (Time.time < _windowUntil) dyn.allDamage += _value;
        else ItemGuide.ClearBadge("item_roomwindow");
    }
}

/// <summary>숨 고르기 — 무피격 value초 후 다음 공격을 ×value2 확정 크리티컬로.</summary>
public sealed class NoHitThenCritEffect : ItemCombatEffectBase
{
    private float _lastHitTime;
    private bool _armed;

    public NoHitThenCritEffect(ItemEffectSlot s) : base(s) { _lastHitTime = -999f; }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        _lastHitTime = Time.time;
        _armed = false;
        ItemGuide.ClearBadge("item_breath");
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (_armed) return;
        float need = _value > 0f ? _value : 4f;
        if (Time.time - _lastHitTime < need) return;
        _armed = true;
        ctx.Stats?.ArmForceCrit(_value2 > 0f ? _value2 : 2f);   // 다음 1타 강제 크릿(배율·연출 반영)
        if (ctx.Player != null) ItemGuide.Badge(ctx.Player.transform, "item_breath", "정조준");
    }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (_armed) { _armed = false; ItemGuide.ClearBadge("item_breath"); }
    }
}

/// <summary>끝맺음의 검 / 확정의 끝맺음 — 예상피해로 처치 가능한 대상에 +value(확정처치).
/// value3≥1이면 처치 시 0.5초 모든 피해 +30%(T4).</summary>
public sealed class ExecuteBonusEffect : ItemCombatEffectBase
{
    private float _killWindowUntil = -999f;

    public ExecuteBonusEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPreDealDamage(ItemEffectContext ctx, ref DamagePacket pkt)
    {
        if (pkt.Target == null || pkt.FinalDamage <= 0f) return;
        var mb = pkt.Target.GetComponentInParent<MonsterBase>();
        if (mb == null || mb.CurrentHp <= 0) return;

        // 예상피해(현 패킷×(1+value))로 처치권이면 보너스 적용 → 확정처치
        float boosted = pkt.FinalDamage * (1f + _value);
        if (mb.CurrentHp <= boosted)
            pkt.FinalDamage = boosted;
    }

    public override void OnKill(ItemEffectContext ctx, GameObject target)
    {
        if (_value3 >= 1f)
        {
            _killWindowUntil = Time.time + 0.5f;
            if (ctx.Player != null) ItemGuide.Toast(ctx.Player.transform.position, "처형");
        }
    }

    public override void ContributeDynamicStats(ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        if (Time.time < _killWindowUntil) dyn.allDamage += 0.30f;
    }
}

/// <summary>여명의 일격 / 영원한 여명 — 방 진입 후 value2초간 치확 +value. value3>0이면 윈도 후에도 치확 +value3 유지(T4).</summary>
public sealed class CombatStartWindowEffect : ItemCombatEffectBase
{
    private float _windowUntil = -999f;
    private bool _entered;

    public CombatStartWindowEffect(ItemEffectSlot s) : base(s) { }

    public override void OnRoomEnter(ItemEffectContext ctx)
    {
        _windowUntil = Time.time + (_value2 > 0f ? _value2 : 5f);
        _entered = true;
        if (ctx.Player != null) ItemGuide.Badge(ctx.Player.transform, "item_dawn", "여명");
    }

    public override void ContributeDynamicStats(ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        if (Time.time < _windowUntil) dyn.critChance += _value;
        else
        {
            if (_entered && _value3 > 0f) dyn.critChance += _value3;   // T4: 이후 영구 치확
            ItemGuide.ClearBadge("item_dawn");
        }
    }
}

/// <summary>기다림의 미학 — 정지 value초 유지 시 근접 사거리 +value2. 이동 시 해제. ColliderInstance(P5)가 사거리 소비.</summary>
public sealed class StationaryRangeBuffEffect : ItemCombatEffectBase
{
    private float _idle;
    private bool _charged;

    public StationaryRangeBuffEffect(ItemEffectSlot s) : base(s) { }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (ctx?.Player == null) return;
        bool moving = ctx.Player.MoveDirection.sqrMagnitude > 0.01f;
        if (moving) { _idle = 0f; if (_charged) { _charged = false; ItemGuide.ClearBadge("item_stance"); } return; }
        if (_charged) return;
        _idle += deltaTime;
        if (_idle >= (_value > 0f ? _value : 5f))
        {
            _charged = true;
            ItemGuide.Badge(ctx.Player.transform, "item_stance", "정자세");
        }
    }

    public override void ContributeCombatMods(ItemEffectContext ctx, ref ItemCombatModifiers mods)
    {
        if (_charged) mods.meleeRangeMult += _value2 > 0f ? _value2 : 0.3f;
    }
}

/// <summary>순간의 균열 / 균열의 시간 — 스킬 시전 직후 윈도 중 피격해도 스킬 강화 유지(스킬피해 +value).
/// (시전 캔슬 가드의 데미지 보강분을 실구현; FSM 무중단 유지는 P5 가드.)</summary>
public sealed class SkillCastGuardEffect : ItemCombatEffectBase
{
    private float _castUntil = -999f;
    private bool _guardArmed;

    public SkillCastGuardEffect(ItemEffectSlot s) : base(s) { }

    public override void OnSkillUse(ItemEffectContext ctx, SkillType skill)
    {
        _castUntil = Time.time + (_duration > 0f ? _duration : 0.6f);
    }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Time.time <= _castUntil)
        {
            _guardArmed = true;
            _castUntil = Time.time + (_duration > 0f ? _duration : 0.6f);   // 강화 유지 연장
            if (ctx.Player != null) ItemGuide.Badge(ctx.Player.transform, "item_castguard", "시전유지");
        }
    }

    public override void ContributeDynamicStats(ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        if (_guardArmed && Time.time <= _castUntil) dyn.skillDamage += _value;
        else if (_guardArmed) { _guardArmed = false; ItemGuide.ClearBadge("item_castguard"); }
    }
}
