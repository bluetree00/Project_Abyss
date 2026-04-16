using UnityEngine;

// ═══════════════════════════════════════════════════════════
// 적 처치 시 발동하는 효과
// ═══════════════════════════════════════════════════════════

public sealed class GoldOnKillEffect : ItemEffectBase
{
    public GoldOnKillEffect(ItemEffectSlot s) : base(s) { }

    public override void OnKill(ItemEffectContext ctx, GameObject target)
    {
        if (Random.value < _value)
        {
            ctx.Session?.AddGold(1);
            Debug.Log("[GoldOnKill] 추가 골드 획득!");
        }
    }
}

public sealed class FullHealOnKillEffect : ItemEffectBase
{
    public FullHealOnKillEffect(ItemEffectSlot s) : base(s) { }

    public override void OnKill(ItemEffectContext ctx, GameObject target)
    {
        if (Random.value < _value)
        {
            ctx.Stats?.SetHp(ctx.Stats.MaxHp);
            Debug.Log("[FullHealOnKill] 체력 전체 회복!");
        }
    }
}
