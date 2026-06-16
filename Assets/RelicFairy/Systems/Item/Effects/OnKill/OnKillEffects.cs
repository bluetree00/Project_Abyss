using UnityEngine;

// ═══════════════════════════════════════════════════════════
// 적 처치 시 발동하는 효과
// ═══════════════════════════════════════════════════════════

public sealed class GoldOnKillEffect : ItemEffectBase
{
    public GoldOnKillEffect(ItemEffectSlot s) : base(s) { }

    public override void OnKill(ItemEffectContext ctx, GameObject target)
    {
        if (Random.value >= _value) return;   // value = 발동 확률

        // value2 = 획득 골드량(미설정 시 1). 하드코딩 1 제거.
        // (전역 GoldGainRate 배율은 GameRunSession.AddGold에서 일괄 적용됨.)
        int gold = _value2 > 0f ? Mathf.Max(1, (int)_value2) : 1;
        ctx.Session?.AddGold(gold);
        Debug.Log($"[GoldOnKill] 추가 골드 +{gold}");
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
