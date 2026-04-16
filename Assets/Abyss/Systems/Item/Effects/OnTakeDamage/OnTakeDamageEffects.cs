using UnityEngine;

// ═══════════════════════════════════════════════════════════
// 피격 시 발동하는 효과
// ═══════════════════════════════════════════════════════════

public sealed class DamageNegateEffect : ItemEffectBase
{
    public DamageNegateEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPreTakeDamage(ItemEffectContext ctx, ref DamagePacket pkt)
    {
        if (Random.value < _value)
        {
            pkt.Negated = true;
            pkt.FinalDamage = 0f;
            Debug.Log("[DamageNegate] 피해 무효화!");
        }
    }
}

public sealed class DamageReflectEffect : ItemEffectBase
{
    public DamageReflectEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.Attacker == null) return;
        float reflect = report.DamageDealt * _value;
        // TODO: 공격자에게 반사 데미지
        // report.Attacker.GetComponent<IDamageable>()?.TakeDamage(reflect);
        Debug.Log($"[DamageReflect] {reflect:F0} 반사 → {report.Attacker.name}");
    }
}

public sealed class DefenseOnHitEffect : ItemEffectBase
{
    private float _cooldownEnd;

    public DefenseOnHitEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        float cooldown = _duration > 0f ? _duration * 2f : 10f; // duration=5 → 쿨다운 10초
        if (Time.time < _cooldownEnd) return;

        var session = ctx.Session;
        if (session?.BuffHandler != null)
        {
            session.BuffHandler.AddBuff(
                new StatModifier(StatType.Defense, _value),
                1, false, false
            );
        }

        _cooldownEnd = Time.time + cooldown;
        Debug.Log($"[DefenseOnHit] 방어력 +{_value} ({_duration}초), 쿨다운 {cooldown}초");
    }
}
