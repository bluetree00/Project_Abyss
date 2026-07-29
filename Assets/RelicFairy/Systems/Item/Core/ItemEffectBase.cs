using UnityEngine;

/// <summary>
/// IItemEffect 기본 구현. 모든 메서드가 빈 virtual.
/// 각 효과 클래스는 이 클래스를 상속하고 필요한 메서드만 override.
/// </summary>
public abstract class ItemEffectBase : IItemEffect
{
    // ── 데이터 ──────────────────────────────────────────────
    protected ItemEffectSlot _slot;
    protected float _value;
    protected float _value2;
    protected float _value3;
    protected int _maxStack;
    protected float _duration;
    protected string _trigger;

    public string EffectType => _slot?.effectType ?? "";
    public string Trigger => _trigger;

    /// <summary>
    /// 슬롯에 저장된 VFX 키가 있으면 그것을, 없으면 기본 키 반환.
    /// ItemSO.effectVfxKey에서 주입된다.
    /// </summary>
    protected string ResolveVfxKey(string defaultKey)
        => !string.IsNullOrEmpty(_slot?.vfxKey) ? _slot.vfxKey : defaultKey;

    public ItemEffectBase(ItemEffectSlot slot)
    {
        _slot = slot;
        _value = slot.value;
        _value2 = slot.value2;
        _value3 = slot.value3;
        _maxStack = slot.maxStack;
        _duration = slot.duration;
        _trigger = slot.trigger;
    }

    // ── 조건 판정 ───────────────────────────────────────────

    /// <summary>
    /// trigger 기반 조건 판정. 원소/HP/장비 조건을 자동 처리.
    /// 특수 조건이 필요하면 override.
    /// </summary>
    public virtual bool IsActive(ItemEffectContext ctx)
    {
        if (string.IsNullOrEmpty(_trigger)) return false;

        switch (_trigger)
        {
            case "Always":            return true;
            case "WithFireWeapon":    return false;
            case "WithWaterWeapon":   return false;
            case "WithGrassWeapon":   return false;
            case "WithEarthWeapon":   return false;
            case "WithLightningWeapon": return false;
            case "WithBowWeapon":     return ctx.WeaponType == WeaponType.Bow
                                          || ctx.WeaponType == WeaponType.Crossbow;
            case "WithMagicWeapon":   return ctx.WeaponType == WeaponType.Staff;
            case "WithShield":        return ctx.HasShield;
            case "HPBelow50":         return ctx.HpRatio <= 0.5f;
            case "HPBelow30":         return ctx.HpRatio <= 0.3f;
            case "CharacterShield":   return ctx.CharacterClass == Define.CharacterClass.Guardian;
            case "CharacterRanger":   return ctx.CharacterClass == Define.CharacterClass.Hunter;

            // 이벤트 트리거 — 이벤트 발생 시점에 호출되므로 항상 true
            case "OnHit":
            case "OnKill":
            case "OnRoomEnter":
            case "OnRoomClear":
            case "OnBossEnter":
            case "OnBossClear":
            case "OnRecipeComplete":
            case "OnRollEnd":
            case "OnRollLand":
            case "OnJumpLand":
            case "OnNearDeath":
            case "OnItemPickup":
            case "OnPickup":
            case "OnOverheal":
            case "OnNextChapterStart":
            case "OnSkillUse":
            case "OnUse":
                return true;

            default:
                return false;
        }
    }

    // ── 빈 기본 구현 ───────────────────────────────────────

    public virtual void OnActivate(ItemEffectContext ctx) { }
    public virtual void OnDeactivate() { }
    public virtual void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats) { }
    public virtual void OnPreDealDamage(ItemEffectContext ctx, ref DamagePacket pkt) { }
    public virtual void OnPostDealDamage(ItemEffectContext ctx, DamageReport report) { }
    public virtual void OnKill(ItemEffectContext ctx, GameObject target) { }
    public virtual void OnPreTakeDamage(ItemEffectContext ctx, ref DamagePacket pkt) { }
    public virtual void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report) { }
    public virtual bool OnNearDeath(ItemEffectContext ctx, ref float healPercent, ref float invincibleDuration) => false;
    public virtual void OnRollEnd(ItemEffectContext ctx) { }
    public virtual void OnRollLand(ItemEffectContext ctx, Vector3 position) { }
    public virtual void OnJumpLand(ItemEffectContext ctx, Vector3 position) { }
    public virtual void OnRoomEnter(ItemEffectContext ctx) { }
    public virtual void OnRoomClear(ItemEffectContext ctx) { }
    public virtual void OnBossEnter(ItemEffectContext ctx) { }
    public virtual void OnBossClear(ItemEffectContext ctx) { }
    public virtual void OnRecipeComplete(ItemEffectContext ctx) { }
    public virtual void OnItemPickup(ItemEffectContext ctx, RuntimeItemData pickedItem) { }
    public virtual void OnSkillUse(ItemEffectContext ctx, SkillType skill) { }
    public virtual void ModifyHeal(ItemEffectContext ctx, ref int amount) { }
    public virtual void OnTick(ItemEffectContext ctx, float deltaTime) { }
    public virtual void ContributeDynamicStats(ItemEffectContext ctx, ref ItemDynamicStats dyn) { }

    /// <summary>보스 클리어 시 추가 행운표 드랍 횟수(보스드랍 아이템만 &gt;0). RoomClearGate가 합산해 추가 롤.</summary>
    public virtual int BonusBossDrops => 0;

    /// <summary>현재 활성 공격 판정 변형(형태/사거리/다단/투사체)을 누적기에 더한다. ItemEffectManager.OnTick이 호출.</summary>
    public virtual void ContributeCombatMods(ItemEffectContext ctx, ref ItemCombatModifiers mods) { }
}
