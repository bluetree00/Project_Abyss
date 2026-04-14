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
        Register("MeleeDamage",                s => new MeleeDamageEffect(s));
        Register("RangedDamage",               s => new RangedDamageEffect(s));
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

        // ── OnHit: 공격 적중 ────────────────────────────────
        Register("Lifesteal",                  s => new LifestealEffect(s));
        Register("PoisonOnHit",                s => new PoisonOnHitEffect(s));
        Register("Freeze",                     s => new FreezeEffect(s));
        Register("ExtraAttack",                s => new ExtraAttackEffect(s));
        Register("TeleportSwap",               s => new TeleportSwapEffect(s));
        Register("HPRegenOnHit",               s => new HPRegenOnHitEffect(s));

        // ── OnTakeDamage: 피격 ──────────────────────────────
        Register("DamageNegate",               s => new DamageNegateEffect(s));
        Register("DamageReflect",              s => new DamageReflectEffect(s));
        Register("DefenseOnHit",               s => new DefenseOnHitEffect(s));

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

        // ── OnSkill: 스킬 ──────────────────────────────────
        Register("FireExplosionOnSkill",       s => new FireExplosionOnSkillEffect(s));
        Register("LightningOnSkill",           s => new LightningOnSkillEffect(s));

        // ── 특수 ────────────────────────────────────────────
        Register("PoisonApple",                s => new PoisonAppleEffect(s));
        Register("RandomElement",              s => new RandomElementEffect(s));
        Register("ItemGradeUp",                s => new ItemGradeUpEffect(s));
        Register("Heal",                       s => new HealOnUseEffect(s));

        // ── 레거시 호환 ─────────────────────────────────────
        Register("MeleeAttack",                s => new MeleeDamageEffect(s));
        Register("RangedAttack",               s => new RangedDamageEffect(s));
        Register("AttackPower",                s => new AllDamageEffect(s));

        Debug.Log($"[ItemEffectRegistry] {_creators.Count}개 effectType 등록 완료");
    }
}
