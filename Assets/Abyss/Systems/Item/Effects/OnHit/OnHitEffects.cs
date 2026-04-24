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
    private const string DefaultVfxKey = "VFX_Poison";

    public PoisonOnHitEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Random.value >= _value) return;
        if (report.Target != null)
            ItemEffectVfxHelper.SpawnOneShotAt(ResolveVfxKey(DefaultVfxKey), report.Target.transform.position);
        // TODO: 상태이상 시스템(MonsterStatusReceiver) 연결
        Debug.Log($"[PoisonOnHit] 독 적용! 대상={report.Target?.name}, 지속={_maxStack}초");
    }
}

public sealed class FreezeEffect : ItemEffectBase
{
    private const string DefaultVfxKey = "VFX_Wet"; // 빙결 = 물 원소 이펙트 재활용

    public FreezeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Random.value >= _value) return;
        if (report.Target != null)
            ItemEffectVfxHelper.SpawnOneShotAt(ResolveVfxKey(DefaultVfxKey), report.Target.transform.position);
        // TODO: 상태이상 시스템 연결
        Debug.Log($"[Freeze] 빙결 적용! 대상={report.Target?.name}, 지속={_maxStack}초");
    }
}

public sealed class ExtraAttackEffect : ItemEffectBase
{
    private const string DefaultVfxKey = "VFX_ExtraAttack";

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
            ItemEffectVfxHelper.SpawnOneShotAt(ResolveVfxKey(DefaultVfxKey), hitPos);
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

public sealed class PetrifyEffect : ItemEffectBase
{
    private const string DefaultVfxKey = "VFX_Petrify";

    public PetrifyEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Random.value >= _value) return;
        if (report.Target != null)
            ItemEffectVfxHelper.SpawnOneShotAt(ResolveVfxKey(DefaultVfxKey), report.Target.transform.position);
        // TODO: 상태이상 시스템 연결 (석화 = 일정 시간 완전 무력화)
        Debug.Log($"[Petrify] 석화 적용! 대상={report.Target?.name}, 지속={_duration}초");
    }
}

public sealed class StunEffect : ItemEffectBase
{
    private const string DefaultVfxKey = "VFX_Shock"; // 번개 원소 이펙트로 기절 표현

    public StunEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Random.value >= _value) return;
        if (report.Target != null)
            ItemEffectVfxHelper.SpawnOneShotAt(ResolveVfxKey(DefaultVfxKey), report.Target.transform.position);
        // TODO: 상태이상 시스템 연결 (기절)
        Debug.Log($"[Stun] 기절 적용! 대상={report.Target?.name}, 지속={_duration}초");
    }
}
