using UnityEngine;

// ═══════════════════════════════════════════════════════════
// 패시브 스탯 효과 — ModifyStats만 override
// 각 클래스는 effectType 1:1 대응
// ═══════════════════════════════════════════════════════════

/// <summary>
/// 영구 패시브 스탯 효과의 공통 베이스. 동작은 ItemEffectBase 그대로 두고,
/// "현재 활성 지속 효과"를 버프창에 상시 노출하는 표시 수집만 한 곳에서 구현한다.
///
/// ■ IsActive 기준: trigger="Always"는 상시, 장비/캐릭터 조건(WithBow/WithShield 등)은 충족 시에만 노출.
/// ■ 게이지/잔여 없음(영구) — Remaining01 = -1. 라벨/아이콘/단위는 표시 포맷터로 일원화.
/// ■ 읽기 전용 — 동작/밸런스 무변경. proc/조건부 효과는 이 베이스를 쓰지 않으므로 영향 없음.
/// </summary>
public abstract class PassiveStatEffectBase : ItemEffectBase, IItemBuffViewProvider
{
    protected PassiveStatEffectBase(ItemEffectSlot s) : base(s) { }

    public virtual bool TryGetBuffView(ItemEffectContext ctx, out BuffViewItem item)
    {
        item = default;
        if (!IsActive(ctx)) return false;

        var d = EffectDescriptionFormatter.Describe(EffectType, _value, _trigger, _slot?.description);
        item = new BuffViewItem(d.IconKey, d.LabelWithValue, 1, -1f, "", BuffSource.Item, isDebuff: d.IsRisk);
        return true;
    }
}

public sealed class MoveSpeedEffect : PassiveStatEffectBase
{
    public MoveSpeedEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.MoveSpeed += _value;
}

public sealed class AllDamageEffect : PassiveStatEffectBase
{
    public AllDamageEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats)
    {
        if (Mathf.Abs(_value) >= 1f)
            stats.AllDamageFlat += _value;
        else
            stats.AllDamagePercent += _value;
    }
}

public sealed class AttackDamageEffect : PassiveStatEffectBase
{
    public AttackDamageEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.AllDamagePercent += _value;
}

public sealed class DefenseEffect : PassiveStatEffectBase
{
    public DefenseEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.Defense += (int)_value;
}

public sealed class MaxHPEffect : PassiveStatEffectBase
{
    public MaxHPEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.MaxHP += (int)_value;
}

public sealed class LuckEffect : PassiveStatEffectBase
{
    public LuckEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.Luck += (int)_value;
}

public sealed class AttackSpeedEffect : PassiveStatEffectBase
{
    public AttackSpeedEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.AttackSpeed += _value;
}

public sealed class RollCooldownEffect : PassiveStatEffectBase
{
    public RollCooldownEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.RollCooldown += _value;
}

public sealed class RollDistanceEffect : PassiveStatEffectBase
{
    public RollDistanceEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.RollDistance += _value;
}

public sealed class RangedRangeEffect : PassiveStatEffectBase
{
    public RangedRangeEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.RangedRange += _value;
}

public sealed class SkillCooldownEffect : PassiveStatEffectBase
{
    public SkillCooldownEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.SkillCooldownReduction += _value;
}

public sealed class ActiveItemCooldownEffect : PassiveStatEffectBase
{
    public ActiveItemCooldownEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.ActiveItemCooldownReduction += _value;
}

public sealed class HealingReceivedEffect : PassiveStatEffectBase
{
    public HealingReceivedEffect(ItemEffectSlot s) : base(s) { }
    // 회복량 증가는 ModifyHeal 한 곳에서만 적용. (ModifyStats의 HealingReceived 누적은
    // 실제 회복 경로에서 소비되지 않는 표기용이라, 두 경로를 두면 향후 이중적용 위험 → 제거.)
    public override void ModifyHeal(ItemEffectContext ctx, ref int amount)
    {
        amount = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(amount * (1f + _value)));
    }
}

public sealed class DebuffResistanceEffect : PassiveStatEffectBase
{
    public DebuffResistanceEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.DebuffResistance += _value;
}

public sealed class DamageReductionEffect : PassiveStatEffectBase
{
    public DamageReductionEffect(ItemEffectSlot s) : base(s) { }
    // ModifyStats만 — 실제 피해 차감은 PlayerController.TakeDamage가 RuntimeStats.DamageReduction
    // 통합 채널(아이템+캐릭터+어둠룬)에서 1회 적용한다. (여기서 OnPreTakeDamage로 또 곱하면 이중적용)
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.DamageReduction += _value;
}

public sealed class AllStatsEffect : PassiveStatEffectBase
{
    public AllStatsEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.AllStatsPercent += _value;
}

public sealed class AllElementBonusEffect : PassiveStatEffectBase
{
    public AllElementBonusEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.AllElementBonus += _value;
}

public sealed class SpecialRoomChanceEffect : PassiveStatEffectBase
{
    public SpecialRoomChanceEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.SpecialRoomChance += _value;
}

public sealed class HighGradeItemChanceEffect : PassiveStatEffectBase
{
    public HighGradeItemChanceEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.HighGradeItemChance += _value;
}

public sealed class ConsumableSlotEffect : PassiveStatEffectBase
{
    public ConsumableSlotEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.ConsumableSlotBonus += (int)_value;
}

public sealed class DebuffDurationEffect : PassiveStatEffectBase
{
    public DebuffDurationEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.DebuffDuration += _value;
}

public sealed class GoldGainEffect : PassiveStatEffectBase
{
    public GoldGainEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.GoldGainRate += _value;
}

public sealed class ShopRoomChanceEffect : PassiveStatEffectBase
{
    public ShopRoomChanceEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.ShopRoomChance += _value;
}

public sealed class SkillDamageEffect : PassiveStatEffectBase
{
    public SkillDamageEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.SkillDamagePercent += _value;
}

public sealed class ProjectilePierceEffect : PassiveStatEffectBase
{
    public ProjectilePierceEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.ProjectilePierceBonus += (int)_value;
}

public sealed class ProjectileCountEffect : PassiveStatEffectBase
{
    public ProjectileCountEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.ProjectileCountBonus += (int)_value;
}

// ── 정적 % 스탯 (무조건). 조건부 버전은 ConditionalStatBuffEffect(Cond*) ──

public sealed class CritChanceEffect : PassiveStatEffectBase
{
    public CritChanceEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.CritChancePercent += _value;
}

public sealed class CritDamageEffect : PassiveStatEffectBase
{
    public CritDamageEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.CritDamagePercent += _value;
}

public sealed class DefensePercentEffect : PassiveStatEffectBase
{
    public DefensePercentEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.DefensePercent += _value;
}

public sealed class MaxHPPercentEffect : PassiveStatEffectBase
{
    public MaxHPPercentEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.MaxHPPercent += _value;
}
