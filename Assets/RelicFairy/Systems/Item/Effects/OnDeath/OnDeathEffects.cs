using UnityEngine;

// ═══════════════════════════════════════════════════════════
// 사망 직전 발동 효과
// ═══════════════════════════════════════════════════════════

public sealed class ReviveHealEffect : ItemEffectBase
{
    private int _usedCount;

    public ReviveHealEffect(ItemEffectSlot s) : base(s) { }

    public override bool OnNearDeath(ItemEffectContext ctx, ref float healPercent, ref float invincibleDuration)
    {
        int limit = _maxStack > 0 ? _maxStack : 1;
        if (_usedCount >= limit) return false;

        _usedCount++;
        healPercent = _value;
        Debug.Log($"[ReviveHeal] 부활! HP {_value * 100f:F0}% 회복 ({_usedCount}/{limit})");
        return true;
    }
}

public sealed class DeathNegateEffect : ItemEffectBase
{
    private int _usedCount;

    public DeathNegateEffect(ItemEffectSlot s) : base(s) { }

    public override bool OnNearDeath(ItemEffectContext ctx, ref float healPercent, ref float invincibleDuration)
    {
        int limit = _maxStack > 0 ? _maxStack : 1;
        if (_usedCount >= limit) return false;

        _usedCount++;
        healPercent = 0.01f; // HP 1%로 생존
        invincibleDuration = _duration;
        Debug.Log($"[DeathNegate] 사망 무효! {_duration}초 무적 ({_usedCount}/{limit})");
        return true;
    }
}
