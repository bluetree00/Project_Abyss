using UnityEngine;

// ═══════════════════════════════════════════════════════════
// 패시브 스탯 효과 — ModifyStats만 override
// 각 클래스는 effectType 1:1 대응
// ═══════════════════════════════════════════════════════════

public sealed class MoveSpeedEffect : ItemEffectBase
{
    public MoveSpeedEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.MoveSpeed += _value;
}

public sealed class MeleeDamageEffect : ItemEffectBase
{
    public MeleeDamageEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.MeleeDamage += (int)_value;
}

public sealed class RangedDamageEffect : ItemEffectBase
{
    public RangedDamageEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.RangedDamage += (int)_value;
}

public sealed class AllDamageEffect : ItemEffectBase
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

public sealed class AttackDamageEffect : ItemEffectBase
{
    public AttackDamageEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.AllDamagePercent += _value;
}

public sealed class DefenseEffect : ItemEffectBase
{
    public DefenseEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.Defense += (int)_value;
}

public sealed class MaxHPEffect : ItemEffectBase
{
    public MaxHPEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.MaxHP += (int)_value;
}

public sealed class LuckEffect : ItemEffectBase
{
    public LuckEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.Luck += (int)_value;
}

public sealed class AttackSpeedEffect : ItemEffectBase
{
    public AttackSpeedEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.AttackSpeed += _value;
}

public sealed class RollCooldownEffect : ItemEffectBase
{
    public RollCooldownEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.RollCooldown += _value;
}

public sealed class RollDistanceEffect : ItemEffectBase
{
    public RollDistanceEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.RollDistance += _value;
}

public sealed class RangedRangeEffect : ItemEffectBase
{
    public RangedRangeEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.RangedRange += _value;
}

public sealed class SkillCooldownEffect : ItemEffectBase
{
    public SkillCooldownEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.SkillCooldownReduction += _value;
}

public sealed class ActiveItemCooldownEffect : ItemEffectBase
{
    public ActiveItemCooldownEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.ActiveItemCooldownReduction += _value;
}

public sealed class HealingReceivedEffect : ItemEffectBase
{
    public HealingReceivedEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.HealingReceived += _value;
    public override void ModifyHeal(ItemEffectContext ctx, ref int amount)
    {
        amount = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(amount * (1f + _value)));
    }
}

public sealed class DebuffResistanceEffect : ItemEffectBase
{
    public DebuffResistanceEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.DebuffResistance += _value;
}

public sealed class DamageReductionEffect : ItemEffectBase
{
    public DamageReductionEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.DamageReduction += _value;
    public override void OnPreTakeDamage(ItemEffectContext ctx, ref DamagePacket pkt)
    {
        pkt.FinalDamage *= (1f - _value);
    }
}

public sealed class AllStatsEffect : ItemEffectBase
{
    public AllStatsEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.AllStatsPercent += _value;
}

public sealed class AllElementBonusEffect : ItemEffectBase
{
    public AllElementBonusEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.AllElementBonus += _value;
}

public sealed class SpecialRoomChanceEffect : ItemEffectBase
{
    public SpecialRoomChanceEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.SpecialRoomChance += _value;
}

public sealed class HighGradeItemChanceEffect : ItemEffectBase
{
    public HighGradeItemChanceEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.HighGradeItemChance += _value;
}

public sealed class ConsumableSlotEffect : ItemEffectBase
{
    public ConsumableSlotEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.ConsumableSlotBonus += (int)_value;
}

public sealed class DebuffDurationEffect : ItemEffectBase
{
    public DebuffDurationEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.DebuffDuration += _value;
}

public sealed class GoldGainEffect : ItemEffectBase
{
    public GoldGainEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.GoldGainRate += _value;
}

public sealed class ShopRoomChanceEffect : ItemEffectBase
{
    public ShopRoomChanceEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.ShopRoomChance += _value;
}

public sealed class SkillDamageEffect : ItemEffectBase
{
    public SkillDamageEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.SkillDamagePercent += _value;
}

public sealed class ProjectilePierceEffect : ItemEffectBase
{
    public ProjectilePierceEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.ProjectilePierceBonus += (int)_value;
}

public sealed class ProjectileCountEffect : ItemEffectBase
{
    public ProjectileCountEffect(ItemEffectSlot s) : base(s) { }
    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) => stats.ProjectileCountBonus += (int)_value;
}
