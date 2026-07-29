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
    private const string DefaultVfxKey = "VFX_DamageReflect";

    public DamageReflectEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.Attacker == null) return;
        float reflect = report.DamageDealt * _value;
        if (reflect <= 0f) return;

        if (report.Attacker.TryGetComponent<IDamageable>(out var damageable))
        {
            var instigator = ctx.Player != null ? ctx.Player.gameObject : null;
            damageable.TakeDamage(reflect, instigator, knockbackMultiplier: 0f);

            ItemEffectVfxHelper.SpawnOneShotAt(ResolveVfxKey(DefaultVfxKey), report.Attacker.transform.position);
            ItemEffectVfxHelper.ShowNotice($"<color=#88CCFF>피해 반사</color> {reflect:F0} → {report.Attacker.name}");
            Debug.Log($"[DamageReflect] {reflect:F0} 반사 → {report.Attacker.name}");
        }
    }
}

/// <summary>
/// 불 속성 피해 반사 (fire_dragon_scale).
/// value=1 은 "1배(100%)" 반사이지만 CSV 원본 의미는 "피해 반사 + 불 속성 부여".
/// 실제 반사량은 DamageReflect와 동일 비율을 쓰되, 원소를 Fire로 고정.
/// </summary>
public sealed class FireReflectEffect : ItemEffectBase
{
    private const string DefaultVfxKey = "VFX_FireReflect";

    public FireReflectEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.Attacker == null) return;
        float reflect = report.DamageDealt * _value;   // CSV value 비율 사용(하드코딩 0.5 제거)
        if (reflect <= 0f) return;

        if (report.Attacker.TryGetComponent<IDamageable>(out var damageable))
        {
            var instigator = ctx.Player != null ? ctx.Player.gameObject : null;
            damageable.TakeDamage(reflect, instigator, knockbackMultiplier: 0f);

            ItemEffectVfxHelper.SpawnOneShotAt(ResolveVfxKey(DefaultVfxKey), report.Attacker.transform.position);
            ItemEffectVfxHelper.ShowNotice($"<color=#FF7744>불 반사</color> {reflect:F0} → {report.Attacker.name}");
            Debug.Log($"[FireReflect] {reflect:F0} 불 반사 → {report.Attacker.name}");
        }
    }
}

public sealed class DefenseOnHitEffect : ItemEffectBase
{
    private float _cooldownEnd;

    public DefenseOnHitEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.DamageDealt <= 0f) return;   // 실드 전흡수 등 실피해 0이면 미발동
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
        if (ctx.Player != null) ItemGuide.Toast(ctx.Player.transform.position, $"방어 +{_value}");
        Debug.Log($"[DefenseOnHit] 방어력 +{_value} ({_duration}초), 쿨다운 {cooldown}초");
    }
}

/// <summary>
/// 피격 시 자해 추가 체력 손실 (prometheus_flame slot2 등).
/// value 절대값 = 현재 최대체력 비율(예: 0.05 = 5%).
/// 설계 ②: RuntimeStats.Damage 직접 호출로 실드/사망무효/피해경감을 우회한 순수 자해.
/// </summary>
public sealed class ExtraDamageOnHitEffect : ItemEffectBase
{
    public ExtraDamageOnHitEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        var stats = ctx.Player?.RuntimeStats;
        if (stats == null || stats.MaxHp <= 0) return;

        int extraLoss = Mathf.Max(1, Mathf.RoundToInt(stats.MaxHp * Mathf.Abs(_value)));
        stats.Damage(extraLoss);   // 실드/사망무효 우회 — 직접 차감
        ItemGuide.Toast(ctx.Player.transform.position, $"자해 -{extraLoss}");

        // 자해로 HP가 0이 되면 다음 피격까지 생존하는 버그 방지 — 즉시 사망 판정.
        if (stats.Hp <= 0) ctx.Player.NotifyHpDepleted();
    }
}
