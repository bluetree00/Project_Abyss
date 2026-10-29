using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 근접 버팀(10-29 사용자 결정 · 설계 기획/RelicFairy_해방기·악몽·랭킹·근접버팀_설계_20261029.md §2) — HP를 채우지 않고 보호막 · 받는 피해 감소로만.
/// 기억의 제단 3칸(강철 의지 · 근접 자세 · 버티는 숨 — 값은 <see cref="MemoryAltarService"/>)과 룬 4종(수호의 돌 · 맞받이 · 성벽 · 숨 고르기).
/// 상한: 보호막 총합 최대 체력 25%(SynergyMechanicsState.ShieldCapRatio) · 받는 피해 감소 총합 40%(PlayerRuntimeStats.MaxDamageReduction).
/// 타격 · 처치마다 채우는 것은 없다(사용자 「타격 시 회복 같은 건 없어야 해」 · 「무한정 회복과 방어는 막아야 해」) — 방 · 시간 · 자세 · 위기 조건으로만 선다.
/// 「방까지」 보호막(강철 의지 · 수호의 돌)은 다음 방에 들어설 때 남은 만큼 걷는다(<see cref="PlayerRuntimeStats.AddRoomShield"/>).
/// </summary>
public static class MeleeWard
{
    public const float LastBreathHp = 0.3f, LastBreathReduction = 0.5f, LastBreathSeconds = 2f;
#if UNITY_EDITOR
    /// <summary>실측 99 H — 제단 세 칸 단계를 이 값으로(−1 = 실제 세이브 · 세이브는 건드리지 않는다).</summary>
    public static int ProbeAltarLevel = -1;
#endif

    /// <summary>강철 의지 방 입장 보호막(최대 체력 비).</summary>
    public static float IronWillShield
    {
        get
        {
#if UNITY_EDITOR
            if (ProbeAltarLevel >= 0) return MemoryAltarCatalog.MeleeIronWillShield[Mathf.Clamp(ProbeAltarLevel, 0, 3)];
#endif
            return MemoryAltarService.MeleeIronWillShield;
        }
    }

    /// <summary>근접 자세 받는 피해 감소.</summary>
    public static float StanceReduction
    {
        get
        {
#if UNITY_EDITOR
            if (ProbeAltarLevel >= 0) return MemoryAltarCatalog.MeleeStanceReduction[Mathf.Clamp(ProbeAltarLevel, 0, 3)];
#endif
            return MemoryAltarService.MeleeStanceReduction;
        }
    }

    /// <summary>버티는 숨을 배웠나.</summary>
    public static bool HasLastBreath
    {
        get
        {
#if UNITY_EDITOR
            if (ProbeAltarLevel >= 0) return ProbeAltarLevel > 0;
#endif
            return MemoryAltarService.HasMeleeLastBreath;
        }
    }

    /// <summary>지금 근접 무기를 들고 있나.</summary>
    public static bool MeleeHeld(ItemEffectContext ctx)
    {
        var w = ctx?.Player != null && ctx.Player.WeaponManager != null ? ctx.Player.WeaponManager.CurrentWeaponData : null;
        return w != null && w.weaponType.GetAttackStatKind() == AttackStatKind.Melee;
    }

    /// <summary>제단 3칸 효과를 켠다(ItemEffectManager.Rebuild) — 배우지 않은 칸은 안 넣는다.</summary>
    public static void AppendAltar(List<IItemEffect> into, ItemEffectContext ctx)
    {
        if (IronWillShield > 0f) Add(new IronWillEffect(), into, ctx);
        if (StanceReduction > 0f) Add(new StanceGuardEffect(), into, ctx);
        if (HasLastBreath) Add(new LastBreathEffect(), into, ctx);
    }

    private static void Add(ItemEffectBase e, List<IItemEffect> into, ItemEffectContext ctx)
    {
        into.Add(e);
        e.OnActivate(ctx);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void Log(string msg) => Debug.Log("[MeleeWard] " + msg);
}

/// <summary>제단 칸 바탕 — 룬 슬롯 없이 만든다(트리거 Always · 각인 배율 없음).</summary>
public abstract class AltarWardEffect : ItemEffectBase
{
    protected AltarWardEffect(string type) : base(new ItemEffectSlot { effectType = type, trigger = "Always" }) { }
    public override bool IsActive(ItemEffectContext ctx) => true;
}

/// <summary>강철 의지(제단) — 근접 무기를 들고 방에 들어서면 보호막 최대 체력 8 / 12 / 16% · 다음 방에 들어설 때 남은 만큼 걷는다.</summary>
public sealed class IronWillEffect : AltarWardEffect
{
    public IronWillEffect() : base("AltarIronWill") { }

    public override void OnRoomEnter(ItemEffectContext ctx)
    {
        float r = MeleeWard.IronWillShield;
        if (r <= 0f || ctx?.Stats == null || !MeleeWard.MeleeHeld(ctx)) return;
        ctx.Stats.AddRoomShield(ctx.Stats.MaxHp * r);
        MeleeWard.Log($"강철 의지 — 보호막 {ctx.Stats.Shield}/{ctx.Stats.ShieldCap}");
    }
}

/// <summary>근접 자세(제단) — 근접 무기를 들고 있는 동안 받는 피해 −6 / 10 / 14%(받는 피해 감소 동적 칸 · 총합 상한 40%).</summary>
public sealed class StanceGuardEffect : AltarWardEffect
{
    public StanceGuardEffect() : base("AltarStanceGuard") { }

    public override void ContributeDynamicStats(ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        if (MeleeWard.MeleeHeld(ctx)) dyn.damageReduction += MeleeWard.StanceReduction;
    }
}

/// <summary>버티는 숨(제단) — 근접 무기 · 체력이 30% 아래로 떨어지는 순간 2초 받는 피해 −50%(방마다 한 번).</summary>
public sealed class LastBreathEffect : AltarWardEffect
{
    private float _lastRatio = 1f, _until = -999f;
    private bool _used;

    public LastBreathEffect() : base("AltarLastBreath") { }

    public override void OnRoomEnter(ItemEffectContext ctx) => _used = false;

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (ctx?.Stats != null && ctx.Stats.MaxHp > 0) _lastRatio = (float)ctx.Stats.Hp / ctx.Stats.MaxHp;
    }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (_used || ctx?.Stats == null || ctx.Stats.MaxHp <= 0 || !MeleeWard.MeleeHeld(ctx)) return;
        float now = (float)ctx.Stats.Hp / ctx.Stats.MaxHp;
        if (_lastRatio >= MeleeWard.LastBreathHp && now < MeleeWard.LastBreathHp && now > 0f)
        {
            _used = true;
            _until = Time.time + MeleeWard.LastBreathSeconds;
            if (ctx.Player != null) ItemGuide.Toast(ctx.Player.transform.position, "버티는 숨");
            MeleeWard.Log($"버티는 숨 — 체력 {_lastRatio:0.00} → {now:0.00} · {MeleeWard.LastBreathSeconds}초");
        }
        _lastRatio = now;
    }

    public override void ContributeDynamicStats(ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        if (Time.time <= _until) dyn.damageReduction += MeleeWard.LastBreathReduction;
    }
}

/// <summary>수호의 돌(룬) — 방을 클리어하면 보호막 최대 체력 × value(5%) · 다음 방에 들어설 때 남은 만큼 걷는다.</summary>
public sealed class WardClearShieldEffect : ItemEffectBase
{
    public WardClearShieldEffect(ItemEffectSlot s) : base(s) { }
    public override bool IsActive(ItemEffectContext ctx) => true;

    public override void OnRoomClear(ItemEffectContext ctx)
    {
        if (ctx?.Stats == null) return;
        ctx.Stats.AddRoomShield(ctx.Stats.MaxHp * (_value > 0f ? _value : 0.05f));
        MeleeWard.Log($"수호의 돌 — 보호막 {ctx.Stats.Shield}/{ctx.Stats.ShieldCap}");
    }
}

/// <summary>성벽(룬) — 근접 무기를 들 때 최대 체력 + value(6%) · 겹침 max_stack(2)까지.</summary>
public sealed class WardRampartEffect : ItemEffectBase
{
    public WardRampartEffect(ItemEffectSlot s) : base(s) { }
    public override bool IsActive(ItemEffectContext ctx) => true;

    public override void ContributeDynamicStats(ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        if (!MeleeWard.MeleeHeld(ctx) || dyn.wardRampartStacks >= (_maxStack > 0 ? _maxStack : 2)) return;
        dyn.wardRampartStacks++;
        dyn.maxHpPercent += _value > 0f ? _value : 0.06f;
    }
}

/// <summary>맞받이(룬) — 근접 콤보 마지막 타가 맞으면 duration(1)초 받는 피해 −value(20%) · 재사용 대기 value2(4)초.</summary>
public sealed class WardFinisherGuardEffect : ItemEffectBase
{
    private float _until = -999f, _readyAt;

    public WardFinisherGuardEffect(ItemEffectSlot s) : base(s) { }
    public override bool IsActive(ItemEffectContext ctx) => true;

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.ActionType != WeaponActionType.GroundLight || report.IsRanged || report.DamageDealt <= 0f || Time.time < _readyAt) return;
        if (!MeleeWard.MeleeHeld(ctx) || !IsFinisherSwing(ctx)) return;
        _readyAt = Time.time + (_value2 > 0f ? _value2 : 4f);
        _until   = Time.time + (_duration > 0f ? _duration : 1f);
        MeleeWard.Log("맞받이 — 막타 뒤 받는 피해 감소");
    }

    public override void ContributeDynamicStats(ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        if (Time.time <= _until) dyn.damageReduction += _value > 0f ? _value : 0.2f;
    }

    /// <summary>지금 휘두름이 콤보 마지막 단계인가(형 런타임의 휘두름 단계 · 무기 지상 콤보 수).</summary>
    private static bool IsFinisherSwing(ItemEffectContext ctx)
    {
        var w = ctx.Player.WeaponManager.CurrentWeaponData;
        var rt = WeaponFormRuntime.Of(ctx.Player);
        int step = rt != null ? rt.SwingStep : (ctx.Player.Combo != null ? ctx.Player.Combo.CurrentComboStep : -1);
        return w != null && step >= Mathf.Max(1, w.groundEndCount) - 1;
    }
}

/// <summary>숨 고르기(룬) — duration(4)초 동안 피해를 안 받으면 보호막 최대 체력 × value(4%) — 한 번 차면 다시 맞을 때까지 더 차지 않는다(이 몫은 4%를 넘지 않음).</summary>
public sealed class WardSteadyBreathEffect : ItemEffectBase
{
    private float _lastHurt;
    private bool _granted;

    public WardSteadyBreathEffect(ItemEffectSlot s) : base(s) { }
    public override bool IsActive(ItemEffectContext ctx) => true;

    public override void OnActivate(ItemEffectContext ctx)
    {
        _lastHurt = Time.time;
        _granted = false;
    }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        _lastHurt = Time.time;
        _granted = false;
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (_granted || ctx?.Stats == null || Time.time - _lastHurt < (_duration > 0f ? _duration : 4f)) return;
        _granted = true;
        ctx.Stats.AddShield(ctx.Stats.MaxHp * (_value > 0f ? _value : 0.04f));
        MeleeWard.Log($"숨 고르기 — 보호막 {ctx.Stats.Shield}/{ctx.Stats.ShieldCap}");
    }
}
