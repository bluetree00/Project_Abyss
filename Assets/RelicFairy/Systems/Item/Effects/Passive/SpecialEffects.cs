using UnityEngine;

// ═══════════════════════════════════════════════════════════
// 특수 효과 (획득 시, 사용 시, 원소 변환 등)
// ═══════════════════════════════════════════════════════════

public sealed class PoisonAppleEffect : ItemEffectBase
{
    public PoisonAppleEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        // 아이템 ID 기반 1회 발동 — Rebuild 시 재발동 방지
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        string key = $"PoisonApple_{_slot?.slot}_{_slot?.effectType}";
        if (mgr == null || !mgr.TryTriggerOnce(key)) return;

        if (Random.value < _value)
        {
            ctx.Player?.TakeDamage(5);
            Debug.Log("[PoisonApple] 독 사과! 체력 -5");
        }
    }
}

public sealed class RandomElementEffect : ItemEffectBase
{
    public RandomElementEffect(ItemEffectSlot s) : base(s) { }
}

public sealed class ItemGradeUpEffect : ItemEffectBase
{
    public ItemGradeUpEffect(ItemEffectSlot s) : base(s) { }

    public override void OnItemPickup(ItemEffectContext ctx, RuntimeItemData pickedItem)
    {
        if (pickedItem == null) return;
        if (Random.value >= _value) return;

        // 등급 상승 (Epic이면 더 올라가지 않음)
        if (pickedItem.rarity < ItemRarity.Epic)
        {
            pickedItem.rarity++;
            Debug.Log($"[ItemGradeUp] {pickedItem.displayName} 등급 상승 → {pickedItem.rarity}");
        }
    }
}

public sealed class HealOnUseEffect : ItemEffectBase
{
    public HealOnUseEffect(ItemEffectSlot s) : base(s) { }

    // OnUse는 액티브 아이템 사용 시스템에서 별도 호출
    // 여기서는 스탯 수정 없음, 사용 시 Heal만
    public override void OnActivate(ItemEffectContext ctx)
    {
        // 액티브 아이템은 별도 사용 시스템에서 처리
    }
}
