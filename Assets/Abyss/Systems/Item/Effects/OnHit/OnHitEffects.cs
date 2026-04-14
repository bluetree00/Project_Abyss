using UnityEngine;

// ═══════════════════════════════════════════════════════════
// 공격 적중 시 발동하는 효과
// ═══════════════════════════════════════════════════════════

public sealed class LifestealEffect : ItemEffectBase
{
    public LifestealEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        float heal = report.DamageDealt * _value;
        if (heal > 0f && ctx.Player != null)
            ctx.Player.Heal(Mathf.Max(1, (int)heal));
    }
}

public sealed class PoisonOnHitEffect : ItemEffectBase
{
    public PoisonOnHitEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Random.value >= _value) return;
        // TODO: 상태이상 시스템 연결
        // report.Target.GetComponent<StatusEffectReceiver>()?.ApplyPoison(_maxStack);
        Debug.Log($"[PoisonOnHit] 독 적용! 대상={report.Target?.name}, 지속={_maxStack}초");
    }
}

public sealed class FreezeEffect : ItemEffectBase
{
    public FreezeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Random.value >= _value) return;
        // TODO: 상태이상 시스템 연결
        // report.Target.GetComponent<StatusEffectReceiver>()?.ApplyFreeze(_maxStack);
        Debug.Log($"[Freeze] 빙결 적용! 대상={report.Target?.name}, 지속={_maxStack}초");
    }
}

public sealed class ExtraAttackEffect : ItemEffectBase
{
    public ExtraAttackEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Random.value >= _value) return;
        // TODO: 동일 타격 판정 1회 추가 발동
        Debug.Log($"[ExtraAttack] 추가 공격 발동!");
    }
}

public sealed class TeleportSwapEffect : ItemEffectBase
{
    public TeleportSwapEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Random.value >= _value) return;
        if (ctx.Player == null || report.Target == null) return;

        var playerPos = ctx.Player.transform.position;
        var targetPos = report.Target.transform.position;
        ctx.Player.transform.position = targetPos;
        report.Target.transform.position = playerPos;
        Debug.Log("[TeleportSwap] 위치 교체!");
    }
}

public sealed class HPRegenOnHitEffect : ItemEffectBase
{
    public HPRegenOnHitEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (ctx.Player != null)
            ctx.Player.Heal((int)_value);
    }
}
