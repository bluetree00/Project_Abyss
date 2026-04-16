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
    private const string VfxKey = "VFX_ExtraAttack";

    public ExtraAttackEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Random.value >= _value) return;
        if (report.Target == null || ctx.Player == null) return;

        if (report.Target.TryGetComponent<IDamageable>(out var damageable))
        {
            damageable.TakeDamage(report.DamageDealt, ctx.Player.gameObject, knockbackMultiplier: 0f);

            var hitPos = report.HitPosition != Vector3.zero
                ? report.HitPosition
                : report.Target.transform.position;
            ItemEffectVfxHelper.SpawnOneShotAt(VfxKey, hitPos);
            ItemEffectVfxHelper.ShowNotice($"<color=#FFDD55>추가 타격</color> {report.DamageDealt:F0} → {report.Target.name}");
            Debug.Log($"[ExtraAttack] 추가 타격 {report.DamageDealt:F0} → {report.Target.name}");
        }
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
