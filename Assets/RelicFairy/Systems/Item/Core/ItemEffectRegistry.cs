using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// effectType 문자열 → IItemEffect 클래스 매핑.
/// 새 효과 추가 시 Register() 한 줄이면 끝.
///
/// ■ 초기화: static 생성자에서 전체 등록
/// ■ 생성: Create(effectType, slot) → IItemEffect 인스턴스
/// ■ 미등록 effectType → GenericStatEffect (기본 스탯 가산 폴백)
/// </summary>
public static class ItemEffectRegistry
{
    private static readonly Dictionary<string, Func<ItemEffectSlot, IItemEffect>> _creators = new();
    private static bool _initialized;

    /// <summary>effectType에 대한 생성자 등록.</summary>
    public static void Register(string effectType, Func<ItemEffectSlot, IItemEffect> creator)
    {
        _creators[effectType] = creator;
    }

    /// <summary>effectType + slot 데이터로 IItemEffect 인스턴스 생성.</summary>
    public static IItemEffect Create(ItemEffectSlot slot)
    {
        EnsureInitialized();

        if (slot == null || string.IsNullOrEmpty(slot.effectType))
            return null;

        if (_creators.TryGetValue(slot.effectType, out var creator))
            return creator(slot);

        // 미등록 → 기본 스탯 가산으로 폴백
        Debug.LogWarning($"[ItemEffectRegistry] 미등록 effectType: {slot.effectType} → GenericStatEffect 폴백");
        return new GenericStatEffect(slot);
    }

    /// <summary>등록된 effectType 수.</summary>
    public static int Count => _creators.Count;

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        RegisterAll();
    }

    /// <summary>
    /// 모든 효과 클래스 등록.
    /// 새 효과 추가 시 여기에 한 줄만 추가.
    /// </summary>
    private static void RegisterAll()
    {
        // ── Passive: 스탯 ───────────────────────────────────
        Register("MoveSpeed",                  s => new MoveSpeedEffect(s));
        Register("AllDamage",                  s => new AllDamageEffect(s));
        Register("AttackDamage",               s => new AttackDamageEffect(s));
        Register("Defense",                    s => new DefenseEffect(s));
        Register("MaxHP",                      s => new MaxHPEffect(s));
        Register("Luck",                       s => new LuckEffect(s));
        Register("AttackSpeed",                s => new AttackSpeedEffect(s));
        Register("RollCooldown",               s => new RollCooldownEffect(s));
        Register("RollDistance",               s => new RollDistanceEffect(s));
        Register("RangedRange",                s => new RangedRangeEffect(s));
        Register("SkillCooldownReduction",     s => new SkillCooldownEffect(s));
        Register("ActiveItemCooldownReduction",s => new ActiveItemCooldownEffect(s));
        Register("HealingReceived",            s => new HealingReceivedEffect(s));
        Register("DebuffResistance",           s => new DebuffResistanceEffect(s));
        Register("DamageReduction",            s => new DamageReductionEffect(s));
        Register("AllStats",                   s => new AllStatsEffect(s));
        Register("AllElementBonus",            s => new AllElementBonusEffect(s));
        Register("SpecialRoomChance",          s => new SpecialRoomChanceEffect(s));
        Register("HighGradeItemChance",        s => new HighGradeItemChanceEffect(s));
        Register("ConsumableSlot",             s => new ConsumableSlotEffect(s));
        Register("DebuffDuration",             s => new DebuffDurationEffect(s));
        Register("GoldGain",                   s => new GoldGainEffect(s));
        Register("ShopRoomChance",             s => new ShopRoomChanceEffect(s));
        Register("SkillDamage",                s => new SkillDamageEffect(s));
        Register("ProjectilePierce",           s => new ProjectilePierceEffect(s));
        Register("ProjectileCount",            s => new ProjectileCountEffect(s));

        // ── 정적 % 스탯 (무조건) ────────────────────────────
        Register("CritChance",                 s => new CritChanceEffect(s));
        Register("CritDamage",                 s => new CritDamageEffect(s));
        Register("DefensePercent",             s => new DefensePercentEffect(s));
        Register("MaxHPPercent",               s => new MaxHPPercentEffect(s));

        // ── 조건부/타임드 동적 스탯 (trigger=조건, value=버프량) ──
        Register("CondAttackPercent",          s => new ConditionalStatBuffEffect(s));
        Register("CondDefensePercent",         s => new ConditionalStatBuffEffect(s));
        Register("CondAttackSpeed",            s => new ConditionalStatBuffEffect(s));
        Register("CondMoveSpeed",              s => new ConditionalStatBuffEffect(s));
        Register("CondCritChance",             s => new ConditionalStatBuffEffect(s));
        Register("CondCritDamage",             s => new ConditionalStatBuffEffect(s));
        Register("CondSkillDamage",            s => new ConditionalStatBuffEffect(s));
        Register("CondAllDamage",              s => new ConditionalStatBuffEffect(s));
        Register("CondMaxHpPercent",           s => new ConditionalStatBuffEffect(s));
        Register("FirstHitBonus",              s => new FirstHitBonusEffect(s));

        // ── OnHit: 공격 적중 ────────────────────────────────
        Register("PoisonOnHit",                s => new PoisonOnHitEffect(s));
        Register("Freeze",                     s => new FreezeEffect(s));
        Register("ExtraAttack",                s => new ExtraAttackEffect(s));
        Register("TeleportSwap",               s => new TeleportSwapEffect(s));
        Register("HPRegenOnHit",               s => new HPRegenOnHitEffect(s));
        Register("Petrify",                    s => new PetrifyEffect(s));
        Register("Stun",                       s => new StunEffect(s));

        // ── OnTakeDamage: 피격 ──────────────────────────────
        Register("DamageNegate",               s => new DamageNegateEffect(s));
        Register("DamageReflect",              s => new DamageReflectEffect(s));
        Register("FireReflect",                s => new FireReflectEffect(s));
        Register("DefenseOnHit",               s => new DefenseOnHitEffect(s));
        Register("ExtraDamageOnHit",           s => new ExtraDamageOnHitEffect(s));

        // ── OnKill: 처치 ────────────────────────────────────
        Register("GoldOnKill",                 s => new GoldOnKillEffect(s));
        Register("FullHealOnKill",             s => new FullHealOnKillEffect(s));

        // ── OnClear: 방/보스 클리어 ─────────────────────────
        Register("HPRegenOnClear",             s => new HPRegenOnClearEffect(s));
        Register("BossDropItem",               s => new BossDropItemEffect(s));
        Register("BuffRefreshOnBoss",          s => new BuffRefreshOnBossEffect(s));

        // ── OnRoll: 회피 ────────────────────────────────────
        Register("FirstAttackAfterRoll",       s => new FirstAttackAfterRollEffect(s));
        Register("RollLandingDamage",          s => new RollLandingDamageEffect(s));

        // ── OnDeath: 사망 ───────────────────────────────────
        Register("ReviveHeal",                 s => new ReviveHealEffect(s));
        Register("DeathNegate",                s => new DeathNegateEffect(s));

        // ── OnRecipe: 시너지 ────────────────────────────────
        Register("HPRegenOnRecipe",            s => new HPRegenOnRecipeEffect(s));
        Register("AllDamageOnRecipe",          s => new AllDamageOnRecipeEffect(s));
        Register("RecipeSynergyNextAttack",    s => new RecipeSynergyNextAttackEffect(s));
        Register("SkillCooldownFlat",          s => new SkillCooldownFlatEffect(s));
        Register("DefensePermStack",           s => new DefensePermStackEffect(s));

        // ── OnRoomEnter / OnBossEnter: 방 진입 ──────────────
        Register("HPRegenOnBossEnter",         s => new HPRegenOnBossEnterEffect(s));
        Register("MaxHPDecreasePerRoom",       s => new MaxHPDecreasePerRoomEffect(s));

        // ── OnSkill: 스킬 ──────────────────────────────────
        Register("FireExplosionOnSkill",       s => new FireExplosionOnSkillEffect(s));
        Register("LightningOnSkill",           s => new LightningOnSkillEffect(s));

        // ── 특수 ────────────────────────────────────────────
        Register("PoisonApple",                s => new PoisonAppleEffect(s));
        Register("RandomElement",              s => new RandomElementEffect(s));
        Register("ItemGradeUp",                s => new ItemGradeUpEffect(s));
        Register("Heal",                       s => new HealOnUseEffect(s));

        // ── T3/T4 잔첨형 (Tier3/ResidualEffects) ────────────

        // ── T3/T4 광폭형 (Tier3/BerserkEffects) ─────────────

        // ── T3/T4 타이밍형 (Tier3/TimingEffects) ────────────

        // ── T3/T4 형태변형 (Tier3/ShapeEffects) ─────────────

        // ── 레전드리 룬 효과 ─────────────────────────────────
        Register("FireLegendAoe",         s => new FireLegendAoeEffect(s));
        Register("FireLegendSingle",       s => new FireLegendSingleEffect(s));
        Register("FireLegendProjectile",   s => new FireLegendProjectileEffect(s));
        Register("IceLegendAoe",          s => new IceLegendAoeEffect(s));
        Register("IceLegendSingle",       s => new IceLegendSingleEffect(s));
        Register("IceLegendField",        s => new IceLegendFieldEffect(s));
        Register("ElecLegendAoe",         s => new ElecLegendAoeEffect(s));
        Register("ElecLegendSingle",      s => new ElecLegendSingleEffect(s));
        Register("ElecLegendProjectile",  s => new ElecLegendProjectileEffect(s));
        Register("GrassLegendAoe",        s => new GrassLegendAoeEffect(s));
        Register("GrassLegendSingle",     s => new GrassLegendSingleEffect(s));
        Register("GrassLegendProjectile", s => new GrassLegendProjectileEffect(s));
        Register("LightLegendAoe",        s => new LightLegendAoeEffect(s));
        Register("LightLegendSingle",     s => new LightLegendSingleEffect(s));
        Register("LightLegendProjectile", s => new LightLegendProjectileEffect(s));
        Register("DarkLegendAoe",         s => new DarkLegendAoeEffect(s));
        Register("DarkLegendSingle",      s => new DarkLegendSingleEffect(s));
        Register("DarkLegendProjectile",  s => new DarkLegendProjectileEffect(s));

        Debug.Log($"[ItemEffectRegistry] {_creators.Count}개 effectType 등록 완료");
    }
}
