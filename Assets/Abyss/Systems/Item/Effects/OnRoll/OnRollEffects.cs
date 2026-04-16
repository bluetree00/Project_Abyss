using UnityEngine;

// ═══════════════════════════════════════════════════════════
// 구르기 관련 효과
// ═══════════════════════════════════════════════════════════

public sealed class FirstAttackAfterRollEffect : ItemEffectBase
{
    private bool _buffActive;

    public FirstAttackAfterRollEffect(ItemEffectSlot s) : base(s) { }

    public override void OnRollEnd(ItemEffectContext ctx)
    {
        _buffActive = true;
    }

    public override void OnPreDealDamage(ItemEffectContext ctx, ref DamagePacket pkt)
    {
        if (!_buffActive) return;
        pkt.FinalDamage *= (1f + _value);
        _buffActive = false;
        Debug.Log($"[FirstAttackAfterRoll] 구르기 후 첫 공격 +{_value * 100f:F0}%");
    }
}

public sealed class RollLandingDamageEffect : ItemEffectBase
{
    public RollLandingDamageEffect(ItemEffectSlot s) : base(s) { }

    public override void OnRollLand(ItemEffectContext ctx, Vector3 position)
    {
        // TODO: 착지 지점 주변 범위 피해
        // Physics.OverlapSphere(position, radius) → 데미지 적용
        Debug.Log($"[RollLandingDamage] 착지 범위 피해 {_value} at {position}");
    }
}
