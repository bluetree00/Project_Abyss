using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

// ═══════════════════════════════════════════════════════════════════════════
// 광폭형 T3/T4 아이템 전투 효과 (설계 §4)
//
// "다음 일반공격 강화"는 QueueNext(bonus)로 OnPreDealDamage 버퍼에 적재.
// 타이머/누적은 OnTick·OnPostTakeDamage로 추적. 방 진입 시 재무장.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>폭주의 코어 / 종말의 코어 — value초마다 다음 공격 ×value2 강화.</summary>
public sealed class TimedEmpowerNextEffect : ItemCombatEffectBase
{
    private float _timer;

    public TimedEmpowerNextEffect(ItemEffectSlot s) : base(s) { }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        float period = _value > 0f ? _value : 30f;
        _timer += deltaTime;
        if (_timer < period) return;
        _timer = 0f;
        float mult = _value2 > 0f ? _value2 : 2.5f;
        QueueNext(mult - 1f);
        if (ctx.Player != null) ItemGuide.Toast(ctx.Player.transform.position, "폭주");
    }
}

/// <summary>심판의 파편 — HP비율이 value 밑으로 내려가는 순간 1회 주변 EffAtk×value2 광역. 방 진입 재무장.</summary>
public sealed class HpThresholdAoEEffect : ItemCombatEffectBase
{
    private static readonly List<MonsterBase> s_buf = new();
    private bool _armed = true;

    public HpThresholdAoEEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!_armed || ctx.Player == null) return;
        if (ctx.HpRatio > (_value > 0f ? _value : 0.25f)) return;
        _armed = false;

        float dmg = EffAtk(ctx) * (_value2 > 0f ? _value2 : 2f);
        Vector3 center = ctx.Player.transform.position;
        ItemGuide.Aoe(center, 5f);
        int n = CombatQuery.GetNearbyEnemies(center, 5f, null, 16, s_buf);
        for (int i = 0; i < n; i++)
            CombatQuery.DealSynergyDamage(s_buf[i], dmg, Self(ctx), 1f, false);
    }

    public override void OnRoomEnter(ItemEffectContext ctx) => _armed = true;
}

/// <summary>각성의 인장 — 모든 스킬이 동시에 IsReady가 되는 순간 다음 일반공격 ×value 강화.</summary>
public sealed class SkillReadyEmpowerEffect : ItemCombatEffectBase
{
    private bool _wasAllReady = true;

    public SkillReadyEmpowerEffect(ItemEffectSlot s) : base(s) { }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        var ct = ctx?.Player?.CooldownTracker;
        if (ct == null) return;
        bool allReady = ct.IsReady(SkillType.Q) && ct.IsReady(SkillType.E) && ct.IsReady(SkillType.R);
        if (allReady && !_wasAllReady)
        {
            QueueNext((_value > 0f ? _value : 1.8f) - 1f);
            if (ctx.Player != null) ItemGuide.Toast(ctx.Player.transform.position, "각성");
        }
        _wasAllReady = allReady;
    }
}

/// <summary>폭발하는 분노 / 파국의 인장 — 누적 피해≥MaxHp×value 시 다음 공격 ×value2. value3≥1이면 누적 절반만 차감(T4).</summary>
public sealed class DamageAccumEmpowerEffect : ItemCombatEffectBase
{
    private float _accum;

    public DamageAccumEmpowerEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (ctx.Stats == null || ctx.Stats.MaxHp <= 0) return;
        _accum += report.DamageDealt;
        float threshold = ctx.Stats.MaxHp * (_value > 0f ? _value : 0.5f);
        if (_accum < threshold) return;

        _accum = _value3 >= 1f ? _accum - threshold * 0.5f : 0f;   // T4: 절반만 차감
        QueueNext((_value2 > 0f ? _value2 : 2.2f) - 1f);
        if (ctx.Player != null) ItemGuide.Toast(ctx.Player.transform.position, "격노");
    }

    public override void OnRoomEnter(ItemEffectContext ctx) => _accum = 0f;
}

/// <summary>최후의 숨결 — HP≤value 시 duration초 무장. 윈도 중 dyn.allDamage+=value2. 방 재충전.</summary>
public sealed class LastBreathEffect : ItemCombatEffectBase
{
    private float _windowUntil = -999f;
    private bool _armed = true;

    public LastBreathEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!_armed) return;
        if (ctx.HpRatio > (_value > 0f ? _value : 0.1f)) return;
        _armed = false;
        _windowUntil = Time.time + (_duration > 0f ? _duration : 5f);
        if (ctx.Player != null) ItemGuide.Badge(ctx.Player.transform, "item_lastbreath", "최후");
    }

    public override void ContributeDynamicStats(ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        if (Time.time < _windowUntil) dyn.allDamage += _value2;
        else ItemGuide.ClearBadge("item_lastbreath");
    }

    public override void OnRoomEnter(ItemEffectContext ctx) { _armed = true; _windowUntil = -999f; }
}

/// <summary>균열의 일격 — value회 연속 비치명 시 다음 공격을 ×value2 확정 크리티컬로.</summary>
public sealed class GuaranteedCritEffect : ItemCombatEffectBase
{
    private int _nonCritStreak;

    public GuaranteedCritEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.IsCrit) { _nonCritStreak = 0; return; }
        int need = _value > 0f ? (int)_value : 3;
        if (++_nonCritStreak < need) return;
        _nonCritStreak = 0;
        ctx.Stats?.ArmForceCrit(_value2 > 0f ? _value2 : 1.9f);   // 다음 1타 강제 크릿(배율·연출 반영)
        if (ctx.Player != null) ItemGuide.Toast(ctx.Player.transform.position, "균열");
    }
}

/// <summary>무게의 추 / 심판의 시간 — 정지 value초 충전 시 다음 공격 ×value2 강화(이동 시 리셋).</summary>
public sealed class ChargeWhileIdleEffect : ItemCombatEffectBase
{
    private float _idle;
    private bool _charged;

    public ChargeWhileIdleEffect(ItemEffectSlot s) : base(s) { }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (ctx?.Player == null) return;
        bool moving = ctx.Player.MoveDirection.sqrMagnitude > 0.01f;
        if (moving) { _idle = 0f; _charged = false; ItemGuide.ClearBadge("item_charge"); return; }

        if (_charged) return;
        _idle += deltaTime;
        float need = _value > 0f ? _value : 3f;
        if (_idle >= need)
        {
            _charged = true;
            QueueNext((_value2 > 0f ? _value2 : 2.8f) - 1f);
            ItemGuide.Badge(ctx.Player.transform, "item_charge", "충전!");
        }
    }
}

/// <summary>최종 결의 — 단일 피해≥MaxHp×value 시 주변 EffAtk×value2 광역(쿨 duration초).</summary>
public sealed class CounterShockwaveEffect : ItemCombatEffectBase
{
    private static readonly List<MonsterBase> s_buf = new();
    private float _cooldownEnd;

    public CounterShockwaveEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (ctx.Player == null || ctx.Stats == null || ctx.Stats.MaxHp <= 0) return;
        if (Time.time < _cooldownEnd) return;
        if (report.DamageDealt < ctx.Stats.MaxHp * (_value > 0f ? _value : 0.4f)) return;

        _cooldownEnd = Time.time + (_duration > 0f ? _duration : 60f);
        float dmg = EffAtk(ctx) * (_value2 > 0f ? _value2 : 2f);
        Vector3 center = ctx.Player.transform.position;
        ItemGuide.Aoe(center, 5f);
        int n = CombatQuery.GetNearbyEnemies(center, 5f, null, 16, s_buf);
        for (int i = 0; i < n; i++)
            CombatQuery.DealSynergyDamage(s_buf[i], dmg, Self(ctx), 1f, false);
    }
}

/// <summary>침묵의 폭발 / 억눌린 격노 — 마지막 스킬 후 value초 경과 시 스킬 강화 무장.
/// 무장 중 dyn.skillDamage+=value2-1(다음 스킬 강화 근사), 스킬 사용 시 소비/재시작.</summary>
public sealed class SkillIdleEmpowerEffect : ItemCombatEffectBase
{
    private float _lastSkillTime;
    private bool _armed;

    public SkillIdleEmpowerEffect(ItemEffectSlot s) : base(s) { _lastSkillTime = -999f; }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        float need = _value > 0f ? _value : 15f;
        if (!_armed && Time.time - _lastSkillTime >= need)
        {
            _armed = true;
            if (ctx.Player != null) ItemGuide.Badge(ctx.Player.transform, "item_skillidle", "침묵");
        }
    }

    public override void OnSkillUse(ItemEffectContext ctx, SkillType skill)
    {
        _lastSkillTime = Time.time;
        _armed = false;
        ItemGuide.ClearBadge("item_skillidle");
    }

    public override void ContributeDynamicStats(ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        if (_armed) dyn.skillDamage += (_value2 > 0f ? _value2 : 3f) - 1f;
    }

    public override void OnRoomEnter(ItemEffectContext ctx) { _armed = false; _lastSkillTime = Time.time; }
}

/// <summary>광기의 파동 — 누적 피해≥MaxHp×value 시 다음 일반공격 1타를 실제 방어무시로.
/// 무장은 PlayerRuntimeStats.ArmPenetrateNextHit, 소비는 ColliderInstance.ApplyDamage(근접 일반공격).</summary>
public sealed class DamageAccumPenetrateEffect : ItemCombatEffectBase
{
    private float _accum;
    private bool _badge;

    public DamageAccumPenetrateEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (ctx.Stats == null || ctx.Stats.MaxHp <= 0) return;
        _accum += report.DamageDealt;
        float threshold = ctx.Stats.MaxHp * (_value > 0f ? _value : 0.3f);
        if (_accum < threshold) return;
        _accum = 0f;
        ctx.Stats.ArmPenetrateNextHit();   // 다음 근접 일반공격 1타를 방어무시(ColliderInstance가 소비)
        if (ctx.Player != null) { ItemGuide.Badge(ctx.Player.transform, "item_penetrate", "관통"); _badge = true; }
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        // 다음 공격이 소비하면 무장 해제 → 배지 정리
        if (_badge && ctx.Stats != null && !ctx.Stats.PenetrateArmed)
        {
            _badge = false;
            ItemGuide.ClearBadge("item_penetrate");
        }
    }

    public override void OnRoomEnter(ItemEffectContext ctx) { _accum = 0f; }
}
